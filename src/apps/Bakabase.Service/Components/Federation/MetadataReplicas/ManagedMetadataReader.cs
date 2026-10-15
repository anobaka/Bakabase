using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Models.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Service.Components.Federation.MetadataReplicas;

/// <summary>Owner-bound metadata operations use the local replica; files and mutations stay live.</summary>
public sealed class ManagedMetadataReader(MetadataReplicaCoordinator coordinator, MetadataReplicaStore store,
    MetadataReplicaQuery query, IBOptionsManager<UIOptions> uiOptions)
{
    public static readonly HashSet<string> Operations = ["resourceSearch", "resourceGet", "properties",
        "customProperties", "getCustomPropertyByKeys", "libraries", "getAllCollections", "resourceMappings",
        "getCollectionIdsByResourceIds", "getResourceSourceLinks", "resourceScopePreferencesGet",
        "getSearchOperationsForProperty", "getSearchOperationsByPropertyType", "getFilterValueProperty", "resourceDisplayOptions",
        "propertyValueResourceCounts", "getResourceSearchKeywordRecommendation"];

    public async Task<ManagedServerResponse?> TryReadAsync(string serverId, ManagedLibraryRequest request, CancellationToken ct)
    {
        if (!Operations.Contains(request.Operation)) return null;
        await coordinator.EnsureFreshAsync(serverId, request.LibraryEpoch, ct);
        var owner = await coordinator.RequireAsync(serverId, request.LibraryEpoch, ct);
        try
        {
            if (request.Operation == "propertyValueResourceCounts")
                return Json(new {code = 0, data = new {isReady = true, counts = await query.PropertyValueResourceCountsAsync(owner,
                    (PropertyPool)int.Parse(Value(request, "pool")), request.Id!.Value,
                    request.Body!.ToObject<ResourceSearchInputModel>()!, ct)}});
            if (request.Operation == "getResourceSearchKeywordRecommendation")
                return Json(new {code = 0, data = await query.KeywordRecommendationsAsync(owner,
                    Value(request, "keyword", ""), int.Parse(Value(request, "maxCount", "10")), ct)});
            if (request.Operation == "resourceDisplayOptions")
            {
                var options = (await CatalogAsync(owner, "resourceOptions", ct)).Single();
                return Json(new {code = 0, data = new {uiResource = uiOptions.Value.Resource,
                    propertyValueScopePriority = options["propertyValueScopePriority"]}});
            }
            if (request.Operation is "resourceSearch" or "resourceGet")
                return await ReadResourcesAsync(owner, request, ct);
            var kind = request.Operation switch
            {
                "properties" => "properties", "customProperties" or "getCustomPropertyByKeys" => "customProperties",
                "libraries" => "mediaLibraries", "getAllCollections" => "collections", _ => null
            };
            if (kind != null)
            {
                var values = await CatalogAsync(owner, kind, ct);
                if (request.Operation == "properties")
                {
                    var pool = int.Parse(Value(request, "pool", ((int)PropertyPool.All).ToString()));
                    values = values.Where(p => (pool & ((int?)p["pool"] ?? 0)) != 0).ToArray();
                }
                if (request.Operation == "getCustomPropertyByKeys")
                    values = values.Where(p => request.Ids!.Contains((int)p["id"]!)).ToArray();
                return Json(new {code = 0, data = values});
            }
            if (request.Operation is "getSearchOperationsForProperty" or "getSearchOperationsByPropertyType" or "getFilterValueProperty")
            {
                var property = request.Operation == "getSearchOperationsByPropertyType" ? null :
                    await PropertyAsync(owner, (PropertyPool)int.Parse(Value(request, "propertyPool")),
                        int.Parse(Value(request, "propertyId")), ct);
                var type = property?.Type ?? (PropertyType)int.Parse(Value(request, "propertyType"));
                var handler = PropertySystem.Property.TryGetSearchHandler(type);
                if (request.Operation != "getFilterValueProperty") return Json(new {code = 0, data = handler?.SearchOperations.Keys});
                var operation = (SearchOperation)int.Parse(Value(request, "operation"));
                if (handler?.SearchOperations.TryGetValue(operation, out var options) != true)
                    throw new FederationAccessException("InvalidSearchOperation", 400, "This property does not support the requested operation.");
                return Json(new {code = 0, data = (options.ConvertProperty?.Invoke(property!) ?? property!).ToViewModel()});
            }
            var ids = request.Operation == "resourceMappings" ? request.Body!.ToObject<int[]>()! :
                request.Operation == "getCollectionIdsByResourceIds" ? request.Ids! : [request.Id!.Value];
            var resources = (await store.GetByIdsAsync(owner, ids, ct)).Select(JObject.Parse).ToArray();
            if (request.Operation is "resourceMappings" or "getCollectionIdsByResourceIds")
            {
                var key = request.Operation == "resourceMappings" ? "mediaLibraries" : "collections";
                return Json(new {code = 0, data = resources.ToDictionary(r => (int)r["id"]!,
                    r => (r[key] as JArray ?? []).Select(v => (int)v["id"]!).ToArray())});
            }
            var resource = resources.FirstOrDefault();
            return Json(new {code = 0, data = resource?[request.Operation == "getResourceSourceLinks" ? "sourceLinks" : "scopePreferences"]});
        }
        catch (ReplicaNotReadyException)
        { throw new FederationAccessException("MetadataSyncPending", 503, "The library metadata is being prepared. Search will be available when the first sync finishes."); }
        catch (ReplicaBudgetExceededException error)
        { throw new FederationAccessException("MetadataQueryTooLarge", 413, error.Message); }
        catch (NotSupportedException error)
        { throw new FederationAccessException("MetadataQueryUnsupported", 422, error.Message); }
    }

    private async Task<ManagedServerResponse> ReadResourcesAsync(ReplicaIdentity owner,
        ManagedLibraryRequest request, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var generation = await store.ReadActiveGenerationAsync(owner, ct);
            ReplicaSearchResult? result = null;
            JObject[] resources;
            if (request.Operation == "resourceSearch")
            {
                var input = request.Body?.ToObject<ResourceSearchInputModel>()
                    ?? throw new ArgumentException("A search is required.");
                input.StandardPageable();
                result = await query.SearchAsync(owner, input, ct);
                resources = result.ResourceJson.Select(JObject.Parse).ToArray();
            }
            else
            {
                var rows = await store.GetRowsByIdsAsync(owner, request.Ids ?? [], ct);
                resources = rows.Select(row =>
                {
                    var value = JObject.Parse(row.ResourceJson);
                    value["metadataRevision"] = row.Revision;
                    return value;
                }).ToArray();
            }
            try
            {
                // Structural generations are unique. A catalog transaction must still see
                // the generation observed before reading the resource page, including when
                // the page has no properties. Retry the whole read if a snapshot was swapped.
                await OverlayPropertyDisplayAsync(owner, generation, resources, ct);
                return result == null ? Json(new {code = 0, data = resources}) :
                    Json(new {code = 0, data = resources, totalCount = result.TotalCount,
                        pageIndex = result.Page, pageSize = result.PageSize, metadataSequence = result.Sequence});
            }
            catch (ReplicaCheckpointConflictException) { }
        }
        throw new FederationAccessException("MetadataSyncPending", 503,
            "The library metadata changed while reading. Retry after the current sync finishes.");
    }

    private async Task OverlayPropertyDisplayAsync(ReplicaIdentity owner, string generation,
        JObject[] resources, CancellationToken ct)
    {
        var fields = new List<(string Key, JObject Value)>();
        var pending = new Stack<JObject>(resources);
        while (pending.TryPop(out var resource))
        {
            if (resource["parent"] is JObject parent) pending.Push(parent);
            if (resource["properties"] is not JObject pools) continue;
            foreach (var pool in pools.Properties())
            {
                if (pool.Value is not JObject properties) continue;
                foreach (var property in properties.Properties())
                {
                    if (property.Value is not JObject value) continue;
                    if (fields.Count >= 131072)
                        throw new ReplicaBudgetExceededException("Resource property definitions exceed the query budget.");
                    fields.Add(($"{pool.Name}:{property.Name}", value));
                }
            }
        }
        var definitions = await store.ReadPropertyDisplayAsync(owner, generation,
            fields.Select(f => f.Key).Distinct().ToArray(), ct);
        foreach (var (key, value) in fields)
        {
            if (!definitions.TryGetValue(key, out var definition) || (int?)value["type"] != definition.Type) continue;
            value["name"] = definition.Name;
            value["order"] = definition.Order;
            // Values, aliases, visibility and profile scope priorities remain owner metadata.
            // Current options are already served by the ordinary properties catalog endpoint.
        }
    }

    private async Task<Property> PropertyAsync(ReplicaIdentity owner, PropertyPool pool, int id, CancellationToken ct)
    {
        var item = (await CatalogAsync(owner, "properties", ct)).SingleOrDefault(p => (int?)p["pool"] == (int)pool && (int?)p["id"] == id)
            ?? throw new FederationAccessException("MetadataPropertyMissing", 409, "Refresh the property definition before using this filter.");
        var type = item["type"]!.ToObject<PropertyType>();
        var descriptor = PropertySystem.Property.TryGetDescriptor(type) ?? throw new NotSupportedException("Unknown property type.");
        return new Property(pool, id, type, (string?)item["name"], descriptor.OptionsType == null || item["options"]?.Type is null or JTokenType.Null
            ? null : item["options"]!.ToObject(descriptor.OptionsType));
    }

    private async Task<JObject[]> CatalogAsync(ReplicaIdentity owner, string kind, CancellationToken ct)
        => (await store.ReadCatalogAllAsync(owner, kind, ct)).Select(row => JObject.Parse(row.Json)).ToArray();

    private static string Value(ManagedLibraryRequest request, string key, string? fallback = null) =>
        request.Query?.GetValueOrDefault(key)?.SingleOrDefault() ?? fallback ?? throw new ArgumentException("Missing " + key);
    private static ManagedServerResponse Json(object value) => new(200, "application/json", JsonConvert.SerializeObject(value));
}
