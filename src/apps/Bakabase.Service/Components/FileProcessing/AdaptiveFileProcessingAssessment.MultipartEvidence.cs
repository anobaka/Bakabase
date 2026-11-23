using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record MultipartRename(int InputId, string ExpectedName);
internal sealed record AdaptiveMultipartNamingEvidence(IReadOnlyList<int> InputIds,
    IReadOnlyList<MultipartRename> Renames, long DeclaredArchiveBytes, string Format = "7z");

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    private static readonly Regex NumberedVolumeName = new(@"^(?<base>.+)\.(?<number>[0-9]{3})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    // An advisory naming repair, not authority to join arbitrary similarly sized files.
    // Only an entire small inventory, one parent and one numeric family are considered.
    // The 7z branch reads 32 bytes per file. The ZIP branch reads bounded metadata only.
    internal static async Task<AdaptiveMultipartNamingEvidence?> InspectMultipartNamingAsync(ArtifactRecord artifact,
        IReadOnlyList<string> visiblePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = artifact.Manifest.Entries;
        if (entries.Count is < 2 or > MaxFormatEvidenceFiles ||
            !visiblePaths.SequenceEqual(entries.Select(e => e.RelativePath), StringComparer.Ordinal) ||
            entries.Any(e => e.Length < 32) || entries.Select(e => Path.GetDirectoryName(e.RelativePath))
                .Distinct(FileProcessingFiles.PathComparer).Count() != 1) return null;
        if (entries.All(e => Path.GetExtension(e.RelativePath).StartsWith(".z", StringComparison.OrdinalIgnoreCase)))
            return await InspectMaskedSplitZipAsync(artifact, ct);
        var members = entries.Select((entry, index) => (entry, index,
            match: NumberedVolumeName.Match(Path.GetFileName(entry.RelativePath)))).ToArray();
        var numbered = members.Where(e => e.match.Success && e.match.Groups["number"].Value != "001").ToArray();
        if (numbered.Length != entries.Count - 1 ||
            numbered.Select(e => e.match.Groups["base"].Value).Distinct(FileProcessingFiles.PathComparer).Count() != 1)
            return null;
        var ordered = numbered.OrderBy(e => int.Parse(e.match.Groups["number"].Value, CultureInfo.InvariantCulture)).ToArray();
        if (!ordered.Select(e => int.Parse(e.match.Groups["number"].Value, CultureInfo.InvariantCulture))
                .SequenceEqual(Enumerable.Range(2, ordered.Length))) return null;
        var entry = members.Single(e => !e.match.Success || e.match.Groups["number"].Value == "001");
        var familyName = ordered[0].match.Groups["base"].Value;
        var entryName = Path.GetFileName(entry.entry.RelativePath);
        // Without a format suffix, require the literal first volume name (optionally masked),
        // not just a shared prefix. A bare "photo.jpg" must never bind nearby "photo.002".
        var hasFormatSuffix = familyName.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
        var requiredStem = hasFormatSuffix ? familyName : familyName + ".001";
        if (!FileProcessingFiles.PathComparer.Equals(entryName, requiredStem) &&
            (entryName.Length <= requiredStem.Length + 1 || entryName[requiredStem.Length] != '.' ||
             !FileProcessingFiles.PathComparer.Equals(entryName[..requiredStem.Length], requiredStem))) return null;
        if (hasFormatSuffix && entry.match.Success) return null; // Already a canonical .7z.001..N family.
        if (ordered.Take(ordered.Length - 1).Any(e => e.entry.Length != entry.entry.Length) ||
            ordered[^1].entry.Length > entry.entry.Length) return null;
        long total;
        try { total = entries.Sum(e => e.Length); }
        catch (OverflowException) { return null; }
        try
        {
            var files = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
            long? declared = null;
            for (var index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[index];
                ArtifactManifestFiles.RejectLinks(file);
                var before = new FileInfo(file);
                var modified = before.LastWriteTimeUtc;
                if (before.Length != entries[index].Length) return null;
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    32, FileOptions.Asynchronous | FileOptions.RandomAccess);
                var bytes = new byte[32];
                if (await stream.ReadAtLeastAsync(bytes, bytes.Length, false, ct) != bytes.Length) return null;
                ArtifactManifestFiles.RejectLinks(file);
                var after = new FileInfo(file);
                if (after.Length != entries[index].Length || after.LastWriteTimeUtc != modified) return null;
                if (index == entry.index) declared = SevenZipVolumeSet.DeclaredExtent(bytes);
                else if (ArchiveSignature.Detect(bytes) != null) return null; // Another container's start is contradictory.
            }
            if (declared != total || declared <= entry.entry.Length) return null;
            var ids = ordered.Select(e => e.index + 1).Prepend(entry.index + 1).ToArray();
            var archiveName = hasFormatSuffix ? familyName : familyName + ".7z";
            var renames = ids.Take(hasFormatSuffix ? 1 : ids.Length).Select((id, index) =>
                new MultipartRename(id, archiveName + "." + (index + 1).ToString("D3", CultureInfo.InvariantCulture))).ToArray();
            return new(ids, renames, total);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { return null; }
    }

    private static string DescribeMultipartNamingEvidence(AdaptiveMultipartNamingEvidence? evidence) =>
        JsonSerializer.Serialize(new
        {
            scope = evidence?.Format == "zip"
                ? "One same-directory split ZIP family. Volume names share one mask, disk indexes are consecutive and the central directory verifies the local header, disk count and complete compressed extent. Naming evidence only; contents and password remain unverified."
                : "One small same-directory family with the same archive basename, unique 7z start header with valid CRC, consecutive volumes 2..N, matching sizes and exact declared archive extent. Naming evidence only; contents/password remain unverified.",
            namingRepair = evidence == null ? null : new
            {
                renames = evidence.Renames.Select(rename => new {inputId = rename.InputId, expectedName = rename.ExpectedName}),
                familyInputIds = evidence.InputIds, declaredArchiveBytes = evidence.DeclaredArchiveBytes,
                requiresRenameBeforeExtraction = true
            }
        }, ManifestJson);

    internal static AdaptiveFileProcessingDecision RequireMultipartNaming(AdaptiveFileProcessingDecision decision,
        AdaptiveMultipartNamingEvidence? evidence, IReadOnlyList<string> paths)
    {
        if (evidence == null) return decision;
        var renames = evidence.Renames.ToDictionary(rename => paths[rename.InputId - 1], rename => rename.ExpectedName, StringComparer.Ordinal);
        var exactRepair = decision.Operations.Count == renames.Count &&
            decision.Operations.Select(o => o.Input).Distinct(StringComparer.Ordinal).Count() == renames.Count &&
            decision.Operations.All(o => o.Type == "rename" && renames.TryGetValue(o.Input, out var name) && o.Destination == name);
        return decision.IsFinal || decision.ShouldContinue && !exactRepair
            ? Human("分卷的当前文件名尚未组成连续归档。请先按已确认的分卷证据统一命名，再基于新清单分析；密码尚未判定错误。",
                "ARCHIVE_FAMILY_REVIEW_REQUIRED", decision.RawText)
            : decision;
    }
}
