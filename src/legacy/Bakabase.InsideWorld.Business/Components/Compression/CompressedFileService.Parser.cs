using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;

namespace Bakabase.InsideWorld.Business.Components.Compression;

internal sealed record ParserArchiveMember(string Name, long Offset, long Size, string Type);

/// <summary>7-Zip owns container recognition. This validates its bounded segment catalog,
/// not a second archive protocol or a claim that an embedded archive's password was tested.</summary>
internal sealed record ParserArchiveCatalog(long Length, DateTime Modified,
    IReadOnlyList<ParserArchiveMember> Members)
{
    internal const long MaximumSourceBytes = 16L * 1024 * 1024 * 1024;
    internal const int MaximumMembers = 256;
    internal const int MaximumOutputBytes = 256 * 1024;
    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
        {"zip", "7z", "rar", "rar5", "tar", "gzip", "bzip2", "xz"};

    internal static bool SafeMemberName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 255 &&
        value is not ("." or "..") && !value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) &&
        !value.EndsWith('.') && !value.EndsWith(' ') &&
        value.Split('.')[0].ToUpperInvariant() is not ("CON" or "PRN" or "AUX" or "NUL") &&
        !(value.Split('.')[0] is {Length: 4} stem &&
          (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
          stem[3] is >= '0' and <= '9');

    internal IReadOnlyList<ParserArchiveMember> Selected(string? name)
    {
        if (name == null) return Members;
        var selected = Members.Where(m => m.Name == name).ToArray();
        if (selected.Length != 1)
            throw new ArchiveFormatProbeIncompleteException("The exact embedded member was not found. Review the parser listing before continuing.");
        return selected;
    }

    internal static ParserArchiveCatalog Parse(string output, long length, DateTime modified)
    {
        if (length <= 0 || length > MaximumSourceBytes)
            throw new ArchiveFormatProbeIncompleteException("Parser mode input exceeds its supported size limit.", ArchiveFormatProbeFailureCodes.BudgetExceeded);
        var lines = output.Split('\n').Select(s => s.TrimEnd('\r')).ToArray();
        var separator = Array.IndexOf(lines, "----------");
        if (separator < 0 || !lines.Take(separator).Contains("Type = #", StringComparer.Ordinal))
            throw new ArchiveFormatProbeIncompleteException("The archive tool did not return an embedded-container catalog.");
        var members = new List<ParserArchiveMember>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long end = 0;
        void Complete()
        {
            if (fields.Count == 0) return;
            if (members.Count >= MaximumMembers || !fields.TryGetValue("Path", out var name) || !SafeMemberName(name) ||
                !names.Add(name.Normalize(NormalizationForm.FormC)) ||
                !fields.TryGetValue("Offset", out var rawOffset) || !long.TryParse(rawOffset, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
                !fields.TryGetValue("Size", out var rawSize) || !long.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out var size) ||
                size <= 0 || offset != end || offset > length || size > length - offset)
                throw new ArchiveFormatProbeIncompleteException("The parser returned unsafe, overlapping, incomplete or excessive segments. No content was discarded.");
            var index = (members.Count + 1).ToString(CultureInfo.InvariantCulture);
            if (name != index && !name.StartsWith(index + ".", StringComparison.Ordinal))
                throw new ArchiveFormatProbeIncompleteException("The parser member numbering is inconsistent.");
            members.Add(new(name, offset, size, fields.GetValueOrDefault("Type", "")));
            end += size;
            fields.Clear();
        }
        foreach (var line in lines.Skip(separator + 1))
        {
            if (line.Length == 0) { Complete(); continue; }
            var split = line.IndexOf(" = ", StringComparison.Ordinal);
            if (split <= 0 || !fields.TryAdd(line[..split], line[(split + 3)..]))
                throw new ArchiveFormatProbeIncompleteException("The parser catalog contains ambiguous metadata.");
        }
        Complete();
        if (end != length || !members.Any(m => Archives.Contains(m.Type)))
            throw new ArchiveFormatProbeIncompleteException("No complete supported embedded-archive layout was confirmed. Revise the extraction step.");
        return new(length, modified, members);
    }
}

public partial class CompressedFileService
{
    private static FileInfo ParserSource(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Parser mode requires an existing regular file.");
        if (info.Length <= 0 || info.Length > ParserArchiveCatalog.MaximumSourceBytes)
            throw new ArchiveFormatProbeIncompleteException("Parser mode is limited to one file of at most 16 GiB.", ArchiveFormatProbeFailureCodes.BudgetExceeded);
        return info;
    }

    private static void VerifyParserSource(string path, ParserArchiveCatalog catalog)
    {
        var current = ParserSource(path);
        if (current.Length != catalog.Length || current.LastWriteTimeUtc != catalog.Modified)
            throw new InvalidDataException("The parser source changed while it was being read.");
    }

    private async Task<ParserArchiveCatalog> ReadParserCatalogAsync(string path, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var before = ParserSource(path);
        var result = await RunParserCommandAsync("l", path, null, null, null,
            ArchiveFormatProbeLimits.PerCandidate, ct);
        var catalog = ParserArchiveCatalog.Parse(result.StandardOutput, before.Length, before.LastWriteTimeUtc);
        VerifyParserSource(path, catalog);
        return catalog;
    }

    private async Task<TestResult> RunParserCommandAsync(string operation, string path, string? destination,
        string? member, Action<string>? progress, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var executable = await GetSevenZipExecutableAsync(ct) ?? throw CreateDependencyNotInstalledException();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(timeout);
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var captured = 0;
        var limited = false;
        PipeTarget Capture(MemoryStream target, bool report) => PipeTarget.Create(async (stream, token) =>
        {
            var buffer = new byte[4096];
            var decoder = Encoding.UTF8.GetDecoder();
            var characters = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
            var progressOutput = report && progress != null ? new SevenZipProgressOutput(progress) : null;
            while (true)
            {
                var count = await stream.ReadAsync(buffer, token);
                if (count == 0) break;
                if (Interlocked.Add(ref captured, count) > ParserArchiveCatalog.MaximumOutputBytes)
                { limited = true; stop.Cancel(); return; }
                target.Write(buffer, 0, count);
                if (progressOutput != null)
                    progressOutput.Append(characters.AsSpan(0, decoder.GetChars(buffer, 0, count, characters, 0)), token);
            }
            token.ThrowIfCancellationRequested();
            progressOutput?.Complete();
        });
        // No arbitrary switches, wildcard expansion, @list files, exhaustive #:e scan,
        // inherited password or interactive input. # itself can scan beyond :s, so bound
        // source size, wall time and output independently rather than claiming :s is a cap.
        var args = new List<string> {operation, "-t#", "-sccUTF-8", "-spd", "-bb0", operation == "l" ? "-bsp0" : "-bsp1"};
        if (operation == "l") args.Add("-slt");
        if (destination != null) { args.Add("-o" + destination); args.Add("-aos"); }
        args.Add("--"); args.Add(Path.GetFullPath(path));
        if (member != null) args.Add(member);
        var command = Cli.Wrap(executable).WithArguments(args, true).WithValidation(CommandResultValidation.None)
            .WithStandardInputPipe(PipeSource.FromStream(Stream.Null))
            .WithStandardOutputPipe(Capture(output, true)).WithStandardErrorPipe(Capture(error, false));
        int exit;
        try { exit = (await command.ExecuteAsync(stop.Token)).ExitCode; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException(limited ? "The parser output exceeded its limit." : "The parser did not complete within its time limit.",
                limited ? ArchiveFormatProbeFailureCodes.OutputLimit : ArchiveFormatProbeFailureCodes.TimedOut);
        }
        ct.ThrowIfCancellationRequested();
        if (limited || exit != 0)
            throw new ArchiveFormatProbeIncompleteException("The archive tool could not complete the explicit parser operation.",
                limited ? ArchiveFormatProbeFailureCodes.OutputLimit : ArchiveFormatProbeFailureCodes.ToolFailed);
        return new() {ExitCode = exit, StandardOutput = Encoding.UTF8.GetString(output.ToArray()), StandardError = Encoding.UTF8.GetString(error.ToArray())};
    }

    private async Task<TestResult> ProcessParserAsync(string path, string? destination, ArchiveOpenOptions options,
        Action<string>? progress, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var catalog = await ReadParserCatalogAsync(path, ct);
        var selected = catalog.Selected(options.MemberSelector);
        if (destination != null)
        {
            destination = Path.GetFullPath(destination);
            var root = new DirectoryInfo(destination);
            if (root.Exists && root.EnumerateFileSystemInfos().Any())
                throw new InvalidDataException("Parser extraction requires an empty private output directory.");
            for (var parent = root; parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Parser output must not pass through a filesystem link.");
            Directory.CreateDirectory(destination);
        }
        var result = await RunParserCommandAsync(destination == null ? "t" : "x", path, destination,
            options.MemberSelector, progress, TimeSpan.FromMinutes(10), ct);
        VerifyParserSource(path, catalog);
        if (destination != null)
        {
            var actual = Directory.GetFileSystemEntries(destination);
            if (actual.Length != selected.Count || !actual.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(selected.Select(m => m.Name)))
                throw new InvalidDataException("Parser output differs from the exact selected catalog.");
            // Parser extraction only copies ranges; it does not authenticate the inner ZIP.
            // Compare every emitted byte with its confirmed source range before committing.
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            var left = new byte[128 * 1024]; var right = new byte[left.Length];
            foreach (var member in selected)
            {
                var outputPath = Path.Combine(destination, member.Name);
                var info = new FileInfo(outputPath);
                if (!info.Exists || info.Length != member.Size || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Parser output type or size differs from the source segment.");
                await using var output = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, left.Length, true);
                source.Position = member.Offset;
                for (long remaining = member.Size; remaining > 0;)
                {
                    var count = (int)Math.Min(left.Length, remaining);
                    await source.ReadExactlyAsync(left.AsMemory(0, count), ct);
                    await output.ReadExactlyAsync(right.AsMemory(0, count), ct);
                    if (!left.AsSpan(0, count).SequenceEqual(right.AsSpan(0, count)))
                        throw new InvalidDataException("Parser output bytes do not match their original source range.");
                    remaining -= count;
                }
            }
            VerifyParserSource(path, catalog);
        }
        return result;
    }
}
