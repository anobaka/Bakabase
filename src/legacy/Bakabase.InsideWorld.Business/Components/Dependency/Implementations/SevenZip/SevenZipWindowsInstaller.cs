using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives.SevenZip;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;

/// <summary>Reads the official installer as an archive; never executes setup or changes the registry.</summary>
internal static class SevenZipWindowsInstaller
{
    private static readonly Dictionary<string, string> PayloadNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["7z.exe"] = "7z.exe", ["7z.dll"] = "7z.dll", ["License.txt"] = "License.txt", ["readme.txt"] = "readme.txt"
    };

    internal static async Task InstallAsync(string archivePath, string destination, ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(SevenZipWindowsInstaller));
        destination = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.install-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.previous-{Guid.NewGuid():N}");
        var installed = false;
        try
        {
            Directory.CreateDirectory(staging);
            await ExtractPayloadAsync(archivePath, staging, RuntimeInformation.OSArchitecture, ct);
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probe.CancelAfter(TimeSpan.FromSeconds(15));
            if (await new SevenZipDiscoverer(loggerFactory).TryDiscoverAt(staging, probe.Token) == null)
                throw new InvalidDataException("The extracted 7-Zip console could not run. The existing installation was kept.");

            ct.ThrowIfCancellationRequested();
            var hadInstallation = Directory.Exists(destination);
            if (hadInstallation) Directory.Move(destination, backup);
            try { Directory.Move(staging, destination); installed = true; }
            catch
            {
                if (hadInstallation) Directory.Move(backup, destination);
                throw;
            }
        }
        finally
        {
            TryDelete(staging, logger);
            if (installed) TryDelete(backup, logger);
        }
    }

    internal static async Task ExtractPayloadAsync(string archivePath, string destination, Architecture architecture,
        CancellationToken ct)
    {
        // PE code also contains the signature as a literal. Like upstream 7zipInstall.c,
        // require a valid start-header CRC and bounded next header, not just those six bytes.
        if (new FileInfo(archivePath).Length > 64 * 1024 * 1024)
            throw new InvalidDataException("The official 7-Zip installer is unexpectedly large.");
        var bytes = await File.ReadAllBytesAsync(archivePath, ct);
        var offset = FindPayloadOffset(bytes);
        using var payload = new MemoryStream(bytes, offset, bytes.Length - offset, writable: false);
        using var archive = SevenZipArchive.Open(payload);
        using var reader = archive.ExtractAllEntries();
        var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            var name = ValidateEntryName(entry.Key, entry.LinkTarget);
            if (entry.IsEncrypted) throw new InvalidDataException("The 7-Zip installer payload is encrypted.");
            if (entry.IsDirectory || !PayloadNames.TryGetValue(name, out var filename)) continue;
            if (!extracted.Add(filename)) throw new InvalidDataException("The 7-Zip installer contains duplicate runtime files.");
            await using var output = new FileStream(Path.Combine(destination, filename), FileMode.CreateNew, FileAccess.Write);
            using var input = reader.OpenEntryStream();
            await input.CopyToAsync(output, ct);
            if (output.Length != entry.Size) throw new InvalidDataException("The 7-Zip installer payload is incomplete.");
        }
        if (!extracted.Contains("7z.exe") || !extracted.Contains("7z.dll") || !extracted.Contains("License.txt"))
            throw new InvalidDataException("The official 7-Zip installer payload layout changed.");
        foreach (var filename in new[] { "7z.exe", "7z.dll" })
        {
            using var file = File.OpenRead(Path.Combine(destination, filename));
            using var pe = new PEReader(file);
            var expected = architecture switch
            {
                Architecture.X64 => Machine.Amd64,
                Architecture.Arm64 => Machine.Arm64,
                Architecture.X86 => Machine.I386,
                _ => throw new PlatformNotSupportedException("Unsupported Windows 7-Zip architecture.")
            };
            if (pe.PEHeaders.CoffHeader.Machine != expected)
                throw new InvalidDataException("The 7-Zip payload architecture does not match this server.");
        }
    }

    internal static int FindPayloadOffset(byte[] bytes)
    {
        var end = Math.Min(bytes.Length - 32, 1 << 22);
        for (var offset = 0; offset <= end; offset++)
        {
            if (bytes[offset] != 0x37 || bytes[offset + 1] != 0x7a || bytes[offset + 2] != 0xbc ||
                bytes[offset + 3] != 0xaf || bytes[offset + 4] != 0x27 || bytes[offset + 5] != 0x1c) continue;
            var crc = uint.MaxValue;
            for (var i = offset + 12; i < offset + 32; i++)
            {
                crc ^= bytes[i];
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
            if (~crc != BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4))) continue;
            var nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset + 12, 8));
            var nextSize = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset + 20, 8));
            var available = (ulong)(bytes.Length - offset - 32);
            if (nextOffset <= available && nextSize <= available - nextOffset) return offset;
        }
        throw new InvalidDataException("The official 7-Zip installer has no valid embedded payload.");
    }

    internal static string ValidateEntryName(string? name, string? linkTarget)
    {
        if (linkTarget != null) throw new InvalidDataException("Links are not supported in the 7-Zip installer payload.");
        name = name?.Replace('\\', '/');
        if (string.IsNullOrEmpty(name) || name.StartsWith('/') || name.Contains(':') || name.Contains('\0'))
            throw new InvalidDataException("The 7-Zip installer contains an unsafe path.");
        foreach (var part in name.Split('/'))
            if (part is "." or "..") throw new InvalidDataException("The 7-Zip installer contains path traversal.");
        return name.TrimEnd('/');
    }

    private static void TryDelete(string directory, ILogger logger)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { logger.LogWarning(error, "Could not remove 7-Zip installation work directory {Directory}", directory); }
    }
}
