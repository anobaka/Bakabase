using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

internal sealed record ImagePackagingEvidence(int InputId, long ZipOffset);

public sealed partial class AdaptiveFileProcessingAssessmentService
{
    // A single large image accompanied only by small captions, notes or packaging is a
    // bounded candidate when the source/processing context also indicates unpacking.
    // A gallery or application tree must not trigger image-by-image inspection.
    internal static async Task<ImagePackagingEvidence?> InspectImagePackagingAsync(ArtifactRecord artifact,
        IReadOnlyList<string> visiblePaths, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entries = artifact.Manifest.Entries;
        if (entries.Count is 0 or > MaxFormatEvidenceFiles ||
            !visiblePaths.SequenceEqual(entries.Select(entry => entry.RelativePath), StringComparer.Ordinal)) return null;
        var candidate = -1;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var extension = Path.GetExtension(entry.RelativePath).ToLowerInvariant();
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                if (candidate >= 0 || entry.Length < 16L * 1024 * 1024) return null;
                candidate = index;
            }
            else if (entry.Length > 4L * 1024 * 1024 ||
                     extension is not (".srt" or ".vtt" or ".ass" or ".ssa" or ".lrc" or ".txt" or ".nfo" or
                         ".pdf" or ".rar" or ".zip" or ".7z")) return null;
        }
        if (candidate < 0) return null;
        var image = entries[candidate];
        try
        {
            var path = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest)[candidate];
            ArtifactManifestFiles.RejectLinks(path);
            var file = new FileInfo(path);
            var modified = file.LastWriteTimeUtc;
            if (file.Length != image.Length) return null;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stop.CancelAfter(TimeSpan.FromSeconds(3));
            var offset = await ArchiveSignature.FindDominantZipBehindImageAsync(path, image.Length, stop.Token);
            ArtifactManifestFiles.RejectLinks(path);
            file.Refresh();
            return offset is { } start && file.Length == image.Length && file.LastWriteTimeUtc == modified
                ? new(candidate + 1, start) : null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    internal static bool HasExtractionStep(string? planJson)
    {
        if (planJson is not {Length: > 0 and <= 131072}) return false;
        try
        {
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(planJson, WorkflowJson.Options);
            return plan?.Steps?.Any(step => step?.Op == "extractArchive") == true;
        }
        catch (JsonException) { return false; }
    }
}
