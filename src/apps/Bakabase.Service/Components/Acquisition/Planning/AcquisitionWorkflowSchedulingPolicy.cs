using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Dynamic acquisition plans share the configured execution pool across starts,
/// retries and startup recovery. A waiting workflow releases its BTask slot naturally.</summary>
public sealed class AcquisitionWorkflowSchedulingPolicy(IBOptions<AcquisitionOptions> options)
    : IWorkflowRunSourceSchedulingPolicy
{
    public const string Group = "acquisition.workflow";

    public bool AppliesTo(string triggerKind, IReadOnlyList<WorkflowSourceRef> sources) =>
        triggerKind == CloudWorkflow.Trigger && sources.Any(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && int.TryParse(s.EntityId, NumberStyles.None,
                CultureInfo.InvariantCulture, out var id) && id > 0);

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) =>
        builder.WithConcurrencyLimit(Group, () => Math.Clamp(options.Value.Concurrency,
            AcquisitionOptions.MinimumConcurrency, AcquisitionOptions.MaximumConcurrency));

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder, string? planSnapshotJson, int? currentStepIndex)
    {
        var configured = Configure(builder);
        try
        {
            var plan = WorkflowRunPlans.Read(planSnapshotJson);
            if (plan?.TriggerKind == CloudWorkflow.Trigger && AcquisitionLocalRecoveryBatchService.IsLocalRemainder(
                    plan, currentStepIndex ?? 0, allowContinueFiles: true, allowDownloadWait: false))
                return configured with {CanShareConcurrencySlotWith = IsWaitingForDownload};
        }
        catch (InvalidDataException) { /* An unverified snapshot can only use the ordinary pool. */ }
        return configured;
    }

    private static bool IsWaitingForDownload(BTask task) =>
        task.Status == BTaskStatus.Running &&
        task.Data is WorkflowTaskProgress {ActivityKind: "download.awaitCompletion", Stage: "downloading"} progress &&
        task.Id == $"workflow.run.{progress.WorkflowRunId}" && task.StartedAt <= progress.ReportedAt;
}
