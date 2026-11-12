using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>Extracts each verified download in its own attempt and publishes only a usable runtime.</summary>
internal static class CloudPythonRuntimeInstaller
{
    internal static async Task InstallAsync(string archiveFile, string installationDirectory, string workingDirectory,
        Func<string, CancellationToken, Task> validateRuntime, CancellationToken ct)
    {
        var attempt = Path.Combine(workingDirectory, "extract-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(attempt);
        else Directory.CreateDirectory(attempt, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            // Linux bundles case-sensitive terminfo aliases (e.g. 2621A/2621a). Docker bind mounts
            // can be case-insensitive. These optional terminal definitions are not automatically
            // used by standalone Python and are unnecessary for our noninteractive tools.
            // Repack instead of extracting individual entries: TarFile retains its path/link checks.
            var filteredArchive = Path.Combine(attempt, "runtime.tar");
            await using (var archive = File.OpenRead(archiveFile))
            await using (var gzip = new GZipStream(archive, CompressionMode.Decompress))
            await using (var reader = new TarReader(gzip))
            await using (var filtered = File.Create(filteredArchive))
            await using (var writer = new TarWriter(filtered, leaveOpen: false))
            {
                while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
                {
                    // Fixed-width tar strings end at their first NUL. The reviewed upstream
                    // ustar has nonzero padding after that terminator; .NET 9 exposes it in
                    // Name/LinkName. Resolve the original POSIX spelling before PAX conversion.
                    if (entry.Format is TarEntryFormat.V7 or TarEntryFormat.Ustar or TarEntryFormat.Gnu)
                    {
                        entry.Name = NullTerminated(entry.Name);
                        if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                            entry.LinkName = NullTerminated(entry.LinkName);
                    }
                    // Reject traversal before filtering so malformed paths cannot hide inside
                    // the omitted subtree. All remaining extraction checks belong to TarFile.
                    var name = ValidateEntry(entry);
                    if (name.TrimEnd('/') == "python/share/terminfo" ||
                        name.StartsWith("python/share/terminfo/", StringComparison.Ordinal)) continue;
                    // Normalise to PAX: some upstream ustar entries use long names accepted
                    // by the reader but not representable by .NET's strict ustar writer.
                    await writer.WriteEntryAsync(new PaxTarEntry(entry), ct);
                }
            }

            var extracted = Path.Combine(attempt, "extracted");
            Directory.CreateDirectory(extracted);
            await TarFile.ExtractToDirectoryAsync(filteredArchive, extracted, overwriteFiles: false, cancellationToken: ct);
            var candidate = Path.Combine(extracted, "python");
            if (!Directory.Exists(candidate)) throw new InvalidDataException("Python archive layout changed.");
            await validateRuntime(candidate, ct);
            ct.ThrowIfCancellationRequested();

            Directory.CreateDirectory(installationDirectory);
            var destination = Path.Combine(installationDirectory, "python");
            string? previous = null;
            if (Directory.Exists(destination))
            {
                try
                {
                    // Never replace a healthy fixed runtime underneath active downloads.
                    await validateRuntime(destination, ct);
                    return;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    // A failed earlier install can be repaired without requiring manual removal.
                    // Retain the invalid copy for diagnosis, and roll back if publication fails.
                    previous = Path.Combine(installationDirectory, "python-invalid-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(destination, previous);
                }
            }

            try { Directory.Move(candidate, destination); }
            catch
            {
                if (previous != null) Directory.Move(previous, destination);
                throw;
            }
        }
        finally
        {
            // Failed legacy temp/extracted trees and other attempts are deliberately untouched.
            Directory.Delete(attempt, recursive: true);
        }
    }

    private static string NullTerminated(string value)
    {
        var terminator = value.IndexOf('\0');
        return terminator < 0 ? value : value[..terminator];
    }

    private static string ValidateEntry(TarEntry entry)
    {
        static string[] Parts(string path)
        {
            if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0'))
                throw new InvalidDataException("Python archive contains an unsupported absolute path.");
            return path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        }

        var parts = new List<string>();
        foreach (var part in Parts(entry.Name))
        {
            if (part == "..") throw new InvalidDataException("Python archive contains path traversal.");
            if (part != ".") parts.Add(part);
        }
        if (parts.Count == 0 || parts[0] != "python")
            throw new InvalidDataException("Python archive contains an unexpected root.");
        var name = string.Join('/', parts);

        if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
        {
            // Symlinks resolve from their containing directory; tar hardlinks resolve from
            // the archive root. Constrain both to the subtree that will actually be published.
            var target = entry.EntryType == TarEntryType.SymbolicLink ? new List<string>(parts) : [];
            if (entry.EntryType == TarEntryType.SymbolicLink) target.RemoveAt(target.Count - 1);
            foreach (var part in Parts(entry.LinkName))
            {
                if (part == "..")
                {
                    if (target.Count == 0) throw new InvalidDataException("Python archive link escapes its runtime.");
                    target.RemoveAt(target.Count - 1);
                }
                else if (part != ".") target.Add(part);
            }
            if (target.Count == 0 || target[0] != "python")
                throw new InvalidDataException("Python archive link escapes its runtime.");
        }
        return name;
    }
}
