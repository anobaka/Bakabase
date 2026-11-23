using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Compression;

public partial class CompressedFileService
{
    private static async Task EnsureAutomaticZipNamesAsync(string path, ArchiveOpenOptions? opening, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows() || opening?.ZipFileNameCodePage != null ||
            opening?.UsesZipCompatibilityReader == true || opening?.OpensSevenZipVolumes == true || opening?.Mode != null) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(ArchiveFormatProbeLimits.PerCandidate);
        try { await ZipNameEncodingProbe.EnsureLosslessAsync(opening?.InspectionPath(path) ?? path, opening?.ExpectedZipOffset ?? 0, stop.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException("ZIP filename inspection exceeded its time limit.",
                ArchiveFormatProbeFailureCodes.TimedOut);
        }
    }

    internal static bool HasZipNameDecodingFailure(TestResult result)
    {
        if (result.ExitCode == 0 || !result.StandardOutput.Split('\n')
                .Any(line => line.Trim().Equals("Type = zip", StringComparison.OrdinalIgnoreCase)))
            return false;

        // This is a tool diagnostic, not a guess based on an archive's language or suffix.
        // Unix 7-Zip can read legacy ZIP bytes but fail to create the decoded member path.
        // Keep space/permissions/missing-parent failures with readable names in their own
        // category, and never choose an encoding or retry extraction automatically here.
        return result.StandardError.Split('\n').Any(line =>
        {
            var match = Regex.Match(line.Trim(),
                @"^ERROR:\s*Cannot open output file\s*:\s*errno=2\s*:\s*No such file or directory\s*:\s*(?<path>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success && match.Groups["path"].Value.Contains('\ufffd');
        });
    }
}
