using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <param name="OutputDirectory">Null keeps each task's download root; empty captures the current acquisition default; nonempty selects a root.</param>
/// <param name="ApplyCurrentSettings">Omitted/null or true captures the current processing, cleanup and delivery defaults. Only explicit false changes solely the requested download root.</param>
/// <param name="PreserveOriginalDownloads">Explicit cleanup still respects original downloads when true.</param>
public sealed record AcquisitionStoragePolicyPreviewInput(IReadOnlyList<int> TaskIds,
    string? OutputDirectory = null, bool? ApplyCurrentSettings = null, bool OverrideRetention = false,
    bool PreserveOriginalDownloads = false);
public sealed record AcquisitionStoragePolicyApplyInput(string PreviewId, IReadOnlyList<int> TaskIds);
public sealed record AcquisitionStoragePolicyRow(int TaskId, string? Title, bool Eligible, string? ReasonCode,
    int Revision, string? DownloadPreview = null, string? ProcessingPreview = null, string? DeliveryPreview = null);
public sealed record AcquisitionStoragePolicyPreview(string PreviewId, DateTimeOffset ExpiresAt,
    IReadOnlyList<AcquisitionStoragePolicyRow> Rows, int EligibleCount, ArtifactStoragePolicy? Policy);
public sealed record AcquisitionStoragePolicySkip(int TaskId, string ReasonCode);
public sealed record AcquisitionStoragePolicyApplyResult(IReadOnlyList<int> AppliedTaskIds, IReadOnlyList<AcquisitionStoragePolicySkip> Skipped);
public sealed record AcquisitionWorkspaceCleanupRow(int TaskId, string? Title, bool Eligible, string? ReasonCode,
    IReadOnlyList<string> ArtifactIds, int FileCount, long Bytes)
{
    public AcquisitionProcessedCheckpointCleanup? ProcessedCheckpointCleanup { get; init; }
}
public sealed record AcquisitionProcessedCheckpointCleanup(IReadOnlyList<string> VerifiedResultArtifactIds,
    IReadOnlyList<string> RetainedDownloadArtifactIds, IReadOnlyList<string> IntermediateArtifactIds);
public sealed record AcquisitionWorkspaceCleanupPreview(string PreviewId, DateTimeOffset ExpiresAt,
    IReadOnlyList<AcquisitionWorkspaceCleanupRow> Rows, int EligibleCount);
public sealed record AcquisitionWorkspaceCleanupResult(IReadOnlyList<int> CompletedTaskIds, int RemovedFiles,
    long RemovedBytes, IReadOnlyList<AcquisitionStoragePolicySkip> Skipped);

public sealed class AcquisitionStoragePolicyPreviewStore
{
    internal sealed class Entry(string kind, DateTimeOffset expiresAt, IReadOnlyDictionary<int, string> versions)
    {
        internal string Kind { get; } = kind;
        internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal IReadOnlyDictionary<int, string> Versions { get; } = versions;
        internal ArtifactStoragePolicy? Policy { get; init; }
        internal AcquisitionDeliverySettings? Delivery { get; init; }
        internal string? OutputDirectory { get; init; }
        internal bool TracksDownloadDefault { get; init; }
        internal bool ApplyCurrentSettings { get; init; }
        internal Dictionary<int, IReadOnlyList<ArtifactWorkspaceCleanupPreview>> Artifacts { get; init; } = [];
        internal Dictionary<int, IReadOnlyList<AcquisitionProcessingWorkspaceCandidate>> ProcessingWorkspaces { get; init; } = [];
        internal Dictionary<int, IReadOnlyList<int>> CleanupOwnerRuns { get; init; } = [];
        internal Dictionary<int, ArtifactDeliveryCleanupConsent> CleanupConsents { get; init; } = [];
        internal Dictionary<int, IReadOnlyDictionary<string, IReadOnlyList<ArtifactCleanupRetirementDependency>>> CleanupDependencies { get; init; } = [];
        internal bool PreserveOriginalDownloads { get; init; }
        internal Dictionary<int, ReviewedCheckpointSelection> ReviewedCheckpoints { get; init; } = [];
        internal string? DefaultsVersion { get; init; }
        internal bool Claimed { get; set; }
        internal object? Result { get; set; }
        internal string? Selection { get; set; }
    }
    private readonly ConcurrentDictionary<string, Entry> entries = new();
    internal void Add(string id, Entry entry)
    {
        foreach (var item in entries) if (item.Value.ExpiresAt < DateTimeOffset.UtcNow) entries.TryRemove(item.Key, out _);
        if (entries.Count >= 100) throw new ArgumentException("STORAGE_PREVIEW_LIMIT");
        entries[id] = entry;
    }
    internal Entry Claim(string id, string kind, IReadOnlyList<int> taskIds)
    {
        if (!Guid.TryParseExact(id, "N", out _) || !entries.TryGetValue(id, out var entry) ||
            entry.Kind != kind || entry.ExpiresAt < DateTimeOffset.UtcNow) throw new ArgumentException("STORAGE_PREVIEW_EXPIRED");
        var selected = string.Join(",", System.Linq.Enumerable.Order(taskIds));
        lock (entry)
        {
            if (System.Linq.Enumerable.Any(taskIds, id => !entry.Versions.ContainsKey(id))) throw new ArgumentException("STORAGE_PREVIEW_SELECTION_INVALID");
            if (entry.Claimed && (entry.Result == null || entry.Selection != selected)) throw new ArgumentException("STORAGE_PREVIEW_OUTCOME_UNKNOWN");
            entry.Claimed = true; entry.Selection = selected; return entry;
        }
    }
}
