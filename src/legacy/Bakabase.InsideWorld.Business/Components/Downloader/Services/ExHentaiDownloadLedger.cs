using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Services;

/// <summary>Durable reservations and byte-preserving page outputs, independent of task checkpoints.</summary>
public sealed class ExHentaiDownloadLedger(Func<string> appData)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public sealed record ImageEntry(string Path, bool IsOriginal, bool OriginalUnavailable = false);
    private sealed class State
    {
        public long ReservedGp { get; set; }
        public Dictionary<string, ImageEntry> Images { get; set; } = new();
    }

    private string GetPath(int taskId)
    {
        if (taskId <= 0) throw new ArgumentOutOfRangeException(nameof(taskId));
        return Path.Combine(appData(), "downloader", "exhentai-state", taskId + ".json");
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string ImageKey(string sourceKey, string pageUrl) => Hash(sourceKey) + ":" + Hash(pageUrl);

    private static async Task<State> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return new State();
        var state = JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(path, ct));
        if (state == null || state.ReservedGp < 0 || state.Images == null)
            throw new InvalidDataException("The ExHentai download ledger is invalid. Paid requests are blocked until it is repaired.");
        return state;
    }

    private static async Task WriteAsync(string path, State state, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task ReserveGpAsync(int taskId, long amount, long maximum, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var path = GetPath(taskId);
            var state = await ReadAsync(path, ct);
            if (amount <= 0 || maximum < 0 || state.ReservedGp > maximum || amount > maximum - state.ReservedGp)
                throw new ExHentaiOriginalImageSafetyException($"Original-image download stopped: the task has reserved {state.ReservedGp:N0} GP of its {maximum:N0} GP budget; the next request requires {amount:N0} GP. Increase the task budget to continue. No paid original-image request was sent.");
            state.ReservedGp += amount;
            // Persist BEFORE requesting fullimg. A timeout/cancel may happen after the site
            // charged the account, so neither a retry nor an app restart refunds this reservation.
            await WriteAsync(path, state, ct);
        }
        finally { Gate.Release(); }
    }

    public async Task<ImageEntry?> GetImageAsync(int taskId, string sourceKey, string pageUrl, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var state = await ReadAsync(GetPath(taskId), ct);
            return state.Images.GetValueOrDefault(ImageKey(sourceKey, pageUrl));
        }
        finally { Gate.Release(); }
    }

    /// <summary>Actual task-owned images, including partial batches without a completed result.</summary>
    public async Task<IReadOnlyCollection<string>> GetImagePathsAsync(int taskId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var state = await ReadAsync(GetPath(taskId), ct);
            return new HashSet<string>(state.Images.Values.Select(x => x.Path), StringComparer.Ordinal);
        }
        finally { Gate.Release(); }
    }

    public async Task RecordImageAsync(int taskId, string sourceKey, string pageUrl, string path, bool isOriginal,
        CancellationToken ct, bool originalUnavailable = false)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var statePath = GetPath(taskId);
            var state = await ReadAsync(statePath, ct);
            state.Images[ImageKey(sourceKey, pageUrl)] = new ImageEntry(Path.GetFullPath(path), isOriginal, originalUnavailable);
            await WriteAsync(statePath, state, ct);
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> HasPreferredImageResultAsync(int taskId, string sourceKey,
        IReadOnlyCollection<string> files, CancellationToken ct)
    {
        if (files.Count == 0) return false;
        await Gate.WaitAsync(ct);
        try
        {
            var state = await ReadAsync(GetPath(taskId), ct);
            var prefix = Hash(sourceKey) + ":";
            var preferred = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (key, entry) in state.Images)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && (entry.IsOriginal || entry.OriginalUnavailable))
                    preferred.Add(entry.Path);
            foreach (var file in files)
                if (!preferred.Contains(Path.GetFullPath(file))) return false;
            return true;
        }
        finally { Gate.Release(); }
    }
}
