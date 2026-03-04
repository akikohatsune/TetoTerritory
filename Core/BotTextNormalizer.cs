using System.Text.RegularExpressions;

namespace TetoTerritory.CSharp.Core;

public static class BotTextNormalizer
{
    private static readonly Regex EveryoneMentionPattern = new("@everyone", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HereMentionPattern = new("@here", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HasLatexPattern = new(@"(?:\$\$|\$|\\\(|\\\)|\\\[|\\\]|\\[a-zA-Z]+)", RegexOptions.Compiled);
    private static readonly Regex ThinkBlockPattern = new(
        @"<\s*think\b[^>]*>[\s\S]*?<\s*/\s*think\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThinkOpenTagPattern = new(
        @"<\s*think\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ThinkCloseTagPattern = new(
        @"<\s*/\s*think\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizeMentions(string text)
    {
        var sanitized = EveryoneMentionPattern.Replace(text, "@\u200beveryone");
        sanitized = HereMentionPattern.Replace(sanitized, "@\u200bhere");
        return sanitized;
    }

    public static string NormalizeModelReply(string text)
    {
        var withoutThinking = StripThinkingContent(text);
        var protectedReply = PromptInjectionGuard.ProtectModelReply(withoutThinking);
        return LatexToPlainMath(protectedReply);
    }

    public static string StripThinkingContent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var output = ThinkBlockPattern.Replace(text, string.Empty);
        var openMatch = ThinkOpenTagPattern.Match(output);
        if (openMatch.Success)
        {
            // If upstream forgot the closing tag, drop everything from <think> onward.
            output = output[..openMatch.Index];
        }

        output = ThinkCloseTagPattern.Replace(output, string.Empty);
        return output.Trim();
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
