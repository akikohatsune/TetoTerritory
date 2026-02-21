using Microsoft.Data.Sqlite;
using TetoTerritory.CSharp.Core;

namespace TetoTerritory.CSharp.Storage;

public sealed class ChatMemoryStore
{
    private readonly string _dbPath;
    private readonly int _maxMessages;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ChatMemoryStore(string dbPath, int maxHistoryTurns)
    {
        _dbPath = dbPath;
        _maxMessages = Math.Max(2, maxHistoryTurns * 2);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureParentDirectory();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await ExecuteAsync(conn, "PRAGMA journal_mode=WAL;", cancellationToken);
            await ExecuteAsync(conn, "PRAGMA synchronous=NORMAL;", cancellationToken);
            await ExecuteAsync(
                conn,
                """
                CREATE TABLE IF NOT EXISTS chat_memory (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    channel_id INTEGER NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                )
                """,
                cancellationToken);

            await ExecuteAsync(
                conn,
                "CREATE INDEX IF NOT EXISTS idx_chat_memory_channel_id_id ON chat_memory (channel_id, id)",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AppendMessageAsync(ulong channelId, string role, string content, CancellationToken cancellationToken = default)
    {
        if (role is not ("user" or "assistant"))
        {
            throw new ArgumentException($"Invalid role: {role}", nameof(role));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO chat_memory (channel_id, role, content) VALUES (@channelId, @role, @content)";
                cmd.Parameters.AddWithValue("@channelId", (long)channelId);
                cmd.Parameters.AddWithValue("@role", role);
                cmd.Parameters.AddWithValue("@content", content);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await TrimChannelAsync(conn, tx, channelId, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MemoryMessage>> GetHistoryAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        var rows = new List<MemoryMessage>();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT role, content
                FROM chat_memory
                WHERE channel_id = @channelId
                ORDER BY id DESC
                LIMIT @limit
                """;
            cmd.Parameters.AddWithValue("@channelId", (long)channelId);
            cmd.Parameters.AddWithValue("@limit", _maxMessages);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new MemoryMessage(
                    Role: reader.GetString(0),
                    Content: reader.GetString(1)));
            }
        }
        finally
        {
            _gate.Release();
        }

        rows.Reverse();
        return rows;
    }

    public async Task ClearChannelAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_memory WHERE channel_id = @channelId";
            cmd.Parameters.AddWithValue("@channelId", (long)channelId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneInactiveChannelsAsync(int idleSeconds, CancellationToken cancellationToken = default)
    {
        if (idleSeconds <= 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                DELETE FROM chat_memory
                WHERE channel_id IN (
                    SELECT channel_id
                    FROM chat_memory
                    GROUP BY channel_id
                    HAVING MAX(created_at) < datetime('now', @offset)
                )
                """;
            cmd.Parameters.AddWithValue("@offset", $"-{idleSeconds} seconds");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TrimChannelAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        ulong channelId,
        CancellationToken cancellationToken)
    {
        long? cutoffId = null;
        await using (var cutoffCmd = conn.CreateCommand())
        {
            cutoffCmd.Transaction = tx;
            cutoffCmd.CommandText =
                """
                SELECT id
                FROM chat_memory
                WHERE channel_id = @channelId
                ORDER BY id DESC
                LIMIT 1 OFFSET @offset
                """;
            cutoffCmd.Parameters.AddWithValue("@channelId", (long)channelId);
            cutoffCmd.Parameters.AddWithValue("@offset", _maxMessages - 1);

            var scalar = await cutoffCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar is long id)
            {
                cutoffId = id;
            }
        }

        if (!cutoffId.HasValue)
        {
            return;
        }

        await using var deleteCmd = conn.CreateCommand();
        deleteCmd.Transaction = tx;
        deleteCmd.CommandText =
            "DELETE FROM chat_memory WHERE channel_id = @channelId AND id < @cutoffId";
        deleteCmd.Parameters.AddWithValue("@channelId", (long)channelId);
        deleteCmd.Parameters.AddWithValue("@cutoffId", cutoffId.Value);
        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={_dbPath}");
    }

    private void EnsureParentDirectory()
    {
        var full = Path.GetFullPath(_dbPath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
