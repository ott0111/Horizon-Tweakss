using Horizon.Core.Interfaces;
using Horizon.Core.Models;
using Microsoft.Data.Sqlite;

namespace Horizon.Infrastructure.Persistence;

public sealed class SqliteHistoryService : IHistoryService
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = LocalPaths.Database }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                name TEXT NOT NULL,
                success INTEGER NOT NULL,
                completed_at TEXT NULL,
                restart INTEGER NOT NULL DEFAULT 0,
                restore_point INTEGER NOT NULL DEFAULT 0,
                source TEXT NOT NULL DEFAULT 'user'
            );
            CREATE TABLE IF NOT EXISTS changes (
                session_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                tweak_id TEXT NOT NULL,
                name TEXT NOT NULL,
                previous_value TEXT NOT NULL,
                new_value TEXT NOT NULL,
                success INTEGER NOT NULL,
                error TEXT NULL,
                result INTEGER NOT NULL DEFAULT 0,
                verified INTEGER NOT NULL DEFAULT 0,
                restart INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(session_id, ordinal),
                FOREIGN KEY(session_id) REFERENCES sessions(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await AddColumnIfMissingAsync(connection, "sessions", "completed_at", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, "sessions", "restart", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "sessions", "restore_point", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "sessions", "source", "TEXT NOT NULL DEFAULT 'user'", cancellationToken);
        await AddColumnIfMissingAsync(connection, "changes", "result", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "changes", "verified", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await AddColumnIfMissingAsync(connection, "changes", "restart", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    public async Task AddAsync(OptimizationSession session, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sessionCommand = connection.CreateCommand();
        sessionCommand.Transaction = (SqliteTransaction)transaction;
        sessionCommand.CommandText = "INSERT OR REPLACE INTO sessions(id, started_at, name, success, completed_at, restart, restore_point, source) VALUES($id,$time,$name,$success,$completed,$restart,$restore,$source)";
        sessionCommand.Parameters.AddWithValue("$id", session.Id.ToString());
        sessionCommand.Parameters.AddWithValue("$time", session.StartedAt.ToString("O"));
        sessionCommand.Parameters.AddWithValue("$name", session.Name);
        sessionCommand.Parameters.AddWithValue("$success", session.Success ? 1 : 0);
        sessionCommand.Parameters.AddWithValue("$completed", (object?)session.CompletedAt?.ToString("O") ?? DBNull.Value);
        sessionCommand.Parameters.AddWithValue("$restart", (int)session.Restart);
        sessionCommand.Parameters.AddWithValue("$restore", session.RestorePointCreated ? 1 : 0);
        sessionCommand.Parameters.AddWithValue("$source", session.Source);
        await sessionCommand.ExecuteNonQueryAsync(cancellationToken);

        for (var index = 0; index < session.Changes.Count; index++)
        {
            var change = session.Changes[index];
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = "INSERT OR REPLACE INTO changes(session_id, ordinal, tweak_id, name, previous_value, new_value, success, error, result, verified, restart) VALUES($session,$ordinal,$tweak,$name,$previous,$new,$success,$error,$result,$verified,$restart)";
            command.Parameters.AddWithValue("$session", session.Id.ToString());
            command.Parameters.AddWithValue("$ordinal", index);
            command.Parameters.AddWithValue("$tweak", change.TweakId);
            command.Parameters.AddWithValue("$name", change.Name);
            command.Parameters.AddWithValue("$previous", change.PreviousValue);
            command.Parameters.AddWithValue("$new", change.NewValue);
            command.Parameters.AddWithValue("$success", change.Success ? 1 : 0);
            command.Parameters.AddWithValue("$error", (object?)change.Error ?? DBNull.Value);
            command.Parameters.AddWithValue("$result", (int)change.Result);
            command.Parameters.AddWithValue("$verified", change.Verified ? 1 : 0);
            command.Parameters.AddWithValue("$restart", (int)change.Restart);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OptimizationSession>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var sessions = new List<OptimizationSession>();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, started_at, name, success, completed_at, restart, restore_point, source FROM sessions ORDER BY started_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(Guid Id, DateTimeOffset Time, string Name, bool Success, DateTimeOffset? Completed, RestartRequirement Restart, bool RestorePoint, string Source)>();
        while (await reader.ReadAsync(cancellationToken)) rows.Add((Guid.Parse(reader.GetString(0)), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3) == 1,
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)), (RestartRequirement)reader.GetInt32(5), reader.GetInt32(6) == 1, reader.GetString(7)));
        await reader.CloseAsync();
        foreach (var row in rows) sessions.Add(new(row.Id, row.Time, row.Name, await ReadChangesAsync(connection, row.Id, cancellationToken), row.Success, row.Completed, row.Restart, row.RestorePoint, row.Source));
        return sessions;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "DELETE FROM sessions WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand(); command.CommandText = "DELETE FROM changes; DELETE FROM sessions;"; await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<OptimizationChange>> ReadChangesAsync(SqliteConnection connection, Guid id, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT tweak_id, name, previous_value, new_value, success, error, result, verified, restart FROM changes WHERE session_id=$id ORDER BY ordinal";
        command.Parameters.AddWithValue("$id", id.ToString());
        var changes = new List<OptimizationChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) changes.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4) == 1,
            reader.IsDBNull(5) ? null : reader.GetString(5), (TweakChangeResult)reader.GetInt32(6), reader.GetInt32(7) == 1, (RestartRequirement)reader.GetInt32(8)));
        return changes;
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition, CancellationToken cancellationToken)
    {
        var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await info.ExecuteReaderAsync(cancellationToken);
        var exists = false;
        while (await reader.ReadAsync(cancellationToken)) if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
        await reader.CloseAsync();
        if (exists) return;
        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
