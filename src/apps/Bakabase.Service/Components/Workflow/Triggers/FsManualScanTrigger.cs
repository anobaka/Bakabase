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
/// The scan's parameters — the definition's trigger filter and the run payload share this
/// shape, because for a manual trigger the "filter" IS the configuration.
/// </summary>
public record FsManualScanPayload
{
    public List<string> Roots { get; init; } = [];
    public FsScanTarget Target { get; init; } = FsScanTarget.Both;

    /// <summary>1 = direct children of each root. Roots themselves are never items.</summary>
    public int Depth { get; init; } = 1;

    /// <summary>Extensions (without dot, case-insensitive) that files must match; empty = all.
    /// Applies to files only — directories always pass.</summary>
    public List<string> ExtensionFilter { get; init; } = [];
}

/// <summary>
/// The first manually-parameterized trigger: no event ever fires it, and a manual run needs no
/// user-typed payload — <see cref="BuildManualPayload"/> builds one from the definition's own
/// configuration, validating it in the request so a bad setup fails the click, not the run.
/// </summary>
public class FsManualScanTrigger : IWorkflowTrigger
{
    private const int MaxDepth = 32;

    public string Kind { get; } = FsWorkflowKinds.TriggerManualScan;
    public string DisplayName => "Manual filesystem scan";
    public Type PayloadType => typeof(FsManualScanPayload);

    // No producer publishes this kind; runs exist only through the manual path. Returning false
    // keeps a stray PublishAsync (a future bug) from fanning out to every enabled definition.
    public bool Matches(object payload, string? triggerFilterJson) => false;

    public bool RequiresManualPayload => false;

    public object BuildManualPayload(string? triggerFilterJson, string? argsJson)
    {
        FsManualScanPayload? config;
        try
        {
            config = string.IsNullOrWhiteSpace(triggerFilterJson)
                ? null
                : JsonSerializer.Deserialize<FsManualScanPayload>(triggerFilterJson, WorkflowJson.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"The scan configuration is not valid JSON: {ex.Message}", ex);
        }

        if (config == null || config.Roots.Count == 0)
        {
            throw new InvalidOperationException("The scan has no root directories configured.");
        }

        var roots = config.Roots
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var root in roots.Where(root => !Directory.Exists(root)))
        {
            throw new InvalidOperationException($"Root directory does not exist: {root}");
        }

        if (config.Depth is < 1 or > MaxDepth)
        {
            throw new InvalidOperationException($"Depth must be between 1 and {MaxDepth}.");
        }

        return config with
        {
            Roots = roots,
            ExtensionFilter = config.ExtensionFilter
                .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
                .Where(e => e.Length > 0)
                .Distinct()
                .ToList()
        };
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not FsManualScanPayload scan)
        {
            return [];
        }

        var extensions = scan.ExtensionFilter.ToHashSet();
        var items = new List<object>();
        foreach (var root in scan.Roots)
        {
            Walk(root, 1);
        }

        return items;

        void Walk(string dir, int level)
        {
            if (level > scan.Depth || !Directory.Exists(dir))
            {
                return;
            }

            // Sorted so a run's plan is deterministic for the same disk state.
            foreach (var path in Directory.EnumerateFileSystemEntries(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var isDirectory = Directory.Exists(path);
                var name = Path.GetFileName(path);

                var wanted = isDirectory
                    ? scan.Target is FsScanTarget.Directories or FsScanTarget.Both
                    : (scan.Target is FsScanTarget.Files or FsScanTarget.Both) &&
                      (extensions.Count == 0 ||
                       extensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()));
                if (wanted)
                {
                    items.Add(new FsEntryItem
                    {
                        Path = path,
                        IsDirectory = isDirectory,
                        OriginalName = name,
                        WorkingName = name
                    });
                }

                if (isDirectory)
                {
                    Walk(path, level + 1);
                }
            }
        }
    }

    public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.FsEntry;
}
