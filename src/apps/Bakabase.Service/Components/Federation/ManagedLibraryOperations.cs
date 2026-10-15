using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Federation.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Service.Components.Federation;

public sealed record ManagedLibraryRequest(string LibraryEpoch, string Operation, int? Id = null,
    int[]? Ids = null, JToken? Body = null, Dictionary<string, string[]>? Query = null);

public sealed record ManagedLibraryOperation(HttpMethod Method, string Path, string? Json);

/// <summary>Named library operations only. Never a general HTTP, file or program-launch proxy.</summary>
public static class ManagedLibraryOperations
{
    public const int MaximumRequestBytes = 1024 * 1024;
    public const int MaximumUploadFileBytes = Bakabase.Modules.RemoteAccess.Abstractions.Models.ManagedResourceUploadLimits.MaximumFileBytes;
    public const int MaximumUploadEnvelopeBytes = Bakabase.Modules.RemoteAccess.Abstractions.Models.ManagedResourceUploadLimits.MaximumEnvelopeBytes;
    public const int MaximumPageSize = 100;
    // A library page must not serialize every file in every displayed album.
    // Playable candidates are discovered only when a user opens playback.
    public const ResourceAdditionalItem MetadataItems = ResourceAdditionalItem.Properties |
        ResourceAdditionalItem.Alias | ResourceAdditionalItem.DisplayName | ResourceAdditionalItem.MediaLibraryName |
        ResourceAdditionalItem.Cover | ResourceAdditionalItem.HasChildren | ResourceAdditionalItem.CollectionName;

    public static ManagedLibraryOperation Build(ManagedLibraryRequest request)
    {
        if (!NodeRequestSignature.IsIdentifier(request.LibraryEpoch)) throw Invalid("A library identity is required.");
        var query = request.Query ?? new Dictionary<string, string[]>();
        var allowedQuery = request.Operation switch
        {
            "resourceSearch" => new[] {"additionalItems", "saveSearch", "searchId"},
            "resourceGet" or "getResourceConflicts" or "getCustomPropertyByKeys" => new[] {"additionalItems"},
            "properties" => new[] {"pool", "includeDeprecated"},
            "propertyValueResourceCounts" => new[] {"pool"},
            "resourceScopePreferencesDelete" => new[] {"propertyPool", "propertyId"},
            "resourcePin" => new[] {"pin"},
            "resourceMarkPlayed" => new[] {"item"},
            "resourceBrowserFiles" => new[] {"offset", "pageSize", "scope", "initialPath"},
            "resourceNativeFallback" => new[] {"key", "origin"},
            "getSearchOperationsForProperty" or "getFilterValueProperty" => new[] {"propertyPool", "propertyId", "operation"},
            "getSearchOperationsByPropertyType" => new[] {"propertyType"},
            "getResourceSearchKeywordRecommendation" => new[] {"keyword", "maxCount"},
            "getSavedSearch" or "putSavedSearchName" or "putSavedSearchDisplayMode" or "deleteSavedSearch" => new[] {"id"},
            "getAllCollections" => new[] {"withProgress"},
            "getResourceEnhancements" => new[] {"additionalItem"},
            "enhanceResourceByEnhancer" or "applyEnhancementContextDataForResourceByEnhancer" => new[] {"enhancerId"},
            "deleteResourceAcquisitionLead" => new[] {"leadId"},
            "getPropertyBizValue" => new[] {"pool", "dbValue"},
            "getPropertyDbValue" => new[] {"pool", "bizValue"},
            "getChildrenIwFsInfo" => new[] {"root", "showHiddenFiles", "cursor", "pageSize"},
            "searchResourcePaths" => new[] {"keyword"},
            "resourcePathMappingTree" => new[] {"parent", "cursor", "limit"},
            "previewCustomPropertyTypeConversion" => new[] {"targetType"},
            "changeCustomPropertyType" => new[] {"type"},
            _ => Array.Empty<string>()
        };
        if (query.Keys.Any(k => !allowedQuery.Contains(k))) throw Invalid("Unsupported operation parameters.");
        string Value(string key, string? fallback = null)
        {
            if (!query.TryGetValue(key, out var values)) return fallback ?? throw Invalid($"Missing {key}.");
            if (values is not {Length: 1} || values[0] == null || values[0].Length > (request.Operation == "resourcePathMappingTree" && key == "cursor" ? ManagedLibraryMappingTreeTokens.MaximumTokenChars : key is "item" or "dbValue" or "bizValue" ? 8192 : key is "key" or "root" or "parent" or "keyword" or "cursor" or "initialPath" ? 4096 : key == "id" ? 128 : 32))
                throw Invalid($"Invalid {key}.");
            return values[0];
        }
        int Id() => request.Id is > 0 ? request.Id.Value : throw Invalid("A positive owner-local ID is required.");
        string Number(string key) => int.TryParse(Value(key), out var n) && n > 0
            ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : throw Invalid($"Invalid {key}.");
        string Pool(string key, string? fallback = null) => int.TryParse(Value(key, fallback), out var n) &&
            n > 0 && (n & ~(int) PropertyPool.All) == 0 ? n.ToString() : throw Invalid($"Invalid {key}.");
        string Boolean(string key, string? fallback = null) => bool.TryParse(Value(key, fallback), out var b)
            ? b.ToString().ToLowerInvariant() : throw Invalid($"Invalid {key}.");
        string BoundedNumber(string key, int fallback, int min, int max) => int.TryParse(Value(key, fallback.ToString()), out var n) &&
            n >= min && n <= max ? n.ToString() : throw Invalid($"Invalid {key}.");
        var requestedMetadata = (int) MetadataItems;
        if (query.ContainsKey("additionalItems") && request.Operation != "getCustomPropertyByKeys")
        {
            if (!int.TryParse(Value("additionalItems"), out var requested) || requested < 0)
                throw Invalid("Invalid additionalItems.");
            requestedMetadata &= requested;
        }
        var body = request.Body?.DeepClone();
        RejectAmbiguousProperties(body);
        var (method, path) = request.Operation switch
        {
            "deviceNameGet" => (HttpMethod.Get, "/management/device-name"),
            "deviceNameSet" => (HttpMethod.Put, "/management/device-name"),
            "resourceDisplayOptions" => (HttpMethod.Get, "/federation/local/management/resource-display-options"),
            "resourceDisplayOptionsPatch" => (HttpMethod.Patch, "/federation/local/management/resource-display-options"),
            "getAllEnhancerDescriptors" => (HttpMethod.Get, "/enhancer/descriptor"),
            "getAllPlaylists" => (HttpMethod.Get, "/playlist"),
            "getPlaylist" => (HttpMethod.Get, $"/playlist/{Id()}"),
            "addPlaylist" => (HttpMethod.Post, "/playlist"),
            "putPlaylist" => (HttpMethod.Put, $"/playlist/{Id()}"),
            "patchPlaylist" => (HttpMethod.Patch, $"/playlist/{Id()}"),
            "deletePlaylist" => (HttpMethod.Delete, $"/playlist/{Id()}"),
            "resourceSearch" => (HttpMethod.Post, $"/resource/search?saveSearch=false&additionalItems={requestedMetadata}"),
            "resourceGet" => (HttpMethod.Get, $"/resource/keys?additionalItems={requestedMetadata}&" + string.Join("&",
                ValidIds(request.Ids).Select(i => $"ids={i}"))),
            "uploadFile" => (HttpMethod.Post, $"/management/library/resources/{Id()}/attachment"),
            "addResourceAcquisitionTorrent" => (HttpMethod.Post, $"/management/library/resources/{Id()}/torrent"),
            "resourceCovers" => (HttpMethod.Get, $"/management/library/resources/{Id()}/covers"),
            "getResourceHierarchyContext" => (HttpMethod.Get, $"/resource/{Id()}/hierarchy-context"),
            "getResourceDataForPreviewer" => (HttpMethod.Get, $"/management/library/resources/{Id()}/previewer"),
            "getResourceSourceLinks" => (HttpMethod.Get, $"/resource/{Id()}/source-links"),
            "getResourceConflicts" => (HttpMethod.Get, $"/resource/{Id()}/conflicts?additionalItems={requestedMetadata}"),
            "getResourceEffectivePlayerOptions" => (HttpMethod.Get, $"/resource/{Id()}/effective-player-options"),
            "getSearchOperationsForProperty" => (HttpMethod.Get, $"/resource/search-operation?propertyPool={Pool("propertyPool")}&propertyId={Number("propertyId")}"),
            "getSearchOperationsByPropertyType" => (HttpMethod.Get, $"/resource/search-operation/by-type?propertyType={BoundedNumber("propertyType", 0, 0, 1000)}"),
            "getFilterValueProperty" => (HttpMethod.Get, $"/resource/filter-value-property?propertyPool={Pool("propertyPool")}&propertyId={Number("propertyId")}&operation={BoundedNumber("operation", 0, 0, 1000)}"),
            "getResourceSearchKeywordRecommendation" => (HttpMethod.Get, $"/resource/search/keyword-recommendation?keyword={Uri.EscapeDataString(Value("keyword", ""))}&maxCount={BoundedNumber("maxCount", 10, 1, MaximumPageSize)}"),
            "addLatestUsedProperty" => (HttpMethod.Post, "/options/ui/latest-used-property"),
            "transferResourceData" => (HttpMethod.Put, "/resource/transfer"),
            "resourcePathMappingTree" => (HttpMethod.Get, $"/management/library/path-mapping-tree?parent={Uri.EscapeDataString(Value("parent", ""))}&cursor={Uri.EscapeDataString(Value("cursor", ""))}&limit={BoundedNumber("limit", 200, 1, ManagedLibraryMappingTreeService.MaximumPageSize)}"),
            "searchResourcePaths" => (HttpMethod.Get, $"/management/library/resource-paths?keyword={Uri.EscapeDataString(Value("keyword", ""))}"),
            "getAssociatedDataCards" => (HttpMethod.Get, $"/data-card/resource/{Id()}/associated"),
            "findDataCardByIdentity" => (HttpMethod.Post, "/data-card/find-by-identity"),
            "updateDataCard" => (HttpMethod.Put, $"/data-card/{Id()}"),
            "addDataCard" => (HttpMethod.Post, "/data-card"),
            "getAllDataCardTypes" => (HttpMethod.Get, "/data-card-type"),
            "updateDataCardTypeDisplayTemplate" => (HttpMethod.Put, $"/data-card-type/{Id()}/display-template"),
            "updateDataCardType" => (HttpMethod.Put, $"/data-card-type/{Id()}"),
            "addDataCardType" => (HttpMethod.Post, "/data-card-type"),
            "getCustomPropertyConversionRules" => (HttpMethod.Get, "/custom-property/conversion-rule"),
            "previewCustomPropertyTypeConversion" => (HttpMethod.Post, $"/management/library/custom-properties/{Id()}/conversion-preview?targetType={BoundedNumber("targetType", 0, 1, 1000)}"),
            "changeCustomPropertyType" => (HttpMethod.Put, $"/custom-property/{Id()}/{BoundedNumber("type", 0, 1, 1000)}"),
            "getResourceAcquisitionLeads" => (HttpMethod.Get, $"/resource/{Id()}/acquisition-leads"),
            "getRecentResourceFilters" => (HttpMethod.Get, "/options/resource/recent-filters"),
            "addRecentResourceFilter" => (HttpMethod.Post, "/options/resource/recent-filters"),
            "getSavedSearch" => (HttpMethod.Get, $"/resource/saved-search?id={Uri.EscapeDataString(Value("id"))}"),
            "saveNewResourceSearch" => (HttpMethod.Post, "/resource/saved-search"),
            "putSavedSearchName" => (HttpMethod.Put, $"/resource/saved-search?id={Uri.EscapeDataString(Value("id"))}"),
            "putSavedSearchDisplayMode" => (HttpMethod.Put, $"/resource/saved-search/display-mode?id={Uri.EscapeDataString(Value("id"))}"),
            "deleteSavedSearch" => (HttpMethod.Delete, $"/resource/saved-search?id={Uri.EscapeDataString(Value("id"))}"),
            "bulkPutResourcePropertyValue" => (HttpMethod.Put, "/resource/bulk/property-value"),
            "bulkDeleteResources" => (HttpMethod.Post, "/resource/bulk-delete"),
            "markResourceAsNotPlayed" => (HttpMethod.Delete, $"/resource/{Id()}/played-at"),
            "mergeResources" => (HttpMethod.Post, "/resource/merge"),
            "saveCover" => (HttpMethod.Put, $"/resource/{Id()}/cover"),
            "createPlaceholderResources" => (HttpMethod.Post, "/resource/placeholder"),
            "materializeResource" => (HttpMethod.Post, $"/resource/{Id()}/materialize"),
            "refreshResourceCache" => (HttpMethod.Post, $"/cache/resource/{Id()}/refresh"),
            "refreshResourcesCache" => (HttpMethod.Post, "/cache/resources/refresh"),
            "getAllCollections" => (HttpMethod.Get, $"/collection?withProgress={Boolean("withProgress", "true")}"),
            "addCollectionMembers" => (HttpMethod.Post, $"/collection/{Id()}/members"),
            "removeCollectionMembers" => (HttpMethod.Delete, $"/collection/{Id()}/members"),
            "getCollectionIdsByResourceIds" => (HttpMethod.Get, "/resource/collections?" + string.Join("&", ValidIds(request.Ids).Select(i => $"resourceIds={i}"))),
            "getCustomPropertyByKeys" => (HttpMethod.Get, $"/custom-property/ids?additionalItems={BoundedNumber("additionalItems", 0, 0, 2)}&" + string.Join("&", ValidIds(request.Ids).Select(i => $"ids={i}"))),
            "getAllHealthScoreProfiles" => (HttpMethod.Get, "/health-score/profiles"),
            "getHealthScoreDiagnosisForResource" => (HttpMethod.Get, $"/health-score/resource/{Id()}/diagnosis"),
            "getMappingsByResourceId" => (HttpMethod.Get, $"/media-library-resource-mapping/by-resource/{Id()}"),
            "addMediaLibraryResourceMapping" => (HttpMethod.Post, "/media-library-resource-mapping"),
            "deleteMediaLibraryResourceMapping" => (HttpMethod.Delete, $"/media-library-resource-mapping/{Id()}"),
            "getMatchingProfilesForResource" => (HttpMethod.Get, $"/resource-profile/by-resource/{Id()}"),
            "bindPropertyToMatchingProfiles" => (HttpMethod.Post, $"/resource-profile/by-resource/{Id()}/bind-property"),
            "updateResourceProfile" => (HttpMethod.Put, $"/resource-profile/{Id()}"),
            "getResourceEnhancements" => (HttpMethod.Get, $"/resource/{Id()}/enhancement?additionalItem={BoundedNumber("additionalItem", 0, 0, 1)}"),
            "enhanceResourceByEnhancer" => (HttpMethod.Post, $"/resource/{Id()}/enhancer/{Number("enhancerId")}/enhancement"),
            "applyEnhancementContextDataForResourceByEnhancer" => (HttpMethod.Post, $"/resource/{Id()}/enhancer/{Number("enhancerId")}/enhancement/apply"),
            "deleteEnhancementsByResources" => (HttpMethod.Delete, "/resources/enhancements"),
            "getAcquisitionCandidate" => (HttpMethod.Get, $"/acquisition/candidates/{Id()}"),
            "createAcquisition" => (HttpMethod.Post, "/acquisition"),
            "addResourceAcquisitionLead" => (HttpMethod.Post, $"/resource/{Id()}/acquisition-leads"),
            "deleteResourceAcquisitionLead" => (HttpMethod.Delete, $"/resource/{Id()}/acquisition-leads/{Number("leadId")}"),
            "aiTranslateResourceProperties" => (HttpMethod.Post, $"/ai/resource/{Id()}/translate"),
            "getAvailablePropertyTypesForManuallySettingValue" => (HttpMethod.Get, "/property/property-types-for-manually-setting-value"),
            "getPropertyBizValue" => (HttpMethod.Get, $"/property/pool/{Pool("pool")}/id/{Id()}/biz-value?dbValue={Uri.EscapeDataString(Value("dbValue", ""))}"),
            "getPropertyDbValue" => (HttpMethod.Get, $"/property/pool/{Pool("pool")}/id/{Id()}/db-value?bizValue={Uri.EscapeDataString(Value("bizValue", ""))}"),
            "getChildrenIwFsInfo" => (HttpMethod.Get, $"/management/library/resources/{Id()}/children?root={Uri.EscapeDataString(Value("root", ""))}&showHiddenFiles={Boolean("showHiddenFiles", "false")}&pageSize={BoundedNumber("pageSize", MaximumPageSize, 1, MaximumPageSize)}&cursor={Uri.EscapeDataString(Value("cursor", ""))}"),
            "resourcePlayableItems" => (HttpMethod.Get, $"/resource/{Id()}/playable-items"),
            "resourceBrowserFiles" => (HttpMethod.Get, $"/management/library/resources/{Id()}/browser-files?offset={BoundedNumber("offset", 0, 0, 1_000_000)}&pageSize={BoundedNumber("pageSize", 100, 1, MaximumPageSize)}&scope={Uri.EscapeDataString(Value("scope", "configured"))}&initialPath={Uri.EscapeDataString(Value("initialPath", ""))}"),
            "resourceNativeFallback" => (HttpMethod.Get, $"/management/library/resources/{Id()}/native-fallback?key={Uri.EscapeDataString(Value("key"))}&origin={BoundedNumber("origin", (int) DataOrigin.FileSystem, (int) DataOrigin.Manual, (int) DataOrigin.FileSystem)}"),
            "properties" => (HttpMethod.Get, $"/property/pool/{Pool("pool", ((int) PropertyPool.All).ToString())}?includeDeprecated={Boolean("includeDeprecated", "false")}"),
            "customProperties" => (HttpMethod.Get, "/custom-property/all"),
            "customPropertyAdd" => (HttpMethod.Post, "/custom-property"),
            "customPropertyPut" => (HttpMethod.Put, $"/custom-property/{Id()}"),
            "customPropertyDelete" => (HttpMethod.Delete, $"/custom-property/{Id()}"),
            "propertyValueResourceCounts" => (HttpMethod.Post, $"/property/pool/{Pool("pool")}/id/{Id()}/value-resource-counts"),
            "libraries" => (HttpMethod.Get, "/media-library-v2"),
            "libraryAdd" => (HttpMethod.Post, "/media-library-v2"),
            "libraryPut" => (HttpMethod.Put, $"/media-library-v2/{Id()}"),
            "libraryPatch" => (HttpMethod.Patch, $"/media-library-v2/{Id()}"),
            "libraryDelete" => (HttpMethod.Delete, $"/media-library-v2/{Id()}"),
            "resourcePropertyPut" => (HttpMethod.Put, $"/resource/{Id()}/property-value"),
            "resourceLibrariesPut" => (HttpMethod.Put, "/resource/media-libraries"),
            "resourceMappings" => (HttpMethod.Post, "/resource/media-library-mappings"),
            "resourceScopePreferencesGet" => (HttpMethod.Get, $"/resource/{Id()}/property-value-scope-preference"),
            "resourceScopePreferencesPut" => (HttpMethod.Put, $"/resource/{Id()}/property-value-scope-preference"),
            "resourceScopePreferencesDelete" => (HttpMethod.Delete, $"/resource/{Id()}/property-value-scope-preference?propertyPool={Pool("propertyPool")}&propertyId={Number("propertyId")}"),
            "resourcePin" => (HttpMethod.Put, $"/resource/{Id()}/pin?pin={Boolean("pin")}"),
            "resourceMarkPlayed" => (HttpMethod.Post, $"/resource/{Id()}/played-at?item={Uri.EscapeDataString(Value("item", ""))}"),
            _ => throw Invalid("This library operation is not supported.")
        };
        if (request.Operation is "resourceSearch" or "propertyValueResourceCounts")
        {
            if (body is not JObject page) throw Invalid("A search object is required.");
            var size = ReadInt(page.GetValue("pageSize", StringComparison.OrdinalIgnoreCase), 24);
            var index = ReadInt(page.GetValue("page", StringComparison.OrdinalIgnoreCase), 1);
            if (size is < 1 or > MaximumPageSize || index is < 1 or > 1_000_000)
                throw Invalid("The requested page exceeds the management limit.");
            foreach (var property in page.Properties().Where(p => p.Name.Equals("pageSize", StringComparison.OrdinalIgnoreCase) ||
                         p.Name.Equals("page", StringComparison.OrdinalIgnoreCase)).ToArray()) property.Remove();
            page["pageSize"] = size;
            page["page"] = index;
        }
        if (request.Operation == "resourceMappings") ValidIds(ReadIds(body));
        if (request.Operation == "deviceNameSet")
        {
            if (body is not JObject name || name.Properties().Any(p => p.Name is not ("name" or "expectedName")) ||
                name["expectedName"]?.Type != JTokenType.String ||
                string.IsNullOrWhiteSpace(name["expectedName"]!.Value<string>()) ||
                name["expectedName"]!.Value<string>()!.Length > 256 ||
                name["name"]?.Type is not (JTokenType.String or JTokenType.Null))
                throw Invalid("A new device name and its expected current name are required.");
            name["libraryEpoch"] = request.LibraryEpoch;
        }
        if (request.Operation == "resourceDisplayOptionsPatch" &&
            (body is not JObject display || display.Properties().Any(p => p.Name != "propertyValueScopePriority") ||
             display["propertyValueScopePriority"] is not JArray priorities || priorities.Count > 32 ||
             priorities.Any(p => p.Type != JTokenType.Integer || !Enum.IsDefined((PropertyValueScope) ReadInt(p, -1)))))
            throw Invalid("Only valid property value priorities can be changed here.");
        if (request.Operation == "resourceLibrariesPut")
        {
            if (body is not JObject libraries) throw Invalid("A library assignment is required.");
            ValidIds(ReadIds(libraries.GetValue("ids", StringComparison.OrdinalIgnoreCase)));
            ValidIds(ReadIds(libraries.GetValue("mediaLibraryIds", StringComparison.OrdinalIgnoreCase)), allowEmpty: true);
        }
        var bodyIdsKey = request.Operation switch
        {
            "bulkPutResourcePropertyValue" or "addCollectionMembers" or "removeCollectionMembers" => "resourceIds",
            "bulkDeleteResources" or "refreshResourcesCache" => "ids",
            _ => null
        };
        if (bodyIdsKey != null)
            ValidIds(ReadIds((body as JObject)?.GetValue(bodyIdsKey, StringComparison.OrdinalIgnoreCase)));
        if (request.Operation == "deleteEnhancementsByResources") ValidIds(ReadIds(body));
        if (request.Operation == "mergeResources")
        {
            var merge = body as JObject ?? throw Invalid("A merge request is required.");
            ValidIds([ReadInt(merge.GetValue("targetResourceId", StringComparison.OrdinalIgnoreCase), 0)]);
            ValidIds(ReadIds(merge.GetValue("sourceResourceIds", StringComparison.OrdinalIgnoreCase) ?? new JArray()), true);
            ValidIds(ReadIds(merge.GetValue("deleteResourceIds", StringComparison.OrdinalIgnoreCase) ?? new JArray()), true);
        }
        if (request.Operation == "createPlaceholderResources" &&
            ((body as JObject)?.GetValue("items", StringComparison.OrdinalIgnoreCase) is not JArray items || items.Count is < 1 or > MaximumPageSize))
            throw Invalid("At most 100 resources can be created in one request.");
        if (request.Operation == "transferResourceData")
        {
            if ((body as JObject)?.GetValue("items", StringComparison.OrdinalIgnoreCase) is not JArray transfers ||
                transfers.Count is < 1 or > MaximumPageSize) throw Invalid("At most 100 transfers are allowed.");
            foreach (var transfer in transfers.OfType<JObject>())
                ValidIds([ReadInt(transfer.GetValue("fromId", StringComparison.OrdinalIgnoreCase), 0),
                    ReadInt(transfer.GetValue("toId", StringComparison.OrdinalIgnoreCase), 0)]);
            if (transfers.Any(t => t is not JObject)) throw Invalid("Invalid resource transfer.");
        }
        var noBody = request.Operation is "resourcePin" or "resourceMarkPlayed" or "refreshResourceCache" or
            "enhanceResourceByEnhancer" or "applyEnhancementContextDataForResourceByEnhancer" or
            "previewCustomPropertyTypeConversion" or "changeCustomPropertyType";
        var needsBody = !noBody && (method != HttpMethod.Get && method != HttpMethod.Delete ||
            request.Operation is "removeCollectionMembers" or "deleteEnhancementsByResources");
        if (needsBody && body == null) throw Invalid("A request body is required.");
        var json = needsBody ? body!.ToString(Formatting.None) : null;
        var requestLimit = request.Operation is "uploadFile" or "addResourceAcquisitionTorrent"
            ? MaximumUploadEnvelopeBytes : MaximumRequestBytes;
        if (json != null && Encoding.UTF8.GetByteCount(json) > requestLimit)
            throw Invalid("The library request is too large.");
        path += (path.Contains('?') ? "&" : "?") + "managedLibraryEpoch=" + Uri.EscapeDataString(request.LibraryEpoch);
        return new ManagedLibraryOperation(method, path, json);
    }

    private static int ReadInt(JToken? token, int fallback) => token == null ? fallback :
        token.Type == JTokenType.Integer && int.TryParse(token.ToString(), out var n) ? n : throw Invalid("Invalid number.");
    private static int[] ReadIds(JToken? token) => token is JArray a ? a.Select(t => ReadInt(t, 0)).ToArray()
        : throw Invalid("An ID list is required.");
    private static int[] ValidIds(int[]? ids, bool allowEmpty = false) => ids != null &&
        (allowEmpty || ids.Length > 0) && ids.Length <= MaximumPageSize && ids.All(i => i > 0)
        ? ids : throw Invalid("At most 100 positive owner-local IDs are allowed.");
    private static FederationAccessException Invalid(string message) => new("InvalidManagedOperation", 400, message);
    private static void RejectAmbiguousProperties(JToken? token)
    {
        if (token is JObject obj && obj.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw Invalid("A request cannot repeat a property with different casing.");
        if (token is JContainer container)
            foreach (var child in container.Children()) RejectAmbiguousProperties(child);
    }
}
