using System.Text;
using System.Text.Json;

namespace TetoTerritory.CSharp.Logging;

public sealed class ChatReplayLogger
{
    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _nextId = 1;

    public ChatReplayLogger(string logPath)
    {
        _logPath = logPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var full = Path.GetFullPath(_logPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(full))
            {
                await File.WriteAllTextAsync(full, string.Empty, Encoding.UTF8, cancellationToken);
                _nextId = 1;
                return;
            }

            var maxId = 0;
            await foreach (var entry in ReadEntriesUnsafeAsync(full, guildId: null, cancellationToken))
            {
                if (entry.Id > maxId)
                {
                    maxId = entry.Id;
                }
            }

            _nextId = maxId + 1;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LogChatAsync(
        ulong? guildId,
        string? guildName,
        ulong channelId,
        string? channelName,
        ulong userId,
        string userName,
        string userDisplay,
        string trigger,
        string prompt,
        int replyLength,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var full = Path.GetFullPath(_logPath);
            var entry = new ReplayEntry
            {
                Id = _nextId++,
                Type = "chat",
                TsUtc = DateTimeOffset.UtcNow.ToString("O"),
                GuildId = guildId,
                GuildName = guildName,
                ChannelId = channelId,
                ChannelName = channelName,
                UserId = userId,
                UserName = userName,
                UserDisplay = userDisplay,
                Trigger = trigger,
                Prompt = prompt.Length <= 600 ? prompt : prompt[..600],
                ReplyLength = replyLength,
            };

            var line = JsonSerializer.Serialize(entry);
            await File.AppendAllTextAsync(
                full,
                line + Environment.NewLine,
                Encoding.UTF8,
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<ReplayEntry>> ReadRecentAsync(
        int limit,
        ulong? guildId,
        CancellationToken cancellationToken = default)
    {
        var safeLimit = Math.Max(1, limit);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var full = Path.GetFullPath(_logPath);
            var latest = new Queue<ReplayEntry>(safeLimit);

            await foreach (var entry in ReadEntriesUnsafeAsync(full, guildId, cancellationToken))
            {
                if (latest.Count == safeLimit)
                {
                    _ = latest.Dequeue();
                }

                latest.Enqueue(entry);
            }

            var records = latest.ToList();
            records.Reverse();
            return records;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ReplayEntry?> GetByIdAsync(
        int recordId,
        ulong? guildId,
        CancellationToken cancellationToken = default)
    {
        if (recordId <= 0)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var full = Path.GetFullPath(_logPath);
            await foreach (var entry in ReadEntriesUnsafeAsync(full, guildId, cancellationToken))
            {
                if (entry.Id == recordId)
                {
                    return entry;
                }

                if (entry.Id > recordId)
                {
                    break;
                }
            }

            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async IAsyncEnumerable<ReplayEntry> ReadEntriesUnsafeAsync(
        string fullPath,
        ulong? guildId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
        {
            yield break;
        }

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var fallbackId = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await reader.ReadLineAsync(cancellationToken);
            if (raw is null)
            {
                yield break;
            }
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            ReplayEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<ReplayEntry>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (entry is null || !string.Equals(entry.Type, "chat", StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.Id <= 0)
            {
                fallbackId++;
                entry.Id = fallbackId;
            }
            else
            {
                fallbackId = Math.Max(fallbackId, entry.Id);
            }

            if (guildId.HasValue && entry.GuildId != guildId)
            {
                continue;
            }

            yield return entry;
        }
    }

    public sealed class ReplayEntry
    {
        public int Id { get; set; }
        public string Type { get; set; } = "chat";
        public string TsUtc { get; set; } = string.Empty;
        public ulong? GuildId { get; set; }
        public string? GuildName { get; set; }
        public ulong ChannelId { get; set; }
        public string? ChannelName { get; set; }
        public ulong UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserDisplay { get; set; } = string.Empty;
        public string Trigger { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public int ReplyLength { get; set; }
    }
}
