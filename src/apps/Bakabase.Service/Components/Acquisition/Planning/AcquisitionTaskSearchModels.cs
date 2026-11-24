using System.Collections.Generic;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionTaskSearchInput(string? Keyword = null, string Stage = "all", string? AttentionTag = null,
    int Page = 1, int PageSize = 24, string? Sort = null, IReadOnlyList<string>? AttentionTags = null,
    IReadOnlyList<string>? Facts = null, string? DownloadSource = null, string? CapabilityId = null,
    string? ExecutionState = null);

/// <summary>Current capability projection supplied by the activity runtime provider.
/// This is deliberately ephemeral and is never persisted as acquisition state.</summary>
public sealed record AcquisitionTaskRuntimeFacet
{
    public string ActivityKind { get; init; } = "";
    public string? CapabilityId { get; init; }
    public string? CapabilityName { get; init; }
    public string? CapabilityNameKey { get; init; }
    public string? ExecutionState { get; init; }
    public string? WaitReason { get; init; }
    public string? ExecutionPolicy { get; init; }
    public int? MaximumParallelism { get; init; }
}
/// <summary>Stored measured evidence, not a prediction of archive expansion. Download references
/// describe acquired inputs; partial values cover only the measured portion of a task.</summary>
public sealed record AcquisitionSizeEstimate(long? Bytes, string Basis, bool Partial = false);
public sealed record AcquisitionTaskPage
{
    public IReadOnlyList<AcquisitionTask> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int TotalTasks { get; init; }
    /// <summary>Distinct non-deleted tasks with an unfinished current processing queue entry,
    /// including paused/space-blocked entries. Independent of list filters; history is excluded.</summary>
    public int QueueTaskCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    /// <summary>Global navigation counts before list filters. Cancelled history is excluded.</summary>
    public IReadOnlyDictionary<string, int> StageCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Distinct tasks matching every filter except download source, before paging. The "all" count
    /// includes tasks with no recognized source; multipart tasks may contribute to several source keys.</summary>
    public IReadOnlyDictionary<string, int> DownloadSourceCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Reason counts within keyword, stage, source, capability and execution state, before reason/fact selection. Tags may overlap.</summary>
    public IReadOnlyDictionary<string, int> AttentionCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Recorded execution facts in the same scope as the reason counts, before reason/fact selection.</summary>
    public IReadOnlyDictionary<string, int> FactCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Current provider-owned capability facets within keyword/stage/source scope,
    /// before capability, state, reason and fact selection.</summary>
    public IReadOnlyDictionary<string, int> RuntimeCapabilityCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> RuntimeExecutionStateCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<AcquisitionTaskRuntimeFacet> RuntimeCapabilities { get; init; } = [];
    /// <summary>Current runtime projections for the visible page only. Missing entries mean
    /// no current workflow node or no provider projection was available.</summary>
    public IReadOnlyDictionary<int, AcquisitionTaskRuntimeFacet> TaskRuntimeFacets { get; init; } =
        new Dictionary<int, AcquisitionTaskRuntimeFacet>();
    /// <summary>Current-page evidence only. Stored completion receipts do not establish that
    /// files still exist or pass content verification; continuation checks them again.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> TaskFacts { get; init; } = new Dictionary<int, IReadOnlyList<string>>();
    public IReadOnlyList<int> FilteredTaskIds { get; init; } = [];
    public IReadOnlyList<int> CheckableTaskIds { get; init; } = [];
    public bool TaskIdsTruncated { get; init; }
    public IReadOnlyDictionary<int, AcquisitionSizeEstimate> SizeEstimates { get; init; } = new Dictionary<int, AcquisitionSizeEstimate>();
}
