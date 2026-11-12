using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Service.Components.CloudDownloads.TeraBox;

internal static class TeraBoxStorage
{
    internal static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static void NoLinks(string path)
    {
        for (FileSystemInfo? current = new FileInfo(path); current != null;
             current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            if (current.LinkTarget != null) throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
    }
    internal static void Directory(string path)
    {
        NoLinks(path); System.IO.Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0x1c0);
    }
    // Payloads inherit the user's selected filesystem permissions. In particular,
    // SMB/NAS destinations need not support Unix chmod; they contain no OAuth data.
    internal static void PayloadDirectory(string path)
    { NoLinks(path); System.IO.Directory.CreateDirectory(path); }
    internal static void SecureFiles(string directory)
    {
        if (!System.IO.Directory.Exists(directory)) return;
        foreach (var path in System.IO.Directory.EnumerateFiles(directory))
        { NoLinks(path); if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, (UnixFileMode)0x180); }
    }
    internal static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        NoLinks(path); if (!File.Exists(path)) return default;
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new TeraBoxException("TERABOX_STATE_INVALID");
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, ct), Json); }
        catch (JsonException) { throw new TeraBoxException("TERABOX_STATE_INVALID"); }
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
    internal static string Segment(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 255 || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(c => c < 32 || "/\\\0<>:\"|?*".Contains(c)) || stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
            throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
        return name;
    }
    internal static string Within(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative != string.Join('/', relative.Split('/').Select(Segment)))
            throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new TeraBoxException("TERABOX_LOCAL_PATH_INVALID");
        NoLinks(path); return path;
    }
}

internal static class TeraBoxJson
{
    internal static string? Id(JsonElement data, string key)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number) && number > 0)
            return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out number) && number > 0
            ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }
    internal static string? Text(JsonElement data, string key) => data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static bool True(JsonElement data, string key) => data.ValueKind == JsonValueKind.Object &&
        data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    internal static long? Number(JsonElement data, string key)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(key, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0) return number;
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number) && number >= 0 ? number : null;
    }
}
