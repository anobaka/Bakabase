using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Abstractions;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Fs;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms;

/// <summary>
/// The existing FileNameModifier rule engine (the file-name-modifier page's operations: insert,
/// delete, replace, change case, …) exposed as a chain transform. It rewrites only the item's
/// working name — the disk is untouched until a downstream saveName.
/// Deliberately fs-domain rather than a generic text node: its Target semantics (extension vs
/// stem) ARE filename structure (docs/file-cleaning-workflow.html §3.0).
/// </summary>
public class FsFileNameOpActivity(IFileNameModifier modifier) : IWorkflowActivity
{
    public string Kind { get; } = FsWorkflowKinds.TransformFileNameOp;
    public string DisplayName => "Modify file name";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Fs;
    public IReadOnlyList<string> AcceptedInputItemTypes => [WorkflowItemTypes.FsEntry];
    // Passthrough: the item keeps its type; only WorkingName changes.

    public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
    {
        if (item is not FsEntryItem fs)
        {
            // The editor's typed-chain validation makes this unreachable; reaching it means an
            // engine bug, and failing loudly beats the silent pass-through older activities do.
            throw new InvalidOperationException(
                $"{Kind} received a {item.GetType().Name}, not a {nameof(FsEntryItem)}.");
        }

        var operations = ctx.GetConfig<Config>()?.Operations;
        if (operations is not {Count: > 0})
        {
            return Task.FromResult(WorkflowItemOutcome.KeepItem);
        }

        var newName = modifier.PreviewModification(fs.WorkingName, operations);
        return Task.FromResult(newName == fs.WorkingName
            ? WorkflowItemOutcome.KeepItem
            : WorkflowItemOutcome.ReplaceWith(fs with {WorkingName = newName}));
    }

    /// <summary>Config shape is the file-name-modifier page's own operation model, so the two
    /// UIs share one editor component and cannot drift.</summary>
    public record Config
    {
        public List<FileNameModifierOperation>? Operations { get; init; }
    }
}
