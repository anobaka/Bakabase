using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// Detects candidates only within the exact inputs already authorized by an extraction plan
/// or the recursive executor. A signature permits probing, not a claim that extraction succeeded.
/// Never discovers siblings, renames originals, executes programs, or unwraps delivery formats.
/// An explicitly selected .exe can only be recognized by the bounded archive-tool probe.
/// </summary>
internal sealed class DetectedArchiveGroup : CompressedFileGroup
{
    public ArchiveOpenOptions? OpenOptions { get; set; }
    public static DetectedArchiveGroup From(CompressedFileGroup group) => new()
        {KeyName = group.KeyName, Extension = group.Extension, Files = group.Files, FileSizes = group.FileSizes};
}

internal static class AuthorizedArchiveDetection
{
    private static readonly HashSet<string> Containers = new(StringComparer.OrdinalIgnoreCase)
        { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".tgz" };
    private static readonly HashSet<string> Deliveries = new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".dotx", ".xlsx", ".xlsm", ".pptx", ".odt", ".ods", ".odp", ".epub", ".jar", ".apk", ".cbz", ".cbr", ".exe" };

    internal static async Task<IReadOnlyList<DetectedArchiveGroup>> DetectAsync(IReadOnlyList<string> exactFiles,
        bool recursive, CancellationToken ct, IArchiveExtractionService? formatProbe = null,
        ArchiveDetectionBudget? budget = null, bool exactExtractionSelection = false,
        IReadOnlyList<string?>? authorizedPasswords = null)
    {
        var files = exactFiles.Distinct(FileProcessingFiles.PathComparer).ToArray();
        foreach (var file in files) { ct.ThrowIfCancellationRequested(); ArtifactManifestFiles.RejectLinks(file); }
        // Keep established multipart ordering and membership. In particular, never interpret a
        // later volume's incidental bytes as another independent archive or adopt nearby files.
        // Filename grouping is only a candidate for SFX volumes. Until the bounded probe
        // confirms their entry format, none of their later volumes is an independent RAR.
        var sfxCandidates = CompressedFileHelper.DetectSfxMultipartCandidates(files);
        var sfxFiles = sfxCandidates.SelectMany(candidate => candidate.Files).ToHashSet(FileProcessingFiles.PathComparer);
        var known = CompressedFileHelper.DetectCompressedFileGroups(files.Where(file => !sfxFiles.Contains(file)).ToArray(), true);
        var grouped = known.SelectMany(g => g.Files).ToHashSet(FileProcessingFiles.PathComparer);
        grouped.UnionWith(sfxFiles);
        var groups = known.Where(g => g.Files.All(f => !Deliveries.Contains(Path.GetExtension(f))) &&
            (!recursive || IsRecursiveContainer(g))).Select(DetectedArchiveGroup.From).ToList();
        if (!recursive && formatProbe != null)
        {
            // A previous explicit rename can give a media-prefixed archive a familiar suffix.
            // Only that bounded mismatch needs discovery; preserve group order/volume membership
            // because existing operation receipts are indexed by the established group order.
            foreach (var group in groups.Where(g => g.Files.Count == 1 &&
                         g.Extension?.ToLowerInvariant() is ".zip" or ".rar" or ".7z"))
            {
                var file = group.Files[0];
                if (new FileInfo(file).Length <= 8 * 1024 * 1024 || !await HasMediaPrefixAsync(file, ct)) continue;
                budget ??= new();
                var format = await budget.ProbeAsync(formatProbe, file, ct, authorizedPasswords);
                if (format == null)
                    throw new ArchiveFormatProbeIncompleteException(
                        "No supported archive was recognized inside the selected media-prefixed file. Revise the extraction step.");
                group.Extension = format.Extension;
                group.OpenOptions = format.OpenOptions;
            }
        }
        var unrecognized = new List<string>();
        if (!recursive && exactExtractionSelection)
        {
            // Even a ZIP signature is insufficient for an executable-looking input: require
            // the installed tool to confirm a real container, rejecting its ordinary PE handler.
            unrecognized.AddRange(files.Where(f => !sfxFiles.Contains(f) && Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)));
            // Document/application suffixes usually describe valid ZIP/RAR delivery formats.
            // With an explicit extraction selection, a contradictory real archive header is
            // evidence of disguise (for example 7z bytes named .apk), not permission to unpack
            // ordinary applications. Executables retain their separate installed-tool probe.
            foreach (var file in files.Where(f => Deliveries.Contains(Path.GetExtension(f)) &&
                         !Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)))
            {
                var signature = await SignatureAsync(file, ct);
                var expected = Path.GetExtension(file).Equals(".cbr", StringComparison.OrdinalIgnoreCase) ? ".rar" : ".zip";
                if (signature == null || signature == expected) continue;
                groups.Add(new DetectedArchiveGroup { KeyName = Path.GetFileNameWithoutExtension(file),
                    Extension = signature, Files = [file], FileSizes = [new FileInfo(file).Length] });
            }
        }
        foreach (var file in files.Where(f => !grouped.Contains(f)))
        {
            ct.ThrowIfCancellationRequested();
            if (Deliveries.Contains(Path.GetExtension(file))) continue;
            var signature = await SignatureAsync(file, ct);
            if (signature == null) { unrecognized.Add(file); continue; }
            groups.Add(new DetectedArchiveGroup { KeyName = Path.GetFileNameWithoutExtension(file),
                Extension = signature, Files = [file], FileSizes = [new FileInfo(file).Length] });
        }
        // Automatic/recursive discovery has no authority to inspect every ordinary delivery
        // with an external tool. An explicit supported extraction selector (or a single input
        // to that explicit step) is the additional authority required for expensive discovery.
        if (!recursive && exactExtractionSelection && formatProbe != null && unrecognized.Count > 0)
        {
            budget ??= new();
            foreach (var file in unrecognized.OrderBy(f => new FileInfo(f).Length)
                         .ThenBy(f => f, FileProcessingFiles.PathComparer))
            {
                ArtifactManifestFiles.RejectLinks(file);
                var format = await budget.ProbeAsync(formatProbe, file, ct, authorizedPasswords);
                ArtifactManifestFiles.RejectLinks(file);
                if (format != null)
                    groups.Add(new DetectedArchiveGroup { KeyName = Path.GetFileNameWithoutExtension(file),
                        Extension = format.Extension, OpenOptions = format.OpenOptions,
                        Files = [file], FileSizes = [new FileInfo(file).Length] });
            }
        }
        if (!recursive && exactExtractionSelection && formatProbe != null)
        {
            budget ??= new();
            foreach (var candidate in sfxCandidates)
            {
                ct.ThrowIfCancellationRequested();
                if (candidate.IsAmbiguous || !candidate.IsComplete)
                    throw new ArchiveFormatProbeIncompleteException(
                        "The selected self-extracting archive has missing or ambiguous volume names. Check the complete authorized volume set before continuing.",
                        candidate.IsAmbiguous ? "ArchiveInputRequired" : "MissingVolumes");
                ValidateCurrentSfxFamily(candidate, ct);
                var format = await budget.ProbeAsync(formatProbe, candidate.EntryFile, ct, authorizedPasswords);
                if (format == null || !CompressedFileHelper.SfxFormatMatches(candidate, format.Extension))
                    throw new ArchiveFormatProbeIncompleteException(
                        "The selected executable was not verified as the expected multipart archive. Its files were retained.", "ArchiveInputRequired");
                foreach (var file in candidate.Files) ArtifactManifestFiles.RejectLinks(file);
                groups.Add(new DetectedArchiveGroup {KeyName = candidate.KeyName, Extension = candidate.ExpectedFormat,
                    OpenOptions = format.OpenOptions, Files = candidate.Files.ToList(),
                    FileSizes = candidate.Files.Select(file => new FileInfo(file).Length).ToList()});
            }
        }
        return groups;
    }

    internal static void ValidateCurrentSfxFamilies(IReadOnlyList<string> exactFiles, CancellationToken ct)
    {
        foreach (var candidate in CompressedFileHelper.DetectSfxMultipartCandidates(exactFiles))
            ValidateCurrentSfxFamily(candidate, ct);
    }

    private static void ValidateCurrentSfxFamily(SfxMultipartArchiveCandidate candidate, CancellationToken ct)
    {
        // Archive tools can read companion volumes implicitly. Recheck names immediately
        // before opening an existing private snapshot; never let a newly added sibling
        // contribute bytes that are absent from its fixed manifest and replacement proof.
        const int maximumSiblingNames = 4096;
        ArtifactManifestFiles.RejectLinks(candidate.EntryFile);
        var parent = Path.GetDirectoryName(candidate.EntryFile)!;
        var names = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(parent))
        {
            ct.ThrowIfCancellationRequested();
            if (names.Count == maximumSiblingNames)
                throw new ArchiveFormatProbeIncompleteException(
                    "The selected multipart archive directory exceeds the bounded sibling-name check. Move this complete family into a dedicated processing directory.",
                    ArchiveFormatProbeFailureCodes.BudgetExceeded);
            names.Add(path);
        }
        var current = CompressedFileHelper.DetectSfxMultipartCandidates(names)
            .SingleOrDefault(family => FileProcessingFiles.PathComparer.Equals(family.KeyName, candidate.KeyName));
        if (current == null || !current.IsComplete || current.IsAmbiguous ||
            !FileProcessingFiles.PathComparer.Equals(current.EntryFile, candidate.EntryFile) ||
            !current.Files.ToHashSet(FileProcessingFiles.PathComparer).SetEquals(candidate.Files))
            throw new ArchiveFormatProbeIncompleteException(
                "The current multipart archive files differ from the authorized volume set. Refresh the current directory before continuing; no new sibling volume was adopted.",
                "ArchiveGroupInputChanged");
        foreach (var file in current.Files)
        {
            ct.ThrowIfCancellationRequested();
            ArtifactManifestFiles.RejectLinks(file);
            if ((File.GetAttributes(file) & FileAttributes.Directory) != 0)
                throw new ArchiveFormatProbeIncompleteException(
                    "An archive volume is not a regular file. Check the authorized volume set before continuing.", "ArchiveGroupInputChanged");
        }
    }

    private static bool IsRecursiveContainer(CompressedFileGroup group)
    {
        var file = group.Files[0];
        if (Deliveries.Contains(Path.GetExtension(file))) return false;
        var stem = Regex.Replace(file, @"\.\d{3,}$", "");
        return Containers.Contains(Path.GetExtension(stem)) ||
               (group.Extension?.StartsWith(".tar.", StringComparison.OrdinalIgnoreCase) ?? false) ||
               Regex.IsMatch(file, @"\.[rz]\d{2,}$", RegexOptions.IgnoreCase);
    }

    internal static async Task<string?> SignatureAsync(string file, CancellationToken ct)
    {
        var header = new byte[8]; var count = 0;
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            8, FileOptions.Asynchronous | FileOptions.SequentialScan);
        while (count < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        return Signature(header.AsSpan(0, count));
    }

    private static async Task<bool> HasMediaPrefixAsync(string file, CancellationToken ct)
    {
        var header = new byte[12]; var count = 0;
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            12, FileOptions.Asynchronous | FileOptions.SequentialScan);
        while (count < header.Length)
        {
            var read = await input.ReadAsync(header.AsMemory(count), ct);
            if (read == 0) break;
            count += read;
        }
        var bytes = header.AsSpan(0, count);
        return bytes.StartsWith(new byte[] {0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a}) ||
               bytes.StartsWith(new byte[] {0xff, 0xd8, 0xff}) || bytes.StartsWith("GIF87a"u8) ||
               bytes.StartsWith("GIF89a"u8) || (bytes.Length >= 8 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8)) ||
               (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) &&
                (bytes.Slice(8, 4).SequenceEqual("AVI "u8) || bytes.Slice(8, 4).SequenceEqual("WEBP"u8)));
    }

    internal static string? Signature(ReadOnlySpan<byte> header) => ArchiveSignature.Detect(header);
}

/// <summary>One budget shared by all explicit extraction steps in a plan attempt.</summary>
internal sealed class ArchiveDetectionBudget
{
    private int _attempts;
    private TimeSpan _spent;

    internal async Task<ArchiveFormatProbeResult?> ProbeAsync(IArchiveExtractionService probe, string file, CancellationToken ct,
        IReadOnlyList<string?>? authorizedPasswords = null)
    {
        ct.ThrowIfCancellationRequested();
        var remaining = ArchiveFormatProbeLimits.PerPlan - _spent;
        if (_attempts >= ArchiveFormatProbeLimits.MaximumCandidates || remaining <= TimeSpan.Zero)
            throw new ArchiveFormatProbeIncompleteException(
                "Archive format discovery exhausted its candidate or time budget. Narrow the extraction selector before continuing.", ArchiveFormatProbeFailureCodes.BudgetExceeded);
        _attempts++;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(remaining < ArchiveFormatProbeLimits.PerCandidate ? remaining : ArchiveFormatProbeLimits.PerCandidate);
        var elapsed = Stopwatch.StartNew();
        try
        {
            var format = await probe.ProbeAuthorizedFormatAsync(file, authorizedPasswords ?? [], stop.Token);
            stop.Token.ThrowIfCancellationRequested();
            return format;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ArchiveFormatProbeIncompleteException(
                "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan.", ArchiveFormatProbeFailureCodes.TimedOut);
        }
        finally { _spent += elapsed.Elapsed; }
    }
}
