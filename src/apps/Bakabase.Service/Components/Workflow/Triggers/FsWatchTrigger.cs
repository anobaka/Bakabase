using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Workflow.Fs;

namespace Bakabase.Service.Components.Workflow.Triggers;

/// <summary>
/// The FileMover-intake shape (capability map E6, design §8): entries appearing in watched
/// directories become a run's items once they've sat still for a settle period — a file mid-copy
/// must not be renamed under the writer. The watch service does the watching, settling, and
/// per-definition filtering; it publishes one event per unique filter, carrying that filter's
/// verbatim JSON so <see cref="Matches"/> is a plain string comparison: definitions sharing an
/// identical filter share one event (each still gets its own run), and no definition can
/// receive entries filtered for someone else's configuration.
/// </summary>
public class FsWatchTrigger : IWorkflowTrigger
{
    /// <summary>What the user configures on the definition.</summary>
    public record FsWatchFilter
    {
        public List<string> Roots { get; init; } = [];
        public FsScanTarget Target { get; init; } = FsScanTarget.Both;

        /// <summary>Extensions (without dot, case-insensitive) files must match; empty = all.
        /// Directories always pass.</summary>
        public List<string> ExtensionFilter { get; init; } = [];

        /// <summary>Seconds an entry must remain quiet before it fires. Guards against
        /// renaming files still being written.</summary>
        public int SettleSeconds { get; init; } = 10;
    }

    /// <summary>What the watch service publishes.</summary>
    public record FsWatchPayload
    {
        /// <summary>The verbatim TriggerFilterJson the paths were filtered for.</summary>
        public string SourceFilterJson { get; init; } = "";

        public List<string> Paths { get; init; } = [];
    }

    public string Kind { get; } = FsWorkflowKinds.TriggerWatch;
    public string DisplayName => "Directory watch";
    public Type PayloadType => typeof(FsWatchPayload);

    public bool Matches(object payload, string? triggerFilterJson) =>
        payload is FsWatchPayload watch &&
        !string.IsNullOrEmpty(watch.SourceFilterJson) &&
        watch.SourceFilterJson == triggerFilterJson;

    // A manual run of a watch definition would need the user to type the "appeared" paths —
    // that's what fs.manualScan is for. Keep the manual door open for replaying though.
    public bool RequiresManualPayload => true;

    public object BuildManualPayload(string? triggerFilterJson, string? argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson))
        {
            throw new InvalidOperationException(
                "A manual run of a watch trigger needs a payload with the paths to process " +
                "(use a manual scan for scanning; this replays an appearance event).");
        }

        FsWatchPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FsWatchPayload>(argsJson, WorkflowJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The payload is not valid JSON: {ex.Message}", ex);
        }

        if (payload is not {Paths.Count: > 0})
        {
            throw new InvalidOperationException("The payload has no paths.");
        }

        return payload with {SourceFilterJson = triggerFilterJson ?? ""};
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not FsWatchPayload watch) return [];

        // Existence re-checked: an entry can vanish between settling and the run starting.
        return watch.Paths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(object (p) =>
            {
                var isDirectory = Directory.Exists(p);
                var name = Path.GetFileName(p);
                return new FsEntryItem
                {
                    Path = p,
                    IsDirectory = isDirectory,
                    OriginalName = name,
                    WorkingName = name
                };
            })
            .ToList();
    }

    public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.FsEntry;
}
