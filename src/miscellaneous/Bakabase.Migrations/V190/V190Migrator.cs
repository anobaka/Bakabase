using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.Alias.Abstractions.Models.Db;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using CustomProperty = Bakabase.Abstractions.Models.Domain.CustomProperty;
using CustomPropertyValue = Bakabase.Abstractions.Models.Domain.CustomPropertyValue;

namespace Bakabase.Migrations.V190
{
    public class V190Migrator : AbstractMigrator
    {
        private readonly LegacyPublisherService _publisherService;
        private readonly LegacyVolumeService _volumeService;
        private readonly LegacySeriesService _seriesService;
        private readonly LegacyOriginalService _originalService;
        private readonly LegacyOriginalResourceMappingService _originalResourceMappingService;
        private readonly LegacyResourcePropertyService _customResourcePropertyService;
        private readonly LegacyFavoritesService _favoritesService;
        private readonly LegacyFavoritesResourceMappingService _favoritesResourceMappingService;
        private readonly LegacyResourceService _legacyResourceService;
        private readonly LegacyTagGroupService _tagGroupService;
        private readonly LegacyTagService _tagService;
        private readonly LegacyResourceTagMappingService _resourceTagMappingService;
        private readonly LegacyPublisherResourceMappingService _publisherResourceMappingService;
        private readonly LegacyAliasService _legacyAliasService;

        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly V190MigrationLocalizer _localizer;
        private readonly IBuiltinPropertyValueService _builtinPropertyValueService;
        private readonly IResourceService _resourceService;
        private readonly IAliasService _aliasService;
        private readonly ICategoryCustomPropertyMappingService _categoryCustomPropertyMappingService;
        private readonly ICategoryService _categoryService;

        private readonly AppService _appService;
        private readonly IFileManager _fileManager;

        private readonly InsideWorldDbContext _dbCtx;

        public V190Migrator(LegacyPublisherService publisherService, LegacyVolumeService volumeService,
            LegacySeriesService seriesService,
            LegacyOriginalService originalService, LegacyOriginalResourceMappingService originalResourceMappingService,
            LegacyResourcePropertyService customResourcePropertyService, LegacyFavoritesService favoritesService,
            LegacyFavoritesResourceMappingService favoritesResourceMappingService,
            LegacyResourceService legacyResourceService,
            LegacyTagGroupService tagGroupService, LegacyResourceTagMappingService resourceTagMappingService,
            LegacyPublisherResourceMappingService publisherResourceMappingService, LegacyTagService tagService,
            ICustomPropertyService customPropertyService, ICustomPropertyValueService customPropertyValueService,
            IServiceProvider serviceProvider, V190MigrationLocalizer localizer,
            IBuiltinPropertyValueService builtinPropertyValueService, IResourceService resourceService,
            IAliasService aliasService, LegacyAliasService legacyAliasService, InsideWorldDbContext dbCtx,
            ICategoryCustomPropertyMappingService categoryCustomPropertyMappingService,
            ICategoryService categoryService, AppService appService, IFileManager fileManager) : base(serviceProvider)
        {
            _publisherService = publisherService;
            _volumeService = volumeService;
            _seriesService = seriesService;
            _originalService = originalService;
            _originalResourceMappingService = originalResourceMappingService;
            _customResourcePropertyService = customResourcePropertyService;
            _favoritesService = favoritesService;
            _favoritesResourceMappingService = favoritesResourceMappingService;
            _legacyResourceService = legacyResourceService;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _tagGroupService = tagGroupService;
            _resourceTagMappingService = resourceTagMappingService;
            _publisherResourceMappingService = publisherResourceMappingService;
            _tagService = tagService;
            _localizer = localizer;
            _builtinPropertyValueService = builtinPropertyValueService;
            _resourceService = resourceService;
            _aliasService = aliasService;
            _legacyAliasService = legacyAliasService;
            _dbCtx = dbCtx;
            _categoryCustomPropertyMappingService = categoryCustomPropertyMappingService;
            _categoryService = categoryService;
            _appService = appService;
            _fileManager = fileManager;
        }

        protected override async Task MigrateAfterDbMigrationInternal(object context)
        {
            await MigrateAliases();
            await MigrateResources();
            await MigrateBuiltinProperties();
            await MigrateCustomProperties();
        }

        private async Task MigrateAliases()
        {
            if (!await _aliasService.Any())
            {
                var legacyMap = await _legacyAliasService.GetAll();
                var legacyGroups = legacyMap.DistinctBy(x => x.Name).GroupBy(x => x.GroupId)
                    .ToDictionary(d => (d.FirstOrDefault(x => x.IsPreferred) ?? d.First()).Name,
                        d => d.Select(c => c.Name).ToHashSet());
                var aliases = legacyGroups.SelectMany(la =>
                    la.Value.Select(t => new Alias {Text = t, Preferred = t == la.Key ? null : la.Key})).ToList();
                await _aliasService.AddAll(aliases);
            }
        }

        private async Task MigrateResources()
        {
            if (!await _resourceService.Any())
            {
                var legacyResources = await _legacyResourceService.GetAll();
                var resources = legacyResources.Select(lr => new Resource
                {
                    Id = lr.Id,
                    CategoryId = lr.CategoryId,
                    MediaLibraryId = lr.MediaLibraryId,
                    ParentId = lr.ParentId,
                    CreateDt = lr.CreateDt,
                    UpdateDt = lr.UpdateDt,
                    FileCreateDt = lr.FileCreateDt,
                    FileModifyDt = lr.FileModifyDt,
                    HasChildren = lr.HasChildren,
                    IsFile = lr.IsSingleFile,
                    Path = lr.RawFullname
                });
                await _resourceService.AddAll(resources);
            }
        }

        private async Task<CustomProperty> CreateOrGetDefaultProperty(LegacyResourceProperty property,
            string? subProperty)
        {
            var cpType = GetDefaultCustomPropertyTypeForLegacyProperties(property, subProperty);
            var cpName = _localizer.DefaultPropertyName(property, subProperty);
            var cp = (await _customPropertyService.GetAll(x => x.Name == cpName && x.Type == (int) cpType))
                .FirstOrDefault() ?? await _customPropertyService.Add(new CustomPropertyAddOrPutDto
                {
                    Name = cpName,
                    Type = (int) cpType,
                });

            await _customPropertyService.EnableAddingNewDataDynamically(cp.Id);

            cp = await _customPropertyService.GetByKey(cp.Id);

            return cp;
        }

        private async Task MigrateBuiltinProperties()
        {
            var resources = await _legacyResourceService.GetAll();
            var doneResourceIds = (await _builtinPropertyValueService.GetAll())
                .Where(x => x.Scope == (int) PropertyValueScope.Manual).Select(x => x.ResourceId).ToHashSet();

            var newValues = new List<Bakabase.Abstractions.Models.Domain.BuiltinPropertyValue>();
            foreach (var r in resources)
            {
                if (!doneResourceIds.Contains(r.Id))
                {
                    if (r.Rate > 0 || !string.IsNullOrEmpty(r.Introduction))
                    {
                        var v = new Bakabase.Abstractions.Models.Domain.BuiltinPropertyValue
                        {
                            ResourceId = r.Id,
                            Scope = (int) PropertyValueScope.Manual,
                            Rating = r.Rate > 0 ? r.Rate : null,
                            Introduction = string.IsNullOrEmpty(r.Introduction) ? null : r.Introduction
                        };
                        newValues.Add(v);
                    }
                }
            }

            await _builtinPropertyValueService.AddRange(newValues);
        }

        private async Task MigrateCustomProperties()
        {
            var customPropertyMigrationContexts =
                new List<(LegacyResourceProperty Property, string? SubProperty, Dictionary<int, object?>?
                    ResourceBizValue)>();

            foreach (var property in SpecificEnumUtils<LegacyResourceProperty>.Values)
            {
                if (property == LegacyResourceProperty.Volume)
                {
                    var resourceVolumeMap = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);
                    {
                        var valueMap =
                            resourceVolumeMap.ToDictionary(x => x.Key, x => (object?) (decimal) x.Value.Index);
                        customPropertyMigrationContexts.Add((property, nameof(Volume.Index), valueMap));
                    }
                    {
                        var valueMap = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Title))
                            .ToDictionary(x => x.Key, x => (object?) x.Value.Title);
                        customPropertyMigrationContexts.Add((property, nameof(Volume.Title), valueMap));
                    }
                    {
                        var valueMap = resourceVolumeMap.Where(x => !string.IsNullOrEmpty(x.Value.Name))
                            .ToDictionary(x => x.Key, x => (object?) x.Value.Name);
                        customPropertyMigrationContexts.Add((property, nameof(Volume.Name), valueMap));
                    }
                }
                else if (property == LegacyResourceProperty.CustomProperty)
                {
                    var typedValueMap = (await _customResourcePropertyService.GetAll())
                        .GroupBy(x => x.Key)
                        .ToDictionary(x => x.Key,
                            x => x.GroupBy(y => y.ResourceId).ToDictionary(d => d.Key, d => d
                                .Select(y => y.Value)
                                .Where(y => !string.IsNullOrEmpty(y)).Distinct()
                                .ToList()));

                    foreach (var (resourceKey, valueMap) in typedValueMap)
                    {
                        customPropertyMigrationContexts.Add(
                            (LegacyResourceProperty.CustomProperty, resourceKey,
                                MergeSameValue(valueMap).ToDictionary(d => d.Key, d => (object?) d.Value)));
                    }
                }
                else
                {
                    Dictionary<int, object?> resourceBizValueMap = null;
                    switch (property)
                    {
                        case LegacyResourceProperty.ReleaseDt:
                        {
                            var resources = await _legacyResourceService.GetAll();
                            resourceBizValueMap = resources.Where(r => r.ReleaseDt.HasValue)
                                .ToDictionary(x => x.Id, x => (object?) x.ReleaseDt!.Value);
                            break;
                        }
                        case LegacyResourceProperty.Publisher:
                        {
                            var mappings = await _publisherResourceMappingService.GetAll();
                            var publisherMap = (await _publisherService.GetAll()).ToDictionary(d => d.Id);
                            var typedValueMap = mappings.GroupBy(d => d.ResourceId).ToDictionary(d => d.Key,
                                d => d.Select(c => publisherMap.GetValueOrDefault(c.PublisherId)?.Name).OfType<string>()
                                    .ToHashSet().ToList());
                            resourceBizValueMap = MergeSameValue(typedValueMap)
                                .ToDictionary(d => d.Key, d => (object?) d.Value);
                            break;
                        }
                        case LegacyResourceProperty.Name:
                        {
                            var resources = await _legacyResourceService.GetAll();
                            resourceBizValueMap = resources.Where(r => !string.IsNullOrEmpty(r.Name))
                                .ToDictionary(x => x.Id, x => (object?) x.Name);
                            break;
                        }
                        case LegacyResourceProperty.Language:
                        {
                            var resources = await _legacyResourceService.GetAll();
                            resourceBizValueMap = resources.Where(r => r.Language > 0)
                                .ToDictionary(x => x.Id, x => (object?) _localizer.Language(x.Language));
                            break;
                        }
                        case LegacyResourceProperty.Original:
                        {
                            var originals = (await _originalService.GetAll()).ToDictionary(x => x.Id, x => x);
                            var resourceOriginals = await _originalResourceMappingService.GetAll();
                            var typedValueMap = resourceOriginals.GroupBy(x => x.ResourceId)
                                .ToDictionary(x => x.Key,
                                    x => x.Select(y => originals.GetValueOrDefault(y.OriginalId)?.Name)
                                        .Where(y => !string.IsNullOrEmpty(y)).Distinct().ToList())
                                .Where(x => x.Value.Any())
                                .ToDictionary(x => x.Key, x => x.Value!);
                            resourceBizValueMap = MergeSameValue(typedValueMap!)
                                .ToDictionary(d => d.Key, d => (object?) d.Value);
                            break;
                        }
                        case LegacyResourceProperty.Series:
                        {
                            var series = (await _seriesService.GetAll()).ToDictionary(x => x.Id, x => x);
                            var volumes = (await _volumeService.GetAll()).ToDictionary(x => x.ResourceId, x => x);
                            resourceBizValueMap = volumes.ToDictionary(x => x.Key,
                                    x => series.GetValueOrDefault(x.Value.SerialId)?.Name)
                                .Where(x => !string.IsNullOrEmpty(x.Value))
                                .ToDictionary(x => x.Key, x => (object?) x.Value);
                            break;
                        }
                        case LegacyResourceProperty.Favorites:
                        {
                            var favorites = (await _favoritesService.GetAll()).ToDictionary(x => x.Id, x => x);
                            var resourceFavoritesMappings = await _favoritesResourceMappingService.GetAll();
                            resourceBizValueMap = resourceFavoritesMappings.GroupBy(x => x.ResourceId)
                                .ToDictionary(x => x.Key,
                                    x => x.Select(y => favorites.GetValueOrDefault(y.FavoritesId)?.Name)
                                        .Where(y => !string.IsNullOrEmpty(y)).Distinct().ToList())
                                .Where(x => x.Value.Any())
                                .ToDictionary(x => x.Key, x => (object?) x.Value);
                            break;
                        }
                        case LegacyResourceProperty.Tag:
                        {
                            var tagGroupMap = (await _tagGroupService.GetAll()).ToDictionary(d => d.Id);
                            var tags = await _tagService.GetAll();
                            var tagValueMap = tags.ToDictionary(d => d.Id,
                                t => new TagValue(tagGroupMap.GetValueOrDefault(t.GroupId)?.Name, t.Name));
                            var resourceTagMappings = await _resourceTagMappingService.GetAll();
                            var typedValueMap = resourceTagMappings.GroupBy(d => d.ResourceId).ToDictionary(d => d.Key,
                                d => d.Select(c => tagValueMap.GetValueOrDefault(c.TagId)).OfType<TagValue>().ToList());
                            resourceBizValueMap = MergeSameValue(typedValueMap)
                                .ToDictionary(d => d.Key, d => (object?) d.Value);
                            break;
                        }
                        case LegacyResourceProperty.Cover:
                        {
                            var prevCoverRootDir = Path.Combine(_appService.TempFilesPath, "cover");
                            if (Directory.Exists(prevCoverRootDir))
                            {
                                var files = Directory.GetFiles(prevCoverRootDir, "cover.*", SearchOption.AllDirectories);
                                if (files.Any())
                                {
                                    resourceBizValueMap = new Dictionary<int, object?>();
                                    var subDirs = Directory.GetDirectories(prevCoverRootDir);
                                    var newCoverRootDir = _fileManager.BuildAbsolutePath("cover");
                                    Directory.CreateDirectory(newCoverRootDir);
                                    foreach (var sd in subDirs)
                                    {
                                        if (int.TryParse(Path.GetFileName(sd), out var resourceId))
                                        {
                                            var newResourceCoverDir =
                                                Path.Combine(newCoverRootDir, resourceId.ToString());
                                            var subFiles = Directory.GetFiles(sd, "*", SearchOption.AllDirectories);
                                            try
                                            {
                                                foreach (var sf in subFiles)
                                                {
                                                    var nf = sf.Replace(sd, newResourceCoverDir);
                                                    if (!File.Exists(nf))
                                                    {
                                                        File.Copy(sf, nf);
                                                    }
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                Logger.LogWarning(e,
                                                    $"Failed to migrate cover files for resource {resourceId}.");
                                            }

                                            var covers = Directory.GetFiles(newResourceCoverDir);
                                            if (covers.Any())
                                            {
                                                resourceBizValueMap[resourceId] =
                                                    new ListStringValueBuilder(covers.ToList()).Value;
                                            }
                                        }
                                    }
                                }
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    customPropertyMigrationContexts.Add((property, null, resourceBizValueMap));
                }
            }

            customPropertyMigrationContexts.RemoveAll(x => x.ResourceBizValue?.Any() != true);

            var propertyIds = new List<int>();
            foreach (var (lp, sp, rbMap) in customPropertyMigrationContexts)
            {
                var property = await CreateOrGetDefaultProperty(lp, sp);
                propertyIds.Add(property.Id);
                var migrated = await _customPropertyValueService.Any(x => x.PropertyId == property.Id);
                if (!migrated)
                {
                    var values = new List<CustomPropertyValue>();
                    var propertyChanged = false;
                    foreach (var (rId, bizValue) in rbMap!)
                    {
                        var result = await _customPropertyValueService.CreateTransient(bizValue, property.BizValueType, property,
                            rId, (int) PropertyValueScope.Manual);
                        if (result.HasValue)
                        {
                            var (pv, pc) = result.Value;
                            if (pc)
                            {
                                propertyChanged = true;
                            }

                            values.Add(pv);
                        }
                    }

                    if (propertyChanged)
                    {
                        await _customPropertyService.Put(property);
                    }

                    await _customPropertyValueService.AddRange(values);
                }
            }

            var allMappings = await _categoryCustomPropertyMappingService.GetAll();
            if (!allMappings.Any())
            {
                var categories = await _categoryService.GetAll(null, CategoryAdditionalItem.None);
                var mappings = categories.SelectMany(c => propertyIds.Select(p => new CategoryCustomPropertyMapping
                {
                    CategoryId = c.Id,
                    PropertyId = p
                })).ToList();
                await _categoryCustomPropertyMappingService.AddAll(mappings);
            }
        }

        public static CustomPropertyType GetDefaultCustomPropertyTypeForLegacyProperties(
            LegacyResourceProperty property,
            string? propertyKey)
        {
            return property switch
            {
                LegacyResourceProperty.ReleaseDt => CustomPropertyType.DateTime,
                LegacyResourceProperty.Publisher => CustomPropertyType.MultipleChoice,
                LegacyResourceProperty.Name => CustomPropertyType.SingleLineText,
                LegacyResourceProperty.Language => CustomPropertyType.SingleChoice,
                LegacyResourceProperty.Volume => propertyKey switch
                {
                    nameof(Volume.Index) => CustomPropertyType.Number,
                    nameof(Volume.Title) => CustomPropertyType.SingleLineText,
                    nameof(Volume.Name) => CustomPropertyType.SingleLineText,
                },
                LegacyResourceProperty.Original => CustomPropertyType.MultipleChoice,
                LegacyResourceProperty.Series => CustomPropertyType.SingleLineText,
                LegacyResourceProperty.CustomProperty => CustomPropertyType.MultipleChoice,
                LegacyResourceProperty.Favorites => CustomPropertyType.MultipleChoice,
                LegacyResourceProperty.Tag => CustomPropertyType.Tags,
                LegacyResourceProperty.Cover => CustomPropertyType.Attachment,
                _ => throw new ArgumentOutOfRangeException(nameof(property), property, null)
            };
        }

        private static Dictionary<int, List<string>> MergeSameValue(Dictionary<int, List<string>> typedValueMap)
        {
            var listMap = new Dictionary<string, List<string>>();
            var separator = $"[{Guid.NewGuid().ToString("N")[..6]}]";
            var valueMap = typedValueMap.ToDictionary(d => d.Key, d =>
            {
                var key = string.Join(separator, d.Value);
                if (!listMap.TryGetValue(key, out var list))
                {
                    list = listMap[key] = d.Value;
                }

                return list;
            });
            return valueMap;
        }

        private static Dictionary<int, List<TagValue>> MergeSameValue(
            Dictionary<int, List<TagValue>> typedValueMap)
        {
            var listMap = new Dictionary<string, List<TagValue>>();
            var separator = $"[{Guid.NewGuid().ToString("N")[..6]}]";
            var tagGroupAndTagSeparator = $"[{Guid.NewGuid().ToString("N")[..6]}]";
            var valueMap = typedValueMap.ToDictionary(d => d.Key, d =>
            {
                var key = string.Join(separator,
                    d.Value.Select(c => string.Join(tagGroupAndTagSeparator, c.Group, c.Name)));
                if (!listMap.TryGetValue(key, out var list))
                {
                    list = listMap[key] = d.Value;
                }

                return list;
            });
            return valueMap;
        }

        protected override string ApplyOnVersionEqualsOrBeforeString => "1.8.999";
    }
}