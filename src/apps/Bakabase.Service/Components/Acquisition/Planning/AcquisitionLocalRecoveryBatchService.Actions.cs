using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed partial class AcquisitionLocalRecoveryBatchService
{
    private const string ContinueAction = "continue";

    private static AcquisitionBatchActionSelection[] Selections(IReadOnlyList<int>? ids,
        IReadOnlyList<AcquisitionBatchActionSelection>? selections)
    {
        if (selections == null)
            return Ids(ids).Order().Select(id => new AcquisitionBatchActionSelection(id, ContinueAction)).ToArray();
        if (selections.Count is 0 or > 100 || selections.Any(s => s == null || s.TaskId < 1 ||
                s.Action is not {Length: > 0 and <= 128}) ||
            selections.Select(s => s.TaskId).Distinct().Count() != selections.Count ||
            ids != null && !Ids(ids).Order().SequenceEqual(selections.Select(s => s.TaskId).Order()))
            throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
        return selections.OrderBy(s => s.TaskId).ToArray();
    }

    private static AcquisitionLocalRecoveryBatchPreviewStore.Entry ForSelection(AcquisitionLocalRecoveryBatchPreviewStore.Entry entry,
        IReadOnlyList<AcquisitionBatchActionSelection> selections)
    {
        var evidence = new Dictionary<int, AcquisitionLocalRecoveryBatchPreviewStore.Evidence>();
        foreach (var row in selections)
        {
            var pinned = row.Action == ContinueAction ? entry.Evidence.GetValueOrDefault(row.TaskId) :
                entry.Alternatives.GetValueOrDefault((row.TaskId, row.Action));
            if (pinned == null) throw new ArgumentException("LOCAL_BATCH_SELECTION_INVALID");
            evidence.Add(row.TaskId, pinned);
        }
        return entry with {Evidence = evidence};
    }

    private async Task<Checked> CheckActionAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        AcquisitionSizeEstimate estimate, string mode, string action, int maxIterations, bool verifyFiles,
        bool restricted, Func<string?, Task>? guardDirectory, CancellationToken ct)
    {
        Checked Block(string reason) => new(new(task.Id, task.Title, run?.Id, false, reason, estimate.Bytes,
            estimate.Partial, null, null, null));
        if (action == ContinueAction)
            return await CheckAsync(task, run, estimate, ct, guardDirectory, mode, verifyFiles, restricted);
        if (mode != "automatic" || task.DeletedAt != null || task.Status is AcquisitionStatus.Completed or AcquisitionStatus.Cancelled ||
            IsExecuting(run) || continuations == null || !_actions.TryGetValue(action, out var provider))
            return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
        try
        {
            string[]? paths = null;
            if (restricted)
            {
                paths = await continuations.CapturePaths(task, run, "{}", ct);
                await continuations.Guard(paths, true, ct);
                foreach (var path in paths) if (guardDirectory != null) await guardDirectory(path);
            }
            var offer = await provider.PreviewAsync(task, run, maxIterations, ct);
            if (offer is AcquisitionBatchWorkflowActionOffer workflow)
            {
                if (!workflow.Diagnosis.Row.CanContinue) return Block(workflow.Diagnosis.Row.ReasonCode);
                return await CheckAsync(task, run, estimate, ct, guardDirectory, mode, verifyFiles, restricted, workflow.Diagnosis, action);
            }
            if (provider is not IAcquisitionBatchFileActionProvider || offer is not AcquisitionBatchFileActionOffer analysis)
                return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");

            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "{}", WorkflowJson.Options);
            if (plan?.Parts is not {Count: > 0 and <= 100}) return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");
            string[] working, delivery;
            if (analysis.UseCurrentStorage)
            {
                var destinations = plans.CaptureLocalFilesStorage(task);
                working = [destinations.Policy.ProcessingWorkspaceDirectory];
                delivery = [destinations.Delivery.RootDirectory];
            }
            else
            {
                working = plan.Parts.Select(p => ArtifactStoragePolicies.Resolve(plan.StoragePolicy ?? new(), p.StoragePolicy)
                        .ProcessingWorkspaceDirectory).Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(ArtifactManifestFiles.PathComparer).ToArray();
                delivery = plan.DeliveryLayout?.PartDirectories.Values.Distinct(ArtifactManifestFiles.PathComparer).ToArray() ?? [];
            }
            paths ??= await continuations.CapturePaths(task, run, "{}", ct);
            await continuations.Guard(paths.Concat(working).Concat(delivery), restricted, ct);
            foreach (var path in paths.Concat(working).Concat(delivery).Distinct(ArtifactManifestFiles.PathComparer))
                if (guardDirectory != null) await guardDirectory(path);
            foreach (var path in working.Concat(delivery))
            {
                storage.EnsurePathAllowed(path);
                ArtifactManifestFiles.RejectLinks(path);
            }
            return new(new(task.Id, task.Title, run?.Id, true, analysis.ReadyReason, estimate.Bytes, true,
                    action, working.FirstOrDefault(), delivery.FirstOrDefault(), action),
                new(AcquisitionLocalRecoveryQueueEntry.ContentVersion(task, run), analysis.Fingerprint, action,
                    Hash(new {working, delivery}), working, delivery, true, mode, action, AuthorizationStamp: Hash(paths),
                    Restricted: restricted));
        }
        catch (UnauthorizedAccessException) {return Block("LOCAL_BATCH_PATH_FORBIDDEN");}
        catch (Exception e) when (e is JsonException or ArgumentException or IOException or InvalidOperationException)
        {return Block("RECOVERY_CHECKPOINT_UNSUPPORTED");}
    }
}
