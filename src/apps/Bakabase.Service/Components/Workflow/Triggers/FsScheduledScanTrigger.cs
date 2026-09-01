using System;
using System.Collections.Generic;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Workflow.Fs;

namespace Bakabase.Service.Components.Workflow.Triggers;

/// <summary>
/// The manual scan on a clock (capability map E6): same roots/target/depth/extension
/// configuration, plus an interval. The scheduler asks <see cref="GetInterval"/> and starts a
/// run through the same payload-building path a manual click uses, so scheduled and manual runs
/// of one definition are byte-identical from the runner's point of view.
/// </summary>
public class FsScheduledScanTrigger : IWorkflowScheduledTrigger
{
    /// <summary>The scan config plus the schedule. Kept assignment-compatible with
    /// <see cref="FsManualScanPayload"/> so the scan logic is shared verbatim.</summary>
    public record FsScheduledScanFilter : FsManualScanPayload
    {
        /// <summary>How often to run. Values below 1 disable the schedule rather than error —
        /// an unconfigured schedule is a manual-only definition, not a broken one.</summary>
        public int IntervalMinutes { get; init; }
    }

    private readonly FsManualScanTrigger _scan = new();

    public string Kind { get; } = FsWorkflowKinds.TriggerScheduledScan;
    public string DisplayName => "Scheduled filesystem scan";
    public Type PayloadType => typeof(FsManualScanPayload);

    // Nothing publishes this kind either — runs come from the scheduler and the manual button.
    public bool Matches(object payload, string? triggerFilterJson) => false;

    public bool RequiresManualPayload => false;

    /// <summary>Validation is the manual scan's — extra JSON fields (the interval) pass through
    /// deserialization untouched.</summary>
    public object BuildManualPayload(string? triggerFilterJson, string? argsJson) =>
        _scan.BuildManualPayload(triggerFilterJson, argsJson);

    public IReadOnlyList<object> ExtractItems(object payload) => _scan.ExtractItems(payload);

    public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.FsEntry;

    public TimeSpan? GetInterval(string? triggerFilterJson)
    {
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return null;
        try
        {
            var filter = JsonSerializer.Deserialize<FsScheduledScanFilter>(
                triggerFilterJson, WorkflowJson.Options);
            return filter is {IntervalMinutes: >= 1}
                ? TimeSpan.FromMinutes(filter.IntervalMinutes)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
