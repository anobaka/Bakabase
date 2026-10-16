using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Federation.Processing;

public sealed record ResourceProcessingOwnerState(ResourceProcessingPrepared? Prepared, string TokenHash,
    IReadOnlyList<ResourceProcessingArtifact> Uploaded, ResourceProcessingReceipt? Receipt = null,
    string? ArtifactKey = null);

/// <summary>The source remains the only metadata writer. Preparing binds one authenticated manager,
/// one source revision and one immutable rule. Applying checks them under the same write transaction
/// as the ordinary property/cache updates and the replay receipt.</summary>
public sealed class ResourceProcessingOwner(BakabaseDbContext db, ResourceProcessingJobStore jobs,
    MetadataSourceJournal journal, INodeIdentityProvider identity, IResourceService resources,
    IPropertyService properties, IEnhancerDescriptors descriptors, IEnhancerService enhancers,
    IEnhancementService enhancements, IEnhancementRecordService records, IFileManager files,
    ResourceProcessingArtifacts artifacts, GlobalCacheVault cacheVault,
    IResourceDataChangeEventPublisher changes, IBOptions<UIOptions> uiOptions)
{
    public async Task<ResourceProcessingPrepared> PrepareAsync(string requesterId, ResourceProcessingInput input,
        CancellationToken ct)
    {
        ResourceProcessingWire.ValidateInput(input);
        await CheckIdentity(input, ct);
        // A reset library cannot accept these old preparations. Release their bounded payloads
        // before admitting new work; authenticated callers can only abandon their own jobs.
        var expired = await db.ResourceProcessingJobs.AsNoTracking().Where(j => j.Role == "Owner" &&
                j.RequesterId == requesterId && (j.OwnerId != input.OwnerId || j.OwnerEpoch != input.OwnerEpoch) &&
                j.State != "Completed" && (j.State != "Cancelled" || j.Phase != "Cancelled"))
            .OrderBy(j => j.CreatedAtUtc).Take(16).Select(j => j.JobId).ToArrayAsync(ct);
        foreach (var expiredJobId in expired) await AbandonAsync(requesterId, expiredJobId, null, ct);
        var old = await jobs.GetAsync(ResourceProcessingRole.Owner, requesterId, input.JobId, ct);
        if (old != null)
        {
            if (old.State == "Cancelled")
                throw new ResourceProcessingException("JobCancelled", "This prepared step was cancelled. Start a new run.");
            var saved = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(old.InputJson);
            if (saved.Prepared == null) throw new ResourceProcessingException(old.State == "Cancelled" ? "JobCancelled" : "AlreadyApplied",
                old.State == "Cancelled" ? "This prepared step was cancelled. Start a new run." : "This processing step has already been applied; read its receipt.");
            if (ResourceProcessingWire.Hash(saved.Prepared.Input) != ResourceProcessingWire.Hash(input))
                throw new ResourceProcessingException("JobConflict", "This job was prepared with different input.");
            return saved.Prepared;
        }
        await journal.EnsureAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // This write acquires the SQLite writer lock before reading a cached domain projection.
        var row = await jobs.CreateAsync(ResourceProcessingRole.Owner, requesterId, input, "{}", ct);
        if (row.InputJson != "{}")
        {
            var saved = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(row.InputJson);
            if (saved.Prepared == null || ResourceProcessingWire.Hash(saved.Prepared.Input) != ResourceProcessingWire.Hash(input))
                throw new ResourceProcessingException("JobConflict", "This step was concurrently prepared with different input.");
            await transaction.CommitAsync(ct);
            return saved.Prepared;
        }
        var head = await journal.HeadAsync(ct);
        var revision = (await journal.RevisionsAsync([input.ResourceId], ct)).GetValueOrDefault(input.ResourceId);
        if (input.ExpectedResourceRevision.HasValue && input.ExpectedResourceRevision != revision)
            throw new ResourceProcessingException("ResourceChanged", "The resource changed after the rule was scheduled. Refresh and retry.");
        using var metadataRead = ResourceMetadataReadScope.Enter([input.ResourceId]);
        var resource = await resources.Get(input.ResourceId, ResourceAdditionalItem.Properties | ResourceAdditionalItem.Alias)
            ?? throw new ResourceProcessingException("ResourceDeleted", "The source resource no longer exists.");
        if (input.Kind == ResourceProcessingKind.PlayableFiles && uiOptions.Value.Resource.DisablePlayableFileCache)
            throw new ResourceProcessingException("CacheDisabled", "Enable the source's playable-file cache before scheduling discovery on another device.");
        if (input.Enhancer != null) await ValidateEnhancer(input.Enhancer);
        var prepared = new ResourceProcessingPrepared(input, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            revision, head.Generation, ResourceProcessingWire.Serialize(resource));
        _ = ResourceProcessingWire.Serialize(prepared);
        row.ResourceRevision = revision;
        row.State = ResourceProcessingState.Running.ToString();
        row.Phase = "Prepared";
        row.InputJson = ResourceProcessingWire.Serialize(new ResourceProcessingOwnerState(prepared, TokenHash(prepared.Token), [], ArtifactKey: ResourceProcessingArtifacts.Key(prepared)));
        await jobs.UpdateAsync(row, ct);
        await transaction.CommitAsync(ct);
        return prepared;
    }

    public async Task<long> PutArtifactChunkAsync(string requesterId, ResourceProcessingArtifactChunk chunk,
        CancellationToken ct)
    {
        var row = await jobs.GetAsync(ResourceProcessingRole.Owner, requesterId, chunk.JobId, ct)
            ?? throw new ResourceProcessingException("JobNotFound", "The prepared job does not exist for this manager.");
        var state = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(row.InputJson);
        CheckToken(state, chunk.Token);
        if (state.Prepared == null || row.State == "Completed")
            throw new ResourceProcessingException("AlreadyApplied", "This job has already completed.");
        await CheckIdentity(state.Prepared.Input, ct);
        ResourceProcessingArtifacts.Validate(chunk.Artifact);
        var old = state.Uploaded.FirstOrDefault(a => a.Hash == chunk.Artifact.Hash);
        if (old != null && old != chunk.Artifact)
            throw new ResourceProcessingException("ArtifactConflict", "The artifact manifest changed.");
        var uploaded = old == null ? state.Uploaded.Append(chunk.Artifact).ToArray() : state.Uploaded;
        if (uploaded.Count > ResourceProcessingWire.MaxArtifacts || uploaded.Sum(a => a.Length) > ResourceProcessingWire.MaxTotalArtifactBytes)
            throw new ResourceProcessingException("BudgetExceeded", "The job attachment budget is exhausted.");
        // Hold the row's writer lock through this bounded chunk: cancellation cannot delete a
        // directory and then have an in-flight writer recreate it after quota was released.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        row.InputJson = ResourceProcessingWire.Serialize(state with {Uploaded = uploaded});
        await jobs.UpdateAsync(row, ct);
        try
        {
            var next = await artifacts.AppendOwnerChunkAsync(state.Prepared, chunk.Artifact, chunk.Offset, chunk.Base64, ct);
            await transaction.CommitAsync(ct);
            return next;
        }
        catch
        {
            // Keep the manifest reservation even for bad/incomplete bytes, bounding abandoned
            // files by the same 128-artifact/64-MiB budget until explicit cancellation cleans them.
            await transaction.CommitAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ResourceProcessingReceipt> ApplyAsync(string requesterId, string jobId, string token,
        ResourceProcessingOutput output, CancellationToken ct)
    {
        var resultHash = ResourceProcessingWire.Hash(output);
        var row = await jobs.GetAsync(ResourceProcessingRole.Owner, requesterId, jobId, ct)
            ?? throw new ResourceProcessingException("JobNotFound", "The prepared job does not exist for this manager.");
        var state = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(row.InputJson);
        CheckToken(state, token);
        if (state.Receipt != null)
        {
            var own = await identity.GetAsync(ct);
            if (own.NodeId != row.OwnerId || own.LibraryEpoch != row.OwnerEpoch)
                throw new ResourceProcessingException("EpochChanged", "The source library identity changed.");
            if (state.Receipt.ResultHash != resultHash) throw new ResourceProcessingException("ResultConflict", "A different result was already applied.");
            return state.Receipt;
        }
        var prepared = state.Prepared ?? throw new ResourceProcessingException("InvalidJob", "The prepared snapshot is missing.");
        await CheckIdentity(prepared.Input, ct);
        ValidateOutput(prepared, output, state.Uploaded);
        await journal.EnsureAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            // CAS claims this exact prepared version and acquires a writer lock; competing edits or
            // duplicate apply requests cannot pass revision checks and commit independently.
            row.Phase = "Applying";
            await jobs.UpdateAsync(row, ct);
            var current = (await journal.RevisionsAsync([row.ResourceId], ct)).GetValueOrDefault(row.ResourceId);
            var head = await journal.HeadAsync(ct);
            if (current != prepared.ResourceRevision || head.Generation != prepared.CatalogGeneration)
                throw new ResourceProcessingException("ResourceChanged", "The source resource or property definitions changed. Recompute this job.");
            Resource resource;
            using (ResourceMetadataReadScope.Enter([row.ResourceId]))
            {
                resource = await resources.Get(row.ResourceId)
                    ?? throw new ResourceProcessingException("ResourceDeleted", "The source resource no longer exists.");
                if (prepared.Input.Enhancer != null) await ValidateEnhancer(prepared.Input.Enhancer);
            }
            if (output.Kind == ResourceProcessingKind.PlayableFiles)
            {
                if (uiOptions.Value.Resource.DisablePlayableFileCache)
                    throw new ResourceProcessingException("CacheDisabled", "The source disabled its playable-file cache.");
                var paths = output.PlayableFiles.Select(p => ResourceProcessingPaths.OwnerFile(resource.Path, resource.IsFile, p)).ToList();
                var cache = await db.ResourceCaches.SingleOrDefaultAsync(c => c.ResourceId == resource.Id, ct);
                if (cache == null) db.ResourceCaches.Add(cache = new ResourceCacheDbModel {ResourceId = resource.Id});
                cache.PlayableFilePaths = paths.Count == 0 ? null : paths.SerializeAsStandardValue(StandardValueType.ListString);
                cache.HasMorePlayableFiles = false;
                cache.CachedTypes |= ResourceCacheType.PlayableFiles;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                var options = prepared.Input.Enhancer!;
                var installed = new Dictionary<string, string>();
                foreach (var artifact in output.Artifacts)
                    installed[artifact.Hash] = await artifacts.InstallOwnerArtifactAsync(prepared, artifact, files, ct);
                var values = new List<Enhancement>();
                foreach (var value in output.Values)
                {
                    var raw = value.SerializedValue?.DeserializeAsStandardValue(value.ValueType, true);
                    if (descriptors[options.EnhancerId][value.Target].PropertyType == PropertyType.Attachment && raw != null)
                    {
                        if (raw is not IEnumerable<string> references)
                            throw new ResourceProcessingException("InvalidAttachment", "The attachment result is malformed.");
                        raw = references.Select(reference => ResolveAttachment(reference, resource, installed)).ToList();
                    }
                    values.Add(new Enhancement {ResourceId = row.ResourceId, EnhancerId = options.EnhancerId,
                        Target = value.Target, DynamicTarget = value.DynamicTarget, ValueType = value.ValueType, Value = raw});
                }
                await enhancements.RemoveAll(e => e.ResourceId == row.ResourceId && e.EnhancerId == options.EnhancerId, true);
                await records.DeleteAll(r => r.ResourceId == row.ResourceId && r.EnhancerId == options.EnhancerId);
                if (values.Count > 0) await enhancements.AddRange(values);
                await records.Add(new EnhancementRecord {ResourceId = row.ResourceId, EnhancerId = options.EnhancerId,
                    Status = EnhancementRecordStatus.ContextCreated, ContextCreatedAt = DateTime.Now, OptionsSnapshot = options});
                await enhancers.ApplyPreparedEnhancementsToResource(row.ResourceId, options, values, ct);
            }
            var revision = (await journal.RevisionsAsync([row.ResourceId], ct)).GetValueOrDefault(row.ResourceId);
            var receipt = new ResourceProcessingReceipt(jobId, revision, resultHash);
            row.State = ResourceProcessingState.Completed.ToString();
            row.Phase = "Applied";
            row.ProcessedCount = output.ProcessedCount;
            row.TotalCount = output.ProcessedCount;
            row.ResourceRevision = revision;
            // Keep only replay identity and receipt. Source snapshots, raw values and artifact manifests
            // are not duplicated indefinitely in completed job history.
            row.InputJson = ResourceProcessingWire.Serialize(new ResourceProcessingOwnerState(null, state.TokenHash, [], receipt, ResourceProcessingArtifacts.Key(prepared)));
            row.ResultJson = null;
            await jobs.UpdateAsync(row, ct);
            await transaction.CommitAsync(ct);
            committed = true;
            ClearResourceCaches();
            changes.PublishResourcesChanged([row.ResourceId]);
            try { artifacts.Cleanup(prepared, "owner"); }
            catch (System.IO.IOException) { /* A stopped-job cleanup can remove leftovers; the receipt is durable. */ }
            return receipt;
        }
        catch when (!committed)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            // Existing property services update their ordinary caches during writes. A rolled-back
            // transaction must not leave those provisional values visible in the current process.
            cacheVault.Clear();
            changes.PublishResourcesChanged([row.ResourceId]);
            throw;
        }
    }

    public async Task<bool> AbandonAsync(string requesterId, string jobId, string? token, CancellationToken ct)
    {
        // Omitting the token only permits abandoning this authenticated requester's own work; it
        // never reveals a preparation or result. This recovers a lost prepare response safely.
        var row = await jobs.GetAsync(ResourceProcessingRole.Owner, requesterId, jobId, ct)
            ?? await jobs.AbandonMissingAsync(ResourceProcessingRole.Owner, requesterId, jobId, ct);
        if (row.State == "Cancelled" && row.InputJson == "{}") return true;
        var state = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(row.InputJson);
        if (token != null) CheckToken(state, token);
        var artifactKey = state.ArtifactKey ?? (state.Prepared == null ? null : ResourceProcessingArtifacts.Key(state.Prepared));
        if (state.Receipt != null)
        {
            artifacts.CleanupOwnerKey(artifactKey);
            return false; // A committed result cannot be cancelled retroactively.
        }
        if (row.State != "Cancelled")
        {
            row.State = "Cancelled";
            row.CancelRequested = true;
            row.Phase = "CleaningUp";
            row.InputJson = ResourceProcessingWire.Serialize(new ResourceProcessingOwnerState(null, state.TokenHash, [], ArtifactKey: artifactKey));
            row.ResultJson = null;
            await jobs.UpdateAsync(row, ct);
        }
        // The compact key supports retry even after a crash between commit and deletion, without
        // preserving a resource snapshot or requiring a prepare response that the manager lost.
        artifacts.CleanupOwnerKey(artifactKey);
        if (row.Phase != "Cancelled")
        {
            row.Phase = "Cancelled";
            await jobs.UpdateAsync(row, ct);
        }
        return true;
    }

    public async Task<ResourceProcessingReceipt?> GetReceiptAsync(string requesterId, string jobId, string token, CancellationToken ct)
    {
        var row = await jobs.GetAsync(ResourceProcessingRole.Owner, requesterId, jobId, ct)
            ?? throw new ResourceProcessingException("JobNotFound", "The prepared job does not exist for this manager.");
        var state = ResourceProcessingWire.Deserialize<ResourceProcessingOwnerState>(row.InputJson);
        CheckToken(state, token);
        var own = await identity.GetAsync(ct);
        if (own.NodeId != row.OwnerId || own.LibraryEpoch != row.OwnerEpoch)
            throw new ResourceProcessingException("EpochChanged", "The source library identity changed.");
        return state.Receipt;
    }
    private async Task CheckIdentity(ResourceProcessingInput input, CancellationToken ct)
    {
        var own = await identity.GetAsync(ct);
        if (own.NodeId != input.OwnerId || own.LibraryEpoch != input.OwnerEpoch)
            throw new ResourceProcessingException("EpochChanged", "The source library identity changed; do not apply this result.");
    }
    private async Task ValidateEnhancer(EnhancerFullOptions options)
    {
        var descriptor = descriptors.TryGet(options.EnhancerId)
            ?? throw new ResourceProcessingException("EnhancerUnavailable", "The source does not recognize this enhancer.");
        foreach (var target in options.TargetOptions ?? [])
        {
            _ = descriptor[target.Target];
            if (target.PropertyId <= 0 || (int) target.PropertyPool <= 0) continue;
            if (target.PropertyPool is not (PropertyPool.Custom or PropertyPool.Reserved))
                throw new ResourceProcessingException("InvalidTarget", "Only source custom and reserved properties can receive enhancement results.");
            _ = await properties.GetProperty(target.PropertyPool, target.PropertyId)
                ?? throw new ResourceProcessingException("PropertyDeleted", "A target property no longer exists on the source.");
        }
    }
    private void ValidateOutput(ResourceProcessingPrepared prepared, ResourceProcessingOutput output,
        IReadOnlyList<ResourceProcessingArtifact> uploaded)
    {
        if (output == null || output.Kind != prepared.Input.Kind || output.PlayableFiles == null ||
            output.Values == null || output.Artifacts == null || output.PlayableFiles.Any(p => p == null) ||
            output.Values.Any(v => v == null) || output.Artifacts.Any(a => a == null) || output.ProcessedCount < 0 ||
            output.PlayableFiles.Count > ResourceProcessingWire.MaxFiles || output.Values.Count > 4096 ||
            output.Artifacts.Count > ResourceProcessingWire.MaxArtifacts || output.Artifacts.Sum(a => a.Length) > ResourceProcessingWire.MaxTotalArtifactBytes)
            throw new ResourceProcessingException("InvalidResult", "The processing result is invalid or exceeds its budget.");
        if (output.Kind == ResourceProcessingKind.PlayableFiles)
        {
            if (output.Values.Count != 0 || output.Artifacts.Count != 0) throw new ResourceProcessingException("InvalidResult", "Discovery cannot return enhancement data.");
            foreach (var path in output.PlayableFiles) ResourceProcessingPaths.ValidateRelative(path);
        }
        else
        {
            if (output.PlayableFiles.Count != 0) throw new ResourceProcessingException("InvalidResult", "An enhancement cannot replace the playable-file cache.");
            foreach (var value in output.Values)
            {
                var target = descriptors[prepared.Input.Enhancer!.EnhancerId][value.Target];
                if (target.ValueType != value.ValueType || !target.IsDynamic && value.DynamicTarget != null)
                    throw new ResourceProcessingException("InvalidTarget", "An enhancement result differs from its source descriptor.");
            }
        }
        foreach (var artifact in output.Artifacts)
        {
            ResourceProcessingArtifacts.Validate(artifact);
            if (!uploaded.Contains(artifact)) throw new ResourceProcessingException("ArtifactMissing", "An enhancement artifact has not been uploaded.");
        }
    }
    private static string ResolveAttachment(string reference, Resource resource, Dictionary<string, string> installed)
    {
        if (reference.StartsWith(ResourceProcessingWire.ArtifactPrefix, StringComparison.Ordinal) &&
            installed.TryGetValue(reference[ResourceProcessingWire.ArtifactPrefix.Length..], out var path)) return path;
        if (reference.StartsWith(ResourceProcessingWire.ResourceFilePrefix, StringComparison.Ordinal))
            return ResourceProcessingPaths.OwnerFile(resource.Path, resource.IsFile, reference[ResourceProcessingWire.ResourceFilePrefix.Length..]);
        throw new ResourceProcessingException("UnsafePath", "Worker paths cannot be stored as source attachment paths.");
    }
    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static void CheckToken(ResourceProcessingOwnerState state, string token)
    {
        if (string.IsNullOrEmpty(token) || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(state.TokenHash), Encoding.ASCII.GetBytes(TokenHash(token))))
            throw new ResourceProcessingException("InvalidToken", "The prepared job token is invalid.");
    }
    private void ClearResourceCaches() => cacheVault.TryRemove(typeof(ResourceCacheDbModel).FullName!, out _);
}
