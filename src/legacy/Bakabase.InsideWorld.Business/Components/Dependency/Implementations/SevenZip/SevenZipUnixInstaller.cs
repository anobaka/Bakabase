using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpCompress.Compressors.Xz;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.SevenZip;

/// <summary>Installs the portable Unix build without depending on a system tar/xz or an existing 7-Zip.</summary>
internal static class SevenZipUnixInstaller
{
    internal static async Task InstallAsync(string archivePath, string destination,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(SevenZipUnixInstaller));
        destination = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(destination)!;
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.install-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.previous-{Guid.NewGuid():N}");
        var installed = false;
        try
        {
            Directory.CreateDirectory(staging);
            // The download can live inside destination/temp: close it before renaming destination.
            await using (var archive = File.OpenRead(archivePath))
            using (var decompressed = new XZStream(archive))
            {
                await ExtractTarAsync(decompressed, staging, ct);
                // Consume the XZ footer too; an incomplete download must not replace a working copy.
                await decompressed.CopyToAsync(Stream.Null, ct);
            }

            using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            var discovered = await new SevenZipDiscoverer(loggerFactory).TryDiscoverAt(staging, probeTimeout.Token);
            if (discovered == null)
                throw new InvalidDataException("The downloaded 7-Zip build cannot run on this system. The existing installation was kept.");

            // Until this point the installed directory (and any system installation) is untouched.
            // Rename whole directories instead of copying over an executable that may still be in use.
            ct.ThrowIfCancellationRequested();
            var hadInstallation = Directory.Exists(destination);
            if (hadInstallation) Directory.Move(destination, backup);
            try
            {
                Directory.Move(staging, destination);
                installed = true;
            }
            catch
            {
                if (hadInstallation) Directory.Move(backup, destination);
                throw;
            }
        }
        finally
        {
            TryDelete(staging, logger);
            // Cleanup trouble must not turn a successful upgrade into a reported failure.
            // A backup whose rollback failed is retained for recovery.
            if (installed) TryDelete(backup, logger);
        }
    }

    internal static async Task ExtractTarAsync(Stream archive, string destination, CancellationToken ct)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var reader = new TarReader(archive, leaveOpen: true);
        while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, entry.Name));
            if (!path.StartsWith(root, StringComparison.Ordinal))
                throw new InvalidDataException("The 7-Zip archive contains a path outside its installation directory.");

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(path);
                    break;
                case TarEntryType.RegularFile:
                case TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (entry.DataStream != null) await entry.DataStream.CopyToAsync(output, ct);
                    }
                    if (!OperatingSystem.IsWindows())
                    {
                        // Keep ordinary executable permissions, never setuid/setgid/sticky bits.
                        File.SetUnixFileMode(path, entry.Mode & (UnixFileMode)0x1FF);
                    }
                    break;
                default:
                    // Official portable builds need no links or special files. Refuse them instead
                    // of allowing a subsequent entry to follow a link outside this private staging area.
                    throw new InvalidDataException($"The 7-Zip archive contains an unsupported entry: {entry.EntryType}.");
            }
        }
    }

    private static void TryDelete(string directory, ILogger logger)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Could not remove 7-Zip installation work directory {Directory}", directory);
        }
    }
}
