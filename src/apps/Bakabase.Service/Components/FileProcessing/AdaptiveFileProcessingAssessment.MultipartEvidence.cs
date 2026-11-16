using System;
using System.Buffers.Binary;
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
using SharpCompress.Compressors.Deflate;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record AdaptiveMultipartNamingEvidence(int EntryInputId, string ExpectedEntryName,
    IReadOnlyList<int> InputIds, long DeclaredArchiveBytes);

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    private static readonly Regex SevenZipVolumeName = new(@"^(?<base>.+\.7z)\.(?<number>[0-9]{3})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    // An advisory naming repair, not authority to join arbitrary similarly sized files.
    // Only an entire small inventory, one parent and one numeric family are considered.
    // Read at most 32 bytes per file; no archive tool, payload hash, or adjacent discovery.
    internal static async Task<AdaptiveMultipartNamingEvidence?> InspectMultipartNamingAsync(ArtifactRecord artifact,
        IReadOnlyList<string> visiblePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = artifact.Manifest.Entries;
        if (entries.Count is < 2 or > MaxFormatEvidenceFiles ||
            !visiblePaths.SequenceEqual(entries.Select(e => e.RelativePath), StringComparer.Ordinal) ||
            entries.Any(e => e.Length < 32) || entries.Select(e => Path.GetDirectoryName(e.RelativePath))
                .Distinct(FileProcessingFiles.PathComparer).Count() != 1) return null;
        var members = entries.Select((entry, index) => (entry, index,
            match: SevenZipVolumeName.Match(Path.GetFileName(entry.RelativePath)))).ToArray();
        var numbered = members.Where(e => e.match.Success).ToArray();
        var unnumbered = members.Where(e => !e.match.Success).ToArray();
        if (unnumbered.Length != 1 || numbered.Length != entries.Count - 1 ||
            numbered.Select(e => e.match.Groups["base"].Value).Distinct(FileProcessingFiles.PathComparer).Count() != 1)
            return null;
        var ordered = numbered.OrderBy(e => int.Parse(e.match.Groups["number"].Value, CultureInfo.InvariantCulture)).ToArray();
        if (!ordered.Select(e => int.Parse(e.match.Groups["number"].Value, CultureInfo.InvariantCulture))
                .SequenceEqual(Enumerable.Range(2, ordered.Length))) return null;
        var entry = unnumbered[0];
        var familyName = ordered[0].match.Groups["base"].Value;
        var entryName = Path.GetFileName(entry.entry.RelativePath);
        // Do not infer shared identity from equal sizes and a valid start header.
        // Historical renamed stems need independent evidence, not hash stripping.
        if (!FileProcessingFiles.PathComparer.Equals(entryName, familyName) &&
            (entryName.Length <= familyName.Length + 1 || entryName[familyName.Length] != '.' ||
             !FileProcessingFiles.PathComparer.Equals(entryName[..familyName.Length], familyName))) return null;
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
                if (index == entry.index) declared = SevenZipDeclaredExtent(bytes);
                else if (ArchiveSignature.Detect(bytes) != null) return null; // Another container's start is contradictory.
            }
            if (declared != total || declared <= entry.entry.Length) return null;
            return new(entry.index + 1, familyName + ".001",
                new[] {entry.index + 1}.Concat(ordered.Select(e => e.index + 1)).ToArray(), total);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        { return null; }
    }

    // https://github.com/ip7z/7zip/blob/main/DOC/7zFormat.txt (SignatureHeader), reviewed 2026-11-12.
    // CRC proves this small structural header only. The selected extractor still verifies the
    // complete volume set, payload and password after any explicitly proposed rename.
    internal static long? SevenZipDeclaredExtent(byte[] header)
    {
        if (header.Length != 32 || ArchiveSignature.Detect(header) != ".7z" || header[6] != 0) return null;
        var crc = new CRC32(); crc.SlurpBlock(header, 12, 20);
        if ((uint)crc.Crc32Result != BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8))) return null;
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(12));
        var size = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(20));
        if (size == 0 || offset > (ulong)long.MaxValue - 32UL || size > (ulong)long.MaxValue - 32UL - offset) return null;
        return checked((long)(32UL + offset + size));
    }

    private static string DescribeMultipartNamingEvidence(AdaptiveMultipartNamingEvidence? evidence) =>
        JsonSerializer.Serialize(new
        {
            scope = "One small same-directory family with the same archive basename, unique 7z start header with valid CRC, consecutive volumes 2..N, matching sizes and exact declared archive extent. Naming evidence only; contents/password remain unverified.",
            namingRepair = evidence == null ? null : new
            {
                inputId = evidence.EntryInputId, expectedName = evidence.ExpectedEntryName,
                familyInputIds = evidence.InputIds, declaredArchiveBytes = evidence.DeclaredArchiveBytes,
                requiresRenameBeforeExtraction = true
            }
        }, ManifestJson);

    internal static AdaptiveFileProcessingDecision RequireMultipartNaming(AdaptiveFileProcessingDecision decision,
        AdaptiveMultipartNamingEvidence? evidence, IReadOnlyList<string> paths)
    {
        if (evidence == null) return decision;
        var members = evidence.InputIds.Select(id => paths[id - 1]).ToHashSet(StringComparer.Ordinal);
        return decision.IsFinal || decision.Operations.Any(o => o.Type == "extract" && members.Contains(o.Input))
            ? Human("分卷的当前文件名尚未组成连续归档。请先按已确认的分卷证据统一命名，再基于新清单分析；密码尚未判定错误。",
                "ARCHIVE_FAMILY_REVIEW_REQUIRED", decision.RawText)
            : decision;
    }
}
