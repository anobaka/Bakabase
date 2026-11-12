using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.CloudDownloads.PikPakCli;

internal static class PikPakCliStorage
{
    internal static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static void NoLinks(string path)
    {
        for (FileSystemInfo? part = new FileInfo(path); part != null;
             part = part is FileInfo f ? f.Directory : ((DirectoryInfo)part).Parent)
            if (part.LinkTarget != null) throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
    }
    internal static void Directory(string path)
    {
        NoLinks(path); System.IO.Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
    }
    internal static void SecureFiles(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return;
        NoLinks(directory);
        foreach (var file in System.IO.Directory.EnumerateFiles(directory))
        { NoLinks(file); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, (UnixFileMode)0x180); }
    }
    internal static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        NoLinks(path); if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new PikPakCliException("PIKPAK_STATE_INVALID");
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json); }
        catch (JsonException) { throw new PikPakCliException("PIKPAK_STATE_INVALID"); }
    }
    internal static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        NoLinks(path); Directory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = (UnixFileMode)0x180;
            await using (var stream = new FileStream(temporary, options))
                await JsonSerializer.SerializeAsync(stream, value, Json, ct);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string Within(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative != string.Join('/', relative.Split('/').Select(Segment)))
            throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
        NoLinks(path); return path;
    }
    internal static string Segment(string name)
    {
        var reserved = name.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "/\\\0<>:\"|?*".Contains(c)) || reserved is "CON" or "PRN" or "AUX" or "NUL" ||
            reserved.Length == 4 && (reserved.StartsWith("COM") || reserved.StartsWith("LPT")) && reserved[3] is >= '1' and <= '9')
            throw new PikPakCliException("PIKPAK_LOCAL_PATH_INVALID");
        return name;
    }
    internal static async Task<string> FileHash(string file, CancellationToken ct)
    { NoLinks(file); return (await CloudPayloadHash.ComputeFileAsync(file, ct)).Sha256.ToLowerInvariant(); }
}

internal static class PikPakCliJson
{
    internal static string? Text(JsonElement data, string key) => data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static string Required(JsonElement data, string key) => Text(data, key) is {Length: > 0 and <= 8192} value
        ? value : throw new PikPakCliException("PIKPAK_CLI_RESPONSE_INVALID");
    internal static long? Number(JsonElement data, string key)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) && n >= 0) return n;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out n) && n >= 0 ? n : null;
    }
    internal static bool True(JsonElement data, string key) => data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
}
