using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>Durable semantic operations, verified file artifacts and expiring consumer references.</summary>
public sealed class ArtifactOperationStore : IArtifactOperationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly BakabaseDbContext _db;
    private readonly Func<string> _appData;
    private readonly IUserStoragePolicy? _storage;

    public ArtifactOperationStore(BakabaseDbContext db, AppService app, IUserStoragePolicy storage)
        : this(db, () => app.AppDataDirectory, storage) { }

    internal ArtifactOperationStore(BakabaseDbContext db, Func<string> appData, IUserStoragePolicy? storage = null)
    { _db = db; _appData = appData; _storage = storage; }

    public Task<IAsyncDisposable> AcquireMaintenanceAsync(CancellationToken ct = default) =>
        ArtifactMaintenanceGate.For(_appData()).EnterMaintenanceAsync(ct);

    public async Task<bool> HasRegisteredFilesAsync(IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var candidates = files.ToHashSet(ArtifactManifestFiles.PathComparer);
        var roots = FileAncestorRoots(files);
        var artifacts = await FileReferences(candidateFiles: files).ToListAsync(ct);
        return artifacts.Any(artifact => MayContain(artifact, roots) && Paths(artifact).Any(candidates.Contains));
    }

    public async Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> files, string excludedArtifactId, CancellationToken ct = default)
    {
        var candidates = files.ToHashSet(ArtifactManifestFiles.PathComparer);
        var roots = FileAncestorRoots(files);
        var artifacts = await FileReferences(excludedArtifactId, candidateFiles: files).ToListAsync(ct);
        return artifacts.Any(artifact => MayContain(artifact, roots) && Paths(artifact).Any(candidates.Contains));
    }

    public Task<ArtifactOperationDbModel?> GetOperationAsync(string operationId, CancellationToken ct = default) =>
        _db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleOrDefaultAsync(o => o.Id == operationId, ct);

    public async Task<ArtifactRecord?> GetArtifactAsync(string artifactId, CancellationToken ct = default)
    {
        var row = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == artifactId, ct);
        return row == null ? null : await Project(row, ct);
    }

    public async Task<bool> HasOtherRegisteredFilesAsync(IReadOnlyList<string> files, string excludedArtifactId,
        IReadOnlyList<string> excludedViewIds, CancellationToken ct = default)
    {
        var candidates = files.ToHashSet(ArtifactManifestFiles.PathComparer);
        var roots = FileAncestorRoots(files);
        var excluded = excludedViewIds.ToArray();
        var artifacts = await FileReferences(excludedArtifactId, excluded, files).ToListAsync(ct);
        return artifacts.Any(artifact => MayContain(artifact, roots) && Paths(artifact).Any(candidates.Contains));
    }

    public async Task<ArtifactRecord?> ResolveAsync(string intentKey, CancellationToken ct = default)
    {
        var ids = await _db.Set<ArtifactOperationDbModel>().AsNoTracking()
            .Where(o => o.IntentKey == intentKey && o.State == ArtifactOperationState.Completed && o.OutputArtifactId != null)
            .OrderByDescending(o => o.UpdatedAt).Select(o => o.OutputArtifactId!).ToListAsync(ct);
        foreach (var id in ids)
            if (await ResolveByIdAsync(id, ct) is { } available) return available;
        return null;
    }

    public async Task<ArtifactRecord?> ResolveByIdAsync(string artifactId, CancellationToken ct = default)
    {
        var row = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == artifactId, ct);
        if (row == null || row.State is ArtifactLocationState.Quarantined or ArtifactLocationState.Retired) return null;
        var artifact = await Project(row, ct);
        EnsureAllowed(artifact.RootDirectory);
        foreach (var file in artifact.Files) EnsureAllowed(file);
        // A disconnected storage root is not evidence that its files were deleted. Preserve
        // the completed generation so a retry cannot silently reacquire the same content.
        var validation = Directory.Exists(artifact.RootDirectory)
            ? await ArtifactManifestFiles.ValidateAsync(artifact.RootDirectory, artifact.Manifest, ct)
            : new ArtifactValidation(ArtifactLocationState.StorageUnavailable, "The artifact storage directory is unavailable.");
        // A concurrent quarantine or location change must never be overwritten by this observation.
        var updated = await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifactId && a.State == row.State &&
                a.RootPath == row.RootPath && a.RootIsAppData == row.RootIsAppData && a.UpdatedAt == row.UpdatedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, validation.State)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
        if (!validation.Available) return null;
        if (updated == 1) return artifact with {State = ArtifactLocationState.Available};
        // Concurrent read-only validation is harmless; a relocation or quarantine is not.
        var latest = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == artifactId, ct);
        return latest is {State: ArtifactLocationState.Available} && latest.RootPath == row.RootPath &&
            latest.RootIsAppData == row.RootIsAppData && latest.ManifestJson == row.ManifestJson
                ? artifact with {State = ArtifactLocationState.Available} : null;
    }

    public async Task<ArtifactOperationClaim> ClaimAsync(ArtifactOperationRequest request, string ownerId,
        TimeSpan leaseDuration, CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        return await ClaimCoreAsync(request, ownerId, leaseDuration, ct);
    }

    public async Task<ArtifactOperationClaim> ClaimSameAttemptAsync(ArtifactOperationRequest request, int expectedAttempt,
        string ownerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        if (expectedAttempt < 1) throw new ArgumentOutOfRangeException(nameof(expectedAttempt));
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        return await ClaimCoreAsync(request, ownerId, leaseDuration, ct, expectedAttempt);
    }

    private async Task<ArtifactOperationClaim> ClaimCoreAsync(ArtifactOperationRequest request, string ownerId,
        TimeSpan leaseDuration, CancellationToken ct = default, int? expectedAttempt = null)
    {
        ValidateRequest(request);
        ValidateDuration(leaseDuration);
        if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("An operation lease needs an owner.");
        var operation = await GetOperationAsync(request.OperationKey, ct);
        if (operation == null)
        {
            if (expectedAttempt != null) return new(ArtifactClaimStatus.NeedsReconciliation, request.OperationKey);
            var added = new ArtifactOperationDbModel {Id = request.OperationKey, IntentKey = request.IntentKey,
                Kind = request.Kind, InputFingerprint = request.InputFingerprint, Restartable = request.Restartable};
            _db.Add(added);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                _db.Entry(added).State = EntityState.Detached;
                if (await GetOperationAsync(request.OperationKey, ct) == null) throw;
            }
            _db.Entry(added).State = EntityState.Detached;
        }
        for (var attempt = 0; attempt < 8; attempt++)
        {
            operation = await GetOperationAsync(request.OperationKey, ct)
                ?? throw new InvalidOperationException("The operation disappeared.");
            if (operation.IntentKey != request.IntentKey || operation.Kind != request.Kind ||
                operation.InputFingerprint != request.InputFingerprint || operation.Restartable != request.Restartable)
                throw new InvalidOperationException("The operation key was reused with different semantic inputs.");
            // Deliberate retirement is not a cache miss. Even if the old files reappear,
            // an automatic retry must not recreate an intentionally removed generation.
            if (operation.OutputArtifactId != null &&
                await _db.Set<ArtifactDbModel>().AsNoTracking().AnyAsync(a =>
                    a.Id == operation.OutputArtifactId && a.State == ArtifactLocationState.Retired, ct))
                return new(ArtifactClaimStatus.NeedsReconciliation, operation.Id);
            // Rechecked on each CAS iteration: queue delay or another consumer must never
            // turn this explicitly authorized continuation into a new operation generation.
            if (expectedAttempt is { } expected && (operation.Attempt != expected ||
                operation.State is not (ArtifactOperationState.Running or ArtifactOperationState.Completed) ||
                !operation.Restartable || operation.State == ArtifactOperationState.Running && operation.LeaseOwner != ownerId))
                return new(ArtifactClaimStatus.NeedsReconciliation, operation.Id);
            if (operation.State == ArtifactOperationState.Completed && operation.OutputArtifactId != null &&
                await ResolveByIdAsync(operation.OutputArtifactId, ct) is { } result)
                return new(ArtifactClaimStatus.Completed, operation.Id, Artifact: result);
            if (operation.State == ArtifactOperationState.Completed && operation.OutputArtifactId != null &&
                await _db.Set<ArtifactDbModel>().AsNoTracking().AnyAsync(a => a.Id == operation.OutputArtifactId &&
                    a.State == ArtifactLocationState.StorageUnavailable, ct))
                return new(ArtifactClaimStatus.StorageUnavailable, operation.Id);
            if (expectedAttempt != null && operation.State == ArtifactOperationState.Completed)
                return new(ArtifactClaimStatus.NeedsReconciliation, operation.Id);
            // A recovery caller can authorize only a first attempt. This request-local constraint
            // is deliberately not part of semantic identity. Check inside the CAS loop so a
            // concurrent first claim cannot turn a read-before-claim race into another generation.
            if (!request.AllowRetry && (operation.Attempt != 0 || operation.State != ArtifactOperationState.Planned))
                return new(ArtifactClaimStatus.NeedsReconciliation, operation.Id);
            var now = DateTime.UtcNow;
            if (operation.State == ArtifactOperationState.Running && operation.LeaseUntil > now)
            {
                if (operation.LeaseOwner != ownerId || operation.LeaseToken == null)
                    return new(ArtifactClaimStatus.Busy, operation.Id);
                var owned = new ArtifactOperationLease(operation.Id, operation.LeaseToken);
                if (await RenewAsync(owned, leaseDuration, ct))
                    return new(ArtifactClaimStatus.Acquired, operation.Id, owned);
                continue;
            }
            if (operation.State == ArtifactOperationState.Unknown ||
                (operation.State is ArtifactOperationState.Running or ArtifactOperationState.Completed && !operation.Restartable))
                return new(ArtifactClaimStatus.NeedsReconciliation, operation.Id);
            var token = Guid.NewGuid().ToString("N");
            var until = now + leaseDuration;
            var generation = operation.State == ArtifactOperationState.Running
                ? operation.Attempt : operation.Attempt + 1;
            var updated = await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == operation.Id &&
                    o.State == operation.State && o.LeaseToken == operation.LeaseToken && o.Attempt == operation.Attempt &&
                    o.LeaseUntil == operation.LeaseUntil)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Running)
                    .SetProperty(o => o.LeaseToken, token).SetProperty(o => o.LeaseOwner, ownerId)
                    .SetProperty(o => o.LeaseUntil, until).SetProperty(o => o.Attempt, generation)
                    .SetProperty(o => o.Error, (string?) null).SetProperty(o => o.UpdatedAt, now), ct);
            if (updated == 1) return new(ArtifactClaimStatus.Acquired, operation.Id, new(operation.Id, token),
                StartedNewAttempt: generation > operation.Attempt);
        }
        return new(ArtifactClaimStatus.Busy, request.OperationKey);
    }

    public Task<bool> RenewAsync(ArtifactOperationLease lease, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        ValidateDuration(leaseDuration);
        return RenewCore(lease, leaseDuration, ct);
    }

    private async Task<bool> RenewCore(ArtifactOperationLease lease, TimeSpan duration, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == lease.OperationId &&
                o.LeaseToken == lease.Token && o.State == ArtifactOperationState.Running && o.LeaseUntil > now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, now + duration)
                .SetProperty(o => o.UpdatedAt, now), ct) == 1;
    }

    public async Task<ArtifactRecord> CompleteAsync(ArtifactOperationLease lease, ArtifactRegistration registration,
        CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        return await CompleteCoreAsync(lease, registration, ct);
    }

    private async Task<ArtifactRecord> CompleteCoreAsync(ArtifactOperationLease lease, ArtifactRegistration registration,
        CancellationToken ct = default)
    {
        var current = await GetOperationAsync(lease.OperationId, ct);
        if (current?.State == ArtifactOperationState.Completed && current.LeaseToken == lease.Token &&
            current.OutputArtifactId != null && await ResolveByIdAsync(current.OutputArtifactId, ct) is { } existing)
            return existing;
        if (current?.State != ArtifactOperationState.Running || current.LeaseToken != lease.Token ||
            current.LeaseUntil <= DateTime.UtcNow) throw new InvalidOperationException("The operation lease is no longer owned.");
        EnsureAllowed(registration.RootDirectory);
        foreach (var file in registration.Files) EnsureAllowed(file);
        foreach (var directory in registration.Directories ?? []) EnsureAllowed(directory);
        var manifest = await ArtifactManifestFiles.CaptureAsync(registration.RootDirectory, registration.Files.ToArray(), ct,
            registration.Directories);
        if (registration.ExpectedManifestFingerprint != null &&
            !string.Equals(registration.ExpectedManifestFingerprint, manifest.Fingerprint, StringComparison.Ordinal))
            throw new IOException("The output files changed after their file-disposition proof was recorded; no artifact was committed.");
        var parents = (registration.ParentArtifactIds ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (parents.Length > 0 && await _db.Set<ArtifactDbModel>().CountAsync(a => parents.Contains(a.Id), ct) != parents.Length)
            throw new InvalidDataException("An artifact's parent does not exist.");
        var location = StoreRoot(registration.RootDirectory);
        var artifact = new ArtifactDbModel {Id = Guid.NewGuid().ToString("N"), OperationId = lease.OperationId,
            Kind = registration.Kind, RootPath = location.Path, RootIsAppData = location.IsAppData,
            ManifestJson = JsonSerializer.Serialize(manifest, Json), Fingerprint = manifest.Fingerprint,
            Managed = registration.Managed, MetadataJson = registration.CreateMetadata == null
                ? registration.MetadataJson : registration.CreateMetadata(manifest)};
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = DateTime.UtcNow;
        var changed = await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == lease.OperationId &&
                o.LeaseToken == lease.Token && o.State == ArtifactOperationState.Running && o.LeaseUntil > now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, ArtifactOperationState.Completed)
                .SetProperty(o => o.OutputArtifactId, artifact.Id).SetProperty(o => o.LeaseUntil, (DateTime?) null)
                .SetProperty(o => o.Error, (string?) null).SetProperty(o => o.UpdatedAt, now), ct);
        if (changed != 1) throw new InvalidOperationException("The operation lease is no longer owned.");
        _db.Add(artifact);
        foreach (var parent in parents)
            _db.Add(new ArtifactLineageDbModel {Id = Identity(parent, artifact.Id), ParentArtifactId = parent,
                ChildArtifactId = artifact.Id});
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        _db.Entry(artifact).State = EntityState.Detached;
        return await Project(artifact, ct);
    }

    public async Task<bool> FailAsync(ArtifactOperationLease lease, string error, bool outcomeUnknown = false,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == lease.OperationId &&
                o.LeaseToken == lease.Token && o.State == ArtifactOperationState.Running && o.LeaseUntil > now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.State, outcomeUnknown ? ArtifactOperationState.Unknown : ArtifactOperationState.Failed)
                .SetProperty(o => o.Error, error).SetProperty(o => o.LeaseUntil, (DateTime?) null)
                .SetProperty(o => o.UpdatedAt, now), ct) == 1;
    }

    public async Task<ArtifactRecord> AdoptAsync(ArtifactOperationRequest request, ArtifactRegistration registration,
        string ownerId, CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        var claim = await ClaimCoreAsync(request, ownerId, TimeSpan.FromMinutes(30), ct);
        // Adoption is local publication, so another completed publisher can be reused safely.
        var waitUntil = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (claim.Status == ArtifactClaimStatus.Busy && DateTime.UtcNow < waitUntil)
        {
            await Task.Delay(100, ct);
            claim = await ClaimCoreAsync(request, ownerId, TimeSpan.FromMinutes(30), ct);
        }
        if (claim.Artifact != null) return claim.Artifact;
        if (claim.Lease == null) throw new InvalidOperationException("Another operation owns this artifact adoption.");
        try { return await CompleteCoreAsync(claim.Lease, registration, ct); }
        catch (Exception error)
        {
            // The same owner can resume twice concurrently. A completed publication wins;
            // the losing publisher must not fail an operation that already has valid output.
            var completed = await GetOperationAsync(claim.OperationId, ct);
            if (completed is {State: ArtifactOperationState.Completed, OutputArtifactId: not null} &&
                await ResolveByIdAsync(completed.OutputArtifactId, ct) is { } result) return result;
            await FailAsync(claim.Lease, error.Message, ct: CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> AttachConsumerAsync(string operationId, string consumerKey, TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        return await AttachConsumerCoreAsync(operationId, consumerKey, leaseDuration, ct);
    }

    private async Task<bool> AttachConsumerCoreAsync(string operationId, string consumerKey, TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ValidateDuration(leaseDuration);
        if (string.IsNullOrWhiteSpace(consumerKey)) throw new ArgumentException("A consumer key is required.");
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var operation = await GetOperationAsync(operationId, ct);
        if (operation == null) return false;
        if (operation.OutputArtifactId != null)
        {
            var artifacts = await FileReferences().ToListAsync(ct);
            var target = artifacts.SingleOrDefault(a => a.Id == operation.OutputArtifactId);
            if (target == null || target.State != ArtifactLocationState.Available) return false;
            var targetPaths = Paths(target).ToHashSet(ArtifactManifestFiles.PathComparer);
            if (artifacts.Any(a => a.State == ArtifactLocationState.Quarantined && Paths(a).Any(targetPaths.Contains))) return false;
        }
        var id = Identity(operationId, consumerKey);
        var until = DateTime.UtcNow + leaseDuration;
        var changed = await _db.Set<ArtifactConsumerDbModel>().Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, until)
                .SetProperty(c => c.ReleasedAt, (DateTime?) null), ct);
        if (changed == 0)
        {
            var row = new ArtifactConsumerDbModel {Id = id, OperationId = operationId,
                ConsumerKey = consumerKey, LeaseUntil = until};
            _db.Add(row);
            await _db.SaveChangesAsync(ct);
            _db.Entry(row).State = EntityState.Detached;
        }
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task ReleaseConsumerAsync(string operationId, string consumerKey, CancellationToken ct = default) =>
        await _db.Set<ArtifactConsumerDbModel>().Where(c => c.Id == Identity(operationId, consumerKey))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReleasedAt, DateTime.UtcNow), ct);

    public async Task<bool> RenewExistingConsumerAsync(string operationId, string consumerKey,
        TimeSpan leaseDuration, CancellationToken ct = default)
    {
        ValidateDuration(leaseDuration);
        if (string.IsNullOrWhiteSpace(consumerKey)) throw new ArgumentException("A consumer key is required.");
        var until = DateTime.UtcNow + leaseDuration;
        // An active consumer already prevents cleanup. Extending it cannot make an eligible
        // artifact become ineligible, so this heartbeat must not wait behind a long hash scan.
        // Evaluate expiry in the SQL statement, not against a captured pre-wait timestamp:
        // an update delayed by a database writer must not resurrect an expired reader.
        return await _db.Set<ArtifactConsumerDbModel>()
            .Where(c => c.Id == Identity(operationId, consumerKey) && c.ReleasedAt == null && c.LeaseUntil > DateTime.UtcNow)
            .Where(c => !_db.Set<ArtifactOperationDbModel>().Any(o => o.Id == c.OperationId &&
                _db.Set<ArtifactDbModel>().Any(a => a.Id == o.OutputArtifactId && a.State == ArtifactLocationState.Retired)))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LeaseUntil, c => c.LeaseUntil > until ? c.LeaseUntil : until), ct) == 1;
    }

    public async Task<bool> PinConsumerAsync(string operationId, string consumerKey, string ownerKind, string ownerId,
        CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        if (ownerKind != "workflow" || !int.TryParse(ownerId, out var runId))
            throw new ArgumentException("A durable consumer owner must identify a workflow run.");
        var previous = await _db.Set<ArtifactConsumerDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == Identity(operationId, consumerKey), ct);
        if (previous?.OwnerKind != null && (previous.OwnerKind != ownerKind || previous.OwnerId != ownerId))
            throw new InvalidOperationException("A consumer key cannot be reassigned to another durable owner.");
        if (!await _db.Set<WorkflowRunDbModel>().AnyAsync(r => r.Id == runId &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting), ct))
            return false;
        if (!await AttachConsumerCoreAsync(operationId, consumerKey, TimeSpan.FromMinutes(5), ct)) return false;
        return await _db.Set<ArtifactConsumerDbModel>().Where(c => c.Id == Identity(operationId, consumerKey) && c.ReleasedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.OwnerKind, ownerKind).SetProperty(c => c.OwnerId, ownerId), ct) == 1;
    }

    public Task<bool> HasActiveConsumersAsync(string operationId, CancellationToken ct = default) =>
        ActiveConsumers().AnyAsync(c => c.OperationId == operationId, ct);

    public async Task<bool> TryQuarantineAsync(string artifactId, CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var artifacts = await FileReferences().ToListAsync(ct);
        var target = artifacts.SingleOrDefault(a => a.Id == artifactId);
        if (target == null || target.State != ArtifactLocationState.Available || !target.Managed) return false;
        var targetPaths = Paths(target).ToHashSet(ArtifactManifestFiles.PathComparer);
        var related = artifacts.Where(a => a.Id == artifactId || Paths(a).Any(targetPaths.Contains)).Select(a => a.Id).ToHashSet();
        var edges = await _db.Set<ArtifactLineageDbModel>().AsNoTracking().ToListAsync(ct);
        // A consumed subset must respect consumers of the full source manifest and its ancestors.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var edge in edges.Where(e => related.Contains(e.ChildArtifactId)))
                changed |= related.Add(edge.ParentArtifactId);
        }
        var operations = artifacts.Where(a => related.Contains(a.Id)).Select(a => a.OperationId).Distinct().ToArray();
        var now = DateTime.UtcNow;
        if (await ActiveConsumers().AnyAsync(c => operations.Contains(c.OperationId), ct)) return false;
        var changedRows = await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifactId && a.State == ArtifactLocationState.Available)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.State, ArtifactLocationState.Quarantined)
                .SetProperty(a => a.QuarantineSourceRootPath, target.RootPath)
                .SetProperty(a => a.QuarantineSourceRootIsAppData, target.RootIsAppData)
                .SetProperty(a => a.UpdatedAt, now), ct);
        await transaction.CommitAsync(ct);
        return changedRows == 1;
    }

    public async Task SetLocationAsync(string artifactId, string rootDirectory, ArtifactLocationState state,
        CancellationToken ct = default)
    {
        await using var mutation = await ArtifactMaintenanceGate.For(_appData()).EnterMutationAsync(ct);
        var row = await _db.Set<ArtifactDbModel>().AsNoTracking().SingleAsync(a => a.Id == artifactId, ct);
        if (row.State != ArtifactLocationState.Quarantined)
            throw new InvalidOperationException("Only a quarantined artifact may change its location.");
        if (state is not (ArtifactLocationState.Available or ArtifactLocationState.Quarantined or ArtifactLocationState.Missing or ArtifactLocationState.Modified or ArtifactLocationState.Retired))
            throw new ArgumentException("Invalid artifact location state.");
        EnsureAllowed(rootDirectory);
        var manifest = ReadManifest(row);
        if (state is ArtifactLocationState.Available or ArtifactLocationState.Quarantined)
        {
            var validation = await ArtifactManifestFiles.ValidateAsync(rootDirectory, manifest, ct);
            if (!validation.Available) throw new InvalidDataException(validation.Message);
        }
        var location = StoreRoot(rootDirectory);
        var changed = await _db.Set<ArtifactDbModel>().Where(a => a.Id == artifactId &&
                a.State == ArtifactLocationState.Quarantined && a.RootPath == row.RootPath && a.UpdatedAt == row.UpdatedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.RootPath, location.Path)
                .SetProperty(a => a.RootIsAppData, location.IsAppData).SetProperty(a => a.State, state)
                .SetProperty(a => a.QuarantineSourceRootPath, state == ArtifactLocationState.Quarantined ? row.QuarantineSourceRootPath : null)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
        if (changed != 1) throw new InvalidOperationException("The artifact location changed concurrently.");
    }

    private async Task<ArtifactRecord> Project(ArtifactDbModel row, CancellationToken ct)
    {
        var manifest = ReadManifest(row);
        var root = ResolveRoot(row.RootPath, row.RootIsAppData);
        var parents = await _db.Set<ArtifactLineageDbModel>().AsNoTracking().Where(e => e.ChildArtifactId == row.Id)
            .Select(e => e.ParentArtifactId).ToListAsync(ct);
        return new(row.Id, row.OperationId, row.Kind, root, ArtifactManifestFiles.ResolveFiles(root, manifest), manifest,
            row.Fingerprint, row.State, row.Managed, parents, row.MetadataJson);
    }

    // Reference guards need path evidence, never processing metadata or timestamps. Query
    // afresh at every safety phase; projecting these columns is not a cross-phase cache.
    private sealed record ArtifactFileReference(string Id, string OperationId, ArtifactLocationState State,
        bool Managed, string RootPath, bool RootIsAppData, string? QuarantineSourceRootPath,
        bool QuarantineSourceRootIsAppData, string ManifestJson, string Fingerprint);

    private IQueryable<ArtifactFileReference> FileReferences(string? excludedArtifactId = null, string[]? excludedViewIds = null,
        IReadOnlyList<string>? candidateFiles = null)
    {
        var rows = _db.Set<ArtifactDbModel>().AsNoTracking();
        if (excludedArtifactId != null) rows = rows.Where(a => a.Id != excludedArtifactId);
        if (excludedViewIds is {Length: > 0}) rows = rows.Where(a => !excludedViewIds.Contains(a.Id));
        if (candidateFiles != null && ArtifactManifestFiles.PathComparer == StringComparer.Ordinal)
        {
            // A manifest can reference a selected file only from one of its ancestor
            // directories. Keep this filter in the SAME query as the manifest projection:
            // a separate root lookup would miss a concurrent registration/relocation.
            var roots = CandidateRoots(candidateFiles);
            var absolute = roots.Where(r => !r.IsAppData).Select(r => r.Path).ToArray();
            var relative = roots.Where(r => r.IsAppData).Select(r => r.Path).ToArray();
            rows = rows.Where(a => (a.RootIsAppData ? relative.Contains(a.RootPath) : absolute.Contains(a.RootPath)) ||
                a.QuarantineSourceRootPath != null && (a.QuarantineSourceRootIsAppData
                    ? relative.Contains(a.QuarantineSourceRootPath) : absolute.Contains(a.QuarantineSourceRootPath)));
        }
        return rows.Select(a => new ArtifactFileReference(a.Id, a.OperationId, a.State, a.Managed, a.RootPath, a.RootIsAppData,
            a.QuarantineSourceRootPath, a.QuarantineSourceRootIsAppData, a.ManifestJson, a.Fingerprint));
    }

    private HashSet<(string Path, bool IsAppData)> CandidateRoots(IEnumerable<string> files)
    {
        var roots = new HashSet<(string, bool)>();
        foreach (var parent in FileAncestorRoots(files))
        {
            // Both representations are valid for historical registrations. AppData
            // relocation changes only relative roots, never explicit absolute roots.
            roots.Add((parent, false));
            roots.Add(StoreRoot(parent));
        }
        return roots;
    }

    private static HashSet<string> FileAncestorRoots(IEnumerable<string> files)
    {
        var roots = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        foreach (var file in files)
            for (var parent = Path.GetDirectoryName(ArtifactManifestFiles.Absolute(file)); parent != null;
                 parent = Path.GetDirectoryName(parent))
                if (!roots.Add(parent)) break;
        return roots;
    }

    private bool MayContain(ArtifactFileReference reference, IReadOnlySet<string> roots)
    {
        // SQLite's NOCASE is ASCII-only. On case-insensitive platforms retain the broad
        // SQL projection and do the root comparison with the established .NET comparer,
        // rather than lose non-ASCII or case-variant references. Unrelated manifests are
        // never parsed, fingerprinted or expanded into thousands of file paths.
        return roots.Contains(ResolveRoot(reference.RootPath, reference.RootIsAppData)) ||
            reference.QuarantineSourceRootPath != null &&
            roots.Contains(ResolveRoot(reference.QuarantineSourceRootPath, reference.QuarantineSourceRootIsAppData));
    }

    private IEnumerable<string> Paths(ArtifactFileReference row)
    {
        var manifest = ReadManifest(row.ManifestJson, row.Fingerprint);
        foreach (var path in ArtifactManifestFiles.ResolveFiles(ResolveRoot(row.RootPath, row.RootIsAppData), manifest)) yield return path;
        if (row.QuarantineSourceRootPath != null)
            foreach (var path in ArtifactManifestFiles.ResolveFiles(ResolveRoot(row.QuarantineSourceRootPath,
                         row.QuarantineSourceRootIsAppData), manifest)) yield return path;
    }

    private static ArtifactManifest ReadManifest(ArtifactDbModel row) => ReadManifest(row.ManifestJson, row.Fingerprint);

    private static ArtifactManifest ReadManifest(string json, string fingerprint)
    {
        var manifest = JsonSerializer.Deserialize<ArtifactManifest>(json, Json)
            ?? throw new InvalidDataException("The artifact has no manifest.");
        if (manifest.Fingerprint != fingerprint) throw new InvalidDataException("The artifact fingerprint is inconsistent.");
        return manifest;
    }

    private IQueryable<ArtifactConsumerDbModel> ActiveConsumers()
    {
        var now = DateTime.UtcNow;
        return _db.Set<ArtifactConsumerDbModel>().Where(c => c.ReleasedAt == null &&
            (c.OwnerKind == "workflow"
                ? _db.Set<WorkflowRunDbModel>().Any(r => r.Id.ToString() == c.OwnerId &&
                    (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting))
                : c.LeaseUntil > now));
    }

    private (string Path, bool IsAppData) StoreRoot(string root)
    {
        root = ArtifactManifestFiles.Absolute(root);
        var appData = ArtifactManifestFiles.Absolute(_appData());
        var relative = Path.GetRelativePath(appData, root);
        var inside = !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return inside ? (relative.Replace('\\', '/'), true) : (root, false);
    }

    private string ResolveRoot(string root, bool isAppData)
    {
        if (!isAppData) return ArtifactManifestFiles.Absolute(root);
        if (Path.IsPathRooted(root) || root.Replace('\\', '/').Split('/').Any(p => p == ".."))
            throw new InvalidDataException("The stored artifact path escapes AppData.");
        return ArtifactManifestFiles.Absolute(Path.Combine(_appData(), root));
    }

    private void EnsureAllowed(string path)
    {
        var stored = StoreRoot(path);
        if (!stored.IsAppData) _storage?.EnsurePathAllowed(path);
    }

    private static string Identity(params string[] parts) => ArtifactManifestFiles.Hash(JsonSerializer.Serialize(parts));
    private static void ValidateRequest(ArtifactOperationRequest request)
    {
        if (new[] {request.OperationKey, request.IntentKey, request.Kind, request.InputFingerprint}
            .Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 2048))
            throw new ArgumentException("Operation identity fields must be nonempty and bounded.");
    }
    private static void ValidateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(duration), "A lease must last between zero and one day.");
    }
}
