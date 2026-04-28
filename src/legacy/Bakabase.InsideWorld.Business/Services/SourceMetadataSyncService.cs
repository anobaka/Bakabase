using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Extensions;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.InsideWorld.Business.Services;

public class SourceMetadataSyncService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, SourceMetadataMappingDbModel, int> orm,
    IServiceProvider serviceProvider,
    ILogger<SourceMetadataSyncService<TDbContext>> logger
) : ScopedService(serviceProvider), ISourceMetadataSyncService where TDbContext : DbContext
{
    #region Mapping CRUD

    public async Task<List<SourceMetadataMapping>> GetMappings(ResourceSource source)
    {
        var dbModels = await orm.GetAll(m => m.Source == source);
        return dbModels.Select(ToDomainModel).ToList();
    }

    public async Task SaveMappings(ResourceSource source, List<SourceMetadataMapping> mappings)
    {
        var existing = await orm.GetAll(m => m.Source == source);
        if (existing.Count > 0) await orm.RemoveRange(existing);

        if (mappings.Count > 0)
        {
            var dbModels = mappings.Select(m => new SourceMetadataMappingDbModel
            {
                Source = source,
                MetadataField = m.MetadataField,
                TargetPool = (int)m.TargetPool,
                TargetPropertyId = m.TargetPropertyId
            }).ToList();
            await orm.AddRange(dbModels);
        }
    }

    #endregion

    #region Apply Metadata to Properties

    public async Task SyncMetadataToProperties(int resourceId, ResourceSource source, CancellationToken ct)
    {
        var mappings = await GetMappings(source);
        if (mappings.Count == 0) return;

        var metadataJson = await GetMetadataJsonForResource(resourceId, source);
        if (string.IsNullOrEmpty(metadataJson)) return;

        var scope = GetPropertyValueScope(source);
        await ApplyMappings(resourceId, metadataJson, mappings, scope, ct);
    }

    public async Task SyncMetadataToPropertiesBatch(ResourceSource source, Action<int>? onProgress,
        CancellationToken ct)
    {
        var mappings = await GetMappings(source);
        if (mappings.Count == 0) return;

        var items = await GetAllItemsWithMetadata(source);
        if (items.Count == 0) return;

        var scope = GetPropertyValueScope(source);
        var processed = 0;

        foreach (var (resourceId, metadataJson) in items)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(metadataJson))
            {
                try
                {
                    await ApplyMappings(resourceId, metadataJson, mappings, scope, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to apply metadata mappings for resource {ResourceId} from {Source}",
                        resourceId, source);
                }
            }

            processed++;
            onProgress?.Invoke(items.Count > 0 ? processed * 100 / items.Count : 100);
        }
    }

    private async Task ApplyMappings(int resourceId, string metadataJson,
        List<SourceMetadataMapping> mappings, PropertyValueScope scope, CancellationToken ct)
    {
        var standardValueService = serviceProvider.GetRequiredService<IStandardValueService>();
        var customPropertyValueService = serviceProvider.GetRequiredService<ICustomPropertyValueService>();
        var customPropertyService = serviceProvider.GetRequiredService<ICustomPropertyService>();
        var reservedPropertyValueService = serviceProvider.GetRequiredService<IReservedPropertyValueService>();

        var propertyMap = (await customPropertyService.GetAll()).ToDictionary(d => d.Id, d => d);

        var rpv = new ReservedPropertyValue
        {
            ResourceId = resourceId,
            Scope = (int)scope
        };
        var hasReservedChanges = false;

        foreach (var mapping in mappings)
        {
            ct.ThrowIfCancellationRequested();

            var value = ExtractFieldValue(mapping.MetadataField, metadataJson, out var valueType);
            if (value == null) continue;

            switch (mapping.TargetPool)
            {
                case PropertyPool.Reserved:
                {
                    var propertyDescriptor =
                        PropertySystem.Builtin.TryGet((ResourceProperty)mapping.TargetPropertyId);
                    if (propertyDescriptor == null) continue;

                    var nv = await standardValueService.Convert(value, valueType,
                        propertyDescriptor.Type.GetBizValueType());

                    switch ((ReservedProperty)mapping.TargetPropertyId)
                    {
                        case ReservedProperty.Introduction:
                            rpv.Introduction = nv as string;
                            hasReservedChanges = true;
                            break;
                        case ReservedProperty.Rating:
                            rpv.Rating = nv is decimal d ? d : null;
                            hasReservedChanges = true;
                            break;
                        case ReservedProperty.Cover:
                            rpv.CoverPaths = nv as List<string>;
                            hasReservedChanges = true;
                            break;
                        case ReservedProperty.Name:
                            rpv.Name = nv as string;
                            hasReservedChanges = true;
                            break;
                    }

                    break;
                }
                case PropertyPool.Custom:
                {
                    if (!propertyMap.TryGetValue(mapping.TargetPropertyId, out var customProperty))
                        continue;

                    var propertyDescriptor = customProperty.ToProperty();
                    var nv = await standardValueService.Convert(value, valueType,
                        propertyDescriptor.Type.GetBizValueType());

                    var result = await customPropertyValueService.CreateTransient(nv,
                        propertyDescriptor.Type.GetBizValueType(),
                        customProperty, resourceId, (int)scope);

                    if (result.HasValue)
                    {
                        var (pv, _) = result.Value;
                        var existingValues = await customPropertyValueService.GetAllDbModels(
                            v => v.ResourceId == resourceId && v.PropertyId == mapping.TargetPropertyId &&
                                 v.Scope == (int)scope);

                        if (existingValues.Any())
                        {
                            pv.Id = existingValues.First().Id;
                            await customPropertyValueService.UpdateRange([pv]);
                        }
                        else
                        {
                            await customPropertyValueService.AddRange([pv]);
                        }
                    }

                    break;
                }
            }
        }

        if (hasReservedChanges)
        {
            var existingRpv = await reservedPropertyValueService.GetFirst(
                v => v.ResourceId == resourceId && v.Scope == (int)scope);

            if (existingRpv != null)
            {
                rpv.Id = existingRpv.Id;
                await reservedPropertyValueService.Update(rpv);
            }
            else
            {
                await reservedPropertyValueService.Add(rpv);
            }
        }
    }

    #endregion

    #region Metadata Extraction (Generic JSON Path)

    /// <summary>
    /// Extracts a value from MetadataJson using dot-separated path.
    /// Auto-infers the StandardValueType from the JSON structure.
    /// </summary>
    private object? ExtractFieldValue(string fieldPath, string metadataJson, out StandardValueType valueType)
    {
        valueType = StandardValueType.String;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var element = NavigateJsonPath(doc.RootElement, fieldPath);
            if (element == null) return null;
            return ConvertJsonElement(element.Value, out valueType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract field '{Field}' from metadata JSON", fieldPath);
            return null;
        }
    }

    private static JsonElement? NavigateJsonPath(JsonElement root, string path)
    {
        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object) return null;

            var found = false;
            foreach (var prop in current.EnumerateObject())
            {
                if (string.Equals(prop.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    current = prop.Value;
                    found = true;
                    break;
                }
            }

            if (!found) return null;
        }

        return current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : current;
    }

    private static object? ConvertJsonElement(JsonElement element, out StandardValueType valueType)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                valueType = StandardValueType.String;
                return element.GetString();

            case JsonValueKind.Number:
                valueType = StandardValueType.Decimal;
                return element.GetDecimal();

            case JsonValueKind.True or JsonValueKind.False:
                valueType = StandardValueType.Boolean;
                return element.GetBoolean();

            case JsonValueKind.Array:
            {
                var items = element.EnumerateArray().ToList();
                if (items.Count == 0) { valueType = StandardValueType.ListString; return null; }

                if (items.All(i => i.ValueKind == JsonValueKind.String))
                {
                    valueType = StandardValueType.ListString;
                    return items.Select(i => i.GetString()!).ToList();
                }

                // Array of objects → extract "description" or "name" field
                if (items.All(i => i.ValueKind == JsonValueKind.Object))
                {
                    var values = items.Select(i =>
                    {
                        foreach (var p in i.EnumerateObject())
                        {
                            if (p.Value.ValueKind == JsonValueKind.String &&
                                p.Name.Equals("description", StringComparison.OrdinalIgnoreCase))
                                return p.Value.GetString();
                            if (p.Value.ValueKind == JsonValueKind.String &&
                                p.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                                return p.Value.GetString();
                        }
                        return null;
                    }).Where(v => v != null).ToList();

                    if (values.Count > 0) { valueType = StandardValueType.ListString; return values!; }
                }

                valueType = StandardValueType.ListString;
                return items.Select(i => i.ToString()).ToList();
            }

            case JsonValueKind.Object:
            {
                // Dict<string, string[]> → ListTag
                var tags = new List<TagValue>();
                var allArrays = true;
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String)
                                tags.Add(new TagValue(prop.Name, item.GetString()!));
                    }
                    else { allArrays = false; break; }
                }
                if (allArrays && tags.Count > 0) { valueType = StandardValueType.ListTag; return tags; }

                valueType = StandardValueType.String;
                return element.ToString();
            }

            default:
                valueType = StandardValueType.String;
                return null;
        }
    }

    #endregion

    #region Helpers

    private static PropertyValueScope GetPropertyValueScope(ResourceSource source) =>
        source.GetPropertyValueScope();

    private async Task<string?> GetMetadataJsonForResource(int resourceId, ResourceSource source)
    {
        var sourceLinkService = serviceProvider.GetRequiredService<IResourceSourceLinkService>();
        var links = await sourceLinkService.GetByResourceId(resourceId);
        return links.FirstOrDefault(l => l.Source == source)?.MetadataJson;
    }

    private async Task<List<(int ResourceId, string? MetadataJson)>> GetAllItemsWithMetadata(ResourceSource source)
    {
        var sourceLinkService = serviceProvider.GetRequiredService<IResourceSourceLinkService>();
        var links = await sourceLinkService.GetAll();
        return links
            .Where(l => l.Source == source && !string.IsNullOrEmpty(l.MetadataJson))
            .Select(l => (l.ResourceId, l.MetadataJson))
            .ToList();
    }

    private static SourceMetadataMapping ToDomainModel(SourceMetadataMappingDbModel db) => new()
    {
        Id = db.Id,
        Source = db.Source,
        MetadataField = db.MetadataField,
        TargetPool = (PropertyPool)db.TargetPool,
        TargetPropertyId = db.TargetPropertyId
    };

    #endregion
}
