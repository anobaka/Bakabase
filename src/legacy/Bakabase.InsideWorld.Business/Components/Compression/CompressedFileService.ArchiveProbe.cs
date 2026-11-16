using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Bounds for expensive format discovery, separate from extraction's expansion limits.</summary>
public static class ArchiveFormatProbeLimits
{
    public const int MaximumCandidates = 4;
    public const int MaximumOutputBytes = 64 * 1024;
    public const int MaximumAuthorizedPasswords = 2;
    public static readonly TimeSpan PerCandidate = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PerPlan = TimeSpan.FromSeconds(15);
}

public sealed class ArchiveFormatProbeIncompleteException(string message,
    string code = ArchiveFormatProbeFailureCodes.Incomplete) : IOException(message)
{
    public string Code { get; } = code;
}

public partial class CompressedFileService
{
    /// <summary>Bounded discovery retries for a source explicitly selected by an extraction instruction.</summary>
    public Task<ArchiveFormatProbeResult?> ProbeAuthorizedArchiveFormatAsync(string entryFile, CancellationToken ct) =>
        ProbeAuthorizedArchiveFormatAsync(entryFile, [], ct);

    public async Task<ArchiveFormatProbeResult?> ProbeAuthorizedArchiveFormatAsync(string entryFile,
        IReadOnlyList<string?> authorizedPasswords, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(ArchiveFormatProbeLimits.PerCandidate);
        var before = new FileInfo(entryFile);
        if (!before.Exists || (before.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The archive probe requires an existing regular file.");
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        var outputBudget = new ProbeOutputBudget();
        var passwords = authorizedPasswords.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal)
            .Take(ArchiveFormatProbeLimits.MaximumAuthorizedPasswords).ToArray();
        async Task<FormatProbeOutcome> Probe(ArchiveOpenOptions? options = null)
        {
            // A single empty line asks 7-Zip to try no password. EOF alone produces exit 255
            // ("Break signaled") before it can identify an encrypted header.
            var result = await ProbeArchiveFormatCoreAsync(entryFile, stop.Token, options, "", outputBudget);
            // A password is supplied only after the tool identifies an encrypted header.
            // All attempts share this candidate's time and output budgets. Never try values
            // from other steps, a global dictionary, or an ordinary discovery scan.
            foreach (var password in passwords)
            {
                if (!result.PasswordRequired) break;
                if (password!.IndexOfAny(['\r', '\n']) >= 0)
                    throw new ArchiveFormatProbeIncompleteException("The supplied archive password cannot be read by the archive tool. Revise the extraction step.", ArchiveFormatProbeFailureCodes.PasswordRequired);
                result = await ProbeArchiveFormatCoreAsync(entryFile, stop.Token, options, password, outputBudget);
            }
            if (result.PasswordRequired)
                throw new ArchiveFormatProbeIncompleteException("The selected archive has an encrypted header. Check the password in this extraction step.", ArchiveFormatProbeFailureCodes.PasswordRequired);
            return result;
        }
        try
        {
            // This path is already authorized by an explicit extraction instruction. Read a
            // bounded amount of ZIP metadata before spending the shared time budget on tool
            // scans. A verified offset needs only one exact, nonrecursive confirmation; a
            // large media prefix must not consume the budget in two unsuccessful scans first.
            // Ordinary discovery calls ProbeArchiveFormatAsync and never requests this hint.
            var structure = await BoundedZipStructureProbe.FindStructureAsync(entryFile, length, stop.Token);
            var after = new FileInfo(entryFile);
            if (!after.Exists || (after.Attributes & FileAttributes.ReparsePoint) != 0 ||
                after.Length != length || after.LastWriteTimeUtc != modified)
                throw new InvalidDataException("The file changed during archive structure discovery.");
            if (structure is {ZipOffset: >= BoundedZipStructureProbe.MaximumPrefixBytes})
            {
                // A metadata-verified ZIP can be opened by seeking to its catalog. Never
                // increase 7-Zip's scan limit or copy the prefixed input to strip its header.
                var opening = ArchiveOpenOptions.VerifiedZipStructure(structure);
                var catalog = await RunZipCompatibilityAsync("list", entryFile, null, null, opening, null, stop.Token);
                ZipPasswordCompatibility.RequireSuccess(catalog);
                return new(".zip", opening);
            }
            if (structure is {ZipOffset: var verifiedPrefix})
            {
                var exactOptions = ArchiveOpenOptions.VerifiedZipPrefix(verifiedPrefix);
                var guided = await Probe(exactOptions);
                return guided.Extension == null ? null : new(guided.Extension, exactOptions);
            }
            var first = await Probe();
            if (first.Extension != null) return new(first.Extension);
            // The default tool scan already covers smaller files. Never turn ordinary media
            // discovery into an unbounded search, or retry a timeout/tool/output-limit failure.
            if (!first.Unrecognized || length <= 8 * 1024 * 1024) return null;
            after.Refresh();
            if (!after.Exists || (after.Attributes & FileAttributes.ReparsePoint) != 0 ||
                after.Length != length || after.LastWriteTimeUtc != modified)
                throw new InvalidDataException("The file changed during archive format discovery.");
            var options = ArchiveOpenOptions.BoundedPrefixFor(entryFile);
            var retry = await Probe(options);
            // A known suffix can be wrong, while wildcard opening can also miss a
            // correctly named prefixed archive. Preserve the typed attempt, then broaden
            // only an explicit unrecognized result within this same time/output budget.
            if (retry.Unrecognized && options != ArchiveOpenOptions.BoundedPrefix)
            {
                after.Refresh();
                if (!after.Exists || (after.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    after.Length != length || after.LastWriteTimeUtc != modified)
                    throw new InvalidDataException("The file changed during archive format discovery.");
                options = ArchiveOpenOptions.BoundedPrefix;
                retry = await Probe(options);
            }
            return retry.Extension == null ? null : new(retry.Extension, options);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException("Archive format discovery exceeded its time limit. Revise the plan.", ArchiveFormatProbeFailureCodes.TimedOut);
        }
    }

    /// <summary>
    /// Ask the installed archive tool to recognize a container, including a prepended stub.
    /// Uses technical listing, never archive testing (which expands data), extraction or execution.
    /// </summary>
    public async Task<string?> ProbeArchiveFormatAsync(string entryFile, CancellationToken ct,
        ArchiveOpenOptions? openOptions = null)
    {
        var result = await ProbeArchiveFormatCoreAsync(entryFile, ct, openOptions);
        if (result.PasswordRequired)
            throw new ArchiveFormatProbeIncompleteException("The archive tool could not read the encrypted header without an authorized extraction password.", ArchiveFormatProbeFailureCodes.PasswordRequired);
        return result.Extension;
    }

    private sealed record FormatProbeOutcome(string? Extension, bool Unrecognized = false, bool PasswordRequired = false);
    private sealed class ProbeOutputBudget { public int Bytes; }

    private async Task<FormatProbeOutcome> ProbeArchiveFormatCoreAsync(string entryFile, CancellationToken ct,
        ArchiveOpenOptions? openOptions = null, string? password = null, ProbeOutputBudget? outputBudget = null)
    {
        ct.ThrowIfCancellationRequested();
        var executable = await GetSevenZipExecutableAsync(ct) ?? throw CreateDependencyNotInstalledException();
        entryFile = Path.GetFullPath(entryFile);
        var before = new FileInfo(entryFile);
        if (!before.Exists || (before.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The archive probe requires an existing regular file.");
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(ArchiveFormatProbeLimits.PerCandidate);
        using var output = new MemoryStream();
        using var errorOutput = new MemoryStream();
        outputBudget ??= new();
        var exceeded = 0;
        bool Append(MemoryStream target, ReadOnlySpan<byte> bytes)
        {
            if (Interlocked.Add(ref outputBudget.Bytes, bytes.Length) > ArchiveFormatProbeLimits.MaximumOutputBytes)
            {
                Interlocked.Exchange(ref exceeded, 1);
                stop.Cancel();
                return false;
            }
            target.Write(bytes);
            return true;
        }
        PipeTarget Capture(MemoryStream target, bool archiveHeaderOnly = false) => PipeTarget.Create(async (stream, token) =>
        {
            var buffer = new byte[4096];
            var headerComplete = false;
            var lineStart = 0;
            while (true)
            {
                var count = await stream.ReadAsync(buffer, token);
                if (count == 0) return;
                if (!archiveHeaderOnly)
                {
                    if (!Append(target, buffer.AsSpan(0, count))) return;
                    continue;
                }
                // Technical listing prints every member after this delimiter. Drain those bytes
                // without retaining them; a large file count is not a large format header.
                // Continue waiting for the real exit code and enforce the original time budget.
                if (headerComplete) continue;
                var segmentStart = 0;
                for (var index = 0; index < count; index++)
                {
                    if (buffer[index] != (byte)'\n') continue;
                    if (!Append(target, buffer.AsSpan(segmentStart, index - segmentStart + 1))) return;
                    var end = (int)target.Length - 1;
                    var captured = target.GetBuffer();
                    if (end > lineStart && captured[end - 1] == (byte)'\r') end--;
                    if (captured.AsSpan(lineStart, end - lineStart).SequenceEqual("----------"u8))
                    {
                        headerComplete = true;
                        break;
                    }
                    lineStart = (int)target.Length;
                    segmentStart = index + 1;
                }
                if (!headerComplete && segmentStart < count && !Append(target, buffer.AsSpan(segmentStart, count - segmentStart)))
                    return;
            }
        });
        // -spd and -- keep a manifest path literal (no glob or @listfile expansion). No password
        // or archive output is logged. Only a step-authorized password enters stdin, never
        // argv; closing it after one line prevents interactive prompts or confirmation loops.
        // 7-Zip's technical Type field: https://github.com/ip7z/7zip/blob/main/CPP/7zip/UI/Console/List.cpp
        var arguments = new[] {"l", "-slt", "-sccUTF-8", "-bsp0", "-bd", "-spd", openOptions?.TypeArgument, "--", entryFile}
            .OfType<string>();
        var command = Cli.Wrap(executable)
            .WithArguments(arguments, true)
            .WithValidation(CommandResultValidation.None)
            .WithStandardInputPipe(password == null ? PipeSource.FromStream(Stream.Null) : PipeSource.FromString(password + "\n", Encoding.UTF8))
            .WithStandardOutputPipe(Capture(output, archiveHeaderOnly: true))
            .WithStandardErrorPipe(Capture(errorOutput));
        int exitCode;
        try { exitCode = (await command.ExecuteAsync(stop.Token)).ExitCode; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException(exceeded != 0
                ? "Archive format discovery exceeded its output limit. Select a known archive or revise the plan."
                : "Archive format discovery exceeded its time limit. Select a known archive or revise the plan.",
                exceeded != 0 ? ArchiveFormatProbeFailureCodes.OutputLimit : ArchiveFormatProbeFailureCodes.TimedOut);
        }
        ct.ThrowIfCancellationRequested();
        if (exceeded != 0)
            throw new ArchiveFormatProbeIncompleteException("Archive format discovery exceeded its output limit. Revise the plan.", ArchiveFormatProbeFailureCodes.OutputLimit);
        var after = new FileInfo(entryFile);
        if (!after.Exists || (after.Attributes & FileAttributes.ReparsePoint) != 0 ||
            after.Length != length || after.LastWriteTimeUtc != modified)
            throw new InvalidDataException("The file changed during archive format discovery.");
        if (exitCode != 0)
        {
            var error = Encoding.UTF8.GetString(errorOutput.ToArray());
            if (exitCode == 2 && Regex.IsMatch(error,
                    @"(?:Cannot|Can not) open encrypted archive\.\s*Wrong password\??",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return new(null, PasswordRequired: true);
            // A generic cannot-open line can accompany explicit damage or a missing
            // volume. Those diagnostics are not permission for another opening mode.
            if (exitCode == 2 && !error.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(error,
                    @"^(?:ERROR:\s*)?(?:Headers Error|Data Error|CRC Failed|Unexpected end of archive|Missing volume)(?:\s*$|\s*:\s)",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                Regex.IsMatch(error, @"(?:Cannot|Can not) open the file as (?:\[(?:zip|rar|rar5|7z)\] )?archive",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return new(null, Unrecognized: true);
            throw new ArchiveFormatProbeIncompleteException(
                "The archive tool could not complete format discovery. Check the archive or revise the plan.", ArchiveFormatProbeFailureCodes.ToolFailed);
        }
        // Read only the archive header, never a member's name/comment claiming to be a Type field.
        var header = Encoding.UTF8.GetString(output.ToArray()).Split('\n').Select(line => line.TrimEnd('\r'))
            .TakeWhile(line => line != "----------").ToArray();
        var type = header
            .FirstOrDefault(line => line.StartsWith("Type = ", StringComparison.Ordinal))?[7..];
        if (openOptions?.ExpectedZipOffset is { } expected &&
            (!string.Equals(type, "zip", StringComparison.OrdinalIgnoreCase) ||
             !long.TryParse(header.FirstOrDefault(line => line.StartsWith("Offset = ", StringComparison.Ordinal))?[9..],
                 NumberStyles.None, CultureInfo.InvariantCulture, out var offset) || offset != expected))
            throw new ArchiveFormatProbeIncompleteException("The archive tool did not confirm the expected ZIP structure. Revise the extraction step.");
        return new(type?.ToLowerInvariant() switch
        {
            "zip" => ".zip", "7z" => ".7z", "rar" or "rar5" => ".rar",
            "tar" => ".tar", "gzip" => ".gz", "bzip2" => ".bz2", "xz" => ".xz",
            _ => null // An image, document, executable, filesystem or other tool handler is not an archive candidate.
        });
    }
}
