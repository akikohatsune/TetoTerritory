using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TetoTerritory.CSharp.Core;

public static class BotTextNormalizer
{
    private static readonly HashSet<string> StructuredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "style",
        "answer",
        "confidence",
    };
    private static readonly Regex EveryoneMentionPattern = new("@everyone", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HereMentionPattern = new("@here", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FencedJsonPattern = new("^```(?:json)?\\s*(.*?)\\s*```$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnswerPropertyPattern = new("\"answer\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"\\\\])*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KeyValueLinePattern = new("^\\s*(?<key>[a-zA-Z_][a-zA-Z0-9_\\-]*)\\s*:\\s*(?<value>.*)$", RegexOptions.Compiled);
    private static readonly Regex HasLatexPattern = new(@"(?:\$\$|\$|\\\(|\\\)|\\\[|\\\]|\\[a-zA-Z]+)", RegexOptions.Compiled);

    public static string SanitizeMentions(string text)
    {
        var sanitized = EveryoneMentionPattern.Replace(text, "@\u200beveryone");
        sanitized = HereMentionPattern.Replace(sanitized, "@\u200bhere");
        return sanitized;
    }

    public static string NormalizeModelReply(string text)
    {
        var answer = ExtractAnswerFromStructuredOutput(text);
        var normalized = answer ?? text;
        return LatexToPlainMath(normalized);
    }

    public static string? ExtractAnswerFromStructuredOutput(string text)
    {
        var stripped = text.Trim();
        if (stripped.Length == 0)
        {
            return null;
        }

        var candidate = stripped;
        var fenced = FencedJsonPattern.Match(stripped);
        if (fenced.Success)
        {
            candidate = fenced.Groups[1].Value.Trim();
        }

        candidate = NormalizeSmartQuotes(candidate);
        if (TryExtractAnswerFromJsonObject(candidate, out var answerFromJson))
        {
            return answerFromJson;
        }

        if (TryExtractAnswerFromAnswerProperty(candidate, out var answerFromProperty))
        {
            return answerFromProperty;
        }

        if (TryExtractAnswerFromKeyValueBlock(candidate, out var answerFromKeyValue))
        {
            return answerFromKeyValue;
        }

        return null;
    }

    private static bool TryExtractAnswerFromJsonObject(string candidate, out string? answer)
    {
        answer = null;
        var bytes = Encoding.UTF8.GetBytes(candidate);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'{')
            {
                continue;
            }

            var reader = new Utf8JsonReader(bytes.AsSpan(i), isFinalBlock: true, state: default);
            try
            {
                if (!JsonDocument.TryParseValue(ref reader, out var doc))
                {
                    continue;
                }

                using (doc)
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("answer", out var answerProp) &&
                        answerProp.ValueKind == JsonValueKind.String)
                    {
                        answer = answerProp.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(answer))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static bool TryExtractAnswerFromAnswerProperty(string candidate, out string? answer)
    {
        answer = null;
        var match = AnswerPropertyPattern.Match(candidate);
        if (!match.Success)
        {
            return false;
        }

        var encodedValue = match.Groups["value"].Value;
        try
        {
            var decoded = JsonSerializer.Deserialize<string>($"\"{encodedValue}\"");
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                answer = decoded.Trim();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        var fallback = encodedValue
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Trim();
        if (fallback.Length == 0)
        {
            return false;
        }

        answer = fallback;
        return true;
    }

    private static string NormalizeSmartQuotes(string text)
    {
        return text
            .Replace('\u201c', '"')
            .Replace('\u201d', '"')
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');
    }

    private static bool TryExtractAnswerFromKeyValueBlock(string candidate, out string? answer)
    {
        answer = null;
        var normalized = candidate.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');

        var hasStructuredShape = false;
        foreach (var rawLine in lines)
        {
            var match = KeyValueLinePattern.Match(rawLine);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            if (StructuredKeys.Contains(key))
            {
                hasStructuredShape = true;
                break;
            }
        }

        if (!hasStructuredShape)
        {
            return false;
        }

        var collectingAnswer = false;
        var buffer = new StringBuilder();
        foreach (var rawLine in lines)
        {
            var match = KeyValueLinePattern.Match(rawLine);
            if (match.Success)
            {
                var key = match.Groups["key"].Value;
                var value = match.Groups["value"].Value.Trim();

                if (collectingAnswer && !key.Equals("answer", StringComparison.OrdinalIgnoreCase) && StructuredKeys.Contains(key))
                {
                    break;
                }

                if (key.Equals("answer", StringComparison.OrdinalIgnoreCase))
                {
                    collectingAnswer = true;
                    if (value.Length > 0)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Append('\n');
                        }

                        buffer.Append(value);
                    }

                    continue;
                }
            }

            if (!collectingAnswer)
            {
                continue;
            }

            var continuation = rawLine.Trim();
            if (continuation.Length == 0)
            {
                continue;
            }

            if (buffer.Length > 0)
            {
                buffer.Append('\n');
            }

            buffer.Append(continuation);
        }

        var result = buffer.ToString().Trim();
        if (result.Length == 0)
        {
            return false;
        }

        answer = result;
        return true;
    }

    public static string LatexToPlainMath(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (!HasLatexPattern.IsMatch(text))
        {
            return text;
        }

        var output = text;
        output = output.Replace("\\left", string.Empty, StringComparison.Ordinal)
            .Replace("\\right", string.Empty, StringComparison.Ordinal)
            .Replace("\\times", "*", StringComparison.Ordinal)
            .Replace("\\cdot", "*", StringComparison.Ordinal)
            .Replace("\\div", "/", StringComparison.Ordinal)
            .Replace("\\pm", "+/-", StringComparison.Ordinal)
            .Replace("\\neq", "!=", StringComparison.Ordinal)
            .Replace("\\leq", "<=", StringComparison.Ordinal)
            .Replace("\\geq", ">=", StringComparison.Ordinal)
            .Replace("\\approx", "~=", StringComparison.Ordinal)
            .Replace("\\pi", "pi", StringComparison.Ordinal);

        for (var i = 0; i < 5; i++)
        {
            var updated = Regex.Replace(output, @"\\frac\s*\{([^{}]+)\}\s*\{([^{}]+)\}", @"($1)/($2)");
            if (updated == output)
            {
                break;
            }

            output = updated;
        }

        for (var i = 0; i < 5; i++)
        {
            var updated = Regex.Replace(output, @"\\sqrt\s*\{([^{}]+)\}", @"sqrt($1)");
            if (updated == output)
            {
                break;
            }

            output = updated;
        }

        output = Regex.Replace(output, @"\\text\s*\{([^{}]+)\}", "$1");
        output = Regex.Replace(output, @"\\(?:quad|qquad|,|;|!)(?![a-zA-Z])", " ");
        output = output.Replace("\\(", string.Empty, StringComparison.Ordinal)
            .Replace("\\)", string.Empty, StringComparison.Ordinal)
            .Replace("\\[", string.Empty, StringComparison.Ordinal)
            .Replace("\\]", string.Empty, StringComparison.Ordinal)
            .Replace("$$", string.Empty, StringComparison.Ordinal)
            .Replace("$", string.Empty, StringComparison.Ordinal);
        output = Regex.Replace(output, @"\s{2,}", " ");
        return output.Trim();
    }
}
