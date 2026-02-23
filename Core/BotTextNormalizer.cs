using System.Text.RegularExpressions;

namespace TetoTerritory.CSharp.Core;

public static class BotTextNormalizer
{
    private static readonly Regex EveryoneMentionPattern = new("@everyone", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HereMentionPattern = new("@here", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HasLatexPattern = new(@"(?:\$\$|\$|\\\(|\\\)|\\\[|\\\]|\\[a-zA-Z]+)", RegexOptions.Compiled);

    public static string SanitizeMentions(string text)
    {
        var sanitized = EveryoneMentionPattern.Replace(text, "@\u200beveryone");
        sanitized = HereMentionPattern.Replace(sanitized, "@\u200bhere");
        return sanitized;
    }

    public static string NormalizeModelReply(string text)
    {
        return LatexToPlainMath(text);
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
