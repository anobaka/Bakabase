using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

public sealed record MetadataSourceManifest(ReplicaIdentity Identity, ReplicaCheckpoint Start, string? Name = null, int ContractVersion = 1);
public sealed record MetadataPageRequest(ReplicaCheckpoint Checkpoint, int PageSize = 50);

/// <summary>Stateless keyset feed. The consumer persists the cursor with each page's data.</summary>
public sealed class MetadataSourceFeed(MetadataSourceJournal journal, MetadataReplicaProjection projection,
    INodeIdentityProvider identity, IBOptionsManager<ResourceOptions> resourceOptions)
{
    private sealed record Position(string Phase, int Offset = 0, long Through = 0,
        long CatalogRevision = 0, string? AfterCatalog = null);
    public const int MaximumResponseBytes = 8 * 1024 * 1024;

    public async Task<MetadataSourceManifest> ManifestAsync(CancellationToken ct)
    {
        var self = await identity.GetAsync(ct);
        var head = await HeadAsync(ct);
        return new(new(self.NodeId, self.LibraryEpoch),
            new(head.Generation, head.Revision, Encode(new("catalog", CatalogRevision: head.CatalogRevision)), false, head.CatalogRevision), self.Name);
    }

    public async Task<ReplicaBatch> PageAsync(MetadataPageRequest request, CancellationToken ct)
    {
        if (request.Checkpoint == null || request.PageSize is < 1 or > 100 || request.Checkpoint.Sequence < 0 || request.Checkpoint.CatalogRevision < 0)
            throw new FederationAccessException("InvalidMetadataCursor", 400, "Invalid metadata page size or revision.");
        var expected = request.Checkpoint;
        var head = await HeadAsync(ct);
        RequireGeneration(expected, head);
        var position = Decode(expected);
        IReadOnlyList<ReplicaCatalog> catalog = [];
        IReadOnlyList<ReplicaResource> resources = [];
        IReadOnlyList<int> deleted = [];
        ReplicaCheckpoint next;
        if (position.Phase == "catalog")
        {
            // Catalog edits only rewind this small catalog pass, never the resource scan.
            // Deletions and semantic changes have already failed RequireGeneration above.
            if (position.CatalogRevision != head.CatalogRevision)
                position = position with {Offset = 0, CatalogRevision = head.CatalogRevision};
            var items = new List<ReplicaCatalog>();
            var skipped = 0;
            var exhausted = true;
            var bytes = 0;
            await foreach (var item in ReadCatalogAsync(ct))
            {
                if (skipped++ < position.Offset) continue;
                var size = Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(item));
                if (items.Count >= request.PageSize || bytes + size > MaximumResponseBytes / 2)
                {
                    if (items.Count == 0) throw new ReplicaBudgetExceededException("A catalog entry exceeds the metadata page budget.");
                    exhausted = false;
                    break;
                }
                items.Add(item);
                bytes += size;
            }
            catalog = items;
            next = expected with
            {
                CatalogRevision = exhausted ? position.CatalogRevision : expected.CatalogRevision,
                Complete = expected.Complete || (exhausted && position.AfterCatalog == "complete"),
                Cursor = !exhausted ? Encode(position with {Offset = position.Offset + items.Count})
                    : position.AfterCatalog == "complete" ? null : Encode(new("resources"))
            };
        }
        else if (position.Phase == "resources")
        {
            var ids = await journal.ResourceIdsAsync(position.Offset, request.PageSize, ct);
            resources = await ProjectAsync(ids, ct);
            next = expected with {Cursor = Encode(ids.Length < request.PageSize
                ? new("catchup", Through: head.Revision) : new("resources", ids[^1]))};
        }
        else
        {
            var through = expected.Complete ? head.Revision : position.Through;
            if (through < expected.Sequence || through > head.Revision)
                throw new FederationAccessException("InvalidMetadataCursor", 409, "The metadata cursor no longer belongs to this library.");
            var changes = await journal.ChangesAsync(expected.Sequence, through, request.PageSize, ct);
            var ids = changes.Select(c => c.ResourceId).ToArray();
            resources = await ProjectAsync(ids, ct);
            deleted = ids.Except(resources.Select(r => r.ResourceId)).ToArray();
            var exhausted = changes.Count < request.PageSize;
            var sequence = exhausted ? through : changes[^1].Revision;
            var refreshCatalog = exhausted && head.CatalogRevision > expected.CatalogRevision;
            next = expected with {Sequence = sequence,
                Complete = expected.Complete || (exhausted && !refreshCatalog),
                Cursor = refreshCatalog
                    ? Encode(new("catalog", CatalogRevision: head.CatalogRevision, AfterCatalog: "complete"))
                    : exhausted ? null : Encode(position)};
        }
        // Semantic changes invalidate a page. Cosmetic/additive catalog edits only restart
        // catalog pagination; resource edits replay after the initial scan watermark.
        var finalHead = await HeadAsync(ct);
        RequireGeneration(expected, finalHead);
        if (position.Phase == "catalog" && position.CatalogRevision != finalHead.CatalogRevision)
        {
            catalog = [];
            next = expected with {Cursor = Encode(position with {Offset = 0, CatalogRevision = finalHead.CatalogRevision})};
        }
        var result = new ReplicaBatch(expected, next, resources, deleted, catalog, []);
        if (Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(result)) > MaximumResponseBytes)
            throw new ReplicaBudgetExceededException("Metadata page exceeds its byte budget; request fewer resources.");
        return result;
    }

    private async Task<IReadOnlyList<ReplicaResource>> ProjectAsync(int[] ids, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        // Persisted projection bypasses all asynchronous caches. Pair it with a stable
        // per-resource revision: a write during any of its bounded reads retries the page.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = await journal.RevisionsAsync(ids, ct);
            var projected = await projection.ReadResourcesAsync(ids, 0, ct);
            var after = await journal.RevisionsAsync(ids, ct);
            if (ids.All(id => before.GetValueOrDefault(id) == after.GetValueOrDefault(id)))
                return projected.Select(r => r with {Revision = before.GetValueOrDefault(r.ResourceId)}).ToArray();
        }
        throw new FederationAccessException("MetadataSourceBusy", 409,
            "These resources changed during metadata export. Retry the same page.");
    }

    private string OptionsJson() => JsonConvert.SerializeObject(new
    {
        propertyValueScopePriority = resourceOptions.Value.PropertyValueScopePriority
    });

    private async Task<MetadataSourceHead> HeadAsync(CancellationToken ct)
    {
        var head = await journal.HeadAsync(ct);
        return head with {Generation = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(head.Generation + OptionsJson()))).ToLowerInvariant()};
    }

    private async IAsyncEnumerable<ReplicaCatalog> ReadCatalogAsync([EnumeratorCancellation] CancellationToken ct)
    {
        yield return new("resourceOptions", "default", OptionsJson());
        await foreach (var item in projection.ReadCatalogAsync(ct)) yield return item;
    }

    private static void RequireGeneration(ReplicaCheckpoint checkpoint, MetadataSourceHead head)
    {
        if (checkpoint.Generation != head.Generation || checkpoint.Sequence > head.Revision || checkpoint.CatalogRevision > head.CatalogRevision)
            throw new FederationAccessException("MetadataGenerationChanged", 409,
                "This library's metadata definitions changed. Resume with a new snapshot.");
    }

    private static string Encode(Position position) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(position)));
    private static Position Decode(ReplicaCheckpoint checkpoint)
    {
        if (checkpoint.Complete && checkpoint.Cursor == null) return new("delta");
        try
        {
            if (checkpoint.Cursor is not {Length: > 0 and <= 1024}) throw new FormatException();
            var p = JsonConvert.DeserializeObject<Position>(Encoding.UTF8.GetString(Convert.FromBase64String(checkpoint.Cursor)));
            if (p == null || p.Offset < 0 || p.Through < 0 || p.CatalogRevision < 0 ||
                p.AfterCatalog is not (null or "complete") || p.Phase is not ("catalog" or "resources" or "catchup" or "delta"))
                throw new FormatException();
            return p;
        }
        catch (Exception e) when (e is JsonException or FormatException)
        { throw new FederationAccessException("InvalidMetadataCursor", 400, "Invalid metadata continuation cursor."); }
    }
}
