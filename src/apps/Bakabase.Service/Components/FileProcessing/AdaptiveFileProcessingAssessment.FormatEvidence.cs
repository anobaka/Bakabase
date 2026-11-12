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

    // Assessment is not archive discovery. Small input sets get a cheap signature hint, while
    // larger collections only inspect the entry file of recognized archive families. This
    // avoids reading thousands of images or every split volume, and never invokes a CLI,
    // decompresses content, scans payloads, or computes a content hash.
    internal static int[] FormatEvidenceCandidates(ArtifactManifest manifest, IReadOnlyList<string> visiblePaths)
    {
        var known = CompressedFileHelper.DetectCompressedFileGroups(visiblePaths.ToArray(),
            inspectFileSizes: false).Where(group => group.Files.Count > 0)
            .Select(group => group.Files[0]).ToHashSet(StringComparer.Ordinal);
        return visiblePaths.Select((path, index) => (path, index))
            .Where(file => manifest.Entries.Count <= MaxFormatEvidenceFiles || known.Contains(file.path))
            .OrderByDescending(file => known.Contains(file.path)).ThenBy(file => file.index)
            .Take(MaxFormatEvidenceFiles).Select(file => file.index).ToArray();
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
