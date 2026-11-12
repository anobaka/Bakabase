using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionContinuationPreviewInput(IReadOnlyList<int> TaskIds, string Scope, string Order = "selection");
public sealed record AcquisitionContinuationApplyInput(string PreviewToken, IReadOnlyList<int> TaskIds, bool? UseEmergencySpaceReserve = null);
public sealed record AcquisitionContinuationPreviewRow(int TaskId, string? Title, int? WorkflowRunId,
    int PlanRevision, bool CanQueue, string ReasonCode, bool CanOverrideStorage = false);
public sealed record AcquisitionContinuationPreview(string PreviewToken, DateTimeOffset ExpiresAt, string Scope,
    string Order, IReadOnlyList<AcquisitionContinuationPreviewRow> Rows);
public sealed record AcquisitionContinuationRow(int TaskId, string? Title, int Order, string Status,
    int? WorkflowRunId, string? ReasonCode);
public sealed record AcquisitionContinuation(string BatchId, string Scope, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, string Status, IReadOnlyList<AcquisitionContinuationRow> Rows);

/// <summary>This is a durable request to check and submit continuation, NOT a local-files
/// execution queue and NOT evidence that a resource has been delivered.</summary>
internal sealed record AcquisitionContinuationEntry
{
    public required string BatchId { get; init; }
    public required string PreviewToken { get; init; }
    public required string Scope { get; init; }
    public required string Version { get; init; }
    public required string Settings { get; init; }
    public required string[] Paths { get; init; }
    public bool Restricted { get; init; }
    public bool? UseEmergencySpaceReserve { get; init; }
    public int? WorkflowRunId { get; init; }
    public int PlanRevision { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public int Order { get; init; }
    public string Status { get; init; } = "queued";
    public string? ReasonCode { get; init; }
    internal static readonly string[] OpenStatuses = ["queued", "checking"];
    internal bool IsOpen => OpenStatuses.Contains(Status);
    internal static AcquisitionContinuationEntry? Read(AcquisitionTaskDbModel task)
    {
        try {return JsonNode.Parse(task.BuilderStateJson ?? "{}")?["continuationRequest"]?.Deserialize<AcquisitionContinuationEntry>(WorkflowJson.Options);}
        catch (Exception e) when (e is JsonException or InvalidOperationException) {return null;}
    }
    internal static string Write(string? json, AcquisitionContinuationEntry entry)
    {
        var root = JsonNode.Parse(json ?? "{}") as JsonObject ?? throw new ArgumentException("ACQUISITION_CHECKPOINT_INVALID");
        if (root["continuationRequest"] is { } old && old["batchId"]?.GetValue<string>() != entry.BatchId)
        {
            var history = root["continuationRequestHistory"] as JsonArray ?? new JsonArray();
            if (history.Parent == null) root["continuationRequestHistory"] = history;
            history.Add(old.DeepClone());
        }
        root["continuationRequest"] = JsonSerializer.SerializeToNode(entry, WorkflowJson.Options);
        return root.ToJsonString(WorkflowJson.Options);
    }
    internal static string ContentVersion(AcquisitionTaskDbModel task, WorkflowRunDbModel? run)
    {
        var state = JsonNode.Parse(task.BuilderStateJson ?? "{}") as JsonObject ?? new();
        state.Remove("continuationRequest"); state.Remove("continuationRequestHistory");
        return AcquisitionRecoveryService.Version(task with {BuilderStateJson = state.ToJsonString(WorkflowJson.Options)}, run);
    }
}

public sealed class AcquisitionContinuationPreviewStore
{
    internal sealed record Evidence(string Version, string Settings, string[] Paths, bool Restricted);
    internal sealed record Entry(AcquisitionContinuationPreview Preview, IReadOnlyDictionary<int, Evidence> Evidence)
    {
        internal string? BatchId;
        internal int[]? Selected;
        internal bool? UseEmergencySpaceReserve;
    }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries.Where(p => p.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow)) _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 50) throw new ArgumentException("RECOVERY_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewToken] = entry;
    }
    internal Entry Get(string token)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var result) || result.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("RECOVERY_PREVIEW_EXPIRED");
        return result;
    }
}
