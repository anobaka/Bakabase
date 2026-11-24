using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.Acquisition.Planning;
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
    // Applications may include packaged data that should be delivered intact.
    private static readonly HashSet<string> ApplicationFormats = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".apk", ".jar", ".app" };

    // A processing-stage commit registers the same manifest at its visible location.
    // Its own no-op plan describes that move. The producer's receipt records the extraction.
    internal static async Task<bool> WasExtractedBeforeDeliveryAsync(ArtifactRecord artifact,
        IArtifactOperationStore store, CancellationToken ct)
    {
        if (artifact.Kind == "publishedFiles")
        {
            var publication = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(artifact.MetadataJson ?? "{}", WorkflowJson.Options);
            if (publication?.SourceMetadataJson == null) return false;
            var metadata = ReadProcessingMetadata(publication.SourceMetadataJson);
            if (metadata?.StageCommit == null && !HasExecutedExtraction(metadata)) return false;
            var source = await store.GetArtifactAsync(publication.SourceArtifactId, ct);
            if (source?.Kind != "processedFiles" || source.OperationId != publication.SourceOperationId ||
                source.MetadataJson != publication.SourceMetadataJson || !FileArtifactDeliveryLayout.PreservesSource(source, artifact, publication.DeliveryLayout))
                throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
            artifact = source;
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (artifact.Kind == "processedFiles" && artifact.MetadataJson != null)
        {
            var metadata = ReadProcessingMetadata(artifact.MetadataJson);
            if (metadata?.StageCommit is not { } receipt) return HasExecutedExtraction(metadata);
            if (!visited.Add(artifact.Id) || receipt.Version != 1 || receipt.Move is not {Completed: true} ||
                receipt.Id != artifact.OperationId || receipt.SourceArtifactId != metadata.InputArtifactId ||
                !artifact.ParentArtifactIds.Contains(receipt.SourceArtifactId))
                throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
            var source = await store.GetArtifactAsync(receipt.SourceArtifactId, ct);
            if (source?.Kind != "processedFiles" || source.Fingerprint != receipt.SourceFingerprint ||
                source.MetadataJson != metadata.InputMetadataJson || !FileDispositionProofs.ValidFor(metadata.FileDisposition, source, artifact) ||
                !FileDispositionProofs.SameManifest(source.Manifest, artifact.Manifest))
                throw new InvalidOperationException("FILE_DELIVERY_INPUT_CHANGED");
            artifact = source;
        }
        return false;
    }

    public static async Task<IReadOnlyList<string>> ArchiveOnlyResultAsync(ArtifactRecord artifact,
        CancellationToken ct, bool extractionWasReviewed = false) =>
        (await AssessAsync(artifact, ct, extractionWasReviewed)).Files;

    internal static async Task<Assessment> AssessAsync(ArtifactRecord artifact,
        CancellationToken ct, bool extractionWasReviewed = false)
    {
        var result = await AssessPackagingAsync(artifact, ct, extractionWasReviewed);
        var retained = RetainedArchiveFiles(artifact);
        if (retained.Count == 0) return result;
        // Keep actionable packaging candidates visible alongside the retained original.
        var files = result.Files.Where(f => !retained.Contains(f, StringComparer.Ordinal)).Take(MaximumFiles - 1)
            .Concat(retained).Take(MaximumFiles).ToArray();
        return new(files, Uncertain: true);
    }

    internal static IReadOnlyList<string> RetainedArchiveFiles(ArtifactRecord artifact)
    {
        if (artifact.Kind != "processedFiles" || artifact.MetadataJson == null ||
            !artifact.MetadataJson.Contains("\"retainedArchiveFiles\"", StringComparison.Ordinal)) return [];
        var retained = ReadProcessingMetadata(artifact.MetadataJson)?.RetainedArchiveFiles;
        if (retained is not {Count: > 0}) return [];
        var paths = retained.ToHashSet(StringComparer.Ordinal);
        return artifact.Manifest.Entries.Where(e => paths.Contains(e.RelativePath)).Select(e => e.RelativePath).Take(MaximumFiles).ToArray();
    }

    private static async Task<Assessment> AssessPackagingAsync(ArtifactRecord artifact,
        CancellationToken ct, bool extractionWasReviewed)
    {
        // Structure proves packaging, not whether it should be extracted or with which password.
        var wrappers = await DisguisedVideoArchivesAsync(artifact, ct);
        if (wrappers.Count > 0) return new(wrappers);
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
        {
            var wrappersToReview = await ReviewNamedArchiveCarriersAsync(artifact, ct);
            if (wrappersToReview.Count > 0)
                return new(wrappersToReview.Concat(unclassifiedFiles).Take(MaximumFiles).ToArray(), Uncertain: true);
            return await IsFinishedResultAsync(artifact, ct) ? new([]) :
                new(unclassifiedFiles.Take(MaximumFiles).ToArray(), Uncertain: true);
        }
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

    // Describes only the displayed, already verified artifact entries. These observations never enter a plan or a review token.
    internal static async Task<IReadOnlyDictionary<string, string>> ReadReviewSignaturesAsync(ArtifactRecord artifact,
        IReadOnlyList<string> displayedFiles, CancellationToken ct)
    {
        var selected = displayedFiles.Take(MaximumFiles).ToHashSet(StringComparer.Ordinal);
        var signatures = new Dictionary<string, string>(StringComparer.Ordinal);
        if (selected.Count == 0) return signatures;
        var paths = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        foreach (var (entry, path) in artifact.Manifest.Entries.Zip(paths))
        {
            if (!selected.Remove(entry.RelativePath)) continue;
            ct.ThrowIfCancellationRequested();
            var header = await ReadRegisteredHeaderAsync(entry, path, ct);
            if (header.Signature != null) signatures.Add(entry.RelativePath, header.Signature);
            if (selected.Count == 0) break;
        }
        return signatures;
    }

    // The inner extension narrows which registered headers we inspect. A genuine PDF or image
    // with such a name stays a finished file. ZIP-based document and application formats keep
    // their own delivery semantics even if their descriptive basename ends in an archive suffix.
    private static bool IsNamedArchiveCarrier(string name)
    {
        var outer = Path.GetExtension(name);
        if (!FinishedFormats.Contains(outer) || ApplicationFormats.Contains(outer) ||
            outer.ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx" or ".epub") return false;
        return Path.GetExtension(Path.GetFileNameWithoutExtension(name)).ToLowerInvariant() is ".zip" or ".rar" or ".7z";
    }

    private static async Task<IReadOnlyList<string>> ReviewNamedArchiveCarriersAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        var entries = artifact.Manifest.Entries;
        var candidates = Enumerable.Range(0, entries.Count).Where(i => IsNamedArchiveCarrier(entries[i].RelativePath))
            .Take(MaximumCompanionProbeFiles + MaximumFiles).ToList();
        if (candidates.Count == 0) return [];
        var paths = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        var confirmed = new List<string>();
        var unresolved = new List<string>();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        for (var index = 0; index < candidates.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var member = candidates[index];
            var entry = entries[member];
            if (index >= MaximumCompanionProbeFiles || deadline.IsCancellationRequested)
            {
                unresolved.Add(entry.RelativePath);
                continue;
            }
            try
            {
                var header = await ReadRegisteredHeaderAsync(entry, paths[member], deadline.Token);
                if (header.Signature != null) confirmed.Add(entry.RelativePath);
                else if (!header.Checked) unresolved.Add(entry.RelativePath);
                if (confirmed.Count == MaximumFiles) break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
            {
                unresolved.Add(entry.RelativePath);
            }
        }
        // Missing or uninspected candidates can require a generic review, but only an observed
        // header is labelled with a format by the prompt. Neither case authorizes extraction.
        return confirmed.Concat(unresolved).Take(MaximumFiles).ToArray();
    }

    private static async Task<(bool Checked, string? Signature)> ReadRegisteredHeaderAsync(
        ArtifactManifestEntry entry, string path, CancellationToken ct)
    {
        try
        {
            ArtifactManifestFiles.RejectLinks(path);
            var file = new FileInfo(path);
            if (file.Exists && file.Length == entry.Length)
            {
                var modified = file.LastWriteTimeUtc;
                var signature = await AuthorizedArchiveDetection.SignatureAsync(path, ct);
                ArtifactManifestFiles.RejectLinks(path);
                file.Refresh();
                if (file.Exists && file.Length == entry.Length && file.LastWriteTimeUtc == modified)
                    return (true, signature);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // The resume path validates the full artifact before applying any explicit instructions.
        }
        return (false, null);
    }

    // Never scan a gallery, software installation or a video collection for embedded payloads.
    // A small delivery can contain an archive carrier beside a cover or a readme.
    internal static bool MayContainDisguisedVideoArchive(ArtifactManifest manifest) =>
        manifest.Entries.Count is > 0 and <= MaximumFiles &&
        !manifest.Entries.Any(e => ApplicationFormats.Contains(Path.GetExtension(e.RelativePath))) &&
        manifest.Entries.Count(IsVideoContainer) is 1 or 2;

    internal static async Task<IReadOnlyList<string>> DisguisedVideoArchivesAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        if (artifact.Kind is not ("processedFiles" or "publishedFiles" or AcquisitionCurrentDirectoryFiles.Kind) ||
            !MayContainDisguisedVideoArchive(artifact.Manifest)) return [];
        var exact = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
        var wrappers = new List<string>();
        foreach (var (entry, path) in artifact.Manifest.Entries.Zip(exact))
            if (IsVideoContainer(entry) && await IsDisguisedVideoArchiveAsync(path, entry.Length, ct))
                wrappers.Add(entry.RelativePath);
        return wrappers;
    }

    private static bool IsVideoContainer(ArtifactManifestEntry entry) => entry.Length >= 22 && HasVideoSuffix(entry.RelativePath);

    internal static bool HasVideoSuffix(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mp4" or ".mov" or ".m4v";

    internal static async Task<bool> IsDisguisedVideoArchiveAsync(string path, long length, CancellationToken ct)
    {
        try
        {
            ArtifactManifestFiles.RejectLinks(path);
            var before = File.GetLastWriteTimeUtc(path);
            var offset = await ArchiveSignature.FindDominantZipAsync(path, length, ct);
            return offset.HasValue && await QuickTimeDeliveryProbe.HasSeparateArchiveAsync(path, length, offset.Value, ct) &&
                new FileInfo(path).Length == length && before == File.GetLastWriteTimeUtc(path);
        }
        catch (ArchiveFormatProbeIncompleteException) { return false; }
    }

    private static async Task<bool> IsFinishedResultAsync(ArtifactRecord artifact, CancellationToken ct)
    {
        var unclassified = artifact.Manifest.Entries.Where(e => !FinishedFormats.Contains(Path.GetExtension(e.RelativePath)))
            .ToArray();
        if (unclassified.Length == 0) return true;
        // Finder metadata may accompany a finished resource but is never a resource by itself.
        if (artifact.Manifest.Entries.All(e => FinderMetadataDeliveryProbe.IsCandidate(e.RelativePath))) return false;
        // Common companion results include camera MOV, M4V, synchronized LRC lyrics and Finder
        // directory metadata. Do not whitelist names/suffixes: check only these
        // bounded unclassified files, seeking over media payloads instead of scanning images
        // or invoking an archive tool. This never grants extraction/deletion authority.
        // This budget is independent of the small archive-discovery and review-display limit.
        if (unclassified.Length > MaximumCompanionProbeFiles || unclassified.Any(e =>
                !string.Equals(Path.GetExtension(e.RelativePath), ".mov", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetExtension(e.RelativePath), ".lrc", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(e.RelativePath).ToLowerInvariant() is not (".flv" or ".xml" or ".m4v") &&
                !FinderMetadataDeliveryProbe.IsCandidate(e.RelativePath))) return false;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var exactFiles = ArtifactManifestFiles.ResolveFiles(artifact.RootDirectory, artifact.Manifest);
            foreach (var (entry, file) in artifact.Manifest.Entries.Zip(exactFiles))
            {
                var suffix = Path.GetExtension(file);
                if (string.Equals(suffix, ".mov", StringComparison.OrdinalIgnoreCase) &&
                    !await QuickTimeDeliveryProbe.IsMovieAsync(file, stop.Token)) return false;
                if (string.Equals(suffix, ".m4v", StringComparison.OrdinalIgnoreCase) &&
                    !await QuickTimeDeliveryProbe.IsM4vAsync(file, entry.Length, stop.Token)) return false;
            }
            if (!await LiveRecordingDeliveryProbe.AreRecordingsAsync(exactFiles, stop.Token)) return false;
            var lyrics = exactFiles.Where(f => string.Equals(Path.GetExtension(f), ".lrc", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (lyrics.Length > 0)
            {
                var tracks = new LyricDeliveryProbe.TrackIndex(exactFiles);
                foreach (var lyric in lyrics)
                    if (!await LyricDeliveryProbe.IsCompanionAsync(lyric, tracks, stop.Token)) return false;
            }
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

    internal static bool HasExecutedExtraction(string? metadata) => HasExecutedExtraction(ReadProcessingMetadata(metadata));

    private static FileArtifactProcessingMetadata? ReadProcessingMetadata(string? metadata)
    {
        if (metadata == null) return null;
        try { return JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(metadata, WorkflowJson.Options); }
        catch (JsonException) { return null; }
    }

    private static bool HasExecutedExtraction(FileArtifactProcessingMetadata? saved)
    {
        try
        {
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
