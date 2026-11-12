using System;
using System.Collections.Generic;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.SourceAvailability;

public sealed record SourceAvailabilityPreviewInput(List<int> TaskIds, bool Force = false);
public sealed record SourceAvailabilityApplyInput(string BatchId, List<string> SelectedKeys, string Action);
public sealed record SourceAvailabilityApplySkip(string Key, string Reason);
public sealed record SourceAvailabilityApplyResult(List<string> AppliedKeys, List<SourceAvailabilityApplySkip> Skipped);
public sealed record SourceAvailabilityRow
{
    public string Key { get; init; } = "";
    public int TaskId { get; init; }
    public string Title { get; init; } = "";
    public string PartId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string Url { get; init; } = "";
    public string? AccessCode { get; init; }
    public string ProviderId { get; init; } = "unknown";
    public SourceAvailabilityResult? Availability { get; init; }
    public bool Disabled { get; init; }
    public bool CanDisable { get; init; }
    public bool CanRestore { get; init; }
    public bool Protected { get; init; }
    public string? BlockReason { get; init; }
    public string? DecisionVersion { get; init; }
}
public sealed record SourceAvailabilityBatchView(string BatchId, string Status, int Processed, int Total,
    DateTimeOffset ExpiresAt, List<SourceAvailabilityRow> Rows, string TaskId);
internal sealed record SourceAvailabilitySnapshot(SourceAvailabilityRow Row, CloudSource Source, int Revision, string PlanHash);
internal sealed record SourceAvailabilityBatch
{
    public string BatchId { get; init; } = "";
    public string Status { get; init; } = "checking";
    public int Processed { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Force { get; init; }
    public List<SourceAvailabilitySnapshot> Items { get; init; } = [];
    public SourceAvailabilityBatchView View() => new(BatchId, Status, Processed, Items.Count, ExpiresAt,
        Items.ConvertAll(i => i.Row), "SourceAvailability:" + BatchId);
}
