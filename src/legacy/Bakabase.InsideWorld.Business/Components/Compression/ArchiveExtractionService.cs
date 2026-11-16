using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>What trying a list of passwords against an archive came to.</summary>
/// <param name="Succeeded">Whether one of them opened it.</param>
/// <param name="Password">The one that worked. Null both when nothing worked and when no password was needed.</param>
/// <param name="WrongPasswords">The ones 7-Zip explicitly rejected, as opposed to failing for another reason.</param>
/// <param name="StandardOutput">The successful test's output, which lists what is inside.</param>
public record ArchivePasswordProbe(
    bool Succeeded,
    string? Password,
    IReadOnlyList<string> WrongPasswords,
    string StandardOutput,
    string Message)
{
    public bool PasswordRejected { get; init; }
    // Internal pipeline diagnostic, not an API field or permission to skip validation.
    [System.Text.Json.Serialization.JsonIgnore]
    public string? FailureCode { get; init; }
}

/// <param name="Files">The group's files. The first is the entry point; the rest are its other volumes.</param>
/// <param name="Directory">Where the files are, and where extraction happens.</param>
/// <param name="DecompressToNewFolder">Put the contents in a folder named after the archive.</param>
/// <param name="MoveToParent">Afterwards, lift what came out up one level and drop the folder.</param>
public record ArchiveExtractionRequest(
    IReadOnlyList<string> Files,
    string Directory,
    string? Password = null,
    bool DecompressToNewFolder = true,
    bool OverwriteExistingFiles = false,
    bool DeleteAfterDecompression = false,
    bool MoveToParent = false,
    ArchiveOpenOptions? OpenOptions = null);

public record ArchiveExtractionResult(bool Succeeded, string TargetDirectory, string Message);

/// <summary>
/// Opening archives: working out which password fits, extracting, and the tidying afterwards.
/// <para>
/// This was the body of two controller actions. It is a service now because the acquisition
/// pipeline needs exactly the same three things, and the alternative was a second implementation
/// that would drift from the first.
/// </para>
/// </summary>
public interface IArchiveExtractionService
{
    /// <summary>
    /// Read-only, bounded format discovery for an explicitly selected extraction candidate.
    /// Null means no supported container was recognized; limits must throw rather than claim absence.
    /// This does not test a password, validate contents, or authorize extraction.
    /// </summary>
    Task<string?> ProbeFormatAsync(string entryFile, CancellationToken ct) => Task.FromResult<string?>(null);

    async Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entryFile, CancellationToken ct)
    {
        var format = await ProbeFormatAsync(entryFile, ct);
        return format == null ? null : new(format);
    }

    /// <summary>Only a specific extraction instruction may provide passwords for opening an
    /// encrypted header. This lists metadata under the same discovery budget; full password
    /// and content validation still belongs to ProbePasswordAsync and ExtractAsync.</summary>
    Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entryFile,
        IReadOnlyList<string?> authorizedPasswords, CancellationToken ct) => ProbeAuthorizedFormatAsync(entryFile, ct);

    /// <summary>
    /// Tries the candidates in order until one opens the archive. A null in the list means "try
    /// without a password", which is worth putting first: most archives do not have one.
    /// </summary>
    Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
        Action<string>? onOutput, CancellationToken ct);

    Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
        Action<string>? onOutput, CancellationToken ct, ArchiveOpenOptions? openOptions) => openOptions == null
        ? ProbePasswordAsync(entryFile, candidates, onOutput, ct)
        : throw new NotSupportedException("This archive service does not support the selected opening mode.");

    Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
        Action<int>? onProgress, CancellationToken ct);
}

public class ArchiveExtractionService(CompressedFileService compressedFileService) : IArchiveExtractionService
{
    public Task<string?> ProbeFormatAsync(string entryFile, CancellationToken ct) =>
        compressedFileService.ProbeArchiveFormatAsync(entryFile, ct);

    public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entryFile, CancellationToken ct) =>
        compressedFileService.ProbeAuthorizedArchiveFormatAsync(entryFile, ct);

    public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entryFile,
        IReadOnlyList<string?> authorizedPasswords, CancellationToken ct) =>
        compressedFileService.ProbeAuthorizedArchiveFormatAsync(entryFile, authorizedPasswords, ct);

    public Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile,
        IReadOnlyList<string?> candidates, Action<string>? onOutput, CancellationToken ct) =>
        ProbePasswordAsync(entryFile, candidates, onOutput, ct, null);

    public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile,
        IReadOnlyList<string?> candidates, Action<string>? onOutput, CancellationToken ct, ArchiveOpenOptions? openOptions)
    {
        var wrongPasswords = new HashSet<string>();
        var rejected = false;
        var message = "";

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();

            var result = await compressedFileService.TestCompressedFile(
                entryFile,
                candidate,
                Path.GetDirectoryName(entryFile),
                onStandardOutput: null,
                onStandardError: line =>
                {
                    if (IsPasswordDiagnostic(line))
                    {
                        rejected = true;
                        if (candidate.IsNotEmpty()) wrongPasswords.Add(candidate!);
                    }
                },
                ct, openOptions);

            // 7-Zip with a closed stdin and no supplied password can exit after prompting,
            // without printing a "wrong password" diagnostic. A filename in ordinary output
            // is not evidence; require its own prompt line and an unsuccessful test.
            if (candidate == null && result.ExitCode != 0 && result.StandardOutput.Split('\n')
                    .Any(s => s.Trim().Equals("Enter password:", StringComparison.OrdinalIgnoreCase) ||
                              s.Trim().Equals("Enter password (will not be echoed):", StringComparison.OrdinalIgnoreCase)))
                rejected = true;
            message += $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
            onOutput?.Invoke(message);

            if (result.ExitCode == 0)
            {
                // The password that actually worked. The controller used to report a variable that
                // was never assigned, so a correct password was found and then not shown.
                return new ArchivePasswordProbe(true, candidate, wrongPasswords.ToList(),
                    result.StandardOutput, message);
            }
            if (NonPasswordFailureCode(result.StandardError) is { } failure)
                return new(false, null, wrongPasswords.ToList(), "", message)
                    {PasswordRejected = rejected, FailureCode = failure};
        }

        return new ArchivePasswordProbe(false, null, wrongPasswords.ToList(), "", message) {PasswordRejected = rejected};
    }

    internal static string? NonPasswordFailureCode(string standardError)
    {
        // Recognize tool diagnostics, not filenames in the ordinary member listing. A
        // structural opening failure is not evidence that another password should be tried.
        var lines = standardError.Split('\n').Select(line => line.Trim()).ToArray();
        if (lines.Any(line => Regex.IsMatch(line,
                @"^(?:ERROR:\s*)?(?:Missing volume\s*:|Unexpected end of archive\s*$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) return "MissingVolumes";
        // Encrypted headers may also print the generic cannot-open line. Preserve
        // explicit password evidence and allow the next approved candidate to run.
        if (lines.Any(IsPasswordDiagnostic)) return null;
        if (lines.Any(line => Regex.IsMatch(line,
                @"^(?:ERROR:\s*(?:.+\s+:\s*)?)?(?:Cannot|Can not) open the file as (?:\[(?:7z|zip|rar|rar5)\] )?archive\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))) return "ArchiveInputRequired";
        return null;
    }

    private static bool IsPasswordDiagnostic(string line)
    {
        var text = line.Trim();
        if (!text.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)) return Exact(text);
        text = text[6..].Trim();
        if (Exact(text)) return true;
        // 7-Zip can put the path before or after its diagnostic. Never search
        // arbitrary filenames for password words (e.g. "Wrong password.zip").
        var first = text.IndexOf(" : ", StringComparison.Ordinal);
        var last = text.LastIndexOf(" : ", StringComparison.Ordinal);
        if (last >= 0 && Regex.IsMatch(text[(last + 3)..].Trim(),
                @"^(?:Cannot|Can not) open the file as (?:\[(?:7z|zip|rar|rar5)\] )?archive\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return false;
        return first >= 0 && (Exact(text[..first].Trim()) || Exact(text[(last + 3)..].Trim()));

        static bool Exact(string value) => value.Equals("Wrong password", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Wrong password?", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Cannot open encrypted archive", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Cannot open encrypted archive. Wrong password?", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Data Error in encrypted file. Wrong password?", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
        Action<int>? onProgress, CancellationToken ct)
    {
        if (request.OpenOptions is {Mode: not null, MemberSelector: not null} && request.DeleteAfterDecompression)
            throw new InvalidOperationException("Extracting a selected embedded member cannot delete its source container. Unselected bytes must remain available.");
        if (request.OpenOptions?.Mode != null && request.Files.Count != 1)
            throw new ArchiveFormatProbeIncompleteException("Parser mode requires one exact source file.");
        if (request.OpenOptions?.ZipFileNameCodePage != null && request.Files.Count != 1)
            throw new ArchiveNameEncodingUnsupportedException("Explicit ZIP filename encoding currently requires a single-volume ZIP.");
        if (request.OpenOptions?.ZipPasswordCodePage != null && request.Files.Count != 1)
            throw new ArchivePasswordEncodingException("ArchivePasswordEncodingUnsupported");
        if (request.OpenOptions?.ZipStructure != null && request.Files.Count != 1)
            throw new ArchivePasswordEncodingException("ArchivePasswordEncodingUnsupported");
        var entry = request.Files[0];
        string? confirmedSfxFormat = null;
        var candidates = CompressedFileHelper.DetectSfxMultipartCandidates(request.Files);
        if (candidates.Count > 0)
        {
            var candidate = candidates.Count == 1 ? candidates[0] : null;
            if (candidate is not {IsComplete: true, IsAmbiguous: false} ||
                candidate.EntryFile != entry || candidate.Files.Count != request.Files.Count)
                throw new InvalidDataException("The multipart archive filenames are incomplete or ambiguous.");
            // The request explicitly authorizes these paths. Do not discover, add, or
            // delete neighboring files that an archive tool might otherwise find itself.
            var probe = await compressedFileService.ProbeAuthorizedArchiveFormatAsync(
                Path.IsPathRooted(entry) ? entry : Path.Combine(request.Directory, entry), [request.Password], ct);
            if (!CompressedFileHelper.SfxFormatMatches(candidate, probe?.Extension))
                throw new InvalidDataException("The selected executable was not confirmed as this multipart RAR archive.");
            confirmedSfxFormat = probe!.Extension;
        }
        if (request.DeleteAfterDecompression && request.Files.Count > 1 &&
            CompressedFileHelper.GetCompleteMultipartArchiveGroup(request.Files, confirmedSfxFormat) == null)
            throw new InvalidDataException("Only one explicitly selected, consecutively named archive family can be removed after extraction.");
        var targetDir = request.DecompressToNewFolder
            ? Path.Combine(request.Directory, CompressedFileHelper.GetExtractionDirectoryName(request.Files, confirmedSfxFormat))
            : request.Directory;

        var result = await compressedFileService.ExtractWithProgress(
            entry,
            targetDir,
            request.Password,
            usePasswordSwitch: true,
            overwriteMode: request.OverwriteExistingFiles
                ? CompressedFileService.OverwriteMode.OverwriteAll
                : CompressedFileService.OverwriteMode.None,
            progressOutput: CompressedFileService.ProgressOutputTarget.StandardOutput,
            workingDirectory: request.Directory,
            onStandardOutput: line =>
            {
                if (SevenZipProgressOutput.TryPercentage(line, out var percentage))
                {
                    onProgress?.Invoke(percentage);
                }
            },
            onStandardError: null,
            ct, request.OpenOptions);

        var message = $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";

        if (result.ExitCode != 0)
        {
            return new ArchiveExtractionResult(false, targetDir, message);
        }

        try
        {
            if (request.DeleteAfterDecompression)
            {
                foreach (var file in request.Files)
                {
                    var path = Path.Combine(request.Directory, file);

                    if (File.Exists(path))
                    {
                        FileUtils.Delete(path, true, true);
                    }
                }
            }

            if (request.MoveToParent)
            {
                MoveContentsToParent(targetDir);
            }
        }
        catch (Exception ex)
        {
            return new ArchiveExtractionResult(false, targetDir,
                $"{message}{Environment.NewLine}Post-processing error: {ex.Message}");
        }

        return new ArchiveExtractionResult(true, targetDir, message);
    }

    /// <summary>
    /// Lifts everything out of <paramref name="targetDir"/> into its parent and removes the empty
    /// shell — unless one of the entries is named the same as the folder, in which case moving it up
    /// would collide with the folder it came out of.
    /// </summary>
    private static void MoveContentsToParent(string targetDir)
    {
        if (!Directory.Exists(targetDir)) return;

        var targetDirName = Path.GetFileName(targetDir);
        var canDeleteTargetDir = true;

        foreach (var p in Directory.GetFileSystemEntries(targetDir))
        {
            var entryName = Path.GetFileName(p);
            var dest = Path.Combine(Path.GetDirectoryName(targetDir)!, entryName);

            if (entryName.Equals(targetDirName, StringComparison.OrdinalIgnoreCase))
            {
                canDeleteTargetDir = false;
            }

            if (Directory.Exists(p)) Directory.Move(p, dest);
            else File.Move(p, dest);
        }

        if (canDeleteTargetDir)
        {
            DirectoryUtils.Delete(targetDir, true, true);
        }
    }
}
