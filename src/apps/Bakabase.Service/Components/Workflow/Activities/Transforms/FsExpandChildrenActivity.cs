using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Fs;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms;

/// <summary>
/// The chain's descent step (capability map E2, design §3.4 "进目录，对它的子文件继续处理"): a
/// directory item expands into its direct children, each inheriting a copy of the parent's
/// variable bag — which is exactly how a season captured from the directory level reaches the
/// episode files below it. Non-directory items pass through untouched, so a mixed stream
/// survives the step. The directory itself leaves the chain unless <c>IncludeSelf</c> keeps it.
/// </summary>
public class FsExpandChildrenActivity : IWorkflowActivity
{
    public string Kind { get; } = FsWorkflowKinds.TransformExpandChildren;
    public string DisplayName => "Expand into children";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Fs;
    public IReadOnlyList<string> AcceptedInputItemTypes => [WorkflowItemTypes.FsEntry];
    public WorkflowActivityCardinality Cardinality => WorkflowActivityCardinality.OneToMany;

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        if (item is not FsEntryItem fs)
        {
            throw new InvalidOperationException(
                $"{Kind} received a {item.GetType().Name}, not a {nameof(FsEntryItem)}.");
        }

        if (!fs.IsDirectory)
        {
            return Task.FromResult(WorkflowItemOutcome.KeepItem);
        }

        var config = ctx.GetConfig<Config>() ?? new Config();
        var extensions = (config.ExtensionFilter ?? [])
            .Select(e => e.Trim().TrimStart('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet();

        var children = new List<object>();
        if (config.IncludeSelf)
        {
            children.Add(fs);
        }

        // The DISK path is enumerated — the item's recorded path — not the working name: the
        // chain may have rewritten the name, but nothing has moved until saveName applies.
        // Sorted for a deterministic plan, same as the scan trigger.
        foreach (var path in Directory.EnumerateFileSystemEntries(fs.Path)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var isDirectory = Directory.Exists(path);
            var wanted = isDirectory
                ? config.Target is FsScanTarget.Directories or FsScanTarget.Both
                : (config.Target is FsScanTarget.Files or FsScanTarget.Both) &&
                  (extensions.Count == 0 ||
                   extensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant()));
            if (!wanted) continue;

            var name = Path.GetFileName(path);
            children.Add(new FsEntryItem
            {
                Path = path,
                IsDirectory = isDirectory,
                OriginalName = name,
                WorkingName = name
            });
        }

        return Task.FromResult(WorkflowItemOutcome.ExpandTo(children));
    }

    public record Config
    {
        /// <summary>What kinds of children to emit.</summary>
        public FsScanTarget Target { get; init; } = FsScanTarget.Files;

        /// <summary>Extensions (without dot, case-insensitive) files must match; empty = all.
        /// Directories always pass.</summary>
        public List<string>? ExtensionFilter { get; init; }

        /// <summary>Keep the directory itself in the chain alongside its children, so its own
        /// rename still happens downstream.</summary>
        public bool IncludeSelf { get; init; }
    }
}
