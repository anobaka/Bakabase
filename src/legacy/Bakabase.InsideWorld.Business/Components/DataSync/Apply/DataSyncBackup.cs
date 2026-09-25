using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>The database copy before a destructive decision could not be made (§8.10.4): nothing was applied.</summary>
public sealed class DataSyncBackupFailedException(string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The brief message the task ends with (<c>DataSyncProblemCode.BackupFailed</c>).</summary>
    public const string Code = "BackupFailed";
}

/// <summary>
/// Backup before destructive decisions (§8.10.4, Q11): <c>VACUUM INTO '{BackupsPath}/data-sync-{yyyyMMdd-HHmmss}.db'</c>
/// on a separate connection to the database file, before the apply's <c>BEGIN IMMEDIATE</c>. The newest
/// <see cref="DataSyncRetention.BackupsKept"/> are kept. Bakabase has no restore command: going back means quitting
/// and replacing the database file with the copy.
/// </summary>
public sealed class DataSyncBackup(IDataSyncDataDirectory directory, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Copies the database of <paramref name="db"/>; returns the file. Throws <see cref="DataSyncBackupFailedException"/>.</summary>
    public async Task<string> CreateAsync(BakabaseDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("The backup is taken before the apply's transaction (§8.10.4).");
        string path;
        try
        {
            Directory.CreateDirectory(directory.BackupsPath);
            path = Path.Combine(directory.BackupsPath, NextFileName(directory.BackupsPath,
                _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));

            var connectionString = db.Database.GetConnectionString() ??
                                   throw new InvalidOperationException("The database has no connection string.");
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", path);
            command.CommandTimeout = 0;
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException
                                      or InvalidOperationException)
        {
            throw new DataSyncBackupFailedException($"The database could not be backed up: {e.Message}", e);
        }

        DataSyncRetention.PruneBackups(directory.BackupsPath);
        return path;
    }

    /// <summary>
    /// <c>data-sync-{stamp}.db</c>, or for another backup in the same second <c>data-sync-{stamp}_{n:D3}.db</c> with
    /// <c>n</c> above every counter of that second already there: '_' sorts after '.' and the counter is zero-padded,
    /// so the names sort by time — and a name retention freed is never taken again — so retention, which keeps the
    /// names that sort last, keeps the newest (<see cref="DataSyncRetention.PruneBackups"/>).
    /// </summary>
    private static string NextFileName(string backupsPath, string stamp)
    {
        var first = $"data-sync-{stamp}.db";
        var prefix = $"data-sync-{stamp}_";
        var taken = Directory.EnumerateFiles(backupsPath, $"data-sync-{stamp}*.db", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Select(name => name == first
                ? 0
                : name!.StartsWith(prefix, StringComparison.Ordinal) &&
                  int.TryParse(name.AsSpan(prefix.Length, name.Length - prefix.Length - ".db".Length),
                      NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                    ? n
                    : -1)
            .Where(n => n >= 0)
            .ToList();
        return taken.Count == 0 ? first : $"{prefix}{taken.Max() + 1:D3}.db";
    }

    /// <summary>The size estimate a dialog shows: the database file's current size, or 0 when unknown.</summary>
    public static long DatabaseBytes(BakabaseDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        return !string.IsNullOrEmpty(source) && File.Exists(source) ? new FileInfo(source).Length : 0;
    }
}
