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
    string Message);

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
    bool MoveToParent = false);

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
    /// Tries the candidates in order until one opens the archive. A null in the list means "try
    /// without a password", which is worth putting first: most archives do not have one.
    /// </summary>
    Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile, IReadOnlyList<string?> candidates,
        Action<string>? onOutput, CancellationToken ct);

    Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
        Action<int>? onProgress, CancellationToken ct);
}

public class ArchiveExtractionService(CompressedFileService compressedFileService) : IArchiveExtractionService
{
    private static readonly Regex ProgressRegex = new(@"\d+\%", RegexOptions.Compiled);

    public async Task<ArchivePasswordProbe> ProbePasswordAsync(string entryFile,
        IReadOnlyList<string?> candidates, Action<string>? onOutput, CancellationToken ct)
    {
        var wrongPasswords = new HashSet<string>();
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
                    if (line.Contains("Wrong password") && candidate.IsNotEmpty())
                    {
                        wrongPasswords.Add(candidate!);
                    }
                },
                ct);

            message += $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
            onOutput?.Invoke(message);

            if (result.ExitCode == 0)
            {
                // The password that actually worked. The controller used to report a variable that
                // was never assigned, so a correct password was found and then not shown.
                return new ArchivePasswordProbe(true, candidate, wrongPasswords.ToList(),
                    result.StandardOutput, message);
            }
        }

        return new ArchivePasswordProbe(false, null, wrongPasswords.ToList(), "", message);
    }

    public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
        Action<int>? onProgress, CancellationToken ct)
    {
        var entry = request.Files[0];
        var targetDir = request.DecompressToNewFolder
            ? Path.Combine(request.Directory, Path.GetFileNameWithoutExtension(entry))
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
                var match = ProgressRegex.Match(line);

                if (match.Success && int.TryParse(match.Value.TrimEnd('%'), out var percentage))
                {
                    onProgress?.Invoke(percentage);
                }
            },
            onStandardError: null,
            ct);

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
