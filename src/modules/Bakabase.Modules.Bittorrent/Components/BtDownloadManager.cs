using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Bittorrent.Components;

/// <summary>
/// Owns durable BT transfers independently of callers such as workflows. Running transfers are queued
/// after interruption, while explicitly stopped transfers stay stopped. Output files are never deleted.
/// The directory factory must resolve an AppData child after the application has initialized AppData.
/// </summary>
public sealed class BtDownloadManager(
    ITorrentDownloader downloader,
    Func<string> storeDirectory,
    Func<int>? concurrencyLimit = null,
    Func<bool>? isShuttingDown = null,
    Func<string>? appDataDirectory = null,
    Func<BtDownloadJob, CancellationToken, Task<bool>>? canResumeOnStartup = null) : IBtDownloadManager, IAsyncDisposable
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
            Directory.CreateDirectory(Path.Combine(_directory, "metadata"));
            RestrictDirectory(_directory);
            RestrictDirectory(Path.Combine(_directory, "metadata"));
            var loaded = new List<StoredJob>();
            foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                var stored = JsonSerializer.Deserialize<StoredJob>(await File.ReadAllTextAsync(path, ct), JsonOptions)
                             ?? throw new InvalidDataException("A BitTorrent download record could not be read.");
                if (!Guid.TryParseExact(stored.Job.Id, "N", out _) ||
                    Path.GetFileNameWithoutExtension(path) != stored.Job.Id)
                    throw new InvalidDataException("A BitTorrent download record has an invalid identity.");
                stored = RestoreAppDataPaths(stored);
                if (stored.Job.Status is BtDownloadStatus.Queued or BtDownloadStatus.Running or BtDownloadStatus.Stopping)
                {
                    // A persisted stopping intent must survive a crash during transport cleanup.
                    // The host may also require an owning workflow/source to reauthorize a recovered transfer.
                    var resume = stored.Job.Status != BtDownloadStatus.Stopping &&
                                 (canResumeOnStartup == null || await canResumeOnStartup(Snapshot(stored.Job), ct));
                    stored.Job = stored.Job with
                    {
                        Status = resume ? BtDownloadStatus.Queued : BtDownloadStatus.Stopped,
                        Stage = resume ? "Queued" : "Stopped",
                        SpeedBytesPerSecond = null, Peers = null, UpdatedAt = DateTime.UtcNow
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

    public async Task<IReadOnlyList<BtDownloadJob>> ListAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try { return _jobs.Values.Select(x => Snapshot(x.Job)).OrderByDescending(x => x.CreatedAt).ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<BtDownloadJob?> GetAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try { return _jobs.TryGetValue(id, out var stored) ? Snapshot(stored.Job) : null; }
        finally { _gate.Release(); }
    }

    public async Task<BtDownloadJob?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default)
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

    public async Task<BtDownloadJob> EnsureQueuedAsync(BtDownloadRequest request, CancellationToken ct = default)
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
                        throw new InvalidOperationException("The BitTorrent request key is already used for different content, ownership or a different output directory.");
                    if (existing.Timeout != request.Timeout &&
                        existing.Job.Status is BtDownloadStatus.Stopped or BtDownloadStatus.Failed or BtDownloadStatus.Completed)
                    {
                        // A caller may edit attempt settings before explicitly resuming or rechecking.
                        // Keep live attempts unchanged, and never alter content, ownership or destination.
                        var updated = existing with
                        {
                            Timeout = request.Timeout,
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
            var job = new BtDownloadJob
            {
                Id = id, Name = request.Name ?? $"BT {id[..8]}", Status = BtDownloadStatus.Queued,
                OutputDirectory = request.OutputDirectory, CreatedAt = now, UpdatedAt = now,
                SourceKind = request.SourceKind, SourceId = request.SourceId,
                WorkflowRunId = request.WorkflowRunId, WorkflowDefinitionId = request.WorkflowDefinitionId,
                DownloadTaskId = request.DownloadTaskId, DownloadResultId = request.DownloadResultId,
                IsLinked = request.IdempotencyKey != null || request.SourceKind != "manual" || request.SourceId != null ||
                           request.WorkflowRunId != null || request.DownloadTaskId != null || request.DownloadResultId != null
            };
            var stored = new StoredJob
            {
                Job = job, IdempotencyKey = request.IdempotencyKey, PayloadHash = payloadHash,
                MagnetUri = request.MagnetUri, TorrentUrl = request.TorrentUrl, Timeout = request.Timeout,
                MetadataFile = request.Metadata == null ? null : $"metadata/{id}.torrent"
            };
            if (stored.MetadataFile != null)
                await WriteAtomicAsync(ResolveMetadata(stored.MetadataFile), request.Metadata!);
            await SaveAsync(stored);
            _jobs.Add(id, stored);
            return Snapshot(job);
        }
        finally { _gate.Release(); }
    }

    public async Task<BtDownloadJob> StopAsync(string id, CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        Task? waitFor = null;
        await _gate.WaitAsync(ct);
        try
        {
            var stored = GetRequired(id);
            if (_active.TryGetValue(id, out var active))
            {
                await CommitJobAsync(stored, stored.Job with { Status = BtDownloadStatus.Stopping, Stage = "Stopping", UpdatedAt = DateTime.UtcNow });
                active.Cancellation.Cancel();
                waitFor = active.Completion.Task;
            }
            else if (stored.Job.Status == BtDownloadStatus.Queued)
            {
                await CommitJobAsync(stored, stored.Job with { Status = BtDownloadStatus.Stopped, Stage = "Stopped", UpdatedAt = DateTime.UtcNow });
            }
        }
        finally { _gate.Release(); }
        if (waitFor != null) await waitFor.WaitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public Task<BtDownloadJob> ResumeAsync(string id, CancellationToken ct = default) => QueueAgainAsync(id, false, ct);

    public Task<BtDownloadJob> RecheckAsync(string id, CancellationToken ct = default) => QueueAgainAsync(id, true, ct);

    private async Task<BtDownloadJob> QueueAgainAsync(string id, bool recheck, CancellationToken ct)
    {
        await InitializeAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            ThrowIfStopping();
            var stored = GetRequired(id);
            if (stored.Job.Status == BtDownloadStatus.Stopping)
                throw new InvalidOperationException("Wait for the BitTorrent download to stop before continuing it.");
            if (stored.Job.Status is BtDownloadStatus.Stopped or BtDownloadStatus.Failed ||
                recheck && stored.Job.Status == BtDownloadStatus.Completed)
            {
                await CommitJobAsync(stored, stored.Job with
                {
                    Status = BtDownloadStatus.Queued, Stage = "Queued", Error = null,
                    SpeedBytesPerSecond = null, Peers = null, UpdatedAt = DateTime.UtcNow, CompletedAt = null,
                    Percentage = recheck ? 0 : stored.Job.Percentage,
                    CompletedBytes = recheck ? null : stored.Job.CompletedBytes,
                    Files = recheck ? [] : stored.Job.Files
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
                throw new InvalidOperationException("Only finished or stopped unlinked BitTorrent downloads can be removed.");
            File.Delete(RecordPath(id));
            _jobs.Remove(id);
            if (stored.MetadataFile != null) File.Delete(ResolveMetadata(stored.MetadataFile));
        }
        finally { _gate.Release(); }
    }

    public async Task<BtDownloadJob> WaitForCompletionAsync(string id, Func<BtDownloadJob, Task>? progress = null,
        bool stopOnCancellation = false, CancellationToken ct = default)
    {
        DateTime? updatedAt = null;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var job = await GetAsync(id, ct) ?? throw new KeyNotFoundException("The BitTorrent download does not exist.");
                if (progress != null && updatedAt != job.UpdatedAt)
                {
                    await progress(job);
                    updatedAt = job.UpdatedAt;
                }
                if (job.Status == BtDownloadStatus.Completed) return job;
                if (job.Status is BtDownloadStatus.Failed or BtDownloadStatus.Stopped)
                    throw new BtDownloadJobException(job);
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
        try { if (pump != null) await pump.WaitAsync(ct); }
        finally { await Task.WhenAll(active).WaitAsync(ct); }
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
                    if (_stopping || isShuttingDown?.Invoke() == true) return;
                    var limit = Math.Clamp(concurrencyLimit?.Invoke() ?? 2, 1, 64);
                    foreach (var stored in _jobs.Values.Where(x => x.Job.Status == BtDownloadStatus.Queued)
                                 .OrderBy(x => x.Job.CreatedAt).ToArray())
                    {
                        if (_active.Count >= limit) break;
                        try
                        {
                            var destination = CanonicalDirectory(stored.Job.OutputDirectory);
                            if (_active.Keys.Any(id => DirectoriesOverlap(destination, CanonicalDirectory(_jobs[id].Job.OutputDirectory))))
                                continue;
                            await CommitJobAsync(stored, stored.Job with
                            { Status = BtDownloadStatus.Running, Stage = "Starting", UpdatedAt = DateTime.UtcNow });
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
        TorrentDownloadResult? result = null;
        Exception? failure = null;
        try
        {
            var request = new TorrentDownloadRequest
            {
                MagnetUri = stored.MagnetUri, TorrentUrl = stored.TorrentUrl,
                Metadata = stored.MetadataFile == null ? null : await File.ReadAllBytesAsync(ResolveMetadata(stored.MetadataFile), active.Cancellation.Token),
                WorkingDirectory = stored.Job.OutputDirectory, UseWorkingDirectoryAsDestination = true, Timeout = stored.Timeout
            };
            result = await downloader.DownloadAsync(request, async progress =>
            {
                await _gate.WaitAsync(active.Cancellation.Token);
                try
                {
                    if (stored.Job.Status != BtDownloadStatus.Running) return;
                    stored.Job = stored.Job with
                    {
                        Stage = progress.Stage, Percentage = Math.Clamp(progress.Percentage, 0, 100),
                        CompletedBytes = progress.CompletedBytes, TotalBytes = progress.TotalBytes,
                        SpeedBytesPerSecond = progress.SpeedBytesPerSecond, Peers = progress.Peers, UpdatedAt = DateTime.UtcNow
                    };
                    // Progress stays in memory between transitions; resume rechecks local pieces instead of trusting counters.
                }
                finally { _gate.Release(); }
            }, active.Cancellation.Token);
        }
        catch (Exception ex) { failure = ex; }

        await _gate.WaitAsync();
        try
        {
            var explicitStop = stored.Job.Status == BtDownloadStatus.Stopping;
            BtDownloadJob next;
            if (explicitStop || _shutdown.IsCancellationRequested)
                next = stored.Job with
                {
                    Status = explicitStop ? BtDownloadStatus.Stopped : BtDownloadStatus.Queued,
                    Stage = explicitStop ? "Stopped" : "Queued", SpeedBytesPerSecond = null, Peers = null, UpdatedAt = DateTime.UtcNow
                };
            else if (failure != null)
                next = stored.Job with
                {
                    Status = BtDownloadStatus.Failed, Stage = "Failed", Error = SafeError(failure),
                    SpeedBytesPerSecond = null, Peers = null, UpdatedAt = DateTime.UtcNow
                };
            else
                next = stored.Job with
                {
                    Status = BtDownloadStatus.Completed, Stage = "Completed", Percentage = 100,
                    CompletedBytes = stored.Job.TotalBytes ?? stored.Job.CompletedBytes,
                    OutputDirectory = result!.Directory, Files = result.Files.ToArray(),
                    SpeedBytesPerSecond = null, Peers = null, Error = null, UpdatedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow
                };
            try { await CommitJobAsync(stored, next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { MarkPersistenceFailure(stored); }
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
        if (_stopping || isShuttingDown?.Invoke() == true)
            throw new InvalidOperationException("BitTorrent downloads cannot be queued while the application is shutting down.");
    }

    private StoredJob GetRequired(string id) => _jobs.TryGetValue(id, out var stored)
        ? stored : throw new KeyNotFoundException("The BitTorrent download does not exist.");

    private Task SaveAsync(StoredJob stored) => WriteAtomicAsync(RecordPath(stored.Job.Id), JsonSerializer.SerializeToUtf8Bytes(PersistAppDataPaths(stored), JsonOptions));
    private async Task CommitJobAsync(StoredJob stored, BtDownloadJob next)
    {
        await SaveAsync(stored with { Job = next });
        stored.Job = next;
    }

    private static void MarkPersistenceFailure(StoredJob stored) => stored.Job = stored.Job with
    {
        Status = BtDownloadStatus.Failed, Stage = "Failed", SpeedBytesPerSecond = null, Peers = null,
        Error = "The BitTorrent download record could not be saved. Check available disk space and AppData permissions before retrying.",
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
        if (appDataDirectory == null) throw new InvalidOperationException("AppData is required to restore this BitTorrent download.");
        var root = Path.GetFullPath(appDataDirectory());
        string Resolve(string relative)
        {
            if (Path.IsPathFullyQualified(relative)) throw new InvalidDataException("A relative BitTorrent download path is invalid.");
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsWithin(root, full)) throw new InvalidDataException("A relative BitTorrent download path is outside AppData.");
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

    private string ResolveMetadata(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_directory!, relative));
        var root = Path.Combine(_directory!, "metadata") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The cached BitTorrent metadata path is invalid.");
        return path;
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

    private static BtDownloadRequest Validate(BtDownloadRequest request)
    {
        var magnet = string.IsNullOrWhiteSpace(request.MagnetUri) ? null : request.MagnetUri.Trim();
        var url = string.IsNullOrWhiteSpace(request.TorrentUrl) ? null : request.TorrentUrl.Trim();
        var metadata = request.Metadata is { Length: > 0 } ? request.Metadata.ToArray() : null;
        if ((magnet != null ? 1 : 0) + (url != null ? 1 : 0) + (metadata != null ? 1 : 0) != 1)
            throw new ArgumentException("Provide exactly one magnet link, torrent URL or torrent file.");
        if (magnet != null && (!Uri.TryCreate(magnet, UriKind.Absolute, out var magnetUri) || magnetUri.Scheme != "magnet"))
            throw new ArgumentException("Provide a valid magnet link.");
        if (url != null && (!Uri.TryCreate(url, UriKind.Absolute, out var torrentUri) ||
                            torrentUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(torrentUri.UserInfo)))
            throw new ArgumentException("Provide an HTTP or HTTPS torrent URL without embedded credentials.");
        if (metadata is { Length: > 4 * 1024 * 1024 }) throw new ArgumentException("Torrent files must not exceed 4 MiB.");
        if (string.IsNullOrWhiteSpace(request.OutputDirectory) || !Path.IsPathFullyQualified(request.OutputDirectory))
            throw new ArgumentException("Choose an absolute output directory.");
        if (request.Timeout <= TimeSpan.Zero) throw new ArgumentException("The download timeout must be positive.");
        return request with
        {
            MagnetUri = magnet, TorrentUrl = url, Metadata = metadata,
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            OutputDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.OutputDirectory)),
            IdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey
        };
    }

    private static string HashPayload(BtDownloadRequest request) => Convert.ToHexString(SHA256.HashData(
        request.Metadata ?? Encoding.UTF8.GetBytes(request.MagnetUri != null ? "magnet:" + request.MagnetUri : "url:" + request.TorrentUrl)));

    private static bool SamePath(string left, string right) => string.Equals(left, right,
        PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool DirectoriesOverlap(string left, string right) => IsWithin(left, right) || IsWithin(right, left);

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

    private static BtDownloadJob Snapshot(BtDownloadJob job) => job with { Files = job.Files.ToArray() };

    // Exceptions can contain tracker URLs or proxy credentials. Persist only endpoint-free categories.
    private static string SafeError(Exception exception) => exception switch
    {
        TimeoutException => "The BitTorrent download timed out. Retry to continue from the existing files.",
        OperationCanceledException => "The BitTorrent transfer was interrupted. Retry to continue from the existing files.",
        UnauthorizedAccessException => "Access to the BitTorrent download directory was denied.",
        IOException => "The BitTorrent transfer failed while accessing files or the network.",
        ArgumentException or InvalidDataException => "The torrent metadata or download request is invalid.",
        _ => "The BitTorrent transfer failed. Check the torrent and the selected BT proxy, then retry."
    };

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
        public required BtDownloadJob Job { get; set; }
        public string? IdempotencyKey { get; init; }
        public required string PayloadHash { get; init; }
        public string? MagnetUri { get; init; }
        public string? TorrentUrl { get; init; }
        public string? MetadataFile { get; init; }
        public TimeSpan Timeout { get; init; }
        public bool OutputDirectoryIsAppDataRelative { get; init; }
        public bool[]? FilePathsAreAppDataRelative { get; init; }
    }
}
