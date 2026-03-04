namespace TetoTerritory.CSharp.Core;

public static class PersonaKeywordMatcher
{
    public static IReadOnlyList<string> ParseMarkdownKeywords(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<string>();
        }

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = markdown.Split('\n');
        foreach (var rawLine in lines)
        {
            var keyword = NormalizeKeywordLine(rawLine);
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (seen.Add(keyword))
            {
                list.Add(keyword);
            }
        }

        return list;
    }

    public static bool ContainsKeyword(string input, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(input) || keywords.Count == 0)
        {
            return false;
        }

        var normalizedInput = input.ToLowerInvariant();
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                continue;
            }

            if (normalizedInput.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeKeywordLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return null;
        }

        if (line.StartsWith("- ", StringComparison.Ordinal) ||
            line.StartsWith("* ", StringComparison.Ordinal))
        {
            line = line[2..].Trim();
        }
        else
        {
            var dotIndex = line.IndexOf('.', StringComparison.Ordinal);
            if (dotIndex > 0 &&
                int.TryParse(line[..dotIndex], out _) &&
                dotIndex + 1 < line.Length)
            {
                line = line[(dotIndex + 1)..].Trim();
            }
        }

        line = line.Trim('`', '"', '\'');
        if (line.Length == 0)
        {
            return null;
        }

        return line.ToLowerInvariant();
    }
}
