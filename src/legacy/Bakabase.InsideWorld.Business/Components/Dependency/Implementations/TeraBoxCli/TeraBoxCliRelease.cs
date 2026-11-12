using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.TeraBoxCli;

/// <summary>
/// Fixed vendor CDN packages reviewed 2026-10-27 against terabox-storage/skills install.sh.
/// These SHA-256 values pin the reviewed downloads; upstream does not publish signatures/checksums.
/// The skills repository's MIT license is not a license for the separately distributed CLI binary.
/// CLI source is not published in that repository; upstream describes this integration as Beta.
/// https://github.com/terabox-storage/skills/blob/main/terabox-storage/scripts/install.sh
/// https://github.com/terabox-storage/skills/blob/main/terabox-storage/reference/notes.md
/// </summary>
public static class TeraBoxCliRelease
{
    public const string Version = "1.1.0";
    internal const long MaximumArchiveBytes = 32 * 1024 * 1024;
    internal const long MaximumBinaryBytes = 64 * 1024 * 1024;

    public sealed record Asset(string Os, string Architecture, string Platform, string Sha256)
    {
        public bool IsZip => Os == "windows";
        public string BinaryName => IsZip ? "terabox.exe" : "terabox";
        public string Name => $"terabox-{Version}-{Platform}.{(IsZip ? "zip" : "tar.gz")}";
        public string Url => $"https://s5.teraboxcdn.com/ai-terabox/terabox-cli/{Version}/{Name}";
    }

    public static IReadOnlyList<Asset> Assets { get; } = Array.AsReadOnly(new[]
    {
        new Asset("linux", "x64", "linux-amd64", "0fa2cd305710f4dc7307062bf22f64c3ed1fa3cd2c8a34d436ad28e338510739"),
        new Asset("linux", "arm64", "linux-arm64", "62785b5fd9ff2163e9abe55a99c161a8f67a22af9f0f9021b68183a15a57817b"),
        new Asset("macos", "x64", "darwin-amd64", "1d914c672bf2b18bee6fb64feac1aaf3b2106b97f47f34650934aebb4744f8ec"),
        new Asset("macos", "arm64", "darwin-arm64", "71a3f45bf82e491f81576ca97c3bde99c24e32a05f25951237c787187b8ec91c"),
        new Asset("windows", "x64", "windows-amd64", "2ec62b0525d4353d769e939db054d12777a817b5fcb1d7883beffe72ea3a0d1f")
    });

    public static Asset Select(string os, string architecture) => Assets.SingleOrDefault(a =>
        a.Os == os && a.Architecture == architecture) ??
        throw new PlatformNotSupportedException("TeraBox CLI is not supported on this platform.");

    internal static async Task ExtractVerifiedAsync(string archive, string destination, Asset asset, CancellationToken ct)
    {
        await using var file = File.OpenRead(archive);
        if (file.Length > MaximumArchiveBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, ct))
                .Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TeraBox CLI release checksum mismatch.");
        file.Position = 0;
        Directory.CreateDirectory(destination);
        if (asset.IsZip) await ExtractZipAsync(file, destination, ct);
        else
        {
            await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
            await ExtractTarAsync(gzip, destination, ct);
        }
    }

    internal static async Task ExtractTarAsync(Stream stream, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var reader = new TarReader(stream, leaveOpen: true);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        TarEntry? entry;
        while ((entry = await reader.GetNextEntryAsync(cancellationToken: ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.Name.TrimEnd('/');
            ValidateEntry(name, entry.Length, "terabox", seen);
            if (name == "terabox")
            {
                if (entry.EntryType != TarEntryType.Directory || entry.Length != 0)
                    throw new InvalidDataException("Unexpected TeraBox CLI archive directory.");
                continue;
            }
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
                throw new InvalidDataException("TeraBox CLI archive must contain regular files only.");
            await CopyEntryAsync(entry.DataStream, name, entry.Length, destination, "terabox", ct);
        }
        RequireBinary(seen, "terabox");
    }

    internal static async Task ExtractZipAsync(Stream stream, string destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 3) throw new InvalidDataException("Unexpected TeraBox CLI archive entries.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.TrimEnd('/');
            ValidateEntry(name, entry.Length, "terabox.exe", seen);
            // Unix file type bits are present on Unix-produced ZIPs; zero denotes a Windows entry.
            var type = (entry.ExternalAttributes >> 16) & 0xf000;
            if (name == "terabox")
            {
                if (!entry.FullName.EndsWith('/') || entry.Length != 0 || type is not (0 or 0x4000))
                    throw new InvalidDataException("Unexpected TeraBox CLI archive directory.");
                continue;
            }
            if (type is not (0 or 0x8000) || entry.FullName.EndsWith('/'))
                throw new InvalidDataException("TeraBox CLI archive must contain regular files only.");
            await using var input = entry.Open();
            await CopyEntryAsync(input, name, entry.Length, destination, "terabox.exe", ct);
        }
        RequireBinary(seen, "terabox.exe");
    }

    private static void ValidateEntry(string name, long length, string binary, HashSet<string> seen)
    {
        if (name != "terabox" && name != "terabox/VERSION" && name != "terabox/" + binary || !seen.Add(name) ||
            length < 0 || length > (name.EndsWith("/VERSION", StringComparison.Ordinal) ? 32 : MaximumBinaryBytes))
            throw new InvalidDataException("Unexpected TeraBox CLI archive entry.");
    }

    private static void RequireBinary(HashSet<string> seen, string binary)
    {
        if (!seen.Contains("terabox/" + binary)) throw new InvalidDataException("TeraBox CLI binary is missing.");
    }

    private static async Task CopyEntryAsync(Stream input, string name, long expected, string destination,
        string binary, CancellationToken ct)
    {
        var isBinary = name == "terabox/" + binary;
        // Never use an archive entry as a destination path or extract VERSION/other metadata.
        await using Stream output = isBinary ? new FileStream(Path.Combine(destination, binary), FileMode.CreateNew) : Stream.Null;
        var buffer = new byte[81920]; long count = 0; int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            count += read;
            if (count > expected) throw new InvalidDataException("TeraBox CLI archive entry size mismatch.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (count != expected || isBinary && count == 0)
            throw new InvalidDataException("TeraBox CLI archive entry size mismatch.");
    }
}
