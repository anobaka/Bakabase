using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Service.Extensions;
using Bootstrap.Components.Orm;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

/// <summary>
/// Projects only this host's ordinary library metadata and its existing typed index postings.
/// Snapshot/journal coordination belongs to the caller; no media discovery, player settings,
/// connection credentials, remote replicas or file contents are exported here.
/// </summary>
public sealed class MetadataReplicaProjection(
    IResourceService resources,
    FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int> resourceRows,
    IResourceSearchIndexExportService index,
    IPropertyService properties,
    ICustomPropertyService customProperties,
    IMediaLibraryV2Service mediaLibraries,
    ICollectionService collections,
    IExtensionGroupService extensionGroups)
{
    public const int MaximumBatchBytes = 8 * 1024 * 1024;
    private const ResourceAdditionalItem MetadataItems = ResourceAdditionalItem.All & ~ResourceAdditionalItem.PlayableItem;
    private static readonly JsonSerializerSettings Json = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MaxDepth = 64,
        TypeNameHandling = TypeNameHandling.None
    };

    public async Task<IReadOnlyList<ReplicaResource>> ReadResourcesAsync(
        int[] ids, long revision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Length > IResourceSearchIndexExportService.MaximumResourceCount || ids.Any(id => id <= 0))
            throw new ArgumentOutOfRangeException(nameof(ids), "Metadata export accepts at most 100 positive resource IDs.");
        if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ct.ThrowIfCancellationRequested();
        ids = ids.Distinct().ToArray();
        using var persisted = ResourceMetadataReadScope.Enter(ids);
        IReadOnlyDictionary<int, IReadOnlyList<ResourceSearchIndexEntry>> postings;
        try { postings = await index.ReadIndexEntriesAsync(ids, ct); }
        catch (ResourceSearchIndexExportBudgetException e) { throw new ReplicaBudgetExceededException(e.Message); }
        var rows = (await resourceRows.GetByKeys(ids)).ToDictionary(row => row.Id);
        ct.ThrowIfCancellationRequested();
        var metadata = await resources.GetByKeys(ids, MetadataItems);
        ct.ThrowIfCancellationRequested();
        var result = new List<ReplicaResource>(metadata.Count);
        var remainingBytes = MaximumBatchBytes - 2;
        foreach (var resource in metadata.OrderBy(resource => resource.Id))
        {
            ct.ThrowIfCancellationRequested();
            if (!rows.TryGetValue(resource.Id, out var row)) continue;
            if (!postings.TryGetValue(resource.Id, out var entries))
                throw new InvalidOperationException("A resource changed while its metadata index was being exported. Retry the batch.");
            var projected = new ReplicaResource(resource.Id, revision,
                SerializeBounded(CopyMetadata(resource), remainingBytes),
                new ReplicaResourceSort
                {
                    Path = row.Path,
                    // Compute on the source OS: the manager may use different path separators.
                    FileName = Path.GetFileName(row.Path),
                    CreateDt = row.CreateDt,
                    FileCreateDt = row.FileCreateDt,
                    FileModifyDt = row.FileModifyDt,
                    PlayedAt = row.PlayedAt,
                    Tags = row.Tags,
                    HealthScore = resource.HealthScore
                },
                entries.Select(entry => new ReplicaPosting(entry.Pool, entry.PropertyId,
                    entry.RangeValue == null ? entry.ValueKey : null,
                    entry.RangeValue == null ? null : ReplicaRangeValue.From(entry.RangeValue))).ToArray());
            if (Encoding.UTF8.GetByteCount(projected.ResourceJson) + MetadataReplicaStore.PostingBytes(projected) >
                MetadataReplicaStore.MaxResourceBytes)
                throw new ReplicaBudgetExceededException("A resource exceeds the metadata byte budget.");
            // Include escaping of ResourceJson and all postings, not just their raw text.
            remainingBytes -= Encoding.UTF8.GetByteCount(SerializeBounded(projected, remainingBytes)) + 1;
            if (remainingBytes < 0) throw new ReplicaBudgetExceededException("The metadata resource batch exceeds 8 MiB.");
            result.Add(projected);
        }
        return result;
    }

    /// <summary>
    /// Streams one catalog item at a time. Callers must page it with their resource-feed
    /// budget (at most 512 items / 8 MiB), rather than retaining a complete catalog batch.
    /// </summary>
    public async IAsyncEnumerable<ReplicaCatalog> ReadCatalogAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var property in (await ReadPersisted(() => properties.GetProperties(PropertyPool.All, includeDeprecated: true)))
                     .OrderBy(property => property.Pool).ThenBy(property => property.Id))
        {
            ct.ThrowIfCancellationRequested();
            yield return Catalog("properties", $"{(int)property.Pool}:{property.Id}", property.ToViewModel());
        }
        foreach (var property in (await ReadPersisted(() => customProperties.GetAll())).OrderBy(property => property.Id))
        {
            ct.ThrowIfCancellationRequested();
            yield return Catalog("customProperties", Id(property.Id), property.ToViewModel());
        }
        foreach (var library in (await ReadPersisted(() => mediaLibraries.GetAll())).OrderBy(library => library.Id))
        {
            ct.ThrowIfCancellationRequested();
            // Do not carry obsolete Players/Template configuration into another host.
            yield return Catalog("mediaLibraries", Id(library.Id), new
            {
                library.Id, library.Name, library.ResourceCount, library.Color
            });
        }
        foreach (var collection in (await ReadPersisted(() => collections.GetAll(withProgress: false, ct))).OrderBy(collection => collection.Id))
        {
            ct.ThrowIfCancellationRequested();
            yield return Catalog("collections", Id(collection.Id), new
            {
                collection.Id, collection.Name, collection.Description, collection.Color,
                collection.CoverPath, collection.RuleSearchJson, collection.Order, collection.HasRule,
                collection.CreatedAt, collection.UpdatedAt
            });
        }
        foreach (var group in (await ReadPersisted(() => extensionGroups.GetAll())).OrderBy(group => group.Id))
        {
            ct.ThrowIfCancellationRequested();
            yield return Catalog("extensionGroups", Id(group.Id), new { group.Id, group.Name, group.Extensions });
        }
    }

    // Async iterator continuations resume in the enumerator caller's ExecutionContext.
    // Enter for each database read, rather than relying on ambient state across yield returns.
    private static async Task<T> ReadPersisted<T>(Func<Task<T>> read)
    {
        using var persisted = PersistedReadScope.Enter();
        return await read();
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static ReplicaCatalog Catalog(string kind, string key, object value)
    {
        var result = new ReplicaCatalog(kind, key, SerializeBounded(value, MaximumBatchBytes));
        // The string is itself encoded in the feed envelope, so apply the budget twice.
        _ = SerializeBounded(result, MaximumBatchBytes);
        return result;
    }

    private static Resource CopyMetadata(Resource resource, int depth = 0)
    {
        if (depth >= 64) throw new ReplicaBudgetExceededException("The metadata resource hierarchy is too deep.");
        return resource with
        {
            Parent = resource.Parent == null ? null : CopyMetadata(resource.Parent, depth + 1),
            PlayableItems = null,
            Cache = null,
            // Opaque third-party payloads are not required by resource cards or filters.
            // Keep their explicit identifiers/cover metadata, never arbitrary source JSON.
            SourceLinks = resource.SourceLinks?.Select(link => new ResourceSourceLink
            {
                Id = link.Id, ResourceId = link.ResourceId, Source = link.Source,
                SourceKey = link.SourceKey, CreateDt = link.CreateDt,
                CoverUrls = link.CoverUrls, LocalCoverPaths = link.LocalCoverPaths,
                CoverDownloadFailedAt = link.CoverDownloadFailedAt,
                MetadataFetchedAt = link.MetadataFetchedAt
            }).ToList(),
            ExternalIdentities = resource.ExternalIdentities?.Select(identity => new ResourceExternalIdentity
            {
                Id = identity.Id, ResourceId = identity.ResourceId, ThirdPartyId = identity.ThirdPartyId,
                ExternalId = identity.ExternalId, CreateDt = identity.CreateDt,
                CoverUrls = identity.CoverUrls, LocalCoverPaths = identity.LocalCoverPaths,
                CoverDownloadFailedAt = identity.CoverDownloadFailedAt
            }).ToList()
        };
    }

    private static string SerializeBounded(object value, int maximumBytes)
    {
        using var output = new BoundedJsonWriter(maximumBytes);
        JsonSerializer.Create(Json).Serialize(output, value);
        return output.ToString();
    }

    private sealed class BoundedJsonWriter(int maximumBytes) : StringWriter(CultureInfo.InvariantCulture)
    {
        private long _remainingBytes = maximumBytes;
        private void Consume(int bytes)
        {
            _remainingBytes -= bytes;
            if (_remainingBytes < 0) throw new ReplicaBudgetExceededException("Metadata export exceeds its byte budget.");
        }

        public override void Write(char value)
        {
            // A surrogate written separately is counted conservatively as three bytes.
            Consume(value <= 0x7f ? 1 : value <= 0x7ff ? 2 : 3);
            GetStringBuilder().Append(value);
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            Consume(Encoding.UTF8.GetByteCount(value));
            GetStringBuilder().Append(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Consume(Encoding.UTF8.GetByteCount(buffer, index, count));
            GetStringBuilder().Append(buffer, index, count);
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            Consume(Encoding.UTF8.GetByteCount(buffer));
            GetStringBuilder().Append(buffer);
        }
    }
}
