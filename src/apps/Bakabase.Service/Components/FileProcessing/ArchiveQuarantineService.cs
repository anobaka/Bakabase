using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record ArchiveQuarantineResult(bool Completed, string Status, string? ReceiptId = null, string? Message = null,
    int RemovedFiles = 0, long RemovedBytes = 0);
public sealed record ArchiveQuarantineReceipt(string ReceiptId, string ArtifactId, string OriginalRoot,
    string QuarantineRoot, string Status, IReadOnlyList<string> OutputArtifactIds, string? Message = null);

/// <summary>Exact-file source retirement. Quarantine is recoverable; explicit deletion requires verified final delivery.</summary>
public sealed partial class ArchiveQuarantineService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly IArtifactOperationStore _store;
    private readonly IUserStoragePolicy _storage;
    private readonly string _appData;
    private readonly string _receipts;

    public ArchiveQuarantineService(IArtifactOperationStore store, AppService app, IUserStoragePolicy storage)
        : this(store, app.AppDataDirectory, storage) { }

    internal ArchiveQuarantineService(IArtifactOperationStore store, string appData, IUserStoragePolicy storage)
    {
        _store = store; _storage = storage; _appData = Path.GetFullPath(appData);
        _receipts = Path.Combine(_appData, "file-processing", "quarantine-receipts");
    }

    public Task<ArchiveQuarantineResult> QuarantineAsync(string artifactId,
        IReadOnlyList<string> verifiedOutputArtifactIds, CancellationToken ct = default) =>
        QuarantineCoreAsync(artifactId, verifiedOutputArtifactIds, false, ct);

    internal sealed record ReceivedDownloadRetirement(string PartId, IReadOnlyList<int> OwnerRunIds);

    private async Task<ArchiveQuarantineResult> QuarantineCoreAsync(string artifactId,
        IReadOnlyList<string> verifiedOutputArtifactIds, bool allowRegisteredBoundaryProof, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null, ArtifactCleanupFileSnapshot? snapshot = null,
        Func<ArtifactRecord, CancellationToken, Task<IReadOnlySet<string>>>? movedEntries = null,
        IReadOnlySet<string>? expectedMovedFiles = null, ReviewedBoundaryApproval? review = null,
        ReceivedDownloadRetirement? received = null,
        Func<ArtifactRecord, CancellationToken, Task<bool>>? canRetireReceived = null)
    {
        var gate = Gates.GetOrAdd(artifactId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        Receipt? receipt = null;
        try
        {
            var artifact = await _store.GetArtifactAsync(artifactId, ct) ?? throw new ArgumentException("The source artifact no longer exists.");
            if (artifact.Kind == "compositeFiles")
                return new(false, "Blocked", Message: "A composed input is a read-only view of separately owned files; retire each original artifact only through its own retention policy.");
            var prior = await FindPending(artifactId, ct);
            if (prior != null)
            {
                receipt = prior;
                if (receipt.ReviewedBoundaryProof != review?.Source.Proof)
                    return new(false, "SelectionChanged", receipt.Id, "SelectionChanged");
                if (expectedMovedFiles != null && !expectedMovedFiles.IsSubsetOf(receipt.MovedBeforeDeletion))
                    return new(false, "SelectionChanged", receipt.Id, "SelectionChanged");
                if (receipt.Status is "Deleting" or "Purged") return new(false, receipt.Status, receipt.Id, "This source has a permanent cleanup receipt and cannot be quarantined or restored again.");
                if (receipt.Status == "Restoring") return new(false, "Pending", receipt.Id, "Resume restoration before quarantining this artifact again.");
                if (receipt.Status == "Quarantined") return new(true, "Quarantined", receipt.Id);
            }
            else
            {
                if (artifact.State != ArtifactLocationState.Available)
                    return new(false, "Blocked", Message: "The source artifact is not available for quarantine.");
                await VerifyOutputs(artifact, verifiedOutputArtifactIds, ct, allowRegisteredBoundaryProof, authorization, review);
                var moved = movedEntries == null ? new HashSet<string>(StringComparer.Ordinal) :
                    (await movedEntries(artifact, ct)).ToHashSet(StringComparer.Ordinal);
                var absent = snapshot != null || moved.Count > 0
                    ? ConfirmAbsentEntries(artifact.RootDirectory, artifact.Manifest, ct) : [];
                if (snapshot == null) absent.IntersectWith(moved);
                if (expectedMovedFiles != null && !expectedMovedFiles.IsSubsetOf(absent))
                    return new(false, "SelectionChanged", Message: "SelectionChanged");
                var absentDirectories = (artifact.Manifest.Directories ?? []).Where(relative => moved.Contains(relative) &&
                    ArtifactCleanupAbsence.Probe(artifact.RootDirectory, Path.Combine(artifact.RootDirectory, relative), ct) ==
                    ArtifactCleanupPathPresence.ConfirmedAbsent).ToHashSet(StringComparer.Ordinal);
                await VerifyManifest(artifact.RootDirectory, artifact.Manifest, ct, snapshot, absent, absentDirectories);
                EnsureAllowed(artifact.RootDirectory, artifact.Managed);
                var id = Guid.NewGuid().ToString("N");
                var destination = Path.Combine(artifact.RootDirectory, ".bakabase-quarantine", id);
                receipt = new Receipt { Id = id, ArtifactId = artifact.Id, OriginalRoot = Encode(artifact.RootDirectory),
                    QuarantineRoot = Encode(destination), Manifest = artifact.Manifest,
                    OutputIds = verifiedOutputArtifactIds.Distinct(StringComparer.Ordinal).ToList(), Managed = artifact.Managed, ReviewedBoundaryProof = review?.Source.Proof,
                    AbsentBeforeDeletion = absent.ToList(), MovedBeforeDeletion = absent.Intersect(moved, StringComparer.Ordinal).ToList(),
                    MovedDirectoriesBeforeDeletion = absentDirectories.ToList() };
                if (authorization != null) receipt.Consents.Add(authorization.Consent);
                await Save(receipt, ct); // A crash after the fence still leaves a recoverable intent.
            }
            await VerifyOutputs(artifact, receipt.OutputIds, ct, allowRegisteredBoundaryProof, authorization, review);
            await VerifyMovedEntriesAsync(receipt, artifact, movedEntries, ct);
            if (received != null && (canRetireReceived == null || !await canRetireReceived(artifact, ct)))
                return new(false, "Blocked", receipt.Id, "The received download is still referenced or its ownership changed.");
            if (artifact.State != ArtifactLocationState.Quarantined && !await FenceAsync())
            {
                receipt.Status = "Blocked"; receipt.Message = "This artifact or a shared input has active consumers.";
                await Save(receipt, ct);
                return new(false, "Blocked", receipt.Id, receipt.Message);
            }
            Task<bool> FenceAsync() => received != null
                ? _store.TryQuarantineReceivedDownloadAsync(artifact.Id, artifact.OperationId, artifact.Fingerprint,
                    received.PartId, received.OwnerRunIds, ct)
                : review != null
                    ? _store.TryQuarantineProcessingBoundaryAsync(artifact.Id, review.Source.Fingerprint, review.Source.DownloadId, review.Source.RunId, ct)
                    : _store.TryQuarantineAsync(artifact.Id, ct);
            receipt.Status = "Moving";
            await Save(receipt, ct);
            await Relocate(receipt, restoring: false, ct, snapshot);
            // The store rehashes a location before registering it as a reusable quarantine.
            // Explicitly accepted permanent deletion needs no intermediate location commit:
            // TryQuarantine already fences consumers, this durable receipt records both paths,
            // and the terminal Retired commit below records the final location. Interruption
            // resumes/restores through that same receipt, never by guessing the old root.
            if (review == null && snapshot == null && receipt.MovedBeforeDeletion.Count == 0 && receipt.MovedDirectoriesBeforeDeletion.Count == 0)
                await _store.SetLocationAsync(artifact.Id, Decode(receipt.QuarantineRoot), ArtifactLocationState.Quarantined, ct);
            await VerifyOutputs(artifact, receipt.OutputIds, ct, allowRegisteredBoundaryProof, authorization, review);
            receipt.Status = "Quarantined"; receipt.Message = null;
            await Save(receipt, ct);
            return new(true, receipt.Status, receipt.Id);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (receipt != null)
            {
                receipt.Message = error is OperationCanceledException ? "Quarantine was interrupted; resume or restore it." : error.Message;
                try { await Save(receipt, CancellationToken.None); } catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException) { }
            }
            return new(false, "Pending", receipt?.Id, error.Message);
        }
        finally { gate.Release(); }
    }

    /// <summary>Explicit permanent retirement after delivery. The durable deleting receipt fences
    /// restoration, and every unlink is limited to verified manifest entries in owned quarantine.</summary>
    public Task<ArchiveQuarantineResult> DeleteAfterDeliveryAsync(string artifactId,
        IReadOnlyList<string> publishedArtifactIds, CancellationToken ct = default) =>
        DeleteAfterDeliveryCoreAsync(artifactId, publishedArtifactIds, false, null, ct);

    // Registered processing checkpoints need the same exact-file retirement guarantees as
    // downloads. Keep this entry point internal: its caller also proves retention policy and
    // all durable owners, including completed workflows whose consumer lease has expired.
    internal Task<ArchiveQuarantineResult> DeleteManagedAncestorAfterDeliveryAsync(string artifactId,
        IReadOnlyList<string> publishedArtifactIds, Func<ArtifactRecord, CancellationToken, Task<bool>> canRetire,
        CancellationToken ct, ArtifactDeliveryCleanupAuthorization? authorization = null,
        Func<ArtifactRecord, CancellationToken, Task<IReadOnlySet<string>>>? movedEntries = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? releasedViews = null,
        IReadOnlySet<string>? expectedMovedFiles = null, ReceivedDownloadRetirement? received = null) =>
        DeleteAfterDeliveryCoreAsync(artifactId, publishedArtifactIds, true, canRetire, ct, authorization, movedEntries, releasedViews,
            expectedMovedFiles, received: received);

    internal async Task<bool> MatchesReviewedBoundaryAsync(string id, string proof, CancellationToken ct) =>
        (await FindPending(id, ct))?.ReviewedBoundaryProof == proof;

    internal Task<ArchiveQuarantineResult> DeleteReviewedBoundaryAsync(ReviewedBoundaryApproval review,
        Func<ArtifactRecord, CancellationToken, Task<bool>> canRetire, CancellationToken ct) =>
        DeleteAfterDeliveryCoreAsync(review.Source.ArtifactId, [review.Source.ResultId], true, canRetire, ct, review: review);

    private static async Task VerifyReviewedBoundary(ArtifactRecord source, IReadOnlyList<string> outputs,
        ReviewedBoundaryApproval approval, CancellationToken ct)
    {
        if (!source.Managed || source.Kind != "processedFiles" || source.Id != approval.Source.ArtifactId ||
            source.OperationId != approval.Source.OperationId || !outputs.SequenceEqual([approval.Source.ResultId]) ||
            !await approval.Verify(ct)) throw new InvalidDataException("The reviewed processing result changed.");
    }

    private async Task<ArchiveQuarantineResult> DeleteAfterDeliveryCoreAsync(string artifactId,
        IReadOnlyList<string> publishedArtifactIds, bool allowProcessed,
        Func<ArtifactRecord, CancellationToken, Task<bool>>? canRetire, CancellationToken ct,
        ArtifactDeliveryCleanupAuthorization? authorization = null,
        Func<ArtifactRecord, CancellationToken, Task<IReadOnlySet<string>>>? movedEntries = null,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? releasedViews = null,
        IReadOnlySet<string>? expectedMovedFiles = null, ReviewedBoundaryApproval? review = null,
        ReceivedDownloadRetirement? received = null)
    {
        var snapshot = ArtifactCleanupFileSnapshot.For(authorization);
        var source = await _store.GetArtifactAsync(artifactId, ct)
            ?? throw new ArgumentException("The source artifact no longer exists.");
        if (!source.Managed && (received == null || canRetire == null || source.Kind != "currentFiles") ||
            source.Kind == "publishedFiles" || source.Kind == "processedFiles" && !allowProcessed)
            return new(false, "Blocked", Message: "Only managed source files may be permanently retired.");
        var prior = await FindPending(artifactId, ct);
        if (prior != null && prior.ReviewedBoundaryProof != review?.Source.Proof) return new(false, "SelectionChanged", prior.Id);
        if (expectedMovedFiles != null && prior != null && !expectedMovedFiles.IsSubsetOf(prior.MovedBeforeDeletion))
            return new(false, "SelectionChanged", prior.Id, "SelectionChanged");
        if (prior?.Status != "Purged" && canRetire != null && !await canRetire(source, ct))
            return new(false, "Blocked", prior?.Id, "The registered input is retained by its policy or another owner.");
        if (prior?.Status != "Purged")
        {
            try
            {
                // Validate before moving anything: a processing checkpoint alone cannot authorize deletion.
                await VerifyPublishedOutputs(source, prior?.Status == "Deleting" ? prior.OutputIds : publishedArtifactIds, ct, allowProcessed, authorization, review);
                if (source.State == ArtifactLocationState.Available &&
                    await (authorization?.HasOtherRegisteredFilesAsync(source.Files, artifactId, ct) ??
                        _store.HasOtherRegisteredFilesAsync(source.Files, artifactId, releasedViews == null ? [] : await releasedViews(ct), ct)))
                    return new(false, "Blocked", Message: "The source files are also registered by another artifact; they were retained.");
            }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            { return new(false, "Blocked", prior?.Id, error.Message); }
        }
        if (prior?.Status is not ("Deleting" or "Purged"))
        {
            var quarantined = await QuarantineCoreAsync(artifactId, publishedArtifactIds, allowProcessed, ct, authorization,
                snapshot, movedEntries, expectedMovedFiles, review, received, received == null ? null : canRetire);
            // A concurrent deletion may have advanced the same receipt while this caller
            // was verifying the publication. Join that deletion under the artifact gate.
            if (!quarantined.Completed && quarantined.Status is not ("Deleting" or "Purged")) return quarantined;
        }
        var gate = Gates.GetOrAdd(artifactId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        Receipt? receipt = null;
        var removedFiles = 0; long removedBytes = 0;
        try
        {
            receipt = await FindPending(artifactId, ct) ?? throw new InvalidDataException("The cleanup receipt is missing.");
            if (receipt.ReviewedBoundaryProof != review?.Source.Proof) return new(false, "SelectionChanged", receipt.Id);
            if (receipt.Status == "Purged") return new(true, "Purged", receipt.Id);
            if (receipt.Status is not ("Quarantined" or "Deleting"))
                return new(false, "Blocked", receipt.Id, "Finish the pending source relocation before deleting it.");
            source = await _store.GetArtifactAsync(artifactId, ct) ?? throw new InvalidDataException("The source artifact no longer exists.");
            if (source.State is not (ArtifactLocationState.Quarantined or ArtifactLocationState.Missing or ArtifactLocationState.Retired))
                return new(false, "Blocked", receipt.Id, "The source quarantine fence changed; no files were removed.");
            var root = Decode(receipt.QuarantineRoot);
            var expectedRoot = Path.Combine(Decode(receipt.OriginalRoot), ".bakabase-quarantine", receipt.Id);
            if (!ArtifactManifestFiles.PathComparer.Equals(root, expectedRoot)) throw new InvalidDataException("Invalid source cleanup directory.");
            EnsureAllowed(Decode(receipt.OriginalRoot), receipt.Managed);
            var files = receipt.Manifest.Entries.Select(e => FileProcessingFiles.Within(Path.Combine(root, e.RelativePath), root)).ToArray();
            var originalRoot = Decode(receipt.OriginalRoot);
            var guardedPaths = files.Concat(receipt.Manifest.Entries.Select(e => FileProcessingFiles.Within(
                Path.Combine(originalRoot, e.RelativePath), originalRoot))).Distinct(FileProcessingFiles.PathComparer).ToArray();
            await using (await _store.AcquireMaintenanceAsync(ct))
            {
                if (await _store.HasActiveConsumersAsync(source.OperationId, ct) ||
                    await (authorization?.HasOtherRegisteredFilesAsync(guardedPaths, artifactId, ct) ??
                        _store.HasOtherRegisteredFilesAsync(guardedPaths, artifactId, releasedViews == null ? [] : await releasedViews(ct), ct)) ||
                    canRetire != null && !await canRetire(source, ct))
                    return new(false, "Blocked", receipt.Id, "The quarantined inputs are still referenced by another artifact or consumer.");
                var outputs = receipt.Status == "Deleting" ? receipt.OutputIds : publishedArtifactIds;
                await VerifyPublishedOutputs(source, outputs, ct, allowProcessed, authorization, review);
                if (authorization != null && receipt.Consents.All(c => c.Id != authorization.Consent.Id))
                {
                    receipt.Consents.Add(authorization.Consent);
                    await Save(receipt, ct);
                }
                await VerifyMovedEntriesAsync(receipt, source, movedEntries, ct);
                VerifyRecordedAbsence(receipt, ct);
                var absentBeforeDeletion = MissingDeletionEntries(receipt);
                var resumingDeletion = receipt.Status == "Deleting";
                if (!resumingDeletion)
                {
                    await VerifyManifest(root, receipt.Manifest, ct, snapshot, absentBeforeDeletion);
                    receipt.OutputIds = outputs.Distinct(StringComparer.Ordinal).ToList();
                    receipt.Status = "Deleting"; receipt.Message = null;
                    if (authorization != null && receipt.Consents.All(c => c.Id != authorization.Consent.Id))
                        receipt.Consents.Add(authorization.Consent);
                    await Save(receipt, ct); // Restore refuses the receipt before the first unlink.
                }
                foreach (var entry in receipt.Manifest.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    // A file that reappears at a previously absent name was never selected for unlinking.
                    if (absentBeforeDeletion.Contains(entry.RelativePath)) continue;
                    var file = FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root);
                    ArtifactManifestFiles.RejectLinks(file);
                    var presence = resumingDeletion
                        ? ArtifactCleanupAbsence.Probe(root, file, ArtifactCleanupAbsence.OpenFileForObservation, ct)
                        : ArtifactCleanupAbsence.Probe(root, file, ct);
                    if (presence == ArtifactCleanupPathPresence.ConfirmedAbsent) continue;
                    if (presence == ArtifactCleanupPathPresence.Unavailable)
                        throw new IOException("The cleanup path or its containing storage could not be verified.");
                    if (File.GetAttributes(file).HasFlag(FileAttributes.Directory))
                        throw new InvalidDataException("A source file was replaced by a directory; it was retained.");
                    await VerifyFile(file, entry, ct, snapshot);
                    ArtifactManifestFiles.RejectLinks(file);
                    File.Delete(file);
                    removedFiles++; removedBytes += entry.Length;
                }
                await VerifyMovedEntriesAsync(receipt, source, movedEntries, ct);
                VerifyRecordedAbsence(receipt, ct);
                if (files.Length > 0 && !ArtifactCleanupAbsence.AllConfirmedAbsent(root, files, ct,
                        ArtifactCleanupAbsence.OpenFileForObservation))
                    throw new IOException("A cleanup path reappeared or its storage became unavailable before retirement.");
            }
            // The source's quarantine state keeps new consumers fenced while location is committed.
            if (source.State == ArtifactLocationState.Quarantined)
                await _store.SetLocationAsync(source.Id, root, ArtifactLocationState.Retired, ct);
            receipt.Status = "Purged"; receipt.Message = null;
            await Save(receipt, ct);
            PruneReceiptDirectories(receipt);
            return new(true, "Purged", receipt.Id, RemovedFiles: removedFiles, RemovedBytes: removedBytes);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (receipt != null)
            {
                receipt.Message = error is OperationCanceledException ? "Permanent cleanup was interrupted; retry to finish it." : error.Message;
                try { await Save(receipt, CancellationToken.None); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            return new(false, "Pending", receipt?.Id, error.Message, removedFiles, removedBytes);
        }
        finally { gate.Release(); }
    }

    private async Task VerifyPublishedOutputs(ArtifactRecord source, IReadOnlyList<string> ids, CancellationToken ct,
        bool allowRegisteredBoundaryProof = false, ArtifactDeliveryCleanupAuthorization? authorization = null,
        ReviewedBoundaryApproval? review = null)
    {
        if (review != null)
        {
            await VerifyReviewedBoundary(source, ids, review, ct);
            return;
        }
        if (ids.Count == 0) throw new InvalidOperationException("Verified final delivery is required before permanent source cleanup.");
        foreach (var id in ids)
            if ((await _store.GetArtifactAsync(id, ct))?.Kind != "publishedFiles")
                throw new InvalidOperationException("Only a published final artifact can authorize permanent source cleanup.");
        await VerifyOutputs(source, ids, ct, allowRegisteredBoundaryProof, authorization, review);
    }

    public async Task<ArchiveQuarantineResult> RestoreAsync(string receiptId, CancellationToken ct = default)
    {
        var receipt = await Read(receiptId, ct);
        var gate = Gates.GetOrAdd(receipt.ArtifactId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            receipt = await Read(receiptId, ct);
            if (receipt.Status is "Deleting" or "Purged" || receipt.AbsentBeforeDeletion.Count > 0)
                return new(false, "ReceiptPurged", receipt.Id, "Permanent source cleanup has begun; this receipt cannot be restored.");
            if (receipt.Status == "Restored")
            {
                PruneReceiptDirectories(receipt);
                return new(true, "Restored", receipt.Id);
            }
            var artifact = await _store.GetArtifactAsync(receipt.ArtifactId, ct) ?? throw new ArgumentException("The artifact no longer exists.");
            if (receipt.Manifest.Entries.Count > 0)
            {
                var relocate = ReadReceiptLocationSnapshot(ct);
                var original = DecodeLocation(receipt.OriginalRoot, _appData, relocate);
                var quarantine = DecodeLocation(receipt.QuarantineRoot, _appData, relocate);
                EnsureAllowed(original, receipt.Managed);
                // A removed task directory and an offline volume can look identical.
                // Neither permits a restore to recreate directories or change the receipt.
                if (!Directory.Exists(original) && !Directory.Exists(quarantine))
                    return new(false, "RecoveryLocationUnavailable", receipt.Id,
                        "Neither the original nor the quarantine directory is available. Restore can be retried when storage is available.");
            }
            // Even a receipt interrupted immediately after its durable intent must acquire the fence.
            if (artifact.State != ArtifactLocationState.Quarantined && !await _store.TryQuarantineAsync(artifact.Id, ct))
                return new(false, "Blocked", receipt.Id, "The artifact has active consumers.");
            receipt.Status = "Restoring";
            await Save(receipt, ct);
            await Relocate(receipt, restoring: true, ct);
            await _store.SetLocationAsync(artifact.Id, Decode(receipt.OriginalRoot), ArtifactLocationState.Available, ct);
            receipt.Status = "Restored"; receipt.Message = null;
            await Save(receipt, ct);
            PruneReceiptDirectories(receipt);
            return new(true, "Restored", receipt.Id);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            receipt.Message = error is OperationCanceledException ? "Restoration was interrupted; resume it to continue." : error.Message;
            try { await Save(receipt, CancellationToken.None); } catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException) { }
            return new(false, "Pending", receipt.Id, receipt.Message);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<ArchiveQuarantineReceipt>> GetReceiptsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_receipts)) return [];
        var results = new List<ArchiveQuarantineReceipt>();
        Func<string, string>? relocate = null;
        foreach (var file in Directory.EnumerateFiles(_receipts, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var receipt = await ReadReceiptHeaderAsync(file, ct);
            // Both locations of every result use one validated mapping for this query.
            // A later safety check gets a fresh snapshot; no aliases survive this call.
            relocate ??= ReadReceiptLocationSnapshot(ct);
            results.Add(new(receipt.Id, receipt.ArtifactId,
                DecodeLocation(receipt.OriginalRoot, _appData, relocate),
                DecodeLocation(receipt.QuarantineRoot, _appData, relocate),
                receipt.Status, receipt.OutputIds, receipt.Message));
        }
        return results;
    }

    private static async Task<ReceiptHeader> ReadReceiptHeaderAsync(string file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = Path.GetFileNameWithoutExtension(file);
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid quarantine receipt identity.");
        ArtifactManifestFiles.RejectLinks(file);
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Validate the whole JSON stream without materializing unrelated manifests or
        // consent histories. Headers select candidates, never authorize file deletion:
        // selected operations still Read() their complete receipt and verify its inputs.
        var receipt = await JsonSerializer.DeserializeAsync<ReceiptHeader>(stream, Json, ct);
        if (receipt == null || receipt.Id != id || string.IsNullOrWhiteSpace(receipt.ArtifactId) ||
            string.IsNullOrWhiteSpace(receipt.OriginalRoot) || string.IsNullOrWhiteSpace(receipt.QuarantineRoot) ||
            string.IsNullOrWhiteSpace(receipt.Status) || receipt.OutputIds == null ||
            receipt.OutputIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The quarantine receipt header is invalid.");
        return receipt;
    }

    private Func<string, string> ReadReceiptLocationSnapshot(CancellationToken ct)
    {
        var mappings = new FileProcessingWorkspaceResolver(_appData, _storage).ReadRelocations();
        return path =>
        {
            ct.ThrowIfCancellationRequested();
            foreach (var mapping in mappings)
                if (ArtifactManifestFiles.PathComparer.Equals(mapping.SourceRoot, path) ||
                    ArtifactCleanupPathReferences.StrictlyUnder(path, mapping.SourceRoot))
                    return Path.GetFullPath(Path.Combine(mapping.DestinationRoot,
                        Path.GetRelativePath(mapping.SourceRoot, path)));
            return path;
        };
    }

    private sealed class ReceiptHeader
    {
        public string Id { get; init; } = "";
        public string ArtifactId { get; init; } = "";
        public string OriginalRoot { get; init; } = "";
        public string QuarantineRoot { get; init; } = "";
        public string Status { get; init; } = "Moving";
        public List<string> OutputIds { get; init; } = [];
        public string? Message { get; init; }
    }

    /// <summary>Prune only receipt-owned empty directories. Unknown files and all quarantine contents remain untouched.</summary>
    public async Task CleanupEmptyDirectoriesAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_receipts)) return;
        foreach (var path in Directory.EnumerateFiles(_receipts, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            var receipt = await Read(Path.GetFileNameWithoutExtension(path), ct);
            var gate = Gates.GetOrAdd(receipt.ArtifactId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                receipt = await Read(receipt.Id, ct);
                if (receipt.Status is "Restored" or "Blocked" or "Purged") PruneReceiptDirectories(receipt);
            }
            finally { gate.Release(); }
        }
    }
    private void PruneReceiptDirectories(Receipt receipt)
    {
        try
        {
            var original = Decode(receipt.OriginalRoot);
            var root = Decode(receipt.QuarantineRoot);
            var expected = Path.Combine(original, ".bakabase-quarantine", receipt.Id);
            if (!ArtifactManifestFiles.PathComparer.Equals(root, expected)) return;
            EnsureAllowed(original, receipt.Managed);
            // Only directories represented by this receipt's exact manifest may be removed.
            var directories = receipt.Manifest.Entries.Select(entry => Path.GetDirectoryName(
                    FileProcessingFiles.Within(Path.Combine(root, entry.RelativePath), root))!)
                .Concat(ArtifactManifestFiles.ResolveDirectories(root, receipt.Manifest))
                .ToHashSet(ArtifactManifestFiles.PathComparer);
            foreach (var directory in directories.ToList())
            {
                if (ArtifactManifestFiles.PathComparer.Equals(directory, root)) continue;
                for (var parent = Path.GetDirectoryName(directory); parent != null &&
                     !ArtifactManifestFiles.PathComparer.Equals(parent, root); parent = Path.GetDirectoryName(parent))
                {
                    FileProcessingFiles.Within(parent, root);
                    directories.Add(parent);
                }
            }
            directories.Add(root);
            foreach (var directory in directories.OrderByDescending(p => p.Length))
            {
                ArtifactManifestFiles.RejectLinks(directory);
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, recursive: false);
            }
            var container = Path.GetDirectoryName(root)!;
            ArtifactManifestFiles.RejectLinks(container);
            if (Directory.Exists(container) && !Directory.EnumerateFileSystemEntries(container).Any())
                Directory.Delete(container, recursive: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { /* Cleanup is best effort; a recovery result remains successful and every unknown entry is retained. */ }
    }

    private async Task Relocate(Receipt receipt, bool restoring, CancellationToken ct, ArtifactCleanupFileSnapshot? snapshot = null)
    {
        var sourceRoot = Decode(restoring ? receipt.QuarantineRoot : receipt.OriginalRoot);
        var destinationRoot = Decode(restoring ? receipt.OriginalRoot : receipt.QuarantineRoot);
        EnsureAllowed(Decode(receipt.OriginalRoot), receipt.Managed);
        // Empty directories are part of the committed manifest even though no file move
        // creates them. Recreate only those exact directories, including on an interrupted
        // Moving/Restoring receipt. The shared helper validates all paths and refuses files
        // or links at those locations before any payload moves. Original directories and
        // unregistered contents stay untouched; terminal pruning removes only empty owned paths.
        ct.ThrowIfCancellationRequested();
        ArtifactManifestFiles.CreateDirectories(destinationRoot, receipt.Manifest);
        var absent = MissingDeletionEntries(receipt);
        if ((absent.Count > 0 || receipt.MovedDirectoriesBeforeDeletion.Count > 0) && restoring)
            throw new InvalidOperationException("This permanent cleanup includes already removed files and cannot be restored.");
        if (absent.Count > 0)
        {
            // The first manifest entry may already be gone, before any move has created
            // the private destination. Probe a real owned directory, not a missing root.
            ArtifactManifestFiles.RejectLinks(destinationRoot);
            CreatePrivate(destinationRoot);
            ArtifactManifestFiles.RejectLinks(destinationRoot);
        }
        // For a single-file archive, the post-move verification is also the complete
        // destination check. Use the full manifest here (including empty directories)
        // rather than immediately hashing the same bytes a second time.
        var singleFile = receipt.Manifest.Entries.Count == 1;
        Task VerifyDestination(string path, ArtifactManifestEntry entry) => singleFile
            ? VerifyManifest(destinationRoot, receipt.Manifest, ct, snapshot, absent) : VerifyFile(path, entry, ct, snapshot);
        foreach (var entry in receipt.Manifest.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var source = FileProcessingFiles.Within(Path.Combine(sourceRoot, entry.RelativePath), sourceRoot);
            var destination = FileProcessingFiles.Within(Path.Combine(destinationRoot, entry.RelativePath), destinationRoot);
            if (absent.Contains(entry.RelativePath))
            {
                RequireAbsent(sourceRoot, source, ct);
                RequireAbsent(destinationRoot, destination, ct);
                continue;
            }
            if (!File.Exists(source))
            {
                await VerifyDestination(destination, entry); // A crash may have committed this atomic move before its receipt.
                continue;
            }
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("Restoration or quarantine would overwrite an existing entry.");
            ArtifactManifestFiles.RejectLinks(source);
            ArtifactManifestFiles.RejectLinks(destination);
            await VerifyFile(source, entry, ct, snapshot);
            CreatePrivate(Path.GetDirectoryName(destination)!);
            FileProcessingFiles.Within(destination, destinationRoot);
            ArtifactManifestFiles.RejectLinks(source);
            ArtifactManifestFiles.RejectLinks(destination);
            File.Move(source, destination, overwrite: false); // Same-volume only; never fall back to copy-and-delete.
            try
            {
                snapshot?.Moved(source, destination, entry.Length, ct);
                await VerifyDestination(destination, entry);
            }
            catch
            {
                // A concurrently changed file must not stay removed from its user's original path.
                // Returning it is recoverable and never overwrites a newly created entry.
                if (!File.Exists(source) && !Directory.Exists(source))
                {
                    ArtifactManifestFiles.RejectLinks(source);
                    ArtifactManifestFiles.RejectLinks(destination);
                    File.Move(destination, source, overwrite: false);
                }
                throw;
            }
            // The Moving/Restoring intent already pins the complete manifest. No progress
            // field changes per file: recovery verifies an existing destination before
            // accepting a move that committed before the terminal receipt was saved.
        }
        if (!singleFile) await VerifyManifest(destinationRoot, receipt.Manifest, ct, snapshot, absent);
    }

    private async Task VerifyOutputs(ArtifactRecord source, IReadOnlyList<string> ids, CancellationToken ct,
        bool allowRegisteredBoundaryProof = false, ArtifactDeliveryCleanupAuthorization? authorization = null,
        ReviewedBoundaryApproval? review = null)
    {
        if (review != null)
        {
            await VerifyReviewedBoundary(source, ids, review, ct);
            return;
        }
        if (ids.Count == 0) throw new InvalidOperationException("A verified successful output is required before quarantining sources.");
        var boundaryProofs = new Dictionary<string, bool>(StringComparer.Ordinal);
        var sourceFiles = source.Files.ToHashSet(FileProcessingFiles.PathComparer);
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var output = await _store.GetArtifactAsync(id, ct) ?? throw new InvalidOperationException("A processing output is missing or modified.");
            var ancestors = await Ancestors(output, ct);
            if (allowRegisteredBoundaryProof && output.Kind == "publishedFiles" && !ancestors.Contains(source.Id))
            {
                var graph = new Dictionary<string, ArtifactRecord>(StringComparer.Ordinal) {{output.Id, output}};
                foreach (var ancestor in ancestors)
                    if (await _store.GetArtifactAsync(ancestor, ct) is {} record) graph.Add(record.Id, record);
                boundaryProofs[id] = await FileProcessingPlanExecutor.BoundaryPublicationProofAsync(_store, source,
                    output, graph, await GetReceiptsAsync(ct), ct) != null;
            }
            var acceptedHistorical = allowRegisteredBoundaryProof && authorization != null &&
                await authorization.CoversHistoricalProcessingAsync(source, [id], ct);
            if (output.Id == source.Id || !ancestors.Contains(source.Id) && !boundaryProofs.GetValueOrDefault(id) && !acceptedHistorical)
                throw new InvalidOperationException("The successful output is not derived from this source.");
            if (output.Files.Any(sourceFiles.Contains))
                throw new InvalidOperationException("The successful output still references a source selected for quarantine.");
            var operation = await _store.GetOperationAsync(output.OperationId, ct);
            if (operation?.State != ArtifactOperationState.Completed)
                throw new InvalidOperationException("The output operation has not completed successfully.");
            // Validate the bytes once, after the provenance checks, at the same final
            // boundary as the former second full scan. Never carry this proof between
            // relocation/deletion phases, or accept a different artifact generation.
            if (authorization?.Consent.DeliveryAccepted == true && await authorization.MatchesAsync(output, ct))
                continue;
            var verified = await _store.ResolveByIdAsync(id, ct);
            if (verified == null || !SameOutputIdentity(output, verified))
                throw new InvalidOperationException("A processing output is missing, modified or changed identity.");
        }
        if (authorization != null && await authorization.SkipsReplacementProofAsync(source, ids, ct)) return;
        var coverage = await ArtifactFileCoverage.EvaluateAsync(_store, source, ids, ct);
        if (!coverage.Complete && !ids.All(id => boundaryProofs.GetValueOrDefault(id)))
            throw new InvalidOperationException(ArtifactFileCoverage.IncompleteCode +
                ": Some source files have no verified retained copy or complete replacement. They were kept for review.");
    }

    private async Task<HashSet<string>> Ancestors(ArtifactRecord artifact, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(artifact.ParentArtifactIds);
        while (queue.TryDequeue(out var id))
        {
            if (!seen.Add(id)) continue;
            if (seen.Count > 10000) throw new InvalidOperationException("The artifact dependency graph is too large.");
            var parent = await _store.GetArtifactAsync(id, ct) ?? throw new InvalidOperationException("The artifact's source provenance is missing.");
            foreach (var ancestor in parent.ParentArtifactIds) queue.Enqueue(ancestor);
        }
        return seen;
    }

    private static async Task VerifyManifest(string root, ArtifactManifest manifest, CancellationToken ct,
        ArtifactCleanupFileSnapshot? snapshot = null, IReadOnlySet<string>? confirmedAbsent = null,
        IReadOnlySet<string>? movedDirectories = null)
    {
        if (snapshot != null || confirmedAbsent is {Count: > 0} || movedDirectories is {Count: > 0})
        {
            var paths = ArtifactManifestFiles.ResolveFiles(root, manifest);
            ArtifactManifestFiles.VerifyDirectories(root, movedDirectories is {Count: > 0}
                ? manifest with {Directories = manifest.Directories!.Where(d => !movedDirectories.Contains(d)).ToArray()} : manifest);
            if (movedDirectories != null)
                foreach (var directory in movedDirectories) RequireAbsent(root, Path.Combine(root, directory), ct);
            for (var index = 0; index < paths.Count; index++)
            {
                if (confirmedAbsent?.Contains(manifest.Entries[index].RelativePath) == true)
                    RequireAbsent(root, paths[index], ct);
                else await VerifyFile(paths[index], manifest.Entries[index], ct, snapshot);
            }
            return;
        }
        var validation = await ArtifactManifestFiles.ValidateAsync(root, manifest, ct);
        if (!validation.Available) throw new InvalidDataException(validation.Message ?? "An artifact is missing or modified.");
    }
    private static async Task VerifyFile(string path, ArtifactManifestEntry expected, CancellationToken ct,
        ArtifactCleanupFileSnapshot? snapshot = null)
    {
        if (snapshot != null) { snapshot.Observe(path, expected.Length, ct); return; }
        ArtifactManifestFiles.RejectLinks(path);
        if (new FileInfo(path).Length != expected.Length)
            throw new InvalidDataException("An artifact file was modified; it was retained for review.");
        // Shared full-content SHA uses bounded 4 MiB reads and validates the opened handle,
        // path and no-links boundary before and after reading. This is not a metadata cache.
        var actual = (await ArtifactManifestFiles.CaptureAsync(Path.GetDirectoryName(path)!, [path], ct)).Entries.Single();
        if (actual.Length != expected.Length || !actual.Sha256.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An artifact file was modified; it was retained for review.");
    }

    private static bool SameOutputIdentity(ArtifactRecord expected, ArtifactRecord verified) =>
        verified.State == ArtifactLocationState.Available && expected.Id == verified.Id &&
        expected.OperationId == verified.OperationId && expected.Kind == verified.Kind && expected.Managed == verified.Managed &&
        expected.Fingerprint == verified.Fingerprint && expected.MetadataJson == verified.MetadataJson &&
        FileProcessingFiles.PathComparer.Equals(expected.RootDirectory, verified.RootDirectory) &&
        expected.Files.SequenceEqual(verified.Files, FileProcessingFiles.PathComparer) &&
        expected.ParentArtifactIds.SequenceEqual(verified.ParentArtifactIds, StringComparer.Ordinal) &&
        expected.Manifest.Fingerprint == verified.Manifest.Fingerprint &&
        expected.Manifest.Entries.SequenceEqual(verified.Manifest.Entries) &&
        (expected.Manifest.Directories ?? []).SequenceEqual(verified.Manifest.Directories ?? [], StringComparer.Ordinal);
    private void EnsureAllowed(string root, bool managed)
    {
        if (!(managed && IsUnderAppData(root))) _storage.EnsurePathAllowed(root);
    }
    private bool IsUnderAppData(string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(_appData) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private string Encode(string path) => IsUnderAppData(path) ? "appdata:" + Path.GetRelativePath(_appData, path) : "absolute:" + Path.GetFullPath(path);
    private string Decode(string path) => DecodeLocation(path, _appData, new FileProcessingWorkspaceResolver(_appData, _storage));
    internal static string DecodeLocation(string path, string appData, FileProcessingWorkspaceResolver locations) =>
        DecodeLocation(path, appData, locations.Relocate);
    private static string DecodeLocation(string path, string appData, Func<string, string> relocate)
    {
        var original = path.StartsWith("appdata:", StringComparison.Ordinal)
            ? FileProcessingFiles.Within(Path.Combine(appData, path[8..]), appData)
            : path.StartsWith("absolute:", StringComparison.Ordinal) ? Path.GetFullPath(path[9..])
                : throw new InvalidDataException("Invalid quarantine location.");
        // Receipt journals stay in AppData. Only the referenced payload follows its verified
        // relocation aliases; retain the original encoded paths as historical evidence.
        return relocate(original);
    }
    private async Task<Receipt?> FindPending(string artifactId, CancellationToken ct)
    {
        if (!Directory.Exists(_receipts)) return null;
        foreach (var path in Directory.EnumerateFiles(_receipts, "*.json"))
        {
            var header = await ReadReceiptHeaderAsync(path, ct);
            if (header.ArtifactId != artifactId || !IsPendingReceiptStatus(header.Status)) continue;
            var receipt = await Read(header.Id, ct);
            // Atomic journal replacement between the two reads must not turn a selected
            // header into evidence for another artifact or an already restored receipt.
            if (receipt.Id != header.Id || receipt.ArtifactId != artifactId ||
                !IsPendingReceiptStatus(receipt.Status))
                throw new InvalidDataException("The quarantine receipt changed during lookup; retry with its current state.");
            return receipt;
        }
        return null;
    }
    private static bool IsPendingReceiptStatus(string status) =>
        status is "Moving" or "Quarantined" or "Restoring" or "Deleting" or "Purged";
    private async Task<Receipt> Read(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid quarantine receipt identity.");
        var path = FileProcessingFiles.Within(Path.Combine(_receipts, id + ".json"), _receipts);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Receipt>(stream, Json, ct)
            ?? throw new InvalidDataException("The quarantine receipt is invalid.");
    }
    private async Task Save(Receipt receipt, CancellationToken ct)
    {
        CreatePrivate(_receipts);
        FileProcessingFiles.Within(_receipts, _appData);
        var path = Path.Combine(_receipts, receipt.Id + ".json");
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt, Json), ct);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
    private static void CreatePrivate(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public sealed class Receipt
    {
        public string Id { get; set; } = "";
        public string ArtifactId { get; set; } = "";
        public string OriginalRoot { get; set; } = "";
        public string QuarantineRoot { get; set; } = "";
        public string Status { get; set; } = "Moving";
        public string? Message { get; set; }
        public bool Managed { get; set; }
        public ArtifactManifest Manifest { get; set; } = null!;
        public List<string> OutputIds { get; set; } = [];
        public string? ReviewedBoundaryProof { get; set; }
        public List<ArtifactDeliveryCleanupConsent> Consents { get; set; } = [];
        public List<string> AbsentBeforeDeletion { get; set; } = [];
        public List<string> MovedBeforeDeletion { get; set; } = [];
        public List<string> MovedDirectoriesBeforeDeletion { get; set; } = [];
    }
}
