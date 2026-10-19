using System.Text.Json;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Downloader.Abstractions;

namespace Bakabase.Modules.HttpDownloads.Components;

/// <summary>
/// Owns durable HTTP transfers independently of callers such as workflows. Running transfers are queued
/// after interruption, while explicitly stopped transfers stay stopped. Output files are never deleted.
/// The directory factory must resolve an AppData child after the application has initialized AppData.
/// </summary>
public sealed partial class HttpDownloadManager(
    IHttpDownloader downloader,
    Func<string> storeDirectory,
    Func<int>? concurrencyLimit = null,
    Func<string>? appDataDirectory = null,
    Func<HttpDownloadJob, CancellationToken, Task<bool>>? canResumeOnStartup = null) : IHttpDownloadManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StoredJob> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveTransfer> _active = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private string? _directory;
    private Task? _pump;
    private bool _initialized;
    private bool _stopping;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            ObjectDisposedException.ThrowIf(_stopping, this);
            _directory = Path.GetFullPath(storeDirectory());
            Directory.CreateDirectory(_directory);

            RestrictDirectory(_directory);

            var loaded = new List<StoredJob>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var stored = JsonSerializer.Deserialize<StoredJob>(await File.ReadAllTextAsync(path, ct), JsonOptions)
                             ?? throw new InvalidDataException("An HTTP download record could not be read.");
                if (!Guid.TryParseExact(stored.Job.Id, "N", out _) ||
                    Path.GetFileNameWithoutExtension(path) != stored.Job.Id)
                    throw new InvalidDataException("An HTTP download record has an invalid identity.");
                stored = RestoreAppDataPaths(stored);
                ValidateStored(stored);
                if (stored.Job.Status is HttpDownloadStatus.Queued or HttpDownloadStatus.Running or HttpDownloadStatus.Stopping)
                {
                    // A persisted stopping intent must survive a crash during transport cleanup.
                    // The host may also require an owning workflow/source to reauthorize a recovered transfer.
                    var resume = stored.Job.Status != HttpDownloadStatus.Stopping &&
                                 (canResumeOnStartup != null ? await canResumeOnStartup(Snapshot(stored.Job), ct) : stored.Job.WorkflowRunId == null && stored.Job.SourceKind != "workflow");
                    stored.Job = stored.Job with
                    {
                        Status = resume ? HttpDownloadStatus.Queued : HttpDownloadStatus.Stopped,
                        Stage = resume ? "Queued" : "Stopped",
                        SpeedBytesPerSecond = null, UpdatedAt = DateTime.UtcNow
                    };
                    await SaveAsync(stored);
                }
                loaded.Add(stored);
            }
            foreach (var stored in loaded) _jobs.Add(stored.Job.Id, stored);
            _initialized = true;
            _pump = Task.Run(PumpAsync);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<HttpDownloadJob>> ListAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try { return _jobs.Values.Select(x => Snapshot(x.Job)).OrderByDescending(x => x.CreatedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<HttpDownloadJob?> GetAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try { return _jobs.TryGetValue(id, out var stored) ? Snapshot(stored.Job) : null; }
        finally { _gate.Release(); }
    }

    public async Task<HttpDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var stored = _jobs.Values.FirstOrDefault(x => x.IdempotencyKey == key);
            return stored == null ? null : Snapshot(stored.Job);
        }
        finally { _gate.Release(); }
    }

    public async Task<HttpDownloadJob> EnsureQueuedAsync(HttpDownloadRequest request, CancellationToken ct = default)
    {
        request = Validate(request);
        var payloadHash = HashPayload(request);
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfStopping();
            if (request.IdempotencyKey != null)
            {
                var existing = _jobs.Values.FirstOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
                if (existing != null)
                {
                    if (existing.PayloadHash != payloadHash || !SamePath(existing.Job.OutputDirectory, request.OutputDirectory) ||
                        existing.Job.SourceKind != request.SourceKind || existing.Job.SourceId != request.SourceId ||
                        existing.Job.WorkflowRunId != request.WorkflowRunId ||
                        existing.Job.WorkflowDefinitionId != request.WorkflowDefinitionId ||
                        existing.Job.DownloadTaskId != request.DownloadTaskId ||
                        existing.Job.DownloadResultId != request.DownloadResultId)
                        throw new InvalidOperationException("The HTTP request key is already used for different content, ownership or a different output directory.");
                    if (existing.Job.Status is HttpDownloadStatus.Stopped or HttpDownloadStatus.Failed or HttpDownloadStatus.Completed)
                    {
                        var updated = existing with
                        {
                            Timeout = request.Timeout, ParallelConnections = request.ParallelConnections,
                            MaxRetries = request.MaxRetries, MaximumBytesPerSecond = request.MaximumBytesPerSecond,
                            Job = existing.Job with { UpdatedAt = DateTime.UtcNow }
                        };
                        await SaveAsync(updated);
                        _jobs[existing.Job.Id] = existing = updated;
                    }
                    return Snapshot(existing.Job);
                }
            }

            var id = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow;
            var job = new HttpDownloadJob
            {
                Id = id, Name = request.Name ?? request.FileName ?? SuggestedFileName(request.Url), Status = HttpDownloadStatus.Queued,
                OutputDirectory = request.OutputDirectory, CreatedAt = now, UpdatedAt = now,
                SourceKind = request.SourceKind, SourceId = request.SourceId,
                WorkflowRunId = request.WorkflowRunId, WorkflowDefinitionId = request.WorkflowDefinitionId,
                DownloadTaskId = request.DownloadTaskId, DownloadResultId = request.DownloadResultId,
                IsLinked = request.IdempotencyKey != null || request.SourceKind != "manual" || request.SourceId != null ||
                           request.WorkflowRunId != null || request.WorkflowDefinitionId != null || request.DownloadTaskId != null || request.DownloadResultId != null
            };
            var stored = new StoredJob
            {
                Job = job, IdempotencyKey = request.IdempotencyKey, PayloadHash = payloadHash,
                Url = request.Url, RequestedFileName = request.FileName,
                HttpClientName = request.HttpClientName, ParallelConnections = request.ParallelConnections,
                MaxRetries = request.MaxRetries, MaximumBytesPerSecond = request.MaximumBytesPerSecond,
                Timeout = request.Timeout
            };
            await SaveAsync(stored);
            _jobs.Add(id, stored);
            return Snapshot(job);
        }
        finally { _gate.Release(); }
    }

    public async Task<HttpDownloadJob> StopAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        Task? waitFor = null;
        await _gate.WaitAsync(ct);
        try
        {
            var stored = GetRequired(id);
            if (_active.TryGetValue(id, out var active))
            {
                await CommitJobAsync(stored, stored.Job with { Status = HttpDownloadStatus.Stopping, Stage = "Stopping", UpdatedAt = DateTime.UtcNow });
                active.Cancellation.Cancel();
                waitFor = active.Completion.Task;
            }
            else if (stored.Job.Status == HttpDownloadStatus.Queued)
            {
                await CommitJobAsync(stored, stored.Job with { Status = HttpDownloadStatus.Stopped, Stage = "Stopped", UpdatedAt = DateTime.UtcNow });
            }
        }
        finally { _gate.Release(); }
        if (waitFor != null) await waitFor.WaitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public Task<HttpDownloadJob> ResumeAsync(string id, CancellationToken ct = default) => QueueAgainAsync(id, false, ct);

    public Task<HttpDownloadJob> RecheckAsync(string id, CancellationToken ct = default) => QueueAgainAsync(id, true, ct);

    private async Task<HttpDownloadJob> QueueAgainAsync(string id, bool recheck, CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfStopping();
            var stored = GetRequired(id);
            if (stored.Job.Status == HttpDownloadStatus.Stopping)
                throw new InvalidOperationException("Wait for the HTTP download to stop before continuing it.");
            if (stored.Job.Status is HttpDownloadStatus.Stopped or HttpDownloadStatus.Failed ||
                recheck && stored.Job.Status == HttpDownloadStatus.Completed)
            {
                await CommitJobAsync(stored, stored.Job with
                {
                    Status = HttpDownloadStatus.Queued, Stage = "Queued", Error = null,
                    SpeedBytesPerSecond = null, UpdatedAt = DateTime.UtcNow, CompletedAt = null,
                    Files = recheck ? [] : stored.Job.Files, Percentage = recheck ? null : stored.Job.Percentage
                });
            }
            return Snapshot(stored.Job);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            var stored = GetRequired(id);
            if (!stored.Job.CanDelete || stored.IdempotencyKey != null)
                throw new InvalidOperationException("Only finished or stopped unlinked HTTP downloads can be removed.");
            File.Delete(RecordPath(id));
            _jobs.Remove(id);
            var staging = StagingDirectory(stored);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
        finally { _gate.Release(); }
    }

    public async Task<HttpDownloadJob> WaitForCompletionAsync(string id, Func<HttpDownloadJob, Task>? progress = null,
        bool stopOnCancellation = false, CancellationToken ct = default)
    {
        DateTime? updatedAt = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var job = await GetAsync(id, ct) ?? throw new KeyNotFoundException("The HTTP download does not exist.");
                if (progress != null && updatedAt != job.UpdatedAt)
                {
                    await progress(job);
                    updatedAt = job.UpdatedAt;
                }
                if (job.Status == HttpDownloadStatus.Completed) return job;
                if (job.Status is HttpDownloadStatus.Failed or HttpDownloadStatus.Stopped)
                    throw new HttpDownloadJobException(job);
                await Task.Delay(200, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (stopOnCancellation) await StopAsync(id, CancellationToken.None);
            throw;
        }
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        Task[] active;
        Task? pump;
        // A host deadline limits draining, not the request to stop new and active transfers.
        // In particular, an already-expired deadline must not leave writers running unchecked.
        await _gate.WaitAsync();
        try
        {
            _stopping = true;
            _shutdown.Cancel();
            active = _active.Values.Select(x => x.Completion.Task).ToArray();
            pump = _pump;
        }
        finally { _gate.Release(); }
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drain.CancelAfter(TimeSpan.FromSeconds(30));
        try { if (pump != null) await pump.WaitAsync(drain.Token); }
        finally { await Task.WhenAll(active).WaitAsync(drain.Token); }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync();

    private async Task PumpAsync()
    {
        try
        {
            while (true)
            {
                await _gate.WaitAsync(_shutdown.Token);
                try
                {
                    if (_stopping) return;
                    var limit = Math.Clamp(concurrencyLimit?.Invoke() ?? 2, 1, 64);
                    foreach (var stored in _jobs.Values.Where(x => x.Job.Status == HttpDownloadStatus.Queued)
                                 .OrderBy(x => x.Job.CreatedAt).ToArray())
                    {
                        if (_active.Count >= limit) break;
                        try
                        {
                            if (_active.Keys.Any(id => OutputsConflict(stored, _jobs[id])))
                                continue;
                            await CommitJobAsync(stored, stored.Job with
                            { Status = HttpDownloadStatus.Running, Stage = "Starting", UpdatedAt = DateTime.UtcNow });
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            MarkPersistenceFailure(stored);
                            continue;
                        }
                        var active = new ActiveTransfer(CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
                        _active.Add(stored.Job.Id, active);
                        _ = Task.Run(() => RunAsync(stored, active));
                    }
                }
                finally { _gate.Release(); }
                await Task.Delay(200, _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private async Task RunAsync(StoredJob stored, ActiveTransfer active)
    {
        string? file = null;
        Exception? failure = null;
        try { file = await TransferAsync(stored, active.Cancellation.Token); }
        catch (Exception ex) { failure = ex; }
        await _gate.WaitAsync();
        try
        {
            var explicitStop = stored.Job.Status == HttpDownloadStatus.Stopping;
            var now = DateTime.UtcNow;
            HttpDownloadJob next;
            if (file != null)
                next = stored.Job with
                {
                    Status = HttpDownloadStatus.Completed, Stage = "Completed", Percentage = 100,
                    Files = [file], Error = null, SpeedBytesPerSecond = null, UpdatedAt = now, CompletedAt = now
                };
            else if (explicitStop || _shutdown.IsCancellationRequested)
                next = stored.Job with
                {
                    Status = explicitStop ? HttpDownloadStatus.Stopped : HttpDownloadStatus.Queued,
                    Stage = explicitStop ? "Stopped" : "Queued", SpeedBytesPerSecond = null, UpdatedAt = now
                };
            else
                next = stored.Job with
                {
                    Status = HttpDownloadStatus.Failed, Stage = "Failed", Error = SafeError(failure!),
                    SpeedBytesPerSecond = null, UpdatedAt = now
                };
            try { await CommitJobAsync(stored, next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MarkPersistenceFailure(stored); }
        }
        finally
        {
            _active.Remove(stored.Job.Id);
            active.Cancellation.Dispose();
            active.Completion.TrySetResult();
            _gate.Release();
        }
    }

    private void ThrowIfStopping()
    {
        if (_stopping)
            throw new InvalidOperationException("HTTP downloads cannot be queued while the application is shutting down.");
    }

    private StoredJob GetRequired(string id) => _jobs.TryGetValue(id, out var stored)
        ? stored : throw new KeyNotFoundException("The HTTP download does not exist.");

    private Task SaveAsync(StoredJob stored) => WriteAtomicAsync(RecordPath(stored.Job.Id), JsonSerializer.SerializeToUtf8Bytes(PersistAppDataPaths(stored), JsonOptions));
    private async Task CommitJobAsync(StoredJob stored, HttpDownloadJob next)
    {
        await SaveAsync(stored with { Job = next });
        stored.Job = next;
    }

    private static void MarkPersistenceFailure(StoredJob stored) => stored.Job = stored.Job with
    {
        Status = HttpDownloadStatus.Failed, Stage = "Failed", SpeedBytesPerSecond = null,
        Error = "The HTTP download record could not be saved. Check available disk space and AppData permissions before retrying.",
        UpdatedAt = DateTime.UtcNow
    };
    private string RecordPath(string id) => Path.Combine(_directory!, id + ".json");

    private StoredJob PersistAppDataPaths(StoredJob stored)
    {
        if (appDataDirectory == null) return stored;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDataDirectory()));
        var outputRelative = IsWithin(root, stored.Job.OutputDirectory);
        var fileRelative = stored.Job.Files.Select(path => IsWithin(root, path)).ToArray();
        return stored with
        {
            OutputDirectoryIsAppDataRelative = outputRelative,
            FilePathsAreAppDataRelative = fileRelative,
            Job = stored.Job with
            {
                OutputDirectory = outputRelative ? Path.GetRelativePath(root, stored.Job.OutputDirectory) : stored.Job.OutputDirectory,
                Files = stored.Job.Files.Select((path, index) => fileRelative[index] ? Path.GetRelativePath(root, path) : path).ToArray()
            }
        };
    }

    private StoredJob RestoreAppDataPaths(StoredJob stored)
    {
        if (!stored.OutputDirectoryIsAppDataRelative && stored.FilePathsAreAppDataRelative?.Any(x => x) != true) return stored;
        if (appDataDirectory == null) throw new InvalidOperationException("AppData is required to restore this HTTP download.");
        var root = Path.GetFullPath(appDataDirectory());
        string Resolve(string relative)
        {
            if (Path.IsPathFullyQualified(relative)) throw new InvalidDataException("A relative HTTP download path is invalid.");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsWithin(root, full)) throw new InvalidDataException("A relative HTTP download path is outside AppData.");
            return full;
        }
        return stored with
        {
            Job = stored.Job with
            {
                OutputDirectory = stored.OutputDirectoryIsAppDataRelative ? Resolve(stored.Job.OutputDirectory) : stored.Job.OutputDirectory,
                Files = stored.Job.Files.Select((path, index) => stored.FilePathsAreAppDataRelative?.ElementAtOrDefault(index) == true ? Resolve(path) : path).ToArray()
            },
            OutputDirectoryIsAppDataRelative = false, FilePathsAreAppDataRelative = null
        };
    }

    private static bool IsWithin(string root, string path)
    {
        var comparison = PathComparison;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, comparison) || path.StartsWith(prefix, comparison);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool SamePath(string left, string right) => string.Equals(left, right,
        PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool DirectoriesOverlap(string left, string right) => IsWithin(left, right) || IsWithin(right, left);

    private static bool OutputsConflict(StoredJob left, StoredJob right)
    {
        var leftDirectory = CanonicalDirectory(left.Job.OutputDirectory);
        var rightDirectory = CanonicalDirectory(right.Job.OutputDirectory);
        if (!DirectoriesOverlap(leftDirectory, rightDirectory)) return false;
        // A server-selected name is not known until the transport finishes. Reserve its directory;
        // explicit, distinct file names can share a directory and download concurrently.
        if (left.RequestedFileName == null || right.RequestedFileName == null) return true;
        var leftFile = Path.Combine(leftDirectory, left.RequestedFileName);
        var rightFile = Path.Combine(rightDirectory, right.RequestedFileName);
        return SamePath(leftFile, rightFile) || IsWithin(leftFile, rightDirectory) || IsWithin(rightFile, leftDirectory);
    }

    private static string CanonicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current)) current = new DirectoryInfo(current).ResolveLinkTarget(true)?.FullName ?? current;
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static HttpDownloadJob Snapshot(HttpDownloadJob job) => job with { Files = job.Files.ToArray() };

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class ActiveTransfer(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record StoredJob
    {
        public required HttpDownloadJob Job { get; set; }
        public string? IdempotencyKey { get; init; }
        public required string PayloadHash { get; init; }
        public required string Url { get; init; }
        public string? RequestedFileName { get; init; }
        public string? FileName { get; set; }
        public string? HttpClientName { get; set; }
        public int ParallelConnections { get; set; } = 4;
        public int MaxRetries { get; set; } = 3;
        public long MaximumBytesPerSecond { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromHours(4);
        public long? ReadyLength { get; set; }
        public bool Publishing { get; set; }
        public string? ReadyContentHash { get; set; }
        public bool OutputDirectoryIsAppDataRelative { get; init; }
        public bool[]? FilePathsAreAppDataRelative { get; init; }
    }
}
