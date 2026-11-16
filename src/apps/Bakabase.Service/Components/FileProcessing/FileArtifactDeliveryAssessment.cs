using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>A bounded review hint, never deletion authority or an instruction to unpack every ZIP.</summary>
internal static class FileArtifactDeliveryAssessment
{
    public const string ReviewRequired = "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED";
    public const string ResultUncertain = "FILE_DELIVERY_RESULT_UNCERTAIN";
    public const int MaximumFiles = 8;
    internal const int MaximumCompanionProbeFiles = 128;

    internal sealed record Assessment(IReadOnlyList<string> Files, bool Automatic = false, bool Uncertain = false);
    private static readonly HashSet<string> FinishedFormats = new(StringComparer.OrdinalIgnoreCase)
    { ".exe", ".app", ".apk", ".jar", ".epub", ".pdf", ".html", ".htm", ".docx", ".xlsx", ".pptx",
      ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".mp4", ".mkv", ".webm", ".mp3", ".flac", ".wav", ".txt", ".md", ".srt", ".ass", ".ssa", ".vtt" };
    // An executable/delivery container beside a ZIP can be an application with packaged data.
    // Its data files are retained verbatim and are never recursively treated as outer packaging.
    private static readonly HashSet<string> ApplicationFormats = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".apk", ".jar", ".app" };

    public static async Task<IReadOnlyList<string>> ArchiveOnlyResultAsync(ArtifactRecord artifact,
        CancellationToken ct, bool extractionWasReviewed = false) =>
        (await AssessAsync(artifact, ct, extractionWasReviewed)).Files;

    internal static async Task<Assessment> AssessAsync(ArtifactRecord artifact,
        CancellationToken ct, bool extractionWasReviewed = false)
    {
        if (artifact.Kind is not ("processedFiles" or "publishedFiles") || artifact.Manifest.Entries.Count == 0)
            return new([]);
        var metadata = artifact.MetadataJson;
        if (artifact.Kind == "publishedFiles")
        {
            try { metadata = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(metadata ?? "{}", WorkflowJson.Options)?.SourceMetadataJson; }
            catch (JsonException) { return new(artifact.Manifest.Entries.Select(e => e.RelativePath).Take(MaximumFiles).ToArray(), Uncertain: true); }
        }
        if (!extractionWasReviewed && !HasExecutedExtraction(metadata)) return new([]);
        var relativeFiles = artifact.Manifest.Entries.Select(e => e.RelativePath).ToArray();
        if (relativeFiles.Any(f => ApplicationFormats.Contains(Path.GetExtension(f)))) return new([]);
        var unclassifiedFiles = relativeFiles.Where(f => !FinishedFormats.Contains(Path.GetExtension(f))).ToArray();
        // No external probes or directory discovery. Large/unknown sets never acquire automatic
        // extraction authority simply because their names resemble compressed files.
        if (relativeFiles.Length > MaximumFiles)
            return await IsFinishedResultAsync(artifact, ct) ? new([]) :
                new(unclassifiedFiles.Take(MaximumFiles).ToArray(), Uncertain: true);
        var files = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        var groups = await AuthorizedArchiveDetection.DetectAsync(files, recursive: true, ct);
        var covered = groups.SelectMany(g => g.Files).ToHashSet(ArtifactManifestFiles.PathComparer);
        if (covered.SetEquals(files))
        {
            var automatic = (await ConfirmedSingleOuterArchiveAsync(files, ct)).Count == 1;
            return new(relativeFiles, automatic, !automatic);
        }
        if (groups.Count > 0) return new(relativeFiles, Uncertain: true);
        if (!await IsFinishedResultAsync(artifact, ct))
            return new(unclassifiedFiles, Uncertain: true);
        return new([]);
    }

    private static async Task<bool> IsFinishedResultAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        var unclassified = artifact.Manifest.Entries.Where(e => !FinishedFormats.Contains(Path.GetExtension(e.RelativePath)))
            .ToArray();
        if (unclassified.Length == 0) return true;
        // Finder metadata may accompany a finished resource but is never a resource by itself.
        if (artifact.Manifest.Entries.All(e => FinderMetadataDeliveryProbe.IsCandidate(e.RelativePath))) return false;
        // Common companion results include camera MOV, synchronized LRC lyrics and Finder
        // directory metadata. Do not whitelist names/suffixes: check only these
        // bounded unclassified files, seeking over media payloads instead of scanning images
        // or invoking an archive tool. This never grants extraction/deletion authority.
        // This budget is independent of the small archive-discovery and review-display limit.
        if (unclassified.Length > MaximumCompanionProbeFiles || unclassified.Any(e =>
                !string.Equals(Path.GetExtension(e.RelativePath), ".mov", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetExtension(e.RelativePath), ".lrc", StringComparison.OrdinalIgnoreCase) &&
                !FinderMetadataDeliveryProbe.IsCandidate(e.RelativePath))) return false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var exactFiles = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
            foreach (var file in exactFiles.Where(f => string.Equals(Path.GetExtension(f), ".mov", StringComparison.OrdinalIgnoreCase)))
                if (!await QuickTimeDeliveryProbe.IsMovieAsync(file, stop.Token)) return false;
            foreach (var file in exactFiles.Where(f => string.Equals(Path.GetExtension(f), ".lrc", StringComparison.OrdinalIgnoreCase)))
                if (!await LyricDeliveryProbe.IsCompanionAsync(file, exactFiles, stop.Token)) return false;
            foreach (var file in exactFiles.Where(FinderMetadataDeliveryProbe.IsCandidate))
                if (!await FinderMetadataDeliveryProbe.IsMetadataAsync(file, stop.Token)) return false;
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    /// <summary>Admission for each automatic layer: exact single group plus a real entry signature.</summary>
    internal static async Task<IReadOnlyList<DetectedArchiveGroup>> ConfirmedSingleOuterArchiveAsync(
        IReadOnlyList<string> exactFiles, CancellationToken ct)
    {
        if (exactFiles.Count is < 1 or > MaximumFiles) return [];
        var groups = await AuthorizedArchiveDetection.DetectAsync(exactFiles, recursive: true, ct);
        if (groups.Count != 1 || !groups[0].Files.ToHashSet(ArtifactManifestFiles.PathComparer).SetEquals(exactFiles)) return [];
        var group = groups[0];
        var signature = await AuthorizedArchiveDetection.SignatureAsync(group.Files[0], ct);
        if (signature == null || !string.Equals(signature, group.Extension, StringComparison.OrdinalIgnoreCase)) return [];
        try { FileProcessingFiles.ValidateVolumes(group.Files, inspectSiblingDownloads: false); }
        catch (InvalidOperationException) { return []; }
        return groups;
    }

    internal static bool HasExecutedExtraction(string? metadata)
    {
        if (metadata == null) return false;
        try
        {
            var saved = JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(metadata, WorkflowJson.Options);
            if (saved == null || string.IsNullOrWhiteSpace(saved.PlanJson)) return false;
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(saved.PlanJson, WorkflowJson.Options);
            FileProcessingPlanValidator.Validate(plan);
            if (plan == null) return false;
            if (saved.ArchiveCount > 0) return true;
            if (plan.Requirement != "required") return false;
            var boundary = saved.CompletedThroughStepId == null ? plan.Steps.Count - 1 :
                plan.Steps.FindIndex(s => s.Id == saved.CompletedThroughStepId);
            return boundary >= 0 && plan.Steps.Take(boundary + 1).Any(s => s.Op == "extractArchive");
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return false; }
    }
}
