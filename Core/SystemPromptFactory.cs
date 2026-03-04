using System.Globalization;

namespace TetoTerritory.CSharp.Core;

public static class SystemPromptFactory
{
    public static string Build(string systemPrompt, DateTimeOffset? nowUtc = null)
    {
        var utcNow = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var year = utcNow.Year.ToString(CultureInfo.InvariantCulture);
        var isoTime = utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var timeBlock =
            $"Authoritative UTC Time: {isoTime}\n" +
            $"Authoritative Current Year: {year}";

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            return timeBlock;
        }

        return $"{timeBlock}\n\n{systemPrompt.Trim()}";
    }
}
