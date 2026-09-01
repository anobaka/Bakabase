using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Fs;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Workflow.Activities.Actions;

/// <summary>
/// Records the rename an upstream chain computed — WorkingName vs OriginalName — as a
/// <c>FileRenameEntry</c> plan row. This batch is preview-only: nothing here touches the disk;
/// apply/undo arrive with the two-phase batch and operate on these same rows by status.
///
/// Built-in, non-optional defenses (docs/file-cleaning-workflow.html §3.5): sanitize, duplicate
/// target detection (within the plan and against the disk), and path-length precheck. A conflict
/// becomes a Conflict row rather than a failed run — one bad name must not abort the other 199.
/// </summary>
public class FsSaveNameActivity : IWorkflowActivity
{
    /// <summary>Windows MAX_PATH minus the NUL — names planned above this break the moment the
    /// library meets a Windows machine, so the check applies on every platform.</summary>
    private const int MaxPathLength = 259;

    public string Kind { get; } = FsWorkflowKinds.ActionSaveName;
    public string DisplayName => "Save file name (plan)";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
    public string Group => WorkflowActivityGroups.Fs;
    public IReadOnlyList<string> AcceptedInputItemTypes => [WorkflowItemTypes.FsEntry];

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        if (item is not FsEntryItem fs)
        {
            throw new InvalidOperationException(
                $"{Kind} received a {item.GetType().Name}, not a {nameof(FsEntryItem)}.");
        }

        if (fs.WorkingName == fs.OriginalName)
        {
            return WorkflowItemOutcome.KeepItem;
        }

        // Own scope, per the engine rule (capability map §5·发现 6): the run-wide scope's
        // DbContext holds the runner's tracked rows, and saving through it would flush them
        // mid-run. Writes from activities go through a child scope, always.
        await using var scope = ctx.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        var entries = scope.ServiceProvider.GetRequiredService<IFileRenameEntryService>();

        var sanitized = FileNameSanitizer.Sanitize(fs.WorkingName);
        if (sanitized == fs.OriginalName)
        {
            // The change the chain made didn't survive sanitizing — nothing to plan.
            return WorkflowItemOutcome.KeepItem;
        }

        // ctx widens run ids to long; the store keys them as the int they are in the DB.
        var runId = checked((int) ctx.RunId);
        var (status, error) = await Judge(runId, fs, sanitized, entries);
        await entries.AddToPlan(runId, fs.Path, fs.OriginalName, sanitized, status, error);
        return WorkflowItemOutcome.KeepItem;
    }

    private static async Task<(FileRenameStatus Status, string? Error)> Judge(int runId, FsEntryItem fs,
        string sanitized, IFileRenameEntryService entries)
    {
        if (sanitized.Length == 0)
        {
            return (FileRenameStatus.Conflict, "The new name is empty after removing invalid characters.");
        }

        var parent = Path.GetDirectoryName(fs.Path);
        if (parent == null)
        {
            return (FileRenameStatus.Conflict, "The entry has no parent directory.");
        }

        var target = Path.Combine(parent, sanitized);
        if (target.Length > MaxPathLength)
        {
            return (FileRenameStatus.Conflict, $"The new path would be {target.Length} characters — over the {MaxPathLength} limit.");
        }

        // A case-only rename is legitimate; anything else that already exists on disk is a
        // conflict now rather than a failure at apply time.
        var caseOnly = string.Equals(target, fs.Path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target)))
        {
            return (FileRenameStatus.Conflict, "An entry with the new name already exists.");
        }

        if (await entries.IsTargetPlanned(runId, target))
        {
            return (FileRenameStatus.Conflict, "Another rename in this run already targets the same name.");
        }

        return (FileRenameStatus.Pending, null);
    }
}
