using System.Collections.Concurrent;
using System.Text.Json;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Services;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Player.Services;

/// <summary>A small device-local JSON file. Reads always see the current file, so
/// switching between this installation's own server and any relay shares choices.</summary>
public sealed class DevicePlaybackPreferencesStore(IOptions<PlayerModuleOptions> options)
    : IDevicePlaybackPreferencesStore
{
    public const string FileName = "device-playback.json";
    private const int MaximumFileSize = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false
    };

    // Separate relay containers may have separate stores over the same file. A
    // path-scoped gate protects their writes; the atomic rename protects readers.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public async Task<DevicePlaybackPreferences> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            var path = ResolvePath();
            if (path == null || !File.Exists(path)) return DevicePlaybackPreferences.CreateDefault();

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaximumFileSize) return DevicePlaybackPreferences.CreateDefault();

            var preferences = await JsonSerializer.DeserializeAsync<DevicePlaybackPreferences>(stream, Json, ct);
            if (preferences == null) return DevicePlaybackPreferences.CreateDefault();
            preferences.Validate();
            return preferences;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Keep an unreadable/corrupt file untouched. A later explicit user save
            // can replace it, but merely opening a player must not destroy it.
            return DevicePlaybackPreferences.CreateDefault();
        }
    }

    public async Task WriteAsync(DevicePlaybackPreferences preferences, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        var path = ResolvePath() ??
                   throw new InvalidOperationException("The device playback preferences location is unavailable.");
        var gate = WriteGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $".{FileName}.{Guid.NewGuid():N}.tmp");
            var streamOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (!OperatingSystem.IsWindows())
                streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            await using (var stream = new FileStream(temporary, streamOptions))
            {
                await JsonSerializer.SerializeAsync(stream, preferences, Json, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            gate.Release();
        }
    }

    private string? ResolvePath()
    {
        var path = options.Value.DevicePlaybackPreferencesFile?.Invoke();
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("The device playback preferences location must be under effective AppData.");
        return Path.GetFullPath(path);
    }
}
