using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability;

public sealed record SourceAvailabilityDecision(string Version, bool Disabled, DateTimeOffset At,
    string BatchId, SourceAvailabilityResult? Evidence);

// Private transport session, never projected as source evidence or an account configuration.
internal sealed record SourceAvailabilitySession(string Credential, DateTimeOffset CreatedAt);

/// <summary>Atomic private snapshots and append-only task-local decisions. Original source records are never edited.</summary>
public sealed class SourceAvailabilityStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _root;
    private readonly SemaphoreSlim _writes = new(1, 1);
    private sealed record CachedObservation(SourceAvailabilityResult? Value, DateTimeOffset RefreshAt);
    private readonly ConcurrentDictionary<string, CachedObservation> _observations = new();
    public SourceAvailabilityStore(AppService app) : this(Path.Combine(app.AppDataDirectory, "source-availability")) { }
    public SourceAvailabilityStore(string root) => _root = Path.GetFullPath(root);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public async Task<SourceAvailabilityResult?> ObservationAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_observations.TryGetValue(key, out var cached) && cached.RefreshAt > DateTimeOffset.UtcNow) return cached.Value;
        // Serialize cache fills/eviction with commits. A slow read must not publish an
        // older snapshot after a concurrent write, including when eviction clears the cache.
        await _writes.WaitAsync(ct);
        try
        {
            if (_observations.TryGetValue(key, out cached) && cached.RefreshAt > DateTimeOffset.UtcNow) return cached.Value;
            var value = await ReadAsync<SourceAvailabilityResult>("observations", key, ct);
            if (_observations.Count > 4096) _observations.Clear();
            _observations[key] = new(value, DateTimeOffset.UtcNow.AddSeconds(30));
            return value;
        }
        finally { _writes.Release(); }
    }
    public async Task SaveObservationAsync(string key, SourceAvailabilityResult value, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            await WriteCoreAsync("observations", key, value, ct);
            _observations[key] = new(value, DateTimeOffset.UtcNow.AddSeconds(30));
        }
        finally { _writes.Release(); }
    }
    internal Task<SourceAvailabilitySession?> SessionAsync(string provider, CancellationToken ct) => ReadAsync<SourceAvailabilitySession>("sessions", provider, ct);
    internal Task SaveSessionAsync(string provider, SourceAvailabilitySession value, CancellationToken ct) => WriteAsync("sessions", provider, value, ct);
    public async Task<SourceAvailabilityDecision?> DecisionAsync(string key, CancellationToken ct) =>
        (await ReadAsync<List<SourceAvailabilityDecision>>("decisions", key, ct))?.LastOrDefault();
    public async Task<bool> DecideAsync(string key, string? expectedVersion, SourceAvailabilityDecision decision, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try
        {
            var history = await ReadAsync<List<SourceAvailabilityDecision>>("decisions", key, ct) ?? [];
            if (history.LastOrDefault()?.Version != expectedVersion) return false;
            history.Add(decision);
            await WriteCoreAsync("decisions", key, history, ct);
            return true;
        }
        finally { _writes.Release(); }
    }
    internal Task<SourceAvailabilityBatch?> BatchAsync(string id, CancellationToken ct) => ReadAsync<SourceAvailabilityBatch>("batches", id, ct);
    internal Task SaveBatchAsync(SourceAvailabilityBatch batch, CancellationToken ct) => WriteAsync("batches", batch.BatchId, batch, ct);
    private async Task<T?> ReadAsync<T>(string category, string key, CancellationToken ct)
    {
        var path = PathFor(category, key);
        RejectLinks(path);
        if (!File.Exists(path)) return default;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<T>(input, Json, ct);
    }
    private async Task WriteAsync<T>(string category, string key, T value, CancellationToken ct)
    {
        await _writes.WaitAsync(ct);
        try { await WriteCoreAsync(category, key, value, ct); }
        finally { _writes.Release(); }
    }
    private async Task WriteCoreAsync<T>(string category, string key, T value, CancellationToken ct)
    {
        var path = PathFor(category, key);
        foreach (var dir in new[] {_root, Path.GetDirectoryName(path)!})
        {
            RejectLinks(dir); Directory.CreateDirectory(dir);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        RejectLinks(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(file, value, Json, ct);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private string PathFor(string category, string key) => Path.Combine(_root, category, Hash(key) + ".json");
    private static void RejectLinks(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.LinkTarget != null) throw new IOException("Source availability storage cannot contain symbolic links.");
    }
}
