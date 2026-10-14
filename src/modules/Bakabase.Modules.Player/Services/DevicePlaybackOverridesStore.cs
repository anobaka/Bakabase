using System.Collections.Concurrent;
using System.Text.Json;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Services;

/// <summary>Atomic per-device choices under effective AppData, shared by its relays.
/// Updating one scope preserves other targets and resources.</summary>
public sealed class DevicePlaybackOverridesStore(IOptions<PlayerModuleOptions> options)
{
    public const string FileName = "device-playback-overrides.json";
    private const int MaxBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private string? ResolvePath()
    {
        var preferencesPath = options.Value.DevicePlaybackPreferencesFile?.Invoke();
        if (string.IsNullOrWhiteSpace(preferencesPath)) return null;
        if (!Path.IsPathFullyQualified(preferencesPath))
            throw new InvalidOperationException("An absolute AppData path is required.");
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preferencesPath))!, FileName);
    }

    public async Task<DevicePlaybackOverride[]> ReadAsync(string target, CancellationToken ct = default)
    {
        DevicePlaybackOverride.ValidateTarget(target);
        return (await ReadAllAsync(ResolvePath(), ct)).Where(r => r.Target == target).ToArray();
    }

    public async Task<DevicePlaybackOverrideChoice?> ResolveAsync(string target, int resourceId, string kind,
        CancellationToken ct = default)
    {
        var rules = await ReadAsync(target, ct);
        return rules.FirstOrDefault(r => r.ResourceId == resourceId)?.Choices.GetValueOrDefault(kind) ??
               rules.FirstOrDefault(r => r.ResourceId == null)?.Choices.GetValueOrDefault(kind);
    }

    private static async Task<DevicePlaybackOverride[]> ReadAllAsync(string? path, CancellationToken ct)
    {
        if (path == null || !File.Exists(path)) return [];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaxBytes) throw new InvalidOperationException("The device opening rules are too large.");
        var rules = await JsonSerializer.DeserializeAsync<DevicePlaybackOverride[]>(stream, Json, ct) ??
                    throw new JsonException("The device opening rules are invalid.");
        if (rules.Length > 1024) throw new InvalidOperationException("Too many device opening rules.");
        foreach (var rule in rules)
        {
            if (rule == null) throw new JsonException("The device opening rules contain an invalid rule.");
            rule.Validate();
        }
        if (rules.DistinctBy(r => (r.Target, r.ResourceId)).Count() != rules.Length)
            throw new JsonException("The device opening rules contain duplicate scopes.");
        return rules;
    }

    public async Task SaveAsync(DevicePlaybackOverride rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Validate();
        var path = ResolvePath() ?? throw new InvalidOperationException("The device configuration directory is unavailable.");
        var gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var rules = (await ReadAllAsync(path, ct)).Where(r => r.Target != rule.Target || r.ResourceId != rule.ResourceId).ToList();
            if (rule.Choices.Count > 0) rules.Add(rule);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(rules, Json);
            if (rules.Count > 1024 || bytes.Length > MaxBytes) throw new ArgumentException("Too many device opening rules.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write,
                Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(temporary, streamOptions))
            {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
            temporary = null;
        }
        finally
        {
            try
            {
                if (temporary != null) File.Delete(temporary);
            }
            finally { gate.Release(); }
        }
    }
}
