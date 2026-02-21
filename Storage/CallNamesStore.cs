using Microsoft.Data.Sqlite;

namespace TetoTerritory.CSharp.Storage;

public sealed class CallNamesStore
{
    private readonly string _dbPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CallNamesStore(string dbPath)
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
                CREATE TABLE IF NOT EXISTS user_call_preferences (
                    guild_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    user_calls_teto TEXT,
                    teto_calls_user TEXT,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (guild_id, user_id)
                )
                """,
                cancellationToken);
            await ExecuteAsync(
                conn,
                "CREATE INDEX IF NOT EXISTS idx_user_call_preferences_guild_user ON user_call_preferences (guild_id, user_id)",
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetUserCallsTetoAsync(ulong guildId, ulong userId, string callName, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(
            guildId,
            userId,
            setUserCallsTeto: callName,
            setTetoCallsUser: null,
            cancellationToken);
    }

    public async Task SetTetoCallsUserAsync(ulong guildId, ulong userId, string callName, CancellationToken cancellationToken = default)
    {
        await UpsertAsync(
            guildId,
            userId,
            setUserCallsTeto: null,
            setTetoCallsUser: callName,
            cancellationToken);
    }

    public async Task<(string? UserCallsTeto, string? TetoCallsUser)> GetUserCallPreferencesAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT user_calls_teto, teto_calls_user
                FROM user_call_preferences
                WHERE guild_id = @guildId AND user_id = @userId
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@guildId", (long)guildId);
            cmd.Parameters.AddWithValue("@userId", (long)userId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return (null, null);
            }

            return (
                UserCallsTeto: reader.IsDBNull(0) ? null : reader.GetString(0),
                TetoCallsUser: reader.IsDBNull(1) ? null : reader.GetString(1)
            );
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task UpsertAsync(
        ulong guildId,
        ulong userId,
        string? setUserCallsTeto,
        string? setTetoCallsUser,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(cancellationToken);

            var setUserClause = setUserCallsTeto is null
                ? "user_calls_teto = user_call_preferences.user_calls_teto"
                : "user_calls_teto = excluded.user_calls_teto";
            var setTetoClause = setTetoCallsUser is null
                ? "teto_calls_user = user_call_preferences.teto_calls_user"
                : "teto_calls_user = excluded.teto_calls_user";

            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"""
                 INSERT INTO user_call_preferences (guild_id, user_id, user_calls_teto, teto_calls_user)
                 VALUES (@guildId, @userId, @userCallsTeto, @tetoCallsUser)
                 ON CONFLICT(guild_id, user_id) DO UPDATE SET
                     {setUserClause},
                     {setTetoClause},
                     updated_at = CURRENT_TIMESTAMP
                 """;
            cmd.Parameters.AddWithValue("@guildId", (long)guildId);
            cmd.Parameters.AddWithValue("@userId", (long)userId);
            cmd.Parameters.AddWithValue("@userCallsTeto", setUserCallsTeto is null ? DBNull.Value : setUserCallsTeto);
            cmd.Parameters.AddWithValue("@tetoCallsUser", setTetoCallsUser is null ? DBNull.Value : setTetoCallsUser);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
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
