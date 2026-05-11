using System.Text.RegularExpressions;

namespace TetoTerritory.CSharp.Core;

public sealed class CommandParser
{
    private readonly string _prefix;
    private readonly Regex _inlineReplayRegex;

    public CommandParser(string prefix)
    {
        _prefix = prefix;
        _inlineReplayRegex = new Regex(
            $"^{Regex.Escape(prefix)}replayteto(\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public bool TryParsePrefixedCommand(string content, out string commandName, out string args)
    {
        commandName = string.Empty;
        args = string.Empty;

        if (!content.StartsWith(_prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = content[_prefix.Length..].TrimStart();
        if (remainder.Length == 0)
        {
            return false;
        }

        var spaceIndex = remainder.IndexOf(' ');
        if (spaceIndex < 0)
        {
            commandName = remainder.ToLowerInvariant();
            return true;
        }

        commandName = remainder[..spaceIndex].ToLowerInvariant();
        args = remainder[(spaceIndex + 1)..].Trim();
        return true;
    }

    public int? ExtractInlineReplayId(string content)
    {
        var matched = _inlineReplayRegex.Match(content.Trim());
        if (!matched.Success)
        {
            return null;
        }

        return int.TryParse(matched.Groups[1].Value, out var id) ? id : null;
    }

    public static ulong? ExtractUserId(string token, IEnumerable<ulong> mentionedUserIds)
    {
        var cleaned = token.Trim();
        if (cleaned.StartsWith("<@!", StringComparison.Ordinal) && cleaned.EndsWith('>'))
        {
            var idPart = cleaned[3..^1];
            if (ulong.TryParse(idPart, out var userId))
            {
                return userId;
            }
        }
        else if (cleaned.StartsWith("<@", StringComparison.Ordinal) && cleaned.EndsWith('>'))
        {
            var idPart = cleaned[2..^1];
            if (ulong.TryParse(idPart, out var userId))
            {
                return userId;
            }
        }

        if (ulong.TryParse(cleaned, out var rawId))
        {
            return rawId;
        }

        // Fallback to first mention only if the token itself doesn't yield a valid ID
        // but it looks like it might have been intended as a mention (e.g. "@user").
        var first = mentionedUserIds.FirstOrDefault();
        return first != 0 ? first : null;
    }

    public static bool TryParseFirstToken(string input, out string firstToken, out string? remainder)
    {
        firstToken = string.Empty;
        remainder = null;
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var space = trimmed.IndexOf(' ');
        if (space < 0)
        {
            firstToken = trimmed;
            return true;
        }

        firstToken = trimmed[..space];
        var rest = trimmed[(space + 1)..].Trim();
        remainder = rest.Length == 0 ? null : rest;
        return true;
    }
}
