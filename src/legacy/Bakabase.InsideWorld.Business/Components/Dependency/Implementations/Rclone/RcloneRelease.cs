using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Rclone;

/// <summary>Reviewed official v1.75.2 release assets, including the release API SHA256 digests (2026-10-20).</summary>
public static class RcloneRelease
{
    public const string Version = "1.75.2";
    public sealed record Asset(string Os, string Architecture, string ArchiveName, string Sha256)
    {
        public string Url => $"https://github.com/rclone/rclone/releases/download/v{Version}/{ArchiveName}";
        public string DirectoryName => ArchiveName[..^4];
    }

    public static IReadOnlyList<Asset> Assets { get; } = Array.AsReadOnly(new[]
    {
        new Asset("linux", "x64", "rclone-v1.75.2-linux-amd64.zip", "349ac8fba6ff65d6247043f1750cdcb518ec5d500ef91463a10d37c0ccdf3702"),
        new Asset("linux", "arm64", "rclone-v1.75.2-linux-arm64.zip", "7e1e8d69654941b7b7df84ee74c5f7fb09cce7d5496947fb7bf16b55ff427d10"),
        new Asset("macos", "x64", "rclone-v1.75.2-osx-amd64.zip", "574e1f31cf1c464e5fb0325cb66fcbdeaf1efa1802bd9b17f13c256fd74b6561"),
        new Asset("macos", "arm64", "rclone-v1.75.2-osx-arm64.zip", "610ab7d48615135dfc60b763858b11a426a0d3d1160c1bb76fb0ae6011b02edd"),
        new Asset("windows", "x64", "rclone-v1.75.2-windows-amd64.zip", "8a9db2bad1dc79a65b3348fd857069598e2aab7cf4d118b77b1df23055aff818"),
        new Asset("windows", "arm64", "rclone-v1.75.2-windows-arm64.zip", "7eb9844c9e5b723b9ef28821891caa17f6d9d1a375c49365cbe04114cec8dcd9")
    });

    public static Asset Select(string os, string architecture) => Assets.SingleOrDefault(a =>
        a.Os == os && a.Architecture == architecture) ?? throw new PlatformNotSupportedException("Rclone is not supported on this platform.");

    internal static async Task ExtractVerifiedAsync(string archive, string directory, Asset asset, CancellationToken ct)
    {
        await using (var stream = File.OpenRead(archive))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rclone release checksum mismatch.");
        }
        Directory.CreateDirectory(directory);
        using var zip = ZipFile.OpenRead(archive);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var binary = asset.Os == "windows" ? "rclone.exe" : "rclone";
        var required = new HashSet<string>(StringComparer.Ordinal) { binary, "README.txt" };
        // Only publish known regular files from the exact versioned release root. Never extract
        // links, archive-controlled parent paths, manuals or arbitrary executable payloads.
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var parts = entry.FullName.Split('/');
            if (entry.FullName.Contains('\\') || parts.Any(p => p is "." or "..") ||
                parts[0] != asset.DirectoryName || (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
                throw new InvalidDataException("Unsafe rclone release archive.");
            if (parts.Length != 2 || !required.Contains(parts[1])) continue;
            if (!seen.Add(parts[1]) || entry.Length > 200 * 1024 * 1024)
                throw new InvalidDataException("Invalid rclone release entry.");
            await using var input = entry.Open();
            await using var output = new FileStream(Path.Combine(directory, parts[1]), FileMode.CreateNew);
            await input.CopyToAsync(output, ct);
        }
        if (!seen.Contains(binary) || !seen.Contains("README.txt"))
            throw new InvalidDataException("Incomplete rclone release archive.");
        if (asset.Os != "windows") File.SetUnixFileMode(Path.Combine(directory, binary),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
