using System.Collections.Generic;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionTaskSearchInput(string? Keyword = null, string Stage = "all", string? AttentionTag = null,
    int Page = 1, int PageSize = 24, string? Sort = null, IReadOnlyList<string>? AttentionTags = null,
    IReadOnlyList<string>? Facts = null);
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
    /// <summary>Global counts, independent of keyword and attention filtering; cancelled history is excluded.</summary>
    public IReadOnlyDictionary<string, int> StageCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Counts within keyword and stage, before the selected attention tag; tags may overlap.</summary>
    public IReadOnlyDictionary<string, int> AttentionCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Recorded execution facts within keyword/stage, before reason/fact filters.</summary>
    public IReadOnlyDictionary<string, int> FactCounts { get; init; } = new Dictionary<string, int>();
    /// <summary>Current-page evidence only. Stored completion receipts do not establish that
    /// files still exist or pass content verification; continuation checks them again.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> TaskFacts { get; init; } = new Dictionary<int, IReadOnlyList<string>>();
    public IReadOnlyList<int> FilteredTaskIds { get; init; } = [];
    public IReadOnlyList<int> CheckableTaskIds { get; init; } = [];
    public bool TaskIdsTruncated { get; init; }
    public IReadOnlyDictionary<int, AcquisitionSizeEstimate> SizeEstimates { get; init; } = new Dictionary<int, AcquisitionSizeEstimate>();
}
