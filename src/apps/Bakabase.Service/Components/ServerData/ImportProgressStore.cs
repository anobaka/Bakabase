using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Bakabase.Service.Components.ServerData;

public sealed record ImportProgressSnapshot
{
    public string Id { get; init; } = "";
    public string Operation { get; init; } = "import";
    public string Phase { get; init; } = "queued";
    public long CompletedBytes { get; init; }
    public long TotalBytes { get; init; }
    public int CompletedFiles { get; init; }
    public int TotalFiles { get; init; }
    public int CompletedEntries { get; init; }
    public int TotalEntries { get; init; }
    public string? CurrentFile { get; init; }
    public double ElapsedSeconds { get; init; }
    public double BytesPerSecond { get; init; }
    public double? RemainingSeconds { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public string? BackupPath { get; init; }
    public string? SourcePath { get; init; }
    public string? TargetPath { get; init; }
    public string? Error { get; init; }
    public string? FailedPhase { get; init; }
    public bool AutomaticMaintenance { get; init; }
}

/// <summary>Read-only monitoring, independent of AppService, SQLite, and the import journal.</summary>
public sealed class ImportProgressStore : IDisposable
{
    public const string FileName = ".bakabase-import-status.json";
    public static ImportProgressStore? Current { get; set; }
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record Saved(string Token, ImportProgressSnapshot Progress, bool Applied);
    private readonly object _gate = new();
    private readonly string _directory;
    private readonly Stopwatch _elapsed = new();
    private readonly Stopwatch _copyElapsed = new();
    private readonly Timer _heartbeat;
    // Publish immutable snapshots independently of the writer gate. Persisting to a
    // slow data disk must not stop the maintenance endpoint from reporting its state.
    private volatile Saved? _saved;
    private DateTimeOffset _lastPersisted;
    private double _elapsedBaseSeconds;
    private bool _disposed;
    private bool _readOnlyFailure;

    public ImportProgressStore(string directory, bool readOnly = false)
    {
        _directory = directory;
        var path = Path.Combine(directory, FileName);
        RejectLink(path);
        if (File.Exists(path))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), JsonOptions);
                if (saved?.Progress is { } progress && Guid.TryParseExact(progress.Id, "N", out _) && saved.Token?.Length == 64)
                {
                    _saved = saved;
                    _elapsedBaseSeconds = saved.Progress.ElapsedSeconds;
                }
            }
            catch (JsonException) { Console.Error.WriteLine("Discarding invalid import monitoring state; the import journal is unchanged."); }
        }
        _heartbeat = new Timer(_ => { if (!readOnly) Heartbeat(); }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void RefreshFromDisk(string directory)
    {
        lock (_gate)
        {
            if (_disposed || _readOnlyFailure) return;
            var path = Path.Combine(directory, FileName);
            RejectLink(path);
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), JsonOptions);
            if (!_disposed && saved?.Progress is { } progress && Guid.TryParseExact(progress.Id, "N", out _) && saved.Token?.Length == 64)
                _saved = saved;
        }
    }

    public string? Token => _saved?.Token;
    public bool Applied => _saved?.Applied == true;
    public ImportProgressSnapshot? Read() => _saved?.Progress;

    public bool Authorize(string? token)
    {
        return AuthorizeSnapshot(_saved, token);
    }

    public bool TryReadAuthorized(string? token, out ImportProgressSnapshot? progress)
    {
        // Token and progress must come from the same snapshot when a new operation
        // revokes the previous capability concurrently with this request.
        var saved = _saved;
        progress = AuthorizeSnapshot(saved, token) ? saved!.Progress : null;
        return progress != null;
    }

    private static bool AuthorizeSnapshot(Saved? saved, string? token) =>
        saved != null && token?.Length == 64 && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(saved.Token));

    public void EnsureQueued(ServerAppDataImport.Journal journal)
        => EnsureOperation(journal.Id, "import", Path.Combine(_directory, ServerAppDataImport.BackupsName, journal.Id));

    public void EnsureRelocation(ServerAppDataRelocation.Journal journal)
    {
        if (journal.ImportSourcePath == null)
            EnsureOperation(journal.Id, "relocate", sourcePath: journal.SourcePath, targetPath: journal.TargetPath);
        else
            EnsureOperation(journal.Id, "import", backupPath: journal.SourcePath,
                sourcePath: journal.ImportSourcePath, targetPath: journal.TargetPath);
    }

    private void EnsureOperation(string id, string operation, string? backupPath = null,
        string? sourcePath = null, string? targetPath = null)
    {
        lock (_gate)
        {
            if (_saved?.Progress.Id == id) return;
            _elapsed.Reset();
            _elapsedBaseSeconds = 0;
            _copyElapsed.Reset();
            _saved = new Saved(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), new ImportProgressSnapshot
            { Id = id, Operation = operation, BackupPath = backupPath, SourcePath = sourcePath, TargetPath = targetPath }, false);
            Persist();
        }
    }

    public void Begin(ServerAppDataImport.Journal journal)
    {
        EnsureQueued(journal);
        BeginOperation();
    }

    public void BeginRelocation(ServerAppDataRelocation.Journal journal)
    {
        EnsureRelocation(journal);
        BeginOperation();
    }

    private void BeginOperation()
    {
        lock (_gate)
        {
            _elapsed.Restart();
            _elapsedBaseSeconds = 0;
            _copyElapsed.Reset();
            _saved = _saved! with { Progress = _saved!.Progress with
            { Phase = "scanning", StartedAtUtc = DateTimeOffset.UtcNow, LastActivityAtUtc = DateTimeOffset.UtcNow,
                Error = null, FailedPhase = null, ElapsedSeconds = 0,
                CompletedBytes = 0, TotalBytes = 0, CompletedFiles = 0, TotalFiles = 0, CurrentFile = null,
                CompletedEntries = 0, TotalEntries = 0, BytesPerSecond = 0, RemainingSeconds = null } };
            Persist();
        }
    }

    public void BeginInitialization()
    {
        lock (_gate)
        {
            _elapsed.Restart();
            _elapsedBaseSeconds = 0;
            _copyElapsed.Reset();
            _saved = new Saved(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), new ImportProgressSnapshot
            {
                Id = Guid.NewGuid().ToString("N"), Operation = "initialize", Phase = "starting",
                StartedAtUtc = DateTimeOffset.UtcNow, LastActivityAtUtc = DateTimeOffset.UtcNow
            }, true);
            Persist();
        }
    }

    public void Report(AppDataImportProgressUpdate update)
    {
        lock (_gate)
        {
            if (_saved == null) return;
            var phaseChanged = _saved.Progress.Phase != update.Phase;
            if (update.Phase == "copying" && phaseChanged) _copyElapsed.Restart();
            _saved = _saved with { Applied = _saved.Applied || update.Phase == "starting", Progress = _saved.Progress with
            { Phase = update.Phase, LastActivityAtUtc = DateTimeOffset.UtcNow,
                CompletedBytes = update.CompletedBytes, TotalBytes = update.TotalBytes,
                CompletedFiles = update.CompletedFiles, TotalFiles = update.TotalFiles, CurrentFile = update.CurrentFile,
                CompletedEntries = update.CompletedEntries, TotalEntries = update.TotalEntries } };
            RefreshClock();
            if (phaseChanged || DateTimeOffset.UtcNow - _lastPersisted >= TimeSpan.FromSeconds(1)) Persist();
        }
    }

    public void Starting()
    {
        lock (_gate)
        {
            if (_saved == null) return;
            if (!_elapsed.IsRunning) _elapsed.Start();
            _saved = _saved with { Applied = true, Progress = _saved.Progress with
            { Phase = "starting", LastActivityAtUtc = DateTimeOffset.UtcNow, CurrentFile = null,
                Error = null, FailedPhase = null, BytesPerSecond = 0, RemainingSeconds = null } };
            RefreshClock();
            Persist();
        }
    }

    public void Complete() => Finish("completed", null);
    public void Fail(string error) => Finish("failed", error.Length > 2000 ? error[..2000] : error);
    public void Cancel() => Finish("cancelled", null);

    internal void FailReadOnly(string error)
    {
        lock (_gate) { _readOnlyFailure = true; Finish("failed", error, false); }
    }

    private void Finish(string phase, string? error, bool persist = true)
    {
        lock (_gate)
        {
            if (_saved == null) return;
            RefreshClock();
            _elapsed.Stop();
            _copyElapsed.Stop();
            var failedPhase = phase == "failed"
                ? _saved.Progress.FailedPhase ?? _saved.Progress.Phase : null;
            _saved = _saved with { Progress = _saved.Progress with
            { Phase = phase, FailedPhase = failedPhase, LastActivityAtUtc = DateTimeOffset.UtcNow,
                Error = error, CurrentFile = phase == "failed" ? _saved.Progress.CurrentFile : null,
                BytesPerSecond = 0, RemainingSeconds = null } };
            if (persist)
            {
                try { Persist(); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { Console.Error.WriteLine($"Cannot save final import progress: {e.Message}"); }
            }
        }
    }

    private void Heartbeat()
    {
        lock (_gate)
        {
            if (_disposed || !_elapsed.IsRunning || _saved == null) return;
            RefreshClock();
            try { Persist(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Console.Error.WriteLine($"Cannot save import progress: {e.Message}"); }
        }
    }

    private void RefreshClock()
    {
        if (_saved == null) return;
        var progress = _saved.Progress;
        var rate = progress.Phase == "copying" && _copyElapsed.Elapsed.TotalSeconds >= 1
            ? progress.CompletedBytes / _copyElapsed.Elapsed.TotalSeconds : 0;
        _saved = _saved with { Progress = progress with
        { UpdatedAtUtc = DateTimeOffset.UtcNow, ElapsedSeconds = _elapsed.IsRunning ? _elapsedBaseSeconds + _elapsed.Elapsed.TotalSeconds : progress.ElapsedSeconds,
            BytesPerSecond = rate, RemainingSeconds = rate > 0 ? Math.Max(0, progress.TotalBytes - progress.CompletedBytes) / rate : null } };
    }

    private void Persist()
    {
        if (_saved == null) return;
        var path = Path.Combine(_directory, FileName);
        RejectLink(path);
        RejectLink(path + ".tmp");
        File.Delete(path + ".tmp");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(path + ".tmp", options))
            JsonSerializer.Serialize(stream, _saved, JsonOptions);
        File.Move(path + ".tmp", path, true);
        _lastPersisted = DateTimeOffset.UtcNow;
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null) throw new IOException($"Import monitoring files cannot be symbolic links: {path}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _heartbeat.Dispose();
        }
    }
}
