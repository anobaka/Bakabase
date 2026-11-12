using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Roles belong to validated JSON locations, never to path strings or names.
/// Every unrecognised field, concrete input and nested source retains ancestor protection.</summary>
internal static class ArtifactAcquisitionAllocationReferences
{
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
        {UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow};
    internal sealed record TaskContext(int Id, int? RunId, int Revision, string? PlanJson, string? HistoryJson);
    private sealed record WorksetWaitPrompt(List<CloudDownloadTarget> Targets, string Reason, string OutputDirectory);

    internal static IReadOnlySet<JsonElement> PlanValues(JsonElement document, CancellationToken ct, int? expectedTaskId = null)
    {
        try
        {
            var plan = ReadPlan(document, ct);
            if (plan == null) return new HashSet<JsonElement>();
            var values = new HashSet<JsonElement> {Property(document, "outputDirectory")!.Value};
            values.UnionWith(DeliveryLayoutValues(document, expectedTaskId ?? plan.DeliveryLayout?.TaskId, plan.Parts.Select(p => p.Id).ToArray(), ct));
            return values;
        }
        catch (Exception e) when (Invalid(e)) { return new HashSet<JsonElement>(); }
    }

    // A validated layout allocates sibling task directories below its root. Its concrete
    // task/part destinations still claim files, and unknown layout members never acquire
    // allocation semantics merely by being named rootDirectory.
    private static IReadOnlySet<JsonElement> DeliveryLayoutValues(JsonElement document, int? taskId,
        IReadOnlyList<string>? partIds, CancellationToken ct)
    {
        try
        {
            if (taskId is not > 0 || partIds is not {Count: > 0} ||
                Property(document, "deliveryLayout") is not {ValueKind: JsonValueKind.Object} raw || !Unique(raw, ct)) return new HashSet<JsonElement>();
            var layout = JsonSerializer.Deserialize<AcquisitionDeliveryLayout>(raw.GetRawText(), Strict);
            if (layout == null || layout.TaskId != taskId) return new HashSet<JsonElement>();
            AcquisitionDeliveryDirectoryResolver.ValidateLayout(layout, partIds);
            var values = new HashSet<JsonElement>();
            if (Property(raw, "rootDirectory") is {ValueKind: JsonValueKind.String} root) values.Add(root);
            if (Property(raw, "settings") is {ValueKind: JsonValueKind.Object} settings &&
                Property(settings, "rootDirectory") is {ValueKind: JsonValueKind.String} configured) values.Add(configured);
            return values;
        }
        catch (Exception e) when (Invalid(e)) { return new HashSet<JsonElement>(); }
    }

    internal static IReadOnlySet<JsonElement> WorksetValues(JsonElement document, string field, string expectedDirectory, CancellationToken ct)
    {
        try
        {
            if (!Unique(document, ct)) return new HashSet<JsonElement>();
            var payload = document;
            if (field == "currentItem")
            {
                if (document.ValueKind != JsonValueKind.Object || document.EnumerateObject().Count() != 2 ||
                    Property(document, "type")?.GetString() != typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name ||
                    Property(document, "item") is not { } item) return new HashSet<JsonElement>();
                payload = item;
            }
            if (Property(payload, "outputDirectory") is not {ValueKind: JsonValueKind.String} output || output.GetString() != expectedDirectory ||
                Property(payload, "targets") is not {ValueKind: JsonValueKind.Array}) return new HashSet<JsonElement>();
            IReadOnlyList<CloudDownloadTarget>? targets;
            if (field == "waitPrompt")
            {
                if (Property(payload, "reason") is not {ValueKind: JsonValueKind.String}) return new HashSet<JsonElement>();
                targets = JsonSerializer.Deserialize<WorksetWaitPrompt>(payload.GetRawText(), Strict)?.Targets;
            }
            else
            {
                if (Property(payload, "id") is not {ValueKind: JsonValueKind.String} id || string.IsNullOrWhiteSpace(id.GetString()) ||
                    Property(payload, "sourceRefs") is not {ValueKind: JsonValueKind.Array}) return new HashSet<JsonElement>();
                targets = JsonSerializer.Deserialize<DownloadWorkset>(payload.GetRawText(), Strict)?.Targets;
            }
            if (targets is not {Count: > 0} || targets.Any(t => string.IsNullOrWhiteSpace(t.Id)) || targets.Select(t => t.Id).Distinct().Count() != targets.Count)
                return new HashSet<JsonElement>();
            return new HashSet<JsonElement> {output};
        }
        catch (Exception e) when (Invalid(e)) { return new HashSet<JsonElement>(); }
    }

    internal static IReadOnlySet<JsonElement> QueueValues(JsonElement document, TaskContext task,
        IReadOnlySet<(int TaskId, int RunId, int Revision)> ownedRevisions, CancellationToken ct)
    {
        var values = new HashSet<JsonElement>();
        if (!Unique(document, ct)) return values;
        try
        {
            using var planDocument = JsonDocument.Parse(task.PlanJson ?? "null");
            var plan = ReadPlan(planDocument.RootElement, ct);
            if (plan != null) values.UnionWith(DeliveryLayoutValues(document, task.Id, plan.Parts.Select(p => p.Id).ToArray(), ct));
        }
        catch (Exception error) when (Invalid(error)) { /* Unknown prepared plans retain all concrete references. */ }
        if (Property(document, "localRecoveryQueue") is {ValueKind: JsonValueKind.Object} current)
            values.UnionWith(QueueEntryValues(current, task, ownedRevisions, ct));
        if (Property(document, "localRecoveryQueueHistory") is {ValueKind: JsonValueKind.Array} history)
            foreach (var entry in history.EnumerateArray())
                values.UnionWith(QueueEntryValues(entry, task, ownedRevisions, ct));
        if (Property(document, "localFilesRecovery") is {ValueKind: JsonValueKind.Object} recovery)
            values.UnionWith(LocalFilesValues(recovery, task, ownedRevisions, ct));
        return values;
    }

    private static IReadOnlySet<JsonElement> QueueEntryValues(JsonElement raw, TaskContext task,
        IReadOnlySet<(int TaskId, int RunId, int Revision)> ownedRevisions, CancellationToken ct)
    {
        try
        {
            if (raw.ValueKind != JsonValueKind.Object) return new HashSet<JsonElement>();
            var entry = JsonSerializer.Deserialize<AcquisitionLocalRecoveryQueueEntry>(raw.GetRawText(), Strict);
            if (entry?.WorkflowRunId is not { } run || !Guid.TryParseExact(entry.BatchId, "N", out _) ||
                !Digest(entry.Version) || !Digest(entry.StorageStamp) || string.IsNullOrWhiteSpace(entry.Signal) ||
                entry.RecoveryScope is not ("workflow" or "localFiles") || entry.PlanRevision <= 0 || entry.PlanRevision > task.Revision ||
                entry.Status is not ("queued" or "starting" or "running" or "paused" or "spaceBlocked" or "attention" or "completed" or "cancelled" or "skipped") ||
                !ownedRevisions.Contains((task.Id, run, entry.PlanRevision)) || entry.WorkingDirectories is not {Count: > 0} ||
                entry.WorkingDirectories.Any(p => !Path.IsPathFullyQualified(p))) return new HashSet<JsonElement>();
            var plan = OwnedPlan(task, run, entry.PlanRevision, ownedRevisions, ct);
            if (plan?.StoragePolicy == null) return new HashSet<JsonElement>();
            var roots = plan.Parts.Select(part => ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy).ProcessingWorkspaceDirectory)
                .Where(p => !string.IsNullOrWhiteSpace(p)).ToHashSet(StringComparer.Ordinal);
            if (!roots.SetEquals(entry.WorkingDirectories) || Property(raw, "workingDirectories") is not {ValueKind: JsonValueKind.Array} paths)
                return new HashSet<JsonElement>();
            // A typed queue's captured policy roots allocate sibling workspaces. Its
            // deliveryDirectories, signal, inputs and any unknown JSON remain references.
            return paths.EnumerateArray().ToHashSet();
        }
        catch (Exception e) when (Invalid(e)) { return new HashSet<JsonElement>(); }
    }

    private static IReadOnlySet<JsonElement> LocalFilesValues(JsonElement raw, TaskContext task,
        IReadOnlySet<(int TaskId, int RunId, int Revision)> ownedRevisions, CancellationToken ct)
    {
        try
        {
            var receipt = JsonSerializer.Deserialize<AcquisitionLocalFilesRecoveryReceipt>(raw.GetRawText(), Strict);
            // A directory in a verified adoption receipt is an allocation only after the
            // resulting local plan records the same input identities and policy root.
            if (receipt?.ResultingRevision is not { } revision || revision != receipt.PreviousRevision + 1 ||
                revision > task.Revision || receipt.PreviousRevision <= 0 || !Digest(receipt.Checkpoint) ||
                receipt.Storage?.Policy == null || receipt.Parts is not {Count: > 0} ||
                receipt.Preview?.Parts is not {Count: > 0})
                return new HashSet<JsonElement>();
            var original = OwnedPlan(task, receipt.PreviousRunId, receipt.PreviousRevision, ownedRevisions, ct);
            if (original == null) return new HashSet<JsonElement>();
            var runs = ownedRevisions.Where(o => o.TaskId == task.Id && o.Revision == revision).ToArray();
            if (runs.Length != 1) return new HashSet<JsonElement>();
            var plan = OwnedPlan(task, runs[0].RunId, revision, ownedRevisions, ct);
            var history = ReadHistory(task, ct);
            if (plan?.StoragePolicy == null || history?.SingleOrDefault(h => h.Revision == revision)?.PreviousRunId != receipt.PreviousRunId ||
                receipt.Parts.Any(p => p == null || string.IsNullOrWhiteSpace(p.PartId)) ||
                receipt.Preview.Parts.Any(p => p == null || string.IsNullOrWhiteSpace(p.PartId)) ||
                receipt.Parts.Select(p => p.PartId).Distinct().Count() != receipt.Parts.Count ||
                receipt.Preview.Parts.Select(p => p.PartId).Distinct().Count() != receipt.Preview.Parts.Count ||
                !plan.Parts.Select(p => p.Id).ToHashSet().SetEquals(receipt.Parts.Select(p => p.PartId)) ||
                !plan.Parts.Select(p => p.Id).ToHashSet().SetEquals(receipt.Preview.Parts.Select(p => p.PartId)))
                return new HashSet<JsonElement>();
            foreach (var proof in receipt.Parts)
            {
                var preview = receipt.Preview.Parts.Single(p => p.PartId == proof.PartId);
                var part = plan.Parts.Single(p => p.Id == proof.PartId);
                if (proof.DownloadTaskId <= 0 || proof.DownloadResultId <= 0 || string.IsNullOrWhiteSpace(proof.DownloadIntent) ||
                    !Digest(proof.DownloadEvidence) || !Digest(proof.Fingerprint) || string.IsNullOrWhiteSpace(proof.ArtifactId) ||
                    string.IsNullOrWhiteSpace(proof.OperationId) || preview.DownloadTaskId != proof.DownloadTaskId ||
                    preview.DownloadResultId != proof.DownloadResultId || preview.ArtifactId != proof.ArtifactId ||
                    preview.Fingerprint != proof.Fingerprint || preview.FileCount <= 0 ||
                    part.ContinueFrom != new AcquisitionPlanArtifactInput(task.Id, receipt.PreviousRunId, proof.ArtifactId, proof.OperationId, proof.Fingerprint) ||
                    !Path.IsPathFullyQualified(preview.WorkingDirectory) || preview.WorkingDirectory != receipt.Storage.Policy.ProcessingWorkspaceDirectory ||
                    preview.WorkingDirectory != ArtifactStoragePolicies.Resolve(plan.StoragePolicy, part.StoragePolicy).ProcessingWorkspaceDirectory)
                    return new HashSet<JsonElement>();
            }
            var parts = Property(Property(raw, "preview")!.Value, "parts")!.Value;
            var values = parts.EnumerateArray().Select(p => Property(p, "workingDirectory")!.Value).ToHashSet();
            // The receipt freezes the caller's delivery settings as well as its
            // processing policy. Only the validated configuration root allocates
            // siblings; each preview part's actual deliveryDirectory remains concrete.
            AcquisitionDeliveryDirectoryResolver.ValidateSettings(receipt.Storage.Delivery);
            if (Path.IsPathFullyQualified(receipt.Storage.Delivery.RootDirectory) &&
                Property(Property(raw, "storage")!.Value, "delivery") is {ValueKind: JsonValueKind.Object} delivery &&
                Property(delivery, "rootDirectory") is {ValueKind: JsonValueKind.String} deliveryRoot)
                values.Add(deliveryRoot);
            // A recovery preview of a legacy plan with no delivery layout displayed
            // the old workset's download allocation in this field. An actual old
            // delivery destination or a different path still protects its full tree.
            if (original.DeliveryLayout == null && original.Parts.Select(p => p.Id).ToHashSet().SetEquals(plan.Parts.Select(p => p.Id)))
                foreach (var part in parts.EnumerateArray())
                    if (Property(part, "originalDeliveryDirectory") is { ValueKind: JsonValueKind.String } previous &&
                        previous.GetString() == original.OutputDirectory)
                        values.Add(previous);
            return values;
        }
        catch (Exception e) when (Invalid(e)) { return new HashSet<JsonElement>(); }
    }

    private static AcquisitionPlan? OwnedPlan(TaskContext task, int run, int revision,
        IReadOnlySet<(int TaskId, int RunId, int Revision)> owners, CancellationToken ct)
    {
        if (revision <= 0 || revision > task.Revision || !owners.Contains((task.Id, run, revision))) return null;
        if (revision == task.Revision && run == task.RunId)
        { using var current = JsonDocument.Parse(task.PlanJson ?? "null"); return ReadPlan(current.RootElement, ct); }
        var history = ReadHistory(task, ct);
        var recorded = history?.SingleOrDefault(h => h.Revision == revision);
        if (recorded == null || !history!.Any(h => h.Revision > revision && h.PreviousRunId == run)) return null;
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(recorded.Plan, Strict));
        return ReadPlan(document.RootElement, ct);
    }

    private static List<AcquisitionPlanRevision>? ReadHistory(TaskContext task, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(task.HistoryJson ?? "[]");
        if (!Unique(document.RootElement, ct)) return null;
        var history = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(document.RootElement.GetRawText(), Strict);
        return history != null && history.All(h => h != null && h.Revision > 0 && h.Revision <= task.Revision) &&
            history.Select(h => h.Revision).Distinct().Count() == history.Count ? history : null;
    }

    internal static string? OwnedPlanDirectory(TaskContext task, int run, int revision,
        IReadOnlySet<(int TaskId, int RunId, int Revision)> owners, CancellationToken ct)
    {
        try { return OwnedPlan(task, run, revision, owners, ct)?.OutputDirectory; }
        catch (Exception e) when (Invalid(e)) { return null; }
    }
    private static AcquisitionPlan? ReadPlan(JsonElement document, CancellationToken ct)
    {
        if (!Unique(document, ct) || Property(document, "version") is not {ValueKind: JsonValueKind.Number} version || !version.TryGetInt32(out var number) || number != 1 ||
            Property(document, "parts") is not {ValueKind: JsonValueKind.Array} || Property(document, "outputDirectory") is not {ValueKind: JsonValueKind.String}) return null;
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(document.GetRawText(), Strict);
        return plan is {Version: 1, Parts.Count: > 0} && !string.IsNullOrWhiteSpace(plan.Title) && Path.IsPathFullyQualified(plan.OutputDirectory) &&
            plan.Parts.All(p => p != null && !string.IsNullOrWhiteSpace(p.Id)) && plan.Parts.Select(p => p.Id).Distinct().Count() == plan.Parts.Count ? plan : null;
    }
    private static bool Digest(string? value) => value is {Length: 64} && value.All(Uri.IsHexDigit);
    private static bool Invalid(Exception error) => error is JsonException or ArgumentException or InvalidOperationException or NotSupportedException;
    private static JsonElement? Property(JsonElement value, string name) => value.ValueKind != JsonValueKind.Object ? null :
        value.EnumerateObject().Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).SingleOrDefault();
    private static bool Unique(JsonElement value, CancellationToken ct, int depth = 0)
    {
        ct.ThrowIfCancellationRequested(); if (depth > 64) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in value.EnumerateObject()) if (!names.Add(p.Name) || !Unique(p.Value, ct, depth + 1)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var element in value.EnumerateArray()) if (!Unique(element, ct, depth + 1)) return false;
        return true;
    }
}
