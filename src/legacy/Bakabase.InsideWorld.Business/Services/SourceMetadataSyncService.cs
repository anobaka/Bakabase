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
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam.Models;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Bakabase.Modules.Property;
using Bakabase.Modules.StandardValue.Models.Domain;

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

        // Remove old mappings
        if (existing.Count > 0)
        {
            await orm.RemoveRange(existing);
        }

        // Add new mappings
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

    #region Available Fields

    public List<SourceMetadataFieldInfo> GetAvailableMetadataFields(ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Steam => SteamFields,
            ResourceSource.DLsite => DLsiteFields,
            ResourceSource.ExHentai => ExHentaiFields,
            _ => []
        };
    }

    private static readonly List<SourceMetadataFieldInfo> SteamFields =
    [
        new("Name", StandardValueType.String),
        new("ShortDescription", StandardValueType.String),
        new("Developers", StandardValueType.ListString),
        new("Publishers", StandardValueType.ListString),
        new("Genres", StandardValueType.ListString),
        new("Categories", StandardValueType.ListString),
        new("MetacriticScore", StandardValueType.Decimal),
        new("HeaderImage", StandardValueType.String),
        new("ReleaseDate", StandardValueType.String),
    ];

    private static readonly List<SourceMetadataFieldInfo> DLsiteFields =
    [
        new("Name", StandardValueType.String),
        new("Introduction", StandardValueType.String),
        new("Rating", StandardValueType.Decimal),
        new("CoverUrls", StandardValueType.ListString),
    ];

    private static readonly List<SourceMetadataFieldInfo> ExHentaiFields =
    [
        new("Name", StandardValueType.String),
        new("RawName", StandardValueType.String),
        new("Introduction", StandardValueType.String),
        new("Rate", StandardValueType.Decimal),
        new("Tags", StandardValueType.ListTag),
        new("Category", StandardValueType.String),
    ];

    #endregion

    #region Metadata Extraction

    private (object? Value, StandardValueType Type)? ExtractField(ResourceSource source, string fieldName,
        string metadataJson)
    {
        try
        {
            return source switch
            {
                ResourceSource.Steam => ExtractSteamField(fieldName, metadataJson),
                ResourceSource.DLsite => ExtractDLsiteField(fieldName, metadataJson),
                ResourceSource.ExHentai => ExtractExHentaiField(fieldName, metadataJson),
                _ => null
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract field {Field} from {Source} metadata", fieldName, source);
            return null;
        }
    }

    private static (object? Value, StandardValueType Type)? ExtractSteamField(string fieldName, string json)
    {
        var detail = JsonSerializer.Deserialize<SteamAppDetails>(json, JsonSerializerOptions.Web);
        if (detail == null) return null;

        return fieldName switch
        {
            "Name" => (detail.Name, StandardValueType.String),
            "ShortDescription" => (detail.ShortDescription, StandardValueType.String),
            "Developers" => (detail.Developers, StandardValueType.ListString),
            "Publishers" => (detail.Publishers, StandardValueType.ListString),
            "Genres" => (detail.Genres?.Select(g => g.Description).Where(d => d != null).ToList() as object,
                StandardValueType.ListString),
            "Categories" => (detail.Categories?.Select(c => c.Description).Where(d => d != null).ToList() as object,
                StandardValueType.ListString),
            "MetacriticScore" => detail.Metacritic != null
                ? ((decimal)detail.Metacritic.Score, StandardValueType.Decimal)
                : null,
            "HeaderImage" => (detail.HeaderImage, StandardValueType.String),
            "ReleaseDate" => (detail.ReleaseDate?.Date, StandardValueType.String),
            _ => null
        };
    }

    private static (object? Value, StandardValueType Type)? ExtractDLsiteField(string fieldName, string json)
    {
        var detail = JsonSerializer.Deserialize<DLsiteProductDetail>(json, JsonSerializerOptions.Web);
        if (detail == null) return null;

        return fieldName switch
        {
            "Name" => (detail.Name, StandardValueType.String),
            "Introduction" => (detail.Introduction, StandardValueType.String),
            "Rating" => detail.Rating.HasValue ? (detail.Rating.Value, StandardValueType.Decimal) : null,
            "CoverUrls" => (detail.CoverUrls?.ToList() as object, StandardValueType.ListString),
            _ => null
        };
    }

    private static (object? Value, StandardValueType Type)? ExtractExHentaiField(string fieldName, string json)
    {
        var detail = JsonSerializer.Deserialize<ExHentaiResource>(json, JsonSerializerOptions.Web);
        if (detail == null) return null;

        return fieldName switch
        {
            "Name" => (detail.Name, StandardValueType.String),
            "RawName" => (detail.RawName, StandardValueType.String),
            "Introduction" => (detail.Introduction, StandardValueType.String),
            "Rate" => detail.Rate != 0 ? (detail.Rate, StandardValueType.Decimal) : null,
            "Tags" => detail.Tags != null
                ? (detail.Tags.SelectMany(kv => kv.Value.Select(v => new TagValue(kv.Key, v))).ToList() as object,
                    StandardValueType.ListTag)
                : null,
            "Category" => (detail.Category?.ToString(), StandardValueType.String),
            _ => null
        };
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
        await ApplyMappings(resourceId, source, metadataJson, mappings, scope, ct);
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
                    await ApplyMappings(resourceId, source, metadataJson, mappings, scope, ct);
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

    private async Task ApplyMappings(int resourceId, ResourceSource source, string metadataJson,
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

            var extracted = ExtractField(source, mapping.MetadataField, metadataJson);
            if (extracted == null) continue;

            var (value, valueType) = extracted.Value;
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
                        // Check if value already exists
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
            // Check if reserved property value already exists for this scope
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

    #region Helpers

    private static PropertyValueScope GetPropertyValueScope(ResourceSource source) => source switch
    {
        ResourceSource.Steam => PropertyValueScope.Steam,
        ResourceSource.DLsite => PropertyValueScope.DLsite,
        ResourceSource.ExHentai => PropertyValueScope.ExHentai,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    private async Task<string?> GetMetadataJsonForResource(int resourceId, ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Steam => (await serviceProvider.GetRequiredService<ISteamAppService>().GetAll())
                .FirstOrDefault(a => a.ResourceId == resourceId)?.MetadataJson,
            ResourceSource.DLsite => (await serviceProvider.GetRequiredService<IDLsiteWorkService>().GetAll())
                .FirstOrDefault(w => w.ResourceId == resourceId)?.MetadataJson,
            ResourceSource.ExHentai => (await serviceProvider.GetRequiredService<IExHentaiGalleryService>().GetAll())
                .FirstOrDefault(g => g.ResourceId == resourceId)?.MetadataJson,
            _ => null
        };
    }

    private async Task<List<(int ResourceId, string? MetadataJson)>> GetAllItemsWithMetadata(ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Steam => (await serviceProvider.GetRequiredService<ISteamAppService>().GetAll())
                .Where(a => a.ResourceId.HasValue && !string.IsNullOrEmpty(a.MetadataJson))
                .Select(a => (a.ResourceId!.Value, a.MetadataJson))
                .ToList(),
            ResourceSource.DLsite => (await serviceProvider.GetRequiredService<IDLsiteWorkService>().GetAll())
                .Where(w => w.ResourceId.HasValue && !string.IsNullOrEmpty(w.MetadataJson))
                .Select(w => (w.ResourceId!.Value, w.MetadataJson))
                .ToList(),
            ResourceSource.ExHentai =>
                (await serviceProvider.GetRequiredService<IExHentaiGalleryService>().GetAll())
                .Where(g => g.ResourceId.HasValue && !string.IsNullOrEmpty(g.MetadataJson))
                .Select(g => (g.ResourceId!.Value, g.MetadataJson))
                .ToList(),
            _ => []
        };
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
