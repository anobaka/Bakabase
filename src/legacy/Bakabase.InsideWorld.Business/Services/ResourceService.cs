﻿using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bootstrap.Components.Orm.Extensions;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ResourceService : IResourceService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.ResourceDbModel,
                int>
            _orm;

        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, ResourceCacheDbModel, int>
            _resourceCacheOrm;

        private readonly ISpecialTextService _specialTextService;
        private readonly IMediaLibraryService _mediaLibraryService;
        private readonly ICategoryService _categoryService;
        private readonly ILogger<ResourceService> _logger;
        private readonly SemaphoreSlim _addOrUpdateLock = new(1, 1);
        private readonly IBOptionsManager<ResourceOptions> _optionsManager;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IAliasService _aliasService;
        private readonly IReservedPropertyValueService _reservedPropertyValueService;
        private readonly ICoverDiscoverer _coverDiscoverer;
        private readonly IPropertyService _propertyService;
        private readonly IFileManager _fileManager;

        public ResourceService(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IAliasService aliasService, IMediaLibraryService mediaLibraryService, ICategoryService categoryService,
            ILogger<ResourceService> logger,
            ICustomPropertyService customPropertyService, ICustomPropertyValueService customPropertyValueService,
            IReservedPropertyValueService reservedPropertyValueService,
            ICoverDiscoverer coverDiscoverer, IBOptionsManager<ResourceOptions> optionsManager,
            IPropertyService propertyService,
            FullMemoryCacheResourceService<InsideWorldDbContext, ResourceCacheDbModel, int> resourceCacheOrm,
            IFileManager fileManager)
        {
            _specialTextService = specialTextService;
            _aliasService = aliasService;
            _mediaLibraryService = mediaLibraryService;
            _categoryService = categoryService;
            _logger = logger;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _reservedPropertyValueService = reservedPropertyValueService;
            _coverDiscoverer = coverDiscoverer;
            _optionsManager = optionsManager;
            _propertyService = propertyService;
            _resourceCacheOrm = resourceCacheOrm;
            _fileManager = fileManager;
            _orm =
                new FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.ResourceDbModel, int>(
                    serviceProvider);
        }

        public InsideWorldDbContext DbContext => _orm.DbContext;

        public async Task DeleteByKeys(int[] ids)
        {
            await DeleteRelatedData(ids.ToList());
            await _orm.RemoveByKeys(ids);
        }

        public async Task<List<Resource>> GetAll(
            Expression<Func<Abstractions.Models.Db.ResourceDbModel, bool>>? selector = null,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var data = await _orm.GetAll(selector, false);
            var dtoList = await ToDomainModel(data.ToArray(), additionalItems);
            return dtoList;
        }

        public async Task<SearchResponse<Resource>> Search(ResourceSearch model)
        {
            var allResources = await GetAll();
            var context = new ResourceSearchContext(allResources);

            await PreparePropertyDbValues(context, model.Group);
            var resourceIds = SearchResourceIds(model.Group, context);
            var ordersForSearch = model.Orders.BuildForSearch();

            Func<Abstractions.Models.Db.ResourceDbModel, bool>? exp = resourceIds == null
                ? null
                : r => resourceIds.Contains(r.Id);

            var resources = await _orm.Search(exp, model.PageIndex, model.PageSize,
                ordersForSearch,
                false);
            var dtoList = await ToDomainModel(resources.Data!.ToArray(), ResourceAdditionalItem.All);

            return model.BuildResponse(dtoList, resources.TotalCount);
        }

        private async Task PreparePropertyDbValues(ResourceSearchContext context, ResourceSearchFilterGroup? group)
        {
            if (group != null)
            {
                var filters = group.ExtractFilters() ?? [];
                if (filters.Any() && context.ResourcesPool?.Any() == true)
                {
                    context.PropertyValueMap = new();
                    if (filters.Any(f => f.PropertyPool == PropertyPool.Internal))
                    {
                        var getValue = SpecificEnumUtils<InternalProperty>.Values.ToDictionary(d => d, d => d switch
                        {
                            InternalProperty.Filename => (Func<Resource, object?>) (r => r.FileName),
                            InternalProperty.DirectoryPath => r => r.Directory,
                            InternalProperty.CreatedAt => r => r.CreatedAt,
                            InternalProperty.FileCreatedAt => r => r.FileCreatedAt,
                            InternalProperty.FileModifiedAt => r => r.FileModifiedAt,
                            InternalProperty.Category => r => r.CategoryId.ToString(),
                            InternalProperty.MediaLibrary => r => new List<string> {r.MediaLibraryId.ToString()},
                            _ => null
                        });
                        context.PropertyValueMap[PropertyPool.Internal] = getValue.Where(x => x.Value != null)
                            .ToDictionary(d => (int) d.Key,
                                d => context.ResourcesPool.ToDictionary(x => x.Key,
                                    x =>
                                    {
                                        var v = d.Value!(x.Value);
                                        return v == null ? null : (List<object>?) [v];
                                    }));
                    }

                    if (filters.Any(f => f.PropertyPool == PropertyPool.Reserved))
                    {
                        var reservedValue =
                            (await _reservedPropertyValueService.GetAll(x =>
                                context.AllResourceIds.Contains(x.ResourceId)))
                            .GroupBy(d => d.ResourceId).ToDictionary(d => d.Key, d => d.ToList());
                        var getValue = SpecificEnumUtils<ReservedProperty>.Values.ToDictionary(d => d, d => d switch
                        {
                            ReservedProperty.Rating => (Func<ReservedPropertyValue, object?>) (r => r.Rating),
                            ReservedProperty.Introduction => r => r.Introduction,
                            _ => null
                        });
                        context.PropertyValueMap[PropertyPool.Reserved] = getValue.Where(x => x.Value != null)
                            .ToDictionary(d => (int) d.Key,
                                d => context.AllResourceIds.ToDictionary(x => x,
                                    x => reservedValue.GetValueOrDefault(x)?.Select(y => d.Value!(y))
                                        .Where(z => z != null).ToList() as List<object>));
                    }

                    if (filters.Any(f => f.PropertyPool == PropertyPool.Custom))
                    {
                        var propertyIds = filters.Where(x => x.PropertyPool == PropertyPool.Custom)
                            .Select(d => d.PropertyId).ToHashSet();
                        var cpValues =
                            (await _customPropertyValueService.GetAll(x => propertyIds.Contains(x.PropertyId),
                                CustomPropertyValueAdditionalItem.None, false)).GroupBy(d => d.PropertyId)
                            .ToDictionary(d => d.Key,
                                d => d.GroupBy(x => x.ResourceId)
                                    .ToDictionary(a => a.Key,
                                        List<object>? (a) => a.Select(b => b.Value).Where(c => c != null).ToList()!));
                        context.PropertyValueMap[PropertyPool.Custom] = cpValues;
                    }
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="group"></param>
        /// <param name="context"></param>
        /// <returns>
        /// <para>Null: all resources are valid</para>
        /// <para>Empty: all resources are invalid</para>
        /// <para>Any: valid resource id list</para>
        /// </returns>
        private HashSet<int>? SearchResourceIds(ResourceSearchFilterGroup? group, ResourceSearchContext context)
        {
            if (group == null || group.Disabled)
            {
                return null;
            }

            var steps = new List<Func<HashSet<int>?>>();

            if (group.Filters?.Any() == true)
            {
                foreach (var filter in group.Filters.Where(f => f.IsValid() && !f.Disabled))
                {
                    var propertyType = filter.Property.Type;
                    var psh = PropertyInternals.PropertySearchHandlerMap.GetValueOrDefault(propertyType);
                    if (psh != null)
                    {
                        steps.Add(() =>
                        {
                            return context.ResourceIdCandidates.Where(id =>
                            {
                                var values = context.PropertyValueMap?.GetValueOrDefault(filter.PropertyPool)
                                    ?.GetValueOrDefault(filter.PropertyId)?.GetValueOrDefault(id);
                                return values?.Any(v => psh.IsMatch(v, filter.Operation, filter.DbValue)) ??
                                       psh.IsMatch(null, filter.Operation, filter.DbValue);
                            }).ToHashSet();
                        });
                    }
                }
            }

            if (group.Groups?.Any() == true)
            {
                foreach (var subGroup in group.Groups.Where(g => !g.Disabled))
                {
                    steps.Add(() => SearchResourceIds(subGroup, context));
                }
            }

            HashSet<int>? result = null;

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var ids = step();

                if (ids == null)
                {
                    if (group.Combinator == SearchCombinator.Or)
                    {
                        break;
                    }
                    else
                    {
                        // do nothing
                    }
                }
                else
                {
                    if (!ids.Any())
                    {
                        if (group.Combinator == SearchCombinator.And)
                        {
                            return [];
                        }
                        else
                        {
                            if (index == steps.Count - 1 && result == null)
                            {
                                return [];
                            }
                            else
                            {
                                // do nothing
                            }
                        }
                    }
                    else
                    {
                        if (result == null)
                        {
                            result = ids;
                        }
                        else
                        {
                            if (group.Combinator == SearchCombinator.Or)
                            {
                                result.UnionWith(ids);
                            }
                            else
                            {
                                result.IntersectWith(ids);
                            }
                        }
                    }
                }
            }

            return result;
        }

        public async Task<Resource?> Get(int id, ResourceAdditionalItem additionalItems)
        {
            var resource = await _orm.GetByKey(id);
            if (resource == null)
            {
                return null;
            }

            return await ToDomainModel(resource, additionalItems);
        }

        public async Task<List<Resource>> GetByKeys(int[] ids,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var resources = (await _orm.GetByKeys(ids)) ?? [];
            var dtoList = await ToDomainModel(resources, additionalItems);
            return dtoList;
        }

        public async Task<Resource> ToDomainModel(Abstractions.Models.Db.ResourceDbModel resource,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            return (await ToDomainModel([resource], additionalItems)).FirstOrDefault()!;
        }

        public async Task<List<Resource>> ToDomainModel(Abstractions.Models.Db.ResourceDbModel[] resources,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var doList = resources.Select(r => r.ToDomainModel()!).ToList();
            var resourceIds = resources.Select(a => a.Id).ToList();
            foreach (var i in SpecificEnumUtils<ResourceAdditionalItem>.Values.OrderBy(x => x))
            {
                if (additionalItems.HasFlag(i))
                {
                    switch (i)
                    {
                        case ResourceAdditionalItem.ReservedProperties:
                        {
                            var reservedPropertyValueMap =
                                (await _reservedPropertyValueService.GetAll(x => resourceIds.Contains(x.ResourceId)))
                                .GroupBy(d => d.ResourceId).ToDictionary(d => d.Key, d => d.ToList());

                            var resourceRatingPropertyMap = reservedPropertyValueMap.ToDictionary(d => d.Key,
                                d =>
                                {
                                    var scopeRatings = d.Value.Where(x => x.Rating.HasValue)
                                        .Select(x => (x.Scope, Rating: x.Rating!.Value)).ToArray();
                                    if (scopeRatings.Any())
                                    {
                                        var p = new Resource.Property(null, StandardValueType.Decimal,
                                            StandardValueType.Decimal,
                                            scopeRatings.Select(s =>
                                                new Resource.Property.PropertyValue(s.Scope, s.Rating, s.Rating,
                                                    s.Rating)).ToList(), true);
                                        return p;
                                    }

                                    return null;
                                });

                            var resourceIntroductionPropertyMap = reservedPropertyValueMap.ToDictionary(d => d.Key,
                                d =>
                                {
                                    var scopeIntroductions = d.Value.Where(x => !string.IsNullOrEmpty(x.Introduction))
                                        .Select(x => (x.Scope, Introduction: x.Introduction!)).ToArray();
                                    if (scopeIntroductions.Any())
                                    {
                                        var p = new Resource.Property(null, StandardValueType.String,
                                            StandardValueType.String,
                                            scopeIntroductions.Select(s =>
                                                new Resource.Property.PropertyValue(s.Scope, s.Introduction,
                                                    s.Introduction,
                                                    s.Introduction)).ToList(), true);
                                        return p;
                                    }

                                    return null;
                                });

                            var reservedPropertyMap =
                                (await _propertyService.GetProperties(PropertyPool.Reserved)).ToDictionary(d => d.Id,
                                    d => d);

                            foreach (var r in doList)
                            {
                                r.Properties ??= [];
                                var reservedProperties =
                                    r.Properties.GetOrAdd((int) PropertyPool.Reserved, () => []);
                                var dbReservedProperties = reservedPropertyValueMap.GetValueOrDefault(r.Id);
                                reservedProperties[(int) ResourceProperty.Rating] = new Resource.Property(
                                    reservedPropertyMap.GetValueOrDefault((int) ResourceProperty.Rating)?.Name,
                                    StandardValueType.Decimal,
                                    StandardValueType.Decimal,
                                    dbReservedProperties?.Select(s =>
                                        new Resource.Property.PropertyValue(s.Scope, s.Rating, s.Rating,
                                            s.Rating)).ToList(), true);
                                reservedProperties[(int) ResourceProperty.Introduction] = new Resource.Property(
                                    reservedPropertyMap.GetValueOrDefault((int) ResourceProperty.Introduction)?.Name,
                                    StandardValueType.String,
                                    StandardValueType.String,
                                    dbReservedProperties?.Select(s =>
                                        new Resource.Property.PropertyValue(s.Scope, s.Introduction, s.Introduction,
                                            s.Introduction)).ToList(), true);
                            }

                            SortPropertyValuesByScope(doList);

                            break;
                        }
                        case ResourceAdditionalItem.CustomProperties:
                        {
                            var categoryIds = resources.Select(r => r.CategoryId).ToHashSet();
                            // ResourceId - PropertyId - Values
                            var customPropertiesValuesMap = (await _customPropertyValueService.GetAll(
                                x => resourceIds.Contains(x.ResourceId), CustomPropertyValueAdditionalItem.None,
                                true)).GroupBy(x => x.ResourceId).ToDictionary(x => x.Key,
                                x => x.GroupBy(y => y.PropertyId).ToDictionary(y => y.Key, y => y.ToList()));
                            var categoryMap =
                                (await _categoryService.GetByKeys(categoryIds, CategoryAdditionalItem.CustomProperties))
                                .ToDictionary(d => d.Id, d => d);
                            var categoryIdCustomPropertyIdsMap = categoryMap.ToDictionary(d => d.Key,
                                d => d.Value.CustomProperties?.Select(x => x.Id).ToHashSet());

                            var propertyIdsOfNotEmptyProperties =
                                customPropertiesValuesMap.Values.SelectMany(x => x.Keys).ToHashSet();
                            var loadedPropertyIds = categoryMap.Values
                                .SelectMany(x => x.CustomProperties?.Select(y => y.Id) ?? []).ToHashSet();

                            var unknownPropertyIds =
                                propertyIdsOfNotEmptyProperties.Except(loadedPropertyIds).ToHashSet();

                            var propertyMap =
                                (await _customPropertyService.GetByKeys(unknownPropertyIds,
                                    CustomPropertyAdditionalItem.None)).ToDictionary(d => d.Id, d => d);
                            var loadedProperties = categoryMap.Values.SelectMany(x => x.CustomProperties ?? [])
                                .GroupBy(d => d.Id).Select(d => d.First());
                            foreach (var p in loadedProperties)
                            {
                                propertyMap[p.Id] = (p as CustomProperty)!;
                            }

                            foreach (var r in doList)
                            {
                                r.Properties ??= [];
                                var customProperties =
                                    r.Properties.GetOrAdd((int) PropertyPool.Custom, () => []);

                                var propertyIds = new List<int>();
                                if (categoryIdCustomPropertyIdsMap.TryGetValue(r.CategoryId,
                                        out var boundPropertyIds) && boundPropertyIds != null)
                                {
                                    propertyIds.AddRange(boundPropertyIds);
                                }

                                if (customPropertiesValuesMap.TryGetValue(r.Id, out var pValues))
                                {
                                    propertyIds.AddRange(pValues.Keys.Except(propertyIds));
                                }

                                foreach (var pId in propertyIds)
                                {
                                    var property = propertyMap.GetValueOrDefault(pId);
                                    if (property == null)
                                    {
                                        continue;
                                    }

                                    var values = pValues?.GetValueOrDefault(pId);
                                    var visible = boundPropertyIds?.Contains(pId) == true;

                                    var p = customProperties.GetOrAdd(pId,
                                        () => new Resource.Property(property.Name, property.Type.GetDbValueType(),
                                            property.Type.GetBizValueType(), [], visible));
                                    if (values != null)
                                    {
                                        p.Values ??= [];
                                        PropertyInternals.DescriptorMap.TryGetValue(property.Type, out var cpd);
                                        foreach (var v in values)
                                        {
                                            var bizValue = cpd?.GetBizValue(property.ToProperty(), v.Value) ?? v.Value;
                                            var pv = new Resource.Property.PropertyValue(v.Scope, v.Value, bizValue,
                                                bizValue);
                                            p.Values.Add(pv);
                                        }
                                    }
                                }
                            }

                            SortPropertyValuesByScope(doList);
                            foreach (var @do in doList)
                            {
                                var customPropertyValues =
                                    @do.Properties?.GetValueOrDefault((int) PropertyPool.Custom);
                                var found = false;
                                if (customPropertyValues != null)
                                {
                                    foreach (var (pId, pvs) in customPropertyValues)
                                    {
                                        if (found)
                                        {
                                            break;
                                        }

                                        if (propertyMap.GetValueOrDefault(pId)?.Type ==
                                            PropertyType.Attachment)
                                        {
                                            if (pvs.Values?.Any() == true)
                                            {
                                                foreach (var listString in pvs.Values.Select(v =>
                                                             v.Value as List<string>))
                                                {
                                                    var images = listString?.Where(x =>
                                                        x.InferMediaType() == MediaType.Image).ToArray();
                                                    if (images?.Any() == true)
                                                    {
                                                        @do.CoverPaths = images.ToList();
                                                        found = true;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            break;
                        }
                        case ResourceAdditionalItem.Alias:
                            break;
                        case ResourceAdditionalItem.None:
                            break;
                        case ResourceAdditionalItem.Category:
                        {
                            var categoryIds = resources.Select(r => r.CategoryId).Distinct().ToArray();
                            var categoryMap = (await _categoryService.GetAll(x => categoryIds.Contains(x.Id),
                                CategoryAdditionalItem.None)).ToDictionary(d => d.Id, d => d);
                            foreach (var r in doList)
                            {
                                r.Category = categoryMap.GetValueOrDefault(r.CategoryId);
                            }

                            break;
                        }
                        case ResourceAdditionalItem.HasChildren:
                        {
                            var children = await _orm.GetAll(a =>
                                a.ParentId.HasValue && resourceIds.Contains(a.ParentId.Value));
                            var parentIds = children.Select(a => a.ParentId!.Value).ToHashSet();
                            foreach (var r in doList)
                            {
                                r.HasChildren = parentIds.Contains(r.Id);
                            }

                            break;
                        }
                        case ResourceAdditionalItem.DisplayName:
                        {
                            var wrappers = (await _specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper))
                                .Select(x => (Left: x.Value1, Right: x.Value2!)).ToArray();
                            foreach (var resource in doList)
                            {
                                var tpl = resource.Category?.ResourceDisplayNameTemplate;
                                if (!string.IsNullOrEmpty(tpl))
                                {
                                    resource.DisplayName =
                                        _categoryService.BuildDisplayNameForResource(resource, tpl, wrappers);
                                }
                            }

                            break;
                        }
                        case ResourceAdditionalItem.All:
                            break;
                        case ResourceAdditionalItem.MediaLibraryName:
                        {
                            var mediaLibraryIds = doList.Select(d => d.MediaLibraryId).ToHashSet();
                            var mediaLibraryMap =
                                (await _mediaLibraryService.GetAll(x => mediaLibraryIds.Contains(x.Id))).ToDictionary(
                                    d => d.Id, d => d);
                            foreach (var resource in doList)
                            {
                                resource.MediaLibraryName =
                                    mediaLibraryMap.GetValueOrDefault(resource.MediaLibraryId)?.Name;
                            }

                            break;
                        }
                        case ResourceAdditionalItem.Cache:
                        {
                            var cacheMap = (await _orm.DbContext.ResourceCaches
                                .Where(x => resourceIds.Contains(x.ResourceId))
                                .ToListAsync()).ToDictionary(d => d.ResourceId, d => d);
                            foreach (var r in doList)
                            {
                                var cache = cacheMap.GetValueOrDefault(r.Id);
                                r.Cache = cache?.ToDomainModel();
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (additionalItems.HasFlag(ResourceAdditionalItem.Alias))
            {
                await ReplaceWithPreferredAlias(doList);
            }

            return doList;
        }

        private void SortPropertyValuesByScope(List<Resource> resources)
        {
            var scopePriorityMap = _optionsManager.Value.PropertyValueScopePriority.Cast<int>()
                .Select((x, i) => (Scope: x, Index: i)).ToDictionary(d => d.Scope, d => d.Index);
            foreach (var resource in resources)
            {
                if (resource.Properties != null)
                {
                    foreach (var (_, ps) in resource.Properties)
                    {
                        foreach (var p in ps.Values)
                        {
                            p.Values?.Sort((a, b) =>
                                scopePriorityMap.GetValueOrDefault(a.Scope, int.MaxValue) -
                                scopePriorityMap.GetValueOrDefault(b.Scope, int.MaxValue));
                        }
                    }
                }
            }
        }

        public async Task<List<Abstractions.Models.Db.ResourceDbModel>> GetAllDbModels(
            Expression<Func<Abstractions.Models.Db.ResourceDbModel, bool>>? selector = null,
            bool returnCopy = true)
        {
            return await _orm.GetAll(selector, returnCopy);
        }

        /// <summary>
        /// <para>All properties of resources will be saved, including null values.</para>
        /// <para>Parents will be saved too, so be sure the properties of parent are fulfilled.</para>
        /// </summary>
        /// <param name="resources"></param>
        /// <returns></returns>
        public async Task<List<DataChangeViewModel>> AddOrPutRange(List<Resource> resources)
        {
            var resourceDtoMap = resources.GroupBy(d => d.Path).ToDictionary(d => d.Key, d => d.First());

            var parents = resources.Select(a => a.Parent).Where(a => a != null).GroupBy(a => a!.Path)
                .Select(a => a.FirstOrDefault()).ToList();
            if (parents.Any())
            {
                await AddOrPutRange(parents!);
            }

            await _addOrUpdateLock.WaitAsync();
            try
            {
                // Resource
                var dbResources = resources.Select(a => a.ToDbModel()!).ToList();
                var existedResources = dbResources.Where(a => a.Id > 0).ToList();
                var newResources = dbResources.Except(existedResources).ToList();
                await _orm.UpdateRange(existedResources);
                dbResources = (await _orm.AddRange(newResources)).Data!.Concat(existedResources).ToList();
                dbResources.ForEach(a => { resourceDtoMap[a.Path].Id = a.Id; });

                // Alias
                await _aliasService.SaveByResources(resources);

                // Built-in properties
                await _reservedPropertyValueService.PutByResources(resources);

                // Custom properties
                await _customPropertyValueService.SaveByResources(resources);

                return [new DataChangeViewModel("Resource", newResources.Count, existedResources.Count, 0)];
            }
            finally
            {
                _addOrUpdateLock.Release();
            }
        }

        private async Task ReplaceWithPreferredAlias(IReadOnlyCollection<Resource> resources)
        {
            var bizValuePropertyValuePairs =
                new List<(object BizValue, StandardValueType BizValueType, Resource.Property.PropertyValue PropertyValue
                    )>();
            foreach (var r in resources)
            {
                if (r.Properties == null) continue;
                foreach (var p in r.Properties.Values.SelectMany(ps => ps.Values))
                {
                    if (p.Values == null) continue;
                    foreach (var v in p.Values)
                    {
                        if (v.BizValue != null)
                        {
                            bizValuePropertyValuePairs.Add((v.BizValue, p.BizValueType, v));
                        }
                    }
                }
            }

            var aliasAppliedBizValues = await _aliasService.GetAliasAppliedValues(bizValuePropertyValuePairs
                .Select(b => (b.BizValue, b.BizValueType)).ToList());

            for (var i = 0; i < bizValuePropertyValuePairs.Count; i++)
            {
                bizValuePropertyValuePairs[i].PropertyValue.AliasAppliedBizValue = aliasAppliedBizValues[i];
            }
        }

        private static string? _findCoverInAttachmentProperty(Resource.Property pvs)
        {
            if (pvs.Values != null)
            {
                foreach (var value in pvs.Values)
                {
                    if (value.BizValue is List<string> list)
                    {
                        foreach (var l in list.Where(p => p.InferMediaType() == MediaType.Image))
                        {
                            if (File.Exists(l))
                            {
                                return l;
                            }
                        }
                    }
                }
            }

            return null;
        }

        public async Task<string?> DiscoverAndCacheCover(int id, CancellationToken ct)
        {
            var resource = await _orm.GetByKey(id, false);
            var coverSelectOrder =
                (await _categoryService.Get(resource.CategoryId))?.CoverSelectionOrder ??
                CoverSelectOrder.FilenameAscending;
            var coverDiscoverResult =
                await _coverDiscoverer.Discover(resource.Path, coverSelectOrder, false,
                    ct);

            if (coverDiscoverResult != null)
            {
                var image = await coverDiscoverResult.LoadByImageSharp(ct);

                var pathWithoutExt = Path
                    .Combine(_fileManager.BuildAbsolutePath("cache", "cover"),
                        resource.Id.ToString())
                    .StandardizePath()!;
                var path = await image.SaveAsThumbnail(pathWithoutExt, ct);
                var cache = await _resourceCacheOrm.GetByKey(id, true);
                var isNewCache = false;
                if (cache == null)
                {
                    cache = new ResourceCacheDbModel {ResourceId = id};
                    isNewCache = true;
                }

                var serializedCoverPaths =
                    new ListStringValueBuilder([path]).Value!
                        .SerializeAsStandardValue(StandardValueType.ListString);
                if (cache.CoverPaths != serializedCoverPaths ||
                    !cache.CachedTypes.HasFlag(ResourceCacheType.Covers))
                {
                    cache.CoverPaths = serializedCoverPaths;
                    cache.CachedTypes |= ResourceCacheType.Covers;
                    if (isNewCache)
                    {
                        await _resourceCacheOrm.Add(cache);
                    }
                    else
                    {
                        await _resourceCacheOrm.Update(cache);
                    }
                }
            }

            return null;
        }

        public async Task<string[]> GetPlayableFiles(int id, CancellationToken ct)
        {
            var r = await Get(id, ResourceAdditionalItem.None);
            if (r != null)
            {
                var selector = await _categoryService.GetFirstComponent<IPlayableFileSelector>(r.CategoryId,
                    ComponentType.PlayableFileSelector);
                if (selector.Data != null)
                {
                    var files = await selector.Data.GetPlayableFiles(r.Path, ct);
                    return files.Select(f => f.StandardizePath()!).ToArray();
                }
            }

            return null;
        }

        public async Task<bool> Any(Func<Abstractions.Models.Db.ResourceDbModel, bool>? selector = null)
        {
            return await _orm.Any(selector);
        }

        public async Task<List<Abstractions.Models.Db.ResourceDbModel>> AddAll(
            IEnumerable<Abstractions.Models.Db.ResourceDbModel> resources)
        {
            return (await _orm.AddRange(resources.ToList())).Data;
        }

        public async Task PrepareCache(Action<int> onProgressChange, CancellationToken ct)
        {
            var caches = await _resourceCacheOrm.GetAll();
            var cachedResourceIds = caches.Select(c => c.ResourceId).ToList();
            var resourceIds = (await GetAllDbModels(null, false)).Select(r => r.Id).ToList();
            var newCaches = resourceIds.Except(cachedResourceIds).Select(x => new ResourceCacheDbModel
            {
                ResourceId = x
            }).ToList();
            await _resourceCacheOrm.AddRange(newCaches);
            _resourceCacheOrm.DbContext.DetachAll(caches.Concat(newCaches));

            var fullCacheType = (ResourceCacheType) SpecificEnumUtils<ResourceCacheType>.Values.Sum(x => (int) x);
            var percentage = 0m;
            var itemPercentage = resourceIds.Count == 0 ? 0 : (100m / resourceIds.Count);
            while (true)
            {
                var cache = await _resourceCacheOrm.GetFirstOrDefault(x => x.CachedTypes != fullCacheType);
                if (cache != null)
                {
                    var cached = false;
                    foreach (var cacheType in SpecificEnumUtils<ResourceCacheType>.Values)
                    {
                        if (!cache.CachedTypes.HasFlag(cacheType))
                        {
                            var resource = await Get(cache.ResourceId, ResourceAdditionalItem.None);
                            if (resource != null)
                            {
                                switch (cacheType)
                                {
                                    case ResourceCacheType.Covers:
                                    {
                                        var coverPath = await DiscoverAndCacheCover(resource.Id, ct);
                                        cache.CoverPaths = coverPath.IsNotEmpty()
                                            ? new ListStringValueBuilder([coverPath]).Value?.SerializeAsStandardValue(
                                                StandardValueType.ListString)
                                            : null;
                                        cache.CachedTypes |= ResourceCacheType.Covers;
                                        break;
                                    }
                                    case ResourceCacheType.PlayableFiles:
                                    {
                                        var pfs = (await _categoryService.GetFirstComponent<IPlayableFileSelector>(
                                            resource.CategoryId, ComponentType.PlayableFileSelector)).Data;
                                        if (pfs != null)
                                        {
                                            var playableFiles =
                                                (await pfs.GetPlayableFiles(resource.Path, CancellationToken.None))
                                                .Select(f => f.StandardizePath()!).ToList();
                                            var trimmedPlayableFiles = playableFiles
                                                .GroupBy(d => $"{Path.GetDirectoryName(d)}-{Path.GetExtension(d)}")
                                                .SelectMany(x =>
                                                    x.Take(InternalOptions.MaxPlayableFilesPerTypeAndSubDir)).ToList();
                                            cache.HasMorePlayableFiles = trimmedPlayableFiles.Any();
                                            cache.HasMorePlayableFiles =
                                                trimmedPlayableFiles.Count < playableFiles.Count;
                                        }
                                        else
                                        {
                                            cache.HasMorePlayableFiles = false;
                                            cache.PlayableFilePaths = null;
                                        }

                                        cache.CachedTypes |= ResourceCacheType.PlayableFiles;

                                        break;
                                    }
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }

                                cached = true;
                            }
                        }
                    }

                    if (cached)
                    {
                        await _resourceCacheOrm.Update(cache);
                    }

                    var newPercentage = percentage + itemPercentage;
                    if ((int) newPercentage != (int) percentage)
                    {
                        onProgressChange((int) newPercentage);
                    }

                    percentage = newPercentage;
                }
                else
                {
                    break;
                }
            }

            onProgressChange(100);
        }

        public async Task Transfer(ResourceTransferInputModel model)
        {
            var fromIds = model.Items.Select(i => i.FromId).ToList();
            var toIds = model.Items.Select(i => i.ToId).ToList();

            if (toIds.GroupBy(x => x).Any(x => x.Count() > 1))
            {
                throw new Exception("Can not transfer multiple resources into a single resource.");
            }

            if (fromIds.Intersect(toIds).Any())
            {
                throw new Exception("Can not transfer a resource into itself directly or indirectly.");
            }

            var resourceIds = model.Items.Select(i => i.FromId).Concat(model.Items.Select(i => i.ToId)).ToHashSet();
            var resourceMap =
                (await GetAll(x => resourceIds.Contains(x.Id), ResourceAdditionalItem.All)).ToDictionary(d => d.Id,
                    d => d);

            var changedResources = new List<Resource>();
            var discardResourceIds = new List<int>();

            foreach (var item in model.Items)
            {
                var fromResource = resourceMap.GetValueOrDefault(item.FromId);
                var toResource = resourceMap.GetValueOrDefault(item.ToId);

                if (fromResource == null || toResource == null)
                {
                    continue;
                }

                var keepMediaLibrary = item.KeepMediaLibrary || model.KeepMediaLibraryForAll;
                var deleteSource = item.DeleteSourceResource || model.DeleteAllSourceResources;

                var resource = fromResource with { };
                resource.Id = toResource.Id;
                resource.CreatedAt = toResource.CreatedAt;
                resource.FileCreatedAt = toResource.FileCreatedAt;
                resource.FileModifiedAt = toResource.FileModifiedAt;
                resource.UpdatedAt = DateTime.Now;
                if (keepMediaLibrary)
                {
                    resource.CategoryId = toResource.CategoryId;
                    resource.MediaLibraryId = toResource.MediaLibraryId;
                }

                changedResources.Add(resource);
                if (deleteSource)
                {
                    discardResourceIds.Add(fromResource.Id);
                }
            }

            await AddOrPutRange(changedResources);
            await DeleteByKeys(discardResourceIds.ToArray());
        }

        public async Task<BaseResponse> PutPropertyValue(int resourceId, ResourcePropertyValuePutInputModel model)
        {
            if (model.IsCustomProperty)
            {
                var value = (await _customPropertyValueService.GetAllDbModels(x =>
                    x.ResourceId == resourceId && x.PropertyId == model.PropertyId &&
                    x.Scope == (int) PropertyValueScope.Manual)).FirstOrDefault();
                if (value == null)
                {
                    value = new CustomPropertyValueDbModel()
                    {
                        ResourceId = resourceId,
                        PropertyId = model.PropertyId,
                        Value = model.Value,
                        Scope = (int) PropertyValueScope.Manual
                    };
                    return await _customPropertyValueService.AddDbModel(value);
                }
                else
                {
                    value.Value = model.Value;
                    return await _customPropertyValueService.UpdateDbModel(value);
                }
            }
            else
            {
                var property = (ResourceProperty) model.PropertyId;
                switch (property)
                {
                    // case ResourceProperty.RootPath:
                    //     break;
                    // case ResourceProperty.ParentResource:
                    //     break;
                    // case ResourceProperty.Resource:
                    //     break;
                    case ResourceProperty.Introduction:
                    case ResourceProperty.Rating:
                    {
                        var scopeValue = await _reservedPropertyValueService.GetFirst(x =>
                            x.ResourceId == resourceId && x.Scope == (int) PropertyValueScope.Manual);
                        var noValue = scopeValue == null;
                        scopeValue ??= new ReservedPropertyValue
                        {
                            ResourceId = resourceId,
                            Scope = (int) PropertyValueScope.Manual
                        };

                        if (property == ResourceProperty.Introduction)
                        {
                            scopeValue.Introduction =
                                model.Value?.DeserializeAsStandardValue<string>(StandardValueType.String);
                        }
                        else
                        {
                            scopeValue.Rating =
                                model.Value?.DeserializeAsStandardValue<decimal>(StandardValueType.Decimal);
                        }

                        return noValue
                            ? await _reservedPropertyValueService.Add(scopeValue)
                            : await _reservedPropertyValueService.Update(scopeValue);
                    }
                    // case ResourceProperty.CustomProperty:
                    //     break;
                    // case ResourceProperty.FileName:
                    //     break;
                    // case ResourceProperty.DirectoryPath:
                    //     break;
                    // case ResourceProperty.CreatedAt:
                    //     break;
                    // case ResourceProperty.FileCreatedAt:
                    //     break;
                    // case ResourceProperty.FileModifiedAt:
                    //     break;
                    // case ResourceProperty.Category:
                    //     break;
                    // case ResourceProperty.MediaLibrary:
                    //     break;
                    default:
                        return BaseResponseBuilder.BuildBadRequest("Unknown property");
                }
            }
        }

        // public async Task<List<Resource>> GetNfoGenerationNeededResources(int[] resourceIds)
        // {
        //     var categories = await _categoryService.GetAll(t => t.GenerateNfo, true);
        //     var categoryIds = categories.Select(t => t.Id).ToHashSet();
        //     var resources = await GetByKeys(resourceIds);
        //     return resources.Where(t => categoryIds.Contains(t.CategoryId)).ToList();
        // }

        // public async Task SaveNfo(Resource resource, bool overwrite, CancellationToken ct = new())
        // {
        //     var nfoFullname = ResourceNfoService.GetFullname(resource);
        //     if (!resource.EnoughToGenerateNfo())
        //     {
        //     	if (File.Exists(nfoFullname))
        //     	{
        //     		File.Delete(nfoFullname);
        //     	}
        //     
        //     	return;
        //     }
        //     
        //     if (!overwrite)
        //     {
        //     	if (File.Exists(nfoFullname))
        //     	{
        //     		return;
        //     	}
        //     }
        //     
        //     var directory = Path.GetDirectoryName(nfoFullname);
        //     if (!Directory.Exists(directory))
        //     {
        //     	return;
        //     }
        //     
        //     var xml = ResourceNfoService.Serialize(resource);
        //     await using (var fs = new FileStream(nfoFullname, FileMode.OpenOrCreate))
        //     {
        //     	fs.Seek(0, SeekOrigin.Begin);
        //     	await using (TextWriter tw = new StreamWriter(fs, Encoding.UTF8, 1024, true))
        //     	{
        //     		await tw.WriteAsync(xml);
        //     	}
        //     
        //     	fs.SetLength(fs.Position);
        //     }
        //     
        //     File.SetAttributes(nfoFullname, File.GetAttributes(nfoFullname) | FileAttributes.Hidden);
        // }

        // private const string NfoGenerationTaskName = $"{nameof(ResourceService)}:NfoGeneration";
        //
        // public async Task TryToGenerateNfoInBackground()
        // {
        //     if (!_backgroundTaskManager.IsRunningByName(NfoGenerationTaskName))
        //     {
        //         var categories = await _categoryService.GetAll(t => t.GenerateNfo, true);
        //         if (categories.Any())
        //         {
        //             _backgroundTaskHelper.RunInNewScope<ResourceService>(NfoGenerationTaskName,
        //                 async (service, task) => await service.StartGeneratingNfo(task));
        //         }
        //     }
        // }

        // public async Task RunBatchSaveNfoBackgroundTask(int[] resourceIds, string backgroundTaskName, bool overwrite)
        // {
        // var resources = await GetNfoGenerationNeededResources(resourceIds);
        // if (resources.Any())
        // {
        // 	_backgroundTaskHelper.RunInNewScope<ResourceService>(backgroundTaskName, async (service, task) =>
        // 	{
        // 		for (var i = 0; i < resources.Count; i++)
        // 		{
        // 			var resource = resources[i];
        // 			await service.SaveNfo(resource, overwrite, task.Cts.Token);
        // 			task.Percentage = (i + 1) * 100 / resources.Count;
        // 		}
        //
        // 		return BaseResponseBuilder.Ok;
        // 	}, BackgroundTaskLevel.Critical);
        // }
        // }

        // public async Task<BaseResponse> StartGeneratingNfo(BackgroundTask task)
        // {
        //     var categories = await _categoryService.GetAll();
        //     var totalCount = 0;
        //     var doneCount = 0;
        //     foreach (var c in categories)
        //     {
        //         task.Cts.Token.ThrowIfCancellationRequested();
        //         var category = await _categoryService.GetByKey(c.Id);
        //         if (category.GenerateNfo)
        //         {
        //             var resources = await GetAll(r => r.CategoryId == c.Id, ResourceAdditionalItem.All);
        //             totalCount += resources.Count;
        //             foreach (var r in resources)
        //             {
        //                 task.Cts.Token.ThrowIfCancellationRequested();
        //                 await SaveNfo(r, false, task.Cts.Token);
        //                 doneCount++;
        //                 task.Percentage = doneCount * 100 / totalCount;
        //             }
        //         }
        //     }
        //
        //     await _optionsManager.SaveAsync(t => t.LastNfoGenerationDt = DateTime.Now);
        //     return BaseResponseBuilder.Ok;
        // }

        public async Task PopulateStatistics(DashboardStatistics statistics)
        {
            var categories = (await _categoryService.GetAll()).ToDictionary(a => a.Id, a => a.Name);
            var allEntities = await GetAllDbModels();

            var allEntitiesMap = allEntities.ToDictionary(a => a.Id, a => a);

            var totalCounts = allEntities.GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .ToList();

            statistics.CategoryResourceCounts = totalCounts;

            var today = DateTime.Today;
            var todayCounts = allEntities.Where(a => a.CreateDt >= today).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            statistics.TodayAddedCategoryResourceCounts = todayCounts;

            var weekdayDiff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var monday = today.AddDays(-1 * weekdayDiff);
            var thisWeekCounts = allEntities.Where(a => a.CreateDt >= monday).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            statistics.ThisWeekAddedCategoryResourceCounts = thisWeekCounts;

            var thisMonth = today.GetFirstDayOfMonth();
            var thisMonthCounts = allEntities.Where(a => a.CreateDt >= thisMonth).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            statistics.ThisMonthAddedCategoryResourceCounts = thisMonthCounts;

            // 12 weeks added counts trending
            {
                var total = allEntities.Count;
                for (var i = 0; i < 12; i++)
                {
                    var offset = -i * 7;
                    var weekStart = today.AddDays(offset - weekdayDiff);
                    var weekEnd = weekStart.AddDays(7);
                    var count = allEntities.Count(a => a.CreateDt >= weekStart && a.CreateDt < weekEnd);
                    statistics.ResourceTrending.Add(new DashboardStatistics.WeekCount(-i, total));
                    total -= count;
                }

                statistics.ResourceTrending.Reverse();
            }

            const int maxPropertyCount = 30;

            // Properties
            {
                var propertyCountList = new List<DashboardStatistics.PropertyAndCount>();
            }
        }

        // public async Task<BaseResponse> Patch(int id, ResourceUpdateRequestModel model)
        // {
        //     throw new NotImplementedException();
        // }

        public async Task<BaseResponse> Play(int resourceId, string file)
        {
            var playerRsp = await _categoryService.GetFirstComponent<IPlayer>(resourceId, ComponentType.Player);
            if (playerRsp.Data == null)
            {
                return playerRsp;
            }

            await playerRsp.Data.Play(file);
            return BaseResponseBuilder.Ok;
        }

        public async Task DeleteUnknown()
        {
            var unknownResources = await GetUnknownResourceDbModels();

            if (unknownResources.Any())
            {
                var unknownResourceIds = unknownResources.Select(r => r.Id).ToList();
                await DeleteRelatedData(unknownResourceIds);
                await _orm.RemoveRange(unknownResources);
            }
        }

        public async Task<List<Resource>> GetUnknownResources()
        {
            var dbModels = await GetUnknownResourceDbModels();
            var domainModels = await ToDomainModel(dbModels.ToArray(), ResourceAdditionalItem.All);
            return domainModels;
        }

        private async Task<List<Abstractions.Models.Db.ResourceDbModel>> GetUnknownResourceDbModels()
        {
            var categories = await _categoryService.GetAll();
            var mediaLibraries = await _mediaLibraryService.GetAll();

            var categoryIds = categories.Select(c => c.Id).ToHashSet();
            var mediaLibraryIds = mediaLibraries.Select(m => m.Id).ToHashSet();

            var unknownResources = await GetAllDbModels(x =>
                !categoryIds.Contains(x.CategoryId) || !mediaLibraryIds.Contains(x.MediaLibraryId));
            return unknownResources;
        }

        public async Task<int> GetUnknownCount()
        {
            var unknownResources = await GetUnknownResourceDbModels();
            return unknownResources.Count;
        }

        public async Task<BaseResponse> ChangeMediaLibraryAndPath(int id, int mediaLibraryId, string path)
        {
            var resource = await _orm.GetByKey(id);
            if (resource == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            if (resource.MediaLibraryId == mediaLibraryId)
            {
                return BaseResponseBuilder.Ok;
            }

            var library = await _mediaLibraryService.Get(mediaLibraryId, MediaLibraryAdditionalItem.None);

            if (library == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            resource.CategoryId = library.CategoryId;
            resource.MediaLibraryId = library.Id;
            resource.Path = path.StandardizePath()!;

            await _orm.Update(resource);

            return BaseResponseBuilder.Ok;
        }

        public async Task Pin(int id, bool pin)
        {
            await _orm.UpdateByKey(id, r => { r.Pinned = pin; });
        }

        private async Task DeleteRelatedData(List<int> ids)
        {
            await _customPropertyValueService.RemoveAll(x => ids.Contains(x.ResourceId));
        }
    }
}