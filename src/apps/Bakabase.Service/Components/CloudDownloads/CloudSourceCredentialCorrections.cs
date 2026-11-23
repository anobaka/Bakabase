using System;
using System.Collections.Generic;
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
    /// <summary>Read-only capability check shared by acquisition editors and the execution guard.
    /// It never probes the provider or creates/reconciles a transfer or download.</summary>
    public async Task<string?> AccessCodeCorrectionBlockReasonAsync(int runId, string activityKind,
        CloudDownloadTarget target, CancellationToken ct)
    {
        if (activityKind is not ("baidu.transfer" or "download.enqueueSupported" or "baidu.download"))
            return "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED";
        if (target.Excluded || target.SelectedSource is not {Kind: "share"} source)
            return "SOURCE_CORRECTION_SOURCE_UNSUPPORTED";
        if (HasAcquiredContent(target)) return "SOURCE_CORRECTION_ALREADY_ACQUIRED";
        if (source.AccessCodeRevisions?.Count >= 32) return "SOURCE_CORRECTION_HISTORY_LIMIT";
        try
        {
            _ = Provider(source);
            await EnsureNoSideEffectsAsync(runId, target, source, null, ct);
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException)
        { return "SOURCE_CORRECTION_OPERATION_UNCERTAIN"; }
    }
    // Each explicit correction gets its own immutable slot. Existing bindings and failed requests
    // remain available for diagnosis; a correction never loosens the original binding's identity.
    internal static string OperationSlot(CloudDownloadTarget target, string kind) =>
        kind == "transfer" && target.ReviewedTransferId != null ? target.Id + ":reviewedTransfer:" + target.ReviewedTransferId :
        target.Id + ":" + kind +
        (target.SelectedSource?.AccessCodeRevisions?.LastOrDefault() is { } revision
            ? ":credential:" + revision.CorrectionId : "");

    // A process may stop after the immutable request was saved but before the workflow's next
    // checkpoint. Recover the already-authorized decision from that request, not from a new guess.
    public async Task<DownloadWorkset> RecoverCredentialCorrectionsAsync(int runId, string activityKind,
        DownloadWorkset item, CancellationToken ct)
    {
        if (activityKind is not ("baidu.transfer" or "download.enqueueSupported" or "baidu.download")) return item;
        var owner = runId.ToString();
        var revisedSlots = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot.Contains(":credential:"))
            .ToListAsync(ct);
        var corrections = new List<CloudSourceCredentialCorrection>();
        foreach (var target in item.Targets.Where(t => !t.Excluded && t.SelectedSource != null))
        {
            var source = target.SelectedSource!;
            var candidates = new List<CloudAccessCodeRevision>();
            if (source.AccessCodeRevisions?.Count > 0)
                await EnsureHistoricalTransfersStillSafeAsync(runId, target, source, ct);
            foreach (var binding in revisedSlots.Where(b => b.Slot.StartsWith(target.Id + ":download:credential:", StringComparison.Ordinal)))
            {
                var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == binding.OperationId, ct);
                var request = operation?.Kind == "download" ? JsonSerializer.Deserialize<CloudDownloadRequest>(operation.RequestJson, Json) : null;
                var savedDownload = request?.Source.AccessCodeRevisions?.LastOrDefault();
                // A queued download needs its existing task reconciled. Never replay old credentials
                // and accidentally enqueue a second task when that checkpoint is missing.
                if (savedDownload == null || source.AccessCodeRevisions?.Any(r => r == savedDownload) != true ||
                    binding.Slot != target.Id + ":download:credential:" + savedDownload.CorrectionId ||
                    request!.AccountId != (target.AccountId ?? source.AccountId) ||
                    JsonSerializer.Serialize(request.Source, Json) != JsonSerializer.Serialize(source, Json))
                    throw new InvalidOperationException("A corrected download already exists. Confirm its saved task before continuing this checkpoint.");
            }
            foreach (var binding in revisedSlots.Where(b => b.Slot.StartsWith(target.Id + ":transfer:credential:", StringComparison.Ordinal)))
            {
                var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == binding.OperationId, ct)
                    ?? throw new InvalidOperationException("The corrected operation has no verifiable frozen request.");
                var request = operation.Kind == "transfer" ? JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json) : null;
                if (request?.Source.AccessCodeRevisions?.LastOrDefault() is not { } revision ||
                    binding.Slot != target.Id + ":transfer:credential:" + revision.CorrectionId)
                    throw new InvalidOperationException("The corrected operation does not match its saved decision.");
                if (source.AccessCodeRevisions?.Any(r => r == revision) == true)
                {
                    if (request.AccountId != target.AccountId ||
                        !request.Source.AccessCodeRevisions!.SequenceEqual(source.AccessCodeRevisions.Take(request.Source.AccessCodeRevisions.Count)) ||
                        JsonSerializer.Serialize(source with {AccessCode = request.Source.AccessCode, AccessCodeRevisions = request.Source.AccessCodeRevisions}, Json)
                            != JsonSerializer.Serialize(request.Source, Json))
                        throw new InvalidOperationException("The corrected operation no longer matches this source and account.");
                    continue;
                }
                if (!MatchesFrozenCorrection(source, request.Source, revision) || request.AccountId != target.AccountId)
                    throw new InvalidOperationException("The corrected operation cannot be matched to the current checkpoint.");
                candidates.Add(revision);
            }
            if (candidates.Count > 1)
                throw new InvalidOperationException("Multiple credential decisions require review before continuing this target.");
            if (candidates.SingleOrDefault() is { } saved)
                corrections.Add(new() {CorrectionId = saved.CorrectionId, TargetId = target.Id, SourceId = source.Id,
                    ExpectedSourceIdentity = saved.ExpectedSourceIdentity, NewAccessCode = saved.NewAccessCode,
                    Basis = saved.Basis, EvidenceReference = saved.EvidenceReference, Evidence = saved.Evidence});
        }
        return await ApplyCredentialCorrectionsAsync(runId, activityKind, item, corrections, ct);
    }

    public async Task<DownloadWorkset> ApplyCredentialCorrectionsAsync(int runId, string activityKind,
        DownloadWorkset item, IReadOnlyList<CloudSourceCredentialCorrection> corrections, CancellationToken ct)
    {
        if (corrections.Count == 0) return item;
        if (corrections.Count > 100 || corrections.Select(c => c.CorrectionId).Distinct().Count() != corrections.Count ||
            corrections.Select(c => (c.TargetId, c.SourceId)).Distinct().Count() != corrections.Count)
            throw new ArgumentException("Choose each saved source once when correcting its access code.");
        var replacements = new Dictionary<string, CloudSource>();
        foreach (var correction in corrections)
        {
            if (!Guid.TryParseExact(correction.CorrectionId, "N", out _) ||
                string.IsNullOrWhiteSpace(correction.TargetId) || string.IsNullOrWhiteSpace(correction.SourceId) ||
                correction.ExpectedSourceIdentity?.Length != 64 || correction.NewAccessCode?.Length > 512 ||
                correction.NewAccessCode?.Any(char.IsControl) == true || correction.Basis is not ("manual" or "sourceEvidence") ||
                correction.EvidenceReference?.Length > 1024 || correction.Evidence?.Length > 4096 ||
                correction.Basis == "sourceEvidence" &&
                    (string.IsNullOrWhiteSpace(correction.EvidenceReference) || string.IsNullOrWhiteSpace(correction.Evidence)))
                throw new ArgumentException("Provide a bounded access-code correction and its saved-source identity.");
            var target = item.Targets.SingleOrDefault(t => t.Id == correction.TargetId);
            var source = target?.SelectedSource;
            if (target == null || target.Excluded || source?.Id != correction.SourceId)
                throw new ArgumentException("The correction must refer to the currently selected saved source.");
            var existing = source.AccessCodeRevisions?.SingleOrDefault(r => r.CorrectionId == correction.CorrectionId);
            if (existing != null)
            {
                if (existing.ExpectedSourceIdentity != correction.ExpectedSourceIdentity || existing.NewAccessCode != correction.NewAccessCode ||
                    existing.Basis != correction.Basis || existing.EvidenceReference != correction.EvidenceReference || existing.Evidence != correction.Evidence)
                    throw new ArgumentException("This correction identity was already used for a different decision.");
                // Replaying the same accepted correction does not add history or reset any completed work.
                continue;
            }
            if (item.Targets.SelectMany(t => t.Sources).Any(s => s.AccessCodeRevisions?.Any(r => r.CorrectionId == correction.CorrectionId) == true))
                throw new ArgumentException("This correction identity belongs to another saved source.");
            if (activityKind is not ("baidu.transfer" or "download.enqueueSupported" or "baidu.download") ||
                source.Kind != "share" || source.AccessCode == correction.NewAccessCode ||
                CloudSourceIdentity.Key(source with {AccessCode = correction.NewAccessCode}) == CloudSourceIdentity.Key(source) ||
                source.AccessCodeRevisions?.Any(r => r.PreviousAccessCode == correction.NewAccessCode || r.NewAccessCode == correction.NewAccessCode) == true ||
                source.AccessCodeRevisions?.Count >= 32 || CloudSourceIdentity.Key(source) != correction.ExpectedSourceIdentity)
                throw new InvalidOperationException("The saved source changed or is not waiting before transfer/download. Reload its current details.");
            if (HasAcquiredContent(target))
                throw new InvalidOperationException("This target already has transferred files, a download or an artifact. Its access code cannot be replaced.");
            var (recovered, failureCode) = await EnsureNoSideEffectsAsync(runId, target, source, correction, ct);
            var revision = new CloudAccessCodeRevision {CorrectionId = correction.CorrectionId,
                ExpectedSourceIdentity = correction.ExpectedSourceIdentity, PreviousAccessCode = source.AccessCode,
                PreviousFailureCode = failureCode,
                NewAccessCode = correction.NewAccessCode, Basis = correction.Basis,
                EvidenceReference = correction.EvidenceReference, Evidence = correction.Evidence, AppliedAt = DateTime.UtcNow};
            replacements.Add(target.Id, recovered ?? source with {AccessCode = correction.NewAccessCode,
                AccessCodeRevisions = [..source.AccessCodeRevisions ?? [], revision]});
        }
        return item with {Targets = item.Targets.Select(t => replacements.TryGetValue(t.Id, out var source)
            ? t with {Sources = t.Sources.Select(s => s.Id == source.Id ? source : s).ToList(), Problem = null} : t).ToList()};
    }

    private static bool HasAcquiredContent(CloudDownloadTarget target) => target.RemoteEntry != null ||
        target.DownloadTaskId != null || target.TransportTaskId != null || target.DownloadOperationId != null ||
        target.DownloadArtifactId != null || target.ProcessingOperationId != null || target.ProcessedArtifactId != null ||
        target.PublicationOperationId != null || target.PublishedArtifactId != null || target.CleanupReceiptId != null ||
        target.Files.Count != 0 || target.ProcessedFiles.Count != 0 || target.PublishedFiles.Count != 0;

    private static bool MatchesFrozenCorrection(CloudSource original, CloudSource frozen, CloudAccessCodeRevision revision)
    {
        if (revision.ExpectedSourceIdentity != CloudSourceIdentity.Key(original) ||
            revision.PreviousAccessCode != original.AccessCode || frozen.AccessCode != revision.NewAccessCode ||
            frozen.AccessCodeRevisions?.Count != (original.AccessCodeRevisions?.Count ?? 0) + 1 ||
            !frozen.AccessCodeRevisions.Take(frozen.AccessCodeRevisions.Count - 1).SequenceEqual(original.AccessCodeRevisions ?? [])) return false;
        return JsonSerializer.Serialize(original with {AccessCode = revision.NewAccessCode, AccessCodeRevisions = frozen.AccessCodeRevisions}, Json)
            == JsonSerializer.Serialize(frozen, Json);
    }

    private async Task EnsureHistoricalTransfersStillSafeAsync(int runId, CloudDownloadTarget target, CloudSource source, CancellationToken ct)
    {
        var owner = runId.ToString();
        var currentSlot = OperationSlot(target, "transfer");
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking().Where(b =>
            b.OwnerKind == "workflow" && b.OwnerId == owner && b.Slot != currentSlot &&
            (b.Slot == target.Id + ":transfer" || b.Slot.StartsWith(target.Id + ":transfer:credential:"))).ToListAsync(ct);
        foreach (var binding in bindings.Where(b => b.Slot == target.Id + ":transfer" ||
            source.AccessCodeRevisions?.Any(r => b.Slot == target.Id + ":transfer:credential:" + r.CorrectionId) == true))
        {
            var operation = await db.CloudDownloadOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == binding.OperationId, ct);
            var request = operation?.Kind == "transfer" ? JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json) : null;
            var proof = request == null ? null : source.AccessCodeRevisions?.FirstOrDefault(r => r.ExpectedSourceIdentity == CloudSourceIdentity.Key(request.Source));
            if (operation == null || operation.State != "failed" || operation.ReceiptJson != null || operation.DownloadTaskId != null ||
                request?.AccountId != target.AccountId || proof == null ||
                !Provider(source).CanCorrectAccessCodeAfterFailure(operation.Error ?? proof.PreviousFailureCode ?? ""))
                throw new InvalidOperationException("An earlier transfer outcome changed or cannot be verified. Confirm it before continuing this corrected source.");
        }
    }

    private async Task<(CloudSource? Recovered, string? FailureCode)> EnsureNoSideEffectsAsync(int runId, CloudDownloadTarget target, CloudSource source,
        CloudSourceCredentialCorrection? correction, CancellationToken ct)
    {
        var owner = runId.ToString();
        var bindings = await db.Set<ArtifactOperationBindingDbModel>().AsNoTracking()
            .Where(b => b.OwnerKind == "workflow" && b.OwnerId == owner &&
                (b.Slot == target.Id + ":transfer" || b.Slot == target.Id + ":download" ||
                 b.Slot.StartsWith(target.Id + ":transfer:credential:") || b.Slot.StartsWith(target.Id + ":download:credential:")))
            .ToListAsync(ct);
        var boundIds = bindings.Select(b => b.OperationId).ToList();
        var operations = await db.CloudDownloadOperations.AsNoTracking().Where(o => boundIds.Contains(o.Id) ||
            o.WorkflowRunId == runId && o.TargetId == target.Id).ToListAsync(ct);
        if (boundIds.Any(id => operations.All(o => o.Id != id)))
            throw new InvalidOperationException("The previous operation binding has no verifiable outcome.");
        CloudSource? recovered = null;
        string? failureCode = null;
        foreach (var operation in operations)
        {
            var previous = operation.Kind == "transfer" ? JsonSerializer.Deserialize<CloudTransferRequest>(operation.RequestJson, Json) : null;
            // Replaying this exact saved decision may reuse a receipt or reconcile its uncertain
            // transfer, using the same operation ID. It must never create another transfer slot.
            if (correction != null && previous?.Source.AccessCodeRevisions?.LastOrDefault() is { } saved &&
                saved.CorrectionId == correction.CorrectionId &&
                bindings.Any(b => b.OperationId == operation.Id && b.Slot == target.Id + ":transfer:credential:" + correction.CorrectionId))
            {
                if (saved.ExpectedSourceIdentity != correction.ExpectedSourceIdentity || saved.NewAccessCode != correction.NewAccessCode ||
                    saved.Basis != correction.Basis || saved.EvidenceReference != correction.EvidenceReference || saved.Evidence != correction.Evidence ||
                    previous.AccountId != target.AccountId || !MatchesFrozenCorrection(source, previous.Source, saved) ||
                    operation.DownloadTaskId != null || operation.State is not ("planned" or "succeeded" or "running" or "unknown" or "failed") ||
                    operation.State == "planned" && operation.ReceiptJson != null ||
                    operation.State == "succeeded" && operation.ReceiptJson == null ||
                    operation.State == "failed" && (!Provider(source).CanCorrectAccessCodeAfterFailure(operation.Error ?? "") || operation.ReceiptJson != null) ||
                    recovered != null)
                    throw new InvalidOperationException("The saved credential decision cannot safely resume its existing operation.");
                recovered = previous.Source;
                continue;
            }
            // A durable binding can belong to an operation first created by another workflow.
            if (operation.Kind != "transfer" || operation.State != "failed" ||
                operation.ReceiptJson != null || operation.DownloadTaskId != null)
                throw new InvalidOperationException("The previous operation may have produced files. Confirm its outcome before changing credentials.");
            var provider = Provider(source);
            // Older failed rows did not retain the typed code. Their durable workflow checkpoint
            // supplies it, but only together with an exact original request and a failed/no-receipt row.
            var previousIdentity = previous == null ? null : CloudSourceIdentity.Key(previous.Source);
            var problem = operation.Error ?? (previousIdentity == CloudSourceIdentity.Key(source) ? target.Problem :
                source.AccessCodeRevisions?.FirstOrDefault(r => r.ExpectedSourceIdentity == previousIdentity)?.PreviousFailureCode);
            var knownIdentity = previousIdentity == CloudSourceIdentity.Key(source) ||
                source.AccessCodeRevisions?.Any(r => r.ExpectedSourceIdentity == previousIdentity) == true;
            if (previous == null || !knownIdentity ||
                previous.AccountId != target.AccountId || !provider.CanCorrectAccessCodeAfterFailure(problem ?? ""))
                throw new InvalidOperationException("Only a confirmed access-code failure before remote writes can be corrected in this run.");
            if (previousIdentity == CloudSourceIdentity.Key(source)) failureCode = problem;
        }
        return (recovered, failureCode);
    }
}
