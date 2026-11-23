using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.CloudDownloads;

public sealed partial class CloudOperationService
{
    private const string TransferRecoveryKind = "transfer-recovery";
    private const string TransferRecoveryBlocked = "RECOVERY_TRANSFER_REVIEW_REQUIRED";

    public async Task<CloudTransferRecoveryCheck> InspectTransferRecoveryAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, bool verifyRemote, CancellationToken ct)
    {
        var saved = await ReadTransferRecoveryAsync(runId, target, accountId, remoteDirectory, ct);
        if (saved == null)
            return await IsFirstTransferAttemptAsync(runId, target, accountId, remoteDirectory, ct)
                ? new(CloudTransferRecoveryDisposition.FirstAttempt)
                : new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);
        return await InspectPersistedTransferRecoveryAsync(runId, target.Id, saved, verifyRemote, ct);
    }

    /// <summary>Persists one approved, isolated follow-up. The workflow worker performs and
    /// revalidates it using the same operation gate. The original journal and namespace are kept.</summary>
    public async Task<CloudTransferRecoveryCheck> AuthorizeTransferRecoveryAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, CancellationToken ct, string? expectedIsolatedOperationId = null)
    {
        if (target.SelectedSource is not {} source)
            return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);
        var id = TransferId(source, accountId, remoteDirectory);
        var gate = Gates.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var saved = await ReadTransferRecoveryAsync(runId, target, accountId, remoteDirectory, ct);
            if (saved == null) return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);
            var check = await InspectPersistedTransferRecoveryAsync(runId, target.Id, saved, true, ct);
            if (check.Disposition != CloudTransferRecoveryDisposition.RequiresConfirmation) return check;
            if (expectedIsolatedOperationId != check.NextIsolatedOperationId)
                return new(CloudTransferRecoveryDisposition.Unavailable, "RECOVERY_TRANSFER_EVIDENCE_CHANGED");
            var replacement = check.NextIsolatedOperationId is {} next
                ? saved.Request with {OperationId = next} : IsolatedTransferRequest(saved.Request);
            await GetOrCreate(replacement.OperationId, runId, target.Id, TransferRecoveryKind, replacement, ct);
            return new(CloudTransferRecoveryDisposition.Authorized);
        }
        finally { gate.Release(); }
    }

    private sealed record PersistedTransferRecovery(CloudOperationDbModel Original, CloudTransferRequest Request);

    private async Task<PersistedTransferRecovery?> ReadTransferRecoveryAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, CancellationToken ct)
    {
        var expected = EligibleTransferRequest(target, accountId, remoteDirectory);
        if (expected == null) return null;
        var owner = runId.ToString();
        var slot = OperationSlot(target, "transfer");
        if (!await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.Slot == slot && b.OperationId == expected.OperationId, ct)) return null;
        var original = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == expected.OperationId, ct);
        if (original is not {Kind: "transfer", DownloadTaskId: null} ||
            original.State is not ("failed" or "running" or "unknown" or "succeeded") ||
            !MatchesTransferRequest(original, expected, out var request)) return null;
        return new(original, request);
    }

    private async Task<bool> IsFirstTransferAttemptAsync(int runId, CloudDownloadTarget target,
        string accountId, string remoteDirectory, CancellationToken ct)
    {
        var expected = EligibleTransferRequest(target, accountId, remoteDirectory);
        if (expected?.Source is not {Kind: "share"} source ||
            !string.IsNullOrWhiteSpace(source.AccountId) && source.AccountId != accountId ||
            Provider(source) is not {SupportsTransfer: true} or not ICloudTransferRecoveryProvider)
            return false;
        var owner = runId.ToString();
        var slot = OperationSlot(target, "transfer");
        if (await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
            b.OwnerId == owner && b.Slot == slot && b.OperationId != expected.OperationId, ct)) return false;
        // The intended transfer is persisted before the provider writes. A target with no
        // conflicting binding and no transfer record can still have an unvisited sibling step.
        return !await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.Id == expected.OperationId ||
            o.WorkflowRunId == runId && o.TargetId == target.Id && (o.Kind == "transfer" || o.Kind == TransferRecoveryKind), ct);
    }

    private static CloudTransferRequest? EligibleTransferRequest(CloudDownloadTarget target, string accountId, string remoteDirectory)
    {
        if (target.Excluded || target.SelectedSource is not {} source || target.AccountId != accountId ||
            string.IsNullOrWhiteSpace(accountId) || target.DownloadTaskId != null || target.TransportTaskId != null ||
            target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null ||
            target.AcquisitionNonce != null || target.RemoteEntry != null || target.Files.Count != 0)
            return null;
        return TransferRequest(source, accountId, remoteDirectory);
    }

    private async Task<CloudTransferRecoveryCheck> InspectPersistedTransferRecoveryAsync(int runId, string targetId,
        PersistedTransferRecovery saved, bool verifyRemote, CancellationToken ct)
    {
        var (original, request) = saved;
        if (original.State == "succeeded")
        {
            try { return new(CloudTransferRecoveryDisposition.Reconcile, Receipt: ReadTransferReceipt(original, request)); }
            catch (Exception error) when (error is JsonException or InvalidOperationException)
            { return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked); }
        }
        if (original.ReceiptJson != null)
            return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);

        var provider = Provider(request.Source);
        if (original.Error is {} problem && (provider.CanCorrectAccessCodeAfterFailure(problem) ||
            provider is ICloudTransferRecoveryProvider classified && classified.IsTerminalTransferProblem(problem)))
            return new(CloudTransferRecoveryDisposition.Unavailable, problem);
        if (provider is not ICloudTransferRecoveryProvider recovery)
            return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);

        var replacement = IsolatedTransferRequest(request);
        var attempt = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == replacement.OperationId, ct);
        if (attempt != null)
        {
            if (attempt.Error is {} rejection && recovery.IsTerminalTransferProblem(rejection))
                return new(CloudTransferRecoveryDisposition.Unavailable, rejection);
            if (!MatchesIsolatedAttempt(attempt, runId, targetId, replacement))
                return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);
            if (attempt.State == "succeeded") return new(CloudTransferRecoveryDisposition.Authorized);
            if (attempt.ReceiptJson != null) return new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked);
            // An uncertain write cannot be replayed. A fresh isolated intent requires a verified
            // empty namespace for every issued attempt and a new user continuation.
            if (attempt.State != "planned")
            {
                var checkedAttempt = await recovery.InspectTransferRecoveryAsync(replacement, verifyRemote, ct);
                if (checkedAttempt is {Disposition: CloudTransferRecoveryDisposition.RequiresConfirmation, VerifiedEmptyTarget: true})
                    return (await InspectAdditionalTransferAsync(runId, targetId, original, request, recovery, attempt, replacement, ct)).Check;
                return checkedAttempt.Disposition switch
                {
                    CloudTransferRecoveryDisposition.Reconcile or CloudTransferRecoveryDisposition.Retry =>
                        new(CloudTransferRecoveryDisposition.Authorized),
                    CloudTransferRecoveryDisposition.Candidate => checkedAttempt,
                    CloudTransferRecoveryDisposition.Unavailable when checkedAttempt.Problem != null => checkedAttempt,
                    _ => new(CloudTransferRecoveryDisposition.Unavailable, TransferRecoveryBlocked)
                };
            }
        }

        var check = await recovery.InspectTransferRecoveryAsync(request, verifyRemote, ct);
        return attempt != null && check.Disposition is (CloudTransferRecoveryDisposition.RequiresConfirmation or CloudTransferRecoveryDisposition.Retry)
            ? new(CloudTransferRecoveryDisposition.Authorized) : check;
    }

    private async Task<CloudTransferReceipt?> RecoverTransferWhileLockedAsync(int runId, string targetId, CloudOperationDbModel original,
        CloudTransferRequest request, ICloudDownloadProvider provider, CancellationToken ct)
    {
        if (original.ReceiptJson != null || original.Error is {} problem && provider.CanCorrectAccessCodeAfterFailure(problem)) return null;
        if (provider is not ICloudTransferRecoveryProvider recovery)
        {
            var legacy = await provider.ReconcileTransferAsync(request, ct);
            return legacy == null ? null : await CompleteTransferAsync(original, request, legacy, ct);
        }
        var replacement = IsolatedTransferRequest(request);
        var attempt = await db.CloudDownloadOperations.SingleOrDefaultAsync(o => o.Id == replacement.OperationId, ct);
        if (attempt != null && !MatchesIsolatedAttempt(attempt, runId, targetId, replacement)) return null;
        if (attempt?.State == "succeeded")
            return await CompleteTransferAsync(original, request, ReadTransferReceipt(attempt, replacement) with {OperationId = request.OperationId}, ct);
        if (attempt?.ReceiptJson != null || original.Error is {} originalIssue && recovery.IsTerminalTransferProblem(originalIssue) ||
            attempt?.Error is {} attemptIssue && recovery.IsTerminalTransferProblem(attemptIssue)) return null;

        var check = await recovery.InspectTransferRecoveryAsync(request, true, ct);
        if (check.Disposition == CloudTransferRecoveryDisposition.Reconcile && check.Receipt != null)
            return await CompleteTransferAsync(original, request, check.Receipt, ct);
        if (attempt == null)
            return check.Disposition == CloudTransferRecoveryDisposition.Retry
                ? await ExecuteTransferAsync(original, request, provider, ct) : null;

        CloudTransferReceipt? receipt = null;
        if (attempt.State == "planned" && check.Disposition is (CloudTransferRecoveryDisposition.RequiresConfirmation or CloudTransferRecoveryDisposition.Retry))
            receipt = await ExecuteTransferAsync(attempt, replacement, provider, ct);
        else if (attempt.State is "running" or "unknown" or "failed")
        {
            var checkedAttempt = await recovery.InspectTransferRecoveryAsync(replacement, true, ct);
            if (checkedAttempt.Disposition == CloudTransferRecoveryDisposition.Reconcile && checkedAttempt.Receipt != null)
                receipt = await CompleteTransferAsync(attempt, replacement, checkedAttempt.Receipt, ct);
            else if (checkedAttempt.Disposition == CloudTransferRecoveryDisposition.Retry &&
                check.Disposition is (CloudTransferRecoveryDisposition.RequiresConfirmation or CloudTransferRecoveryDisposition.Retry))
                receipt = await ExecuteTransferAsync(attempt, replacement, provider, ct);
            else if (checkedAttempt is {Disposition: CloudTransferRecoveryDisposition.RequiresConfirmation, VerifiedEmptyTarget: true})
            {
                var next = await InspectAdditionalTransferAsync(runId, targetId, original, request, recovery, attempt, replacement, ct, check);
                if (next.Check.Disposition == CloudTransferRecoveryDisposition.Authorized && next.Request != null)
                {
                    var approved = await db.CloudDownloadOperations.SingleAsync(o => o.Id == next.Request.OperationId, ct);
                    if (approved.State == "succeeded") receipt = ReadTransferReceipt(approved, next.Request);
                    else if (next.Check.Receipt is {} savedReceipt)
                        receipt = await CompleteTransferAsync(approved, next.Request, savedReceipt, ct);
                    else receipt = await ExecuteTransferAsync(approved, next.Request, provider, ct);
                }
            }
        }
        return receipt == null ? null : await CompleteTransferAsync(original, request, receipt with {OperationId = request.OperationId}, ct);
    }

    private sealed record AdditionalTransfer(CloudTransferRecoveryCheck Check, CloudTransferRequest? Request = null);

    private async Task<AdditionalTransfer> InspectAdditionalTransferAsync(int runId, string targetId,
        CloudOperationDbModel original, CloudTransferRequest request, ICloudTransferRecoveryProvider provider,
        CloudOperationDbModel previous, CloudTransferRequest previousRequest, CancellationToken ct, CloudTransferRecoveryCheck? originalEvidence = null)
    {
        AdditionalTransfer Block(string? problem = null) =>
            new(new(CloudTransferRecoveryDisposition.Unavailable, problem ?? TransferRecoveryBlocked));

        // A shared original may still reconcile its receipt. Additional cloud writes belong only
        // to the unchanged original owner and cannot supersede a later download or another binding.
        var owner = runId.ToString();
        var slot = OperationSlot(new CloudDownloadTarget {Id = targetId, Sources = [request.Source],
            SelectedSourceId = request.Source.Id}, "transfer");
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(binding => binding.OperationId == original.Id)
            .Select(binding => new {binding.OwnerKind, binding.OwnerId, binding.Slot}).ToArrayAsync(ct);
        if (original.WorkflowRunId != runId || original.TargetId != targetId || previous.DownloadTaskId != null ||
            bindings.Length != 1 || bindings[0].OwnerKind != "workflow" || bindings[0].OwnerId != owner ||
            bindings[0].Slot != slot ||
            await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().AnyAsync(b => b.OwnerKind == "workflow" &&
                b.OwnerId == owner && b.Slot.StartsWith(targetId + ":") && b.Slot != slot, ct) ||
            await db.CloudDownloadOperations.AsNoTracking().AnyAsync(o => o.WorkflowRunId == runId && o.TargetId == targetId &&
                (o.DownloadTaskId != null || o.Kind != "transfer" && o.Kind != TransferRecoveryKind), ct))
            return Block();

        originalEvidence ??= await provider.InspectTransferRecoveryAsync(request, true, ct);
        if (!originalEvidence.VerifiedEmptyTarget || originalEvidence.Disposition != CloudTransferRecoveryDisposition.RequiresConfirmation)
            return Block(originalEvidence.Problem);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (previous.Error is {} issue && provider.IsTerminalTransferProblem(issue)) return Block(issue);
            var next = request with {OperationId = Key("transfer-recovery.v2", previousRequest.OperationId)};
            var attempt = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == next.OperationId, ct);
            if (attempt == null)
                return new(new(CloudTransferRecoveryDisposition.RequiresConfirmation) {NextIsolatedOperationId = next.OperationId});
            if (attempt.DownloadTaskId != null || !MatchesIsolatedAttempt(attempt, runId, targetId, next)) return Block();
            if (attempt.State == "succeeded")
            {
                try { return new(new(CloudTransferRecoveryDisposition.Authorized, Receipt: ReadTransferReceipt(attempt, next)), next); }
                catch (Exception error) when (error is JsonException or InvalidOperationException) { return Block(); }
            }
            if (attempt.ReceiptJson != null) return Block();
            if (attempt.State == "planned") return new(new(CloudTransferRecoveryDisposition.Authorized), next);
            if (attempt.State is not ("running" or "unknown" or "failed")) return Block();
            var evidence = await provider.InspectTransferRecoveryAsync(next, true, ct);
            if (evidence is {Disposition: CloudTransferRecoveryDisposition.Reconcile, Receipt: not null})
                return new(new(CloudTransferRecoveryDisposition.Authorized, Receipt: evidence.Receipt), next);
            if (attempt.Error is {} rejected && provider.IsTerminalTransferProblem(rejected)) return Block(rejected);
            if (evidence.Disposition == CloudTransferRecoveryDisposition.Retry)
                return new(new(CloudTransferRecoveryDisposition.Authorized), next);
            if (!evidence.VerifiedEmptyTarget || evidence.Disposition != CloudTransferRecoveryDisposition.RequiresConfirmation)
                return Block(evidence.Problem);
            previous = attempt;
            previousRequest = next;
        }
    }

    private static string TransferId(CloudSource source, string accountId, string remoteDirectory) =>
        Key("transfer.v2", CloudSourceIdentity.Key(source, accountId), remoteDirectory);

    private static CloudTransferRequest TransferRequest(CloudSource source, string accountId, string remoteDirectory)
    {
        var id = TransferId(source, accountId, remoteDirectory);
        return new(id, source, accountId, remoteDirectory.TrimEnd('/') + "/" + id);
    }

    private static CloudTransferRequest IsolatedTransferRequest(CloudTransferRequest original) =>
        original with {OperationId = Key(TransferRecoveryKind + ".v1", original.OperationId)};

    private static bool MatchesIsolatedAttempt(CloudOperationDbModel attempt, int runId, string targetId, CloudTransferRequest expected) =>
        attempt.Kind == TransferRecoveryKind && attempt.WorkflowRunId == runId && attempt.TargetId == targetId &&
        attempt.DownloadTaskId == null && attempt.RequestJson == JsonSerializer.Serialize(expected, Json);

    private static bool MatchesTransferRequest(CloudOperationDbModel operation, CloudTransferRequest expected, out CloudTransferRequest request)
    {
        try { request = JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json)!; }
        catch (JsonException) {request = null!; return false;}
        return request?.Source != null && operation.Id == request.OperationId && request.OperationId == expected.OperationId &&
            request.RemoteDirectory == expected.RemoteDirectory && request.AccountId == expected.AccountId &&
            CloudSourceIdentity.Fingerprint(request.Source with {ArchivePassword = null}) ==
            CloudSourceIdentity.Fingerprint(expected.Source with {ArchivePassword = null}) &&
            CloudSourceIdentity.Key(request.Source, request.AccountId) == CloudSourceIdentity.Key(expected.Source, expected.AccountId);
    }

    private static CloudTransferReceipt ReadTransferReceipt(CloudOperationDbModel operation, CloudTransferRequest request)
    {
        var receipt = JsonSerializer.Deserialize<CloudTransferReceipt>(operation.ReceiptJson ?? "null", Json);
        return ValidateTransferReceipt(request, receipt);
    }

    private static CloudTransferReceipt ValidateTransferReceipt(CloudTransferRequest request, CloudTransferReceipt? receipt)
    {
        if (receipt?.Entry == null || receipt.OperationId != request.OperationId || receipt.Entry.ProviderId != request.Source.ProviderId ||
            receipt.Entry.AccountId != request.AccountId)
            throw new InvalidOperationException("The transfer receipt does not match this operation.");
        return receipt;
    }

    private async Task<CloudTransferReceipt> CompleteTransferAsync(CloudOperationDbModel operation, CloudTransferRequest request,
        CloudTransferReceipt receipt, CancellationToken ct)
    {
        ValidateTransferReceipt(request, receipt);
        operation.ReceiptJson = JsonSerializer.Serialize(receipt, Json);
        operation.State = "succeeded";
        operation.Error = null;
        operation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return receipt;
    }

    private async Task<CloudTransferReceipt> ExecuteTransferAsync(CloudOperationDbModel operation, CloudTransferRequest request,
        ICloudDownloadProvider provider, CancellationToken ct)
    {
        operation.State = "running";
        operation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        try
        {
            var receipt = await provider.TransferAsync(request, ct);
            return await CompleteTransferAsync(operation, request, receipt, CancellationToken.None);
        }
        catch (Exception error)
        {
            operation.State = error is CloudProviderException {MayHaveSideEffects: false} ? "failed" : "unknown";
            // Persist only provider-reviewed stable codes. The separate state retains the
            // uncertainty of a request that may have reached the cloud.
            operation.Error = (operation.State == "failed" && provider.CanCorrectAccessCodeAfterFailure(error.Message) ||
                error is CloudProviderException && provider is ICloudTransferRecoveryProvider diagnostics &&
                diagnostics.IsTerminalTransferProblem(error.Message)) ? error.Message : null;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}
