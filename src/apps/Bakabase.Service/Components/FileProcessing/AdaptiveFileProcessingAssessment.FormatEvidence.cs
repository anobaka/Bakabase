using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    internal const int MaxFormatEvidenceFiles = 16;
    internal const int MaxOuterFormatEvidenceFiles = 4;
    private const long MinimumOuterFormatEvidenceBytes = 1024 * 1024;

    // Assessment is not archive discovery. Keep a few slots for large extensionless files
    // beside an expanded collection: their archive suffix may have been removed. The rest
    // inspect recognized archive entry files, never every image or split volume. All hints
    // only select an eight-byte read; they do not establish package intent or authorize extraction.
    internal static int[] FormatEvidenceCandidates(ArtifactManifest manifest, IReadOnlyList<string> visiblePaths)
    {
        var known = CompressedFileHelper.DetectCompressedFileGroups(visiblePaths.ToArray(),
            inspectFileSizes: false).Where(group => group.Files.Count > 0)
            .Select(group => group.Files[0]).ToHashSet(StringComparer.Ordinal);
        if (manifest.Entries.Count > MaxFormatEvidenceFiles)
        {
            var outer = OuterFormatEvidenceCandidates(manifest, visiblePaths, known);
            return visiblePaths.Select((path, index) => (path, index))
                .Where(file => known.Contains(file.path)).Take(MaxFormatEvidenceFiles - outer.Length)
                .Select(file => file.index).Concat(outer).ToArray();
        }
        return visiblePaths.Select((path, index) => (path, index))
            .OrderByDescending(file => known.Contains(file.path)).ThenBy(file => file.index)
            .Take(MaxFormatEvidenceFiles).Select(file => file.index).ToArray();
    }

    private static int[] OuterFormatEvidenceCandidates(ArtifactManifest manifest,
        IReadOnlyList<string> visiblePaths, HashSet<string> known)
    {
        var entries = manifest.Entries;
        var common = ParentSegments(entries[0].RelativePath);
        foreach (var entry in entries.Skip(1))
        {
            var parent = ParentSegments(entry.RelativePath);
            common = common.Take(common.Zip(parent).TakeWhile(pair => pair.First == pair.Second).Count()).ToArray();
            if (common.Length == 0) break;
        }
        var prefixLength = common.Length == 0 ? 0 : string.Join('/', common).Length + 1;
        string Branch(string path)
        {
            var relative = path[prefixLength..];
            var slash = relative.IndexOf('/');
            return slash < 0 ? "" : relative[..slash];
        }
        var branchCounts = entries.GroupBy(entry => Branch(entry.RelativePath), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var applicationDirectories = entries.Where(entry => Path.GetExtension(entry.RelativePath).ToLowerInvariant()
                is ".exe" or ".dll" or ".so" or ".dylib")
            .Select(entry => Path.GetDirectoryName(entry.RelativePath) ?? "").ToHashSet(StringComparer.Ordinal);
        var lengths = entries.ToDictionary(entry => entry.RelativePath, entry => entry.Length, StringComparer.Ordinal);
        return visiblePaths.Select((path, index) => (path, index))
            .Where(file => !known.Contains(file.path) && Path.GetExtension(file.path).Length == 0 &&
                           lengths.TryGetValue(file.path, out var length) && length >= MinimumOuterFormatEvidenceBytes &&
                           !applicationDirectories.Contains(Path.GetDirectoryName(file.path) ?? "") &&
                           (Branch(file.path).Length == 0 || branchCounts[Branch(file.path)] <= MaxOuterFormatEvidenceFiles))
            .OrderByDescending(file => lengths[file.path]).ThenBy(file => file.index)
            .Take(MaxOuterFormatEvidenceFiles).Select(file => file.index).ToArray();
    }

    internal static async Task<string> DescribeFormatEvidenceAsync(ArtifactRecord artifact,
        IReadOnlyList<string> visiblePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var selected = FormatEvidenceCandidates(artifact.Manifest, visiblePaths);
        var evidence = new List<object>();
        IReadOnlyDictionary<string, (string Path, long Length)>? files = null;
        if (selected.Length > 0)
        {
            try
            {
                var paths = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
                files = artifact.Manifest.Entries.Select((entry, index) => (entry, index))
                    .ToDictionary(pair => pair.entry.RelativePath,
                        pair => (paths[pair.index], pair.entry.Length), StringComparer.Ordinal);
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException)
            {
                // Optional evidence must never turn an invalid inventory into trusted file access.
                // The ordinary processing and artifact guards remain authoritative.
            }
        }
        foreach (var index in selected)
        {
            ct.ThrowIfCancellationRequested();
            string? format = null;
            var status = "unavailable";
            if (files != null && files.TryGetValue(visiblePaths[index], out var file))
            {
                try
                {
                    ArtifactManifestFiles.RejectLinks(file.Path);
                    if (new FileInfo(file.Path).Length == file.Length)
                    {
                        format = await AuthorizedArchiveDetection.SignatureAsync(file.Path, ct);
                        ArtifactManifestFiles.RejectLinks(file.Path);
                        if (new FileInfo(file.Path).Length == file.Length)
                            status = format == null ? "notRecognizedAtOffsetZero" : "recognizedArchiveSignature";
                        else format = null;
                    }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    format = null;
                }
            }
            evidence.Add(new {inputId = index + 1, status, archiveFormat = format});
        }
        return JsonSerializer.Serialize(new
        {
            scope = "At most 8 bytes at offset zero per selected file; not an archive integrity, password, or content inspection.",
            maximumFiles = MaxFormatEvidenceFiles,
            uninspectedFiles = artifact.Manifest.Entries.Count - selected.Length,
            files = evidence
        }, ManifestJson);
    }
}
