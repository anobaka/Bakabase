using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SharpCompress.Compressors.Xz;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.MegaCmd;

/// <summary>Official Debian 12 repository packages, reviewed 2026-10-27. No package scripts are executed.</summary>
public static class MegaCmdRelease
{
    public const string Version = "2.6.0";
    public const string ReceiptName = "bakabase-release.json";
    public const string ServerFileName = "bakabase-megacmd-server";
    public sealed record Asset(string Architecture, string PackageVersion, string Sha256)
    {
        public string DebianArchitecture => Architecture == "x64" ? "amd64" : "arm64";
        public string Url => $"https://mega.nz/linux/repo/Debian_12/{DebianArchitecture}/megacmd_{PackageVersion}_{DebianArchitecture}.deb";
        public string DirectoryName => $"linux-{Architecture}-{PackageVersion}";
    }
    public static IReadOnlyList<Asset> Assets { get; } = Array.AsReadOnly(new[]
    {
        new Asset("x64", "2.6.0-2.1", "f10bd96dc622a72d91b09ed66ba5a70a6c2ca7df344694b0289cf1685458db14"),
        new Asset("arm64", "2.6.0-1.1", "7fa0cc55f09a95f7b263b5c20e81d91795cbb632578de636159762ef6a6a7a3d")
    });
    public static Asset Select(string architecture) => Assets.SingleOrDefault(a => a.Architecture == architecture)
        ?? throw new PlatformNotSupportedException("No reviewed MEGAcmd package is available for this platform.");

    // These are regular files, not the package's symlinks or scripts. Store libraries directly
    // under their SONAMEs. The managed client cannot autostart the renamed server accidentally.
    internal static IReadOnlyDictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["usr/bin/mega-exec"] = "mega-exec",
        ["usr/bin/mega-cmd-server"] = ServerFileName,
        ["usr/share/doc/megacmd/copyright"] = "copyright",
        ["opt/megacmd/lib/libavcodec.so.61.19.101"] = "lib/libavcodec.so.61",
        ["opt/megacmd/lib/libavformat.so.61.7.100"] = "lib/libavformat.so.61",
        ["opt/megacmd/lib/libavutil.so.59.39.100"] = "lib/libavutil.so.59",
        ["opt/megacmd/lib/libswresample.so.5.3.100"] = "lib/libswresample.so.5",
        ["opt/megacmd/lib/libswscale.so.8.3.100"] = "lib/libswscale.so.8"
    };

    internal sealed record Receipt(string Version, string Architecture, string PackageSha256, Dictionary<string, string> Files);

    internal static async Task ExtractVerifiedAsync(string archive, string destination, Asset asset, CancellationToken ct)
    {
        await using var input = File.OpenRead(archive);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MEGAcmd release checksum mismatch.");
        input.Position = 0;
        var magic = new byte[8]; await input.ReadExactlyAsync(magic, ct);
        if (Encoding.ASCII.GetString(magic) != "!<arch>\n") throw new InvalidDataException("Invalid MEGAcmd Debian archive.");
        var foundPayload = false;
        while (input.Position < input.Length)
        {
            ct.ThrowIfCancellationRequested();
            var header = new byte[60]; await input.ReadExactlyAsync(header, ct);
            var name = Encoding.ASCII.GetString(header, 0, 16).TrimEnd(' ', '/');
            if (header[58] != '`' || header[59] != '\n' ||
                !long.TryParse(Encoding.ASCII.GetString(header, 48, 10).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
                size < 0 || size > 100 * 1024 * 1024 || input.Position + size > input.Length)
                throw new InvalidDataException("Invalid MEGAcmd Debian member.");
            var next = input.Position + size + size % 2;
            if (name == "data.tar.xz")
            {
                if (foundPayload) throw new InvalidDataException("Duplicate MEGAcmd payload.");
                foundPayload = true;
                // The compressed package is bounded to 100 MiB before this method. Isolate its
                // member so the XZ decoder cannot consume subsequent ar metadata as input.
                var data = new byte[checked((int)size)]; await input.ReadExactlyAsync(data, ct);
                using var compressed = new MemoryStream(data, writable: false);
                using var xz = new XZStream(compressed);
                await ExtractTarAsync(xz, destination, ct);
                await xz.CopyToAsync(Stream.Null, ct);
            }
            input.Position = next;
        }
        if (!foundPayload) throw new InvalidDataException("MEGAcmd payload is missing.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Files.Values)
        {
            await using var stream = File.OpenRead(Path.Combine(destination, file));
            hashes[file] = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        }
        await File.WriteAllTextAsync(Path.Combine(destination, ReceiptName), JsonConvert.SerializeObject(
            new Receipt(Version, asset.Architecture, asset.Sha256, hashes)), ct);
    }

    internal static async Task ExtractTarAsync(Stream archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var reader = new TarReader(archive, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
        {
            var name = entry.Name.StartsWith("./", StringComparison.Ordinal) ? entry.Name[2..] : entry.Name;
            if (!Files.TryGetValue(name, out var relative)) continue;
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                !seen.Add(relative) || entry.Length <= 0 || entry.Length > 100 * 1024 * 1024 ||
                (total += entry.Length) > 150 * 1024 * 1024)
                throw new InvalidDataException("Unsafe MEGAcmd release entry.");
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var output = new FileStream(target, FileMode.CreateNew))
                await entry.DataStream!.CopyToAsync(output, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite |
                (relative is "mega-exec" or ServerFileName ? UnixFileMode.UserExecute : 0));
        }
        if (seen.Count != Files.Count) throw new InvalidDataException("MEGAcmd release is incomplete.");
    }

    internal static async Task<bool> VerifyInstallationAsync(string directory, Asset asset, CancellationToken ct)
    {
        var receiptPath = Path.Combine(directory, ReceiptName);
        if (!File.Exists(receiptPath) || new FileInfo(receiptPath).Length > 16384) return false;
        var receipt = JsonConvert.DeserializeObject<Receipt>(await File.ReadAllTextAsync(receiptPath, ct));
        if (receipt?.Version != Version || receipt.Architecture != asset.Architecture ||
            receipt.PackageSha256 != asset.Sha256 || receipt.Files?.Count != Files.Count) return false;
        foreach (var relative in Files.Values)
        {
            var path = Path.Combine(directory, relative);
            if (!receipt.Files.TryGetValue(relative, out var expected) || !File.Exists(path) ||
                new FileInfo(path).LinkTarget != null || new FileInfo(path).Length > 100 * 1024 * 1024) return false;
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).Equals(expected, StringComparison.Ordinal)) return false;
        }
        return true;
    }
}
