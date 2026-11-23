using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public partial class CompressedFileService
{
    internal sealed record ManualExtractionEntry(string Path, long Size, bool Directory = false);

    /// <summary>Manual extraction retains its source and existing output. A successful tool exit
    /// is only accepted when every declared member exists with its declared length.</summary>
    public Task<TestResult> ExtractForFileExplorerAsync(string entry, string targetDirectory,
        string? password, Func<int, Task>? onProgress, CancellationToken ct) => onProgress == null
        ? ExtractForFileExplorerCoreAsync(entry, targetDirectory, password, null, ct)
        : ArchiveExtractionProgress.RunAsync((output, token) =>
            ExtractForFileExplorerCoreAsync(entry, targetDirectory, password, output, token), onProgress, ct);

    private async Task<TestResult> ExtractForFileExplorerCoreAsync(string entry, string targetDirectory,
        string? password, Action<string>? onProgress, CancellationToken ct)
    {
        entry = Path.GetFullPath(entry);
        targetDirectory = Path.GetFullPath(targetDirectory);
        var source = new FileInfo(entry);
        if (!source.Exists || source.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Archive extraction requires an existing regular source file.");
        var size = source.Length; var modified = source.LastWriteTimeUtc;
        var opening = await ResolveAutomaticOpeningAsync(entry, null, ct);
        var catalog = await ReadManualExtractionCatalogAsync(entry, password, opening, ct);
        ValidateManualExtractionTargets(targetDirectory, catalog, beforeExtraction: true);
        VerifySource();
        var result = await ExtractWithProgress(entry, targetDirectory, password,
            usePasswordSwitch: true, overwriteMode: OverwriteMode.SkipExisting,
            progressOutput: ProgressOutputTarget.StandardError, workingDirectory: Path.GetDirectoryName(entry),
            onStandardOutput: null, onStandardError: onProgress, ct, opening);
        if (result.ExitCode != 0) return result; // Keep partial output; no silent overwrite on a later retry.
        VerifySource();
        ValidateManualExtractionTargets(targetDirectory, catalog, beforeExtraction: false);
        return result;

        void VerifySource()
        {
            source.Refresh();
            if (!source.Exists || source.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                source.Length != size || source.LastWriteTimeUtc != modified)
                throw new InvalidDataException("The archive changed during extraction. The source and output have been retained.");
        }
    }

    internal async Task<long?> TryGetExpandedBytesAsync(string entry, string? password,
        ArchiveOpenOptions? opening, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            opening = await ResolveAutomaticOpeningAsync(entry, opening, ct);
            if (opening?.Mode != null)
            {
                var catalog = await ReadParserCatalogAsync(entry, ct);
                return catalog.Selected(opening.MemberSelector).Sum(member => member.Size);
            }
            if (opening?.ZipFileNameCodePage != null && !opening.UsesZipCompatibilityReader)
            {
                using var zip = new ExplicitZipNameReader(entry, opening);
                return zip.Entries().Sum(member => member.Size);
            }
            var members = await ReadManualExtractionCatalogAsync(entry, password, opening, ct);
            return members.Sum(member => member.Size);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is InvalidDataException or IOException or OverflowException or
                                              InvalidOperationException or UnauthorizedAccessException)
        {
            // Listing is an optional capacity estimate. The normal extraction and its live
            // free-space guard remain authoritative when metadata is incomplete or unsupported.
            ct.ThrowIfCancellationRequested();
            return null;
        }
    }

    private async Task<IReadOnlyList<ManualExtractionEntry>> ReadManualExtractionCatalogAsync(string path,
        string? password, ArchiveOpenOptions? opening, CancellationToken ct)
    {
        if (opening?.UsesZipCompatibilityReader == true)
        {
            // The compatibility reader already validates names, links and directory records.
            var result = await RunZipCompatibilityAsync("list", path, null, password, opening, null, ct);
            ZipPasswordCompatibility.RequireSuccess(result);
            if (result.Entries == null) throw new InvalidDataException("The archive did not provide a verifiable entry list.");
            return result.Entries.Select(e => new ManualExtractionEntry(e.Path, e.Size)).ToArray();
        }
        var executable = await GetSevenZipExecutableAsync(ct) ?? throw CreateDependencyNotInstalledException();
        var stdout = new StringBuilder(); var stderr = new StringBuilder();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        void Append(StringBuilder text, string line)
        {
            if (text.Length + line.Length > 24 * 1024 * 1024)
                throw new InvalidDataException("The archive entry list exceeded the verification limit.");
            text.AppendLine(line);
        }
        var args = new[] {"l", "-slt", "-sccUTF-8", "-bsp0", "-bd", "-spd", opening?.TypeArgument,
            string.IsNullOrEmpty(password) ? null : "-p" + password, "--", path}.OfType<string>();
        var command = Cli.Wrap(executable).WithArguments(args, true).WithValidation(CommandResultValidation.None)
            .WithStandardInputPipe(PipeSource.FromString("\n", Encoding.UTF8))
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line => Append(stdout, line), Encoding.UTF8))
            .WithStandardErrorPipe(PipeTarget.ToDelegate(line => Append(stderr, line), Encoding.UTF8));
        var exit = await command.ExecuteAsync(stop.Token);
        if (exit.ExitCode != 0)
            throw new InvalidDataException("Cannot verify the archive entry list. Check the archive and password before extracting. " +
                                           (stderr.Length > 2000 ? stderr.ToString(0, 2000) : stderr.ToString()));
        return ParseManualExtractionCatalog(stdout.ToString());
    }

    internal static IReadOnlyList<ManualExtractionEntry> ParseManualExtractionCatalog(string output)
    {
        var rows = new List<ManualExtractionEntry>();
        var block = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!members) { if (line == "----------") members = true; continue; }
            if (string.IsNullOrWhiteSpace(line)) { Flush(); continue; }
            var split = line.IndexOf(" = ", StringComparison.Ordinal);
            if (split <= 0 || !block.TryAdd(line[..split], line[(split + 3)..]))
                throw new InvalidDataException("The archive entry list is ambiguous and cannot be verified.");
        }
        Flush();
        if (!members) throw new InvalidDataException("The archive did not provide a verifiable entry list.");
        return rows;

        void Flush()
        {
            if (block.Count == 0) return;
            if (!block.TryGetValue("Path", out var name) || string.IsNullOrWhiteSpace(name) || rows.Count >= 100000)
                throw new InvalidDataException("The archive entry list is incomplete or exceeds the verification limit.");
            var attributes = block.GetValueOrDefault("Attributes", "");
            if (block.ContainsKey("Symbolic Link") || block.ContainsKey("Hard Link") ||
                Regex.IsMatch(attributes, @"(?:^|\s)l[rwxstST-]{9}(?:\s|$)", RegexOptions.CultureInvariant) ||
                block.GetValueOrDefault("Anti") == "+")
                throw new InvalidDataException("Archive links and special entries cannot be extracted through this action.");
            var directory = block.GetValueOrDefault("Folder") == "+" || attributes.StartsWith('D');
            var hasSize = long.TryParse(block.GetValueOrDefault("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size);
            if (!directory && !hasSize || size < 0 || directory && hasSize && size != 0)
                throw new InvalidDataException("The archive entry length cannot be verified.");
            rows.Add(new(name, size, directory));
            block.Clear();
        }
    }

    internal static IReadOnlyList<string> ValidateManualExtractionTargets(string root,
        IReadOnlyList<ManualExtractionEntry> entries, bool beforeExtraction)
    {
        root = Path.GetFullPath(root);
        var names = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        CheckAncestors(root);
        foreach (var entry in entries)
        {
            var relative = entry.Path.Replace('\\', '/').TrimEnd('/');
            var parts = relative.Split('/');
            if (entry.Size < 0 || Path.IsPathRooted(relative) || parts.Any(p => p.Length == 0 || p is "." or ".." ||
                    p.IndexOfAny([':', '\0', '\r', '\n']) >= 0))
                throw new InvalidDataException("The archive contains an unsafe member path.");
            if (!names.TryAdd(relative.Normalize(NormalizationForm.FormC), entry.Directory))
                throw new InvalidDataException("The archive contains colliding member paths.");
            var path = Path.Combine([root, .. parts]);
            CheckAncestors(path);
            if (beforeExtraction)
            {
                if (File.Exists(path) || !entry.Directory && Directory.Exists(path))
                    throw new IOException("Extraction target already exists and was preserved: " + path +
                                          ". Remove unwanted partial output or choose an empty output directory before retrying.");
            }
            else if (entry.Directory ? !Directory.Exists(path) : !File.Exists(path) || new FileInfo(path).Length != entry.Size)
                throw new InvalidDataException("Archive extraction output is incomplete or has an unexpected length: " + path +
                                               ". The source and partial output have been retained.");
            paths.Add(path);
        }
        // A file cannot also be the parent directory of another member.
        foreach (var name in names.Keys)
            for (var split = name.LastIndexOf('/'); split > 0; split = name.LastIndexOf('/', split - 1))
                if (names.TryGetValue(name[..split], out var directory) && !directory)
                    throw new InvalidDataException("The archive contains conflicting file and directory paths.");
        return paths;
    }

    private static void CheckAncestors(string path)
    {
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Extraction paths must not pass through filesystem links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
