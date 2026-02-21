using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TetoTerritory.CSharp.Core;

public static class BotTextNormalizer
{
    private static readonly Regex EveryoneMentionPattern = new("@everyone", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HereMentionPattern = new("@here", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FencedJsonPattern = new("^```(?:json)?\\s*(.*?)\\s*```$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
                        var answer = answerProp.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(answer))
                        {
                            return answer;
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return null;
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
