using Microsoft.Data.Sqlite;

namespace TetoTerritory.CSharp.Storage;

public sealed class BanStore
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BanStore(string dbPath)
    {
        _dbPath = dbPath;
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
                CREATE TABLE IF NOT EXISTS bot_banned_users (
                    guild_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    banned_by INTEGER,
                    reason TEXT,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (guild_id, user_id)
                )
                """,
                cancellationToken);
            await ExecuteAsync(
                conn,
                "CREATE INDEX IF NOT EXISTS idx_bot_banned_users_guild_user ON bot_banned_users (guild_id, user_id)",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> BanUserAsync(ulong guildId, ulong userId, ulong? bannedBy = null, string? reason = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken);

            var existed = false;
            await using (var existsCmd = conn.CreateCommand())
            {
                existsCmd.Transaction = tx;
                existsCmd.CommandText =
                    """
                    SELECT 1
                    FROM bot_banned_users
                    WHERE guild_id = @guildId AND user_id = @userId
                    LIMIT 1
                    """;
                existsCmd.Parameters.AddWithValue("@guildId", (long)guildId);
                existsCmd.Parameters.AddWithValue("@userId", (long)userId);
                existed = await existsCmd.ExecuteScalarAsync(cancellationToken) is not null;
            }

            await using (var upsertCmd = conn.CreateCommand())
            {
                upsertCmd.Transaction = tx;
                upsertCmd.CommandText =
                    """
                    INSERT INTO bot_banned_users (guild_id, user_id, banned_by, reason)
                    VALUES (@guildId, @userId, @bannedBy, @reason)
                    ON CONFLICT(guild_id, user_id) DO UPDATE SET
                        banned_by = excluded.banned_by,
                        reason = excluded.reason,
                        updated_at = CURRENT_TIMESTAMP
                    """;
                upsertCmd.Parameters.AddWithValue("@guildId", (long)guildId);
                upsertCmd.Parameters.AddWithValue("@userId", (long)userId);
                upsertCmd.Parameters.AddWithValue("@bannedBy", bannedBy.HasValue ? (long)bannedBy.Value : DBNull.Value);
                upsertCmd.Parameters.AddWithValue("@reason", string.IsNullOrWhiteSpace(reason) ? DBNull.Value : reason);
                await upsertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return !existed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> UnbanUserAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "DELETE FROM bot_banned_users WHERE guild_id = @guildId AND user_id = @userId";
            cmd.Parameters.AddWithValue("@guildId", (long)guildId);
            cmd.Parameters.AddWithValue("@userId", (long)userId);
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            return affected > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsUserBannedAsync(ulong guildId, ulong userId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT 1
                FROM bot_banned_users
                WHERE guild_id = @guildId AND user_id = @userId
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@guildId", (long)guildId);
            cmd.Parameters.AddWithValue("@userId", (long)userId);
            return await cmd.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            _gate.Release();
        }
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
