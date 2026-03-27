using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Extensions;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CliWrap;
using DotNext.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StackExchange.Profiling;
using static Bakabase.Abstractions.Models.View.ResourceDisplayNameViewModel;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ResourceService : ScopedService, IResourceService
    {
        private readonly FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int> _orm;
        private readonly FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> _resourceCacheOrm;
        private readonly ISpecialTextService _specialTextService;
        private IMediaLibraryV2Service MediaLibraryV2Service => GetRequiredService<IMediaLibraryV2Service>();
        private IResourceProfileService ResourceProfileService => GetRequiredService<IResourceProfileService>();
        private IMediaLibraryResourceMappingService MediaLibraryResourceMappingService => GetRequiredService<IMediaLibraryResourceMappingService>();
        private IResourceDataChangeEventPublisher ResourceDataChangeEventPublisher => GetRequiredService<IResourceDataChangeEventPublisher>();
        private IResourceSearchIndexService ResourceSearchIndexService => GetRequiredService<IResourceSearchIndexService>();
        private IEnumerable<IResourceResolver> ResourceResolvers => GetRequiredService<IEnumerable<IResourceResolver>>();
        private IResourceSourceLinkService ResourceSourceLinkService => GetRequiredService<IResourceSourceLinkService>();
        private readonly ILogger<ResourceService> _logger;
        private readonly SemaphoreSlim _addOrUpdateLock = new(1, 1);
        private readonly IBOptionsManager<ResourceOptions> _optionsManager;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IAliasService _aliasService;
        private readonly IReservedPropertyValueService _reservedPropertyValueService;
        private readonly ICoverDiscoverer _coverDiscoverer;
        private readonly IPropertyService _propertyService;
        private readonly IFileManager _fileManager;
        private readonly IPlayHistoryService _playHistoryService;
        private readonly ISystemPlayer _systemPlayer;
        private readonly IPropertyLocalizer _propertyLocalizer;
        private readonly LogService _logService;
        private readonly IResourceLegacySearchService _legacySearchService;
        private readonly IPrepareCacheTrigger _prepareCacheTrigger;
        private readonly IBOptions<InsideWorld.Models.Configs.UIOptions> _uiOptions;

        /// <summary>
        /// Gets the ParallelOptions configured with the user's max parallelism setting.
        /// </summary>
        private ParallelOptions GetParallelOptions(CancellationToken ct = default) => new()
        {
            MaxDegreeOfParallelism = _appOptionsManager.Value.EffectiveMaxParallelism,
            CancellationToken = ct
        };

        public ResourceService(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IAliasService aliasService, 
            ILogger<ResourceService> logger,
            ICustomPropertyService customPropertyService, ICustomPropertyValueService customPropertyValueService,
            IReservedPropertyValueService reservedPropertyValueService,
            ICoverDiscoverer coverDiscoverer, IBOptionsManager<ResourceOptions> optionsManager,
            IBOptionsManager<AppOptions> appOptionsManager,
            IPropertyService propertyService,
            FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int> resourceCacheOrm,
            FullMemoryCacheResourceService<BakabaseDbContext, Abstractions.Models.Db.ResourceDbModel, int> orm,
            IFileManager fileManager, IPlayHistoryService playHistoryService,
            ISystemPlayer systemPlayer, IPropertyLocalizer propertyLocalizer, LogService logService,
            IResourceLegacySearchService legacySearchService,
            IPrepareCacheTrigger prepareCacheTrigger,
            IBOptions<InsideWorld.Models.Configs.UIOptions> uiOptions) : base(serviceProvider)
        {
            _specialTextService = specialTextService;
            _aliasService = aliasService;
            _logger = logger;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _reservedPropertyValueService = reservedPropertyValueService;
            _coverDiscoverer = coverDiscoverer;
            _optionsManager = optionsManager;
            _appOptionsManager = appOptionsManager;
            _propertyService = propertyService;
            _resourceCacheOrm = resourceCacheOrm;
            _fileManager = fileManager;
            _playHistoryService = playHistoryService;
            _systemPlayer = systemPlayer;
            _propertyLocalizer = propertyLocalizer;
            _logService = logService;
            _legacySearchService = legacySearchService;
            _prepareCacheTrigger = prepareCacheTrigger;
            _uiOptions = uiOptions;
            _orm = orm;
        }

        public BakabaseDbContext DbContext => _orm.DbContext;

        public async Task DeleteByKeys(int[] ids, bool deleteFiles = false)
        {
            if (deleteFiles)
            {
                var resources = await _orm.GetByKeys(ids);
                foreach (var resource in resources)
                {
                    if (!string.IsNullOrEmpty(resource.Path))
                    {
                        try
                        {
                            if (resource.IsFile)
                            {
                                if (File.Exists(resource.Path))
                                {
                                    File.Delete(resource.Path);
                                }
                            }
                            else
                            {
                                if (Directory.Exists(resource.Path))
                                {
                                    Directory.Delete(resource.Path, true);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Best-effort file deletion: log and continue
                        }
                    }
                }
            }

            await DeleteRelatedData(ids.ToList());
            await _orm.RemoveByKeys(ids);

            // Publish resource removed event (triggers index updates via event subscription)
            ResourceDataChangeEventPublisher.PublishResourcesRemoved(ids);
        }

        public async Task<List<Resource>> GetAll(
            Expression<Func<Abstractions.Models.Db.ResourceDbModel, bool>>? selector = null,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var sw = Stopwatch.StartNew();
            var data = await _orm.GetAll(selector, false);
            _logger.LogInformation($"[GetAll Perf] _orm.GetAll: {sw.ElapsedMilliseconds}ms, Count: {data.Count}");

            sw.Restart();
            var dtoList = await ToDomainModel(data.ToArray(), additionalItems);
            _logger.LogInformation($"[GetAll Perf] ToDomainModel: {sw.ElapsedMilliseconds}ms");

            return dtoList;
        }

        public async Task<SearchResponse<Resource>> Search(ResourceSearch model,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.All,
            bool asNoTracking = true)
        {
            var totalSw = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();

            HashSet<int>? resourceIds = null;

            // Try to use inverted index first for property filters
            if (model.Group != null && !model.Group.Disabled)
            {
                using (MiniProfiler.Current.Step("IndexLookup"))
                {
                    resourceIds = await ResourceSearchIndexService.SearchResourceIdsAsync(model.Group);
                }
                _logger.LogInformation(
                    "[Search Perf] IndexLookup: {Ms}ms, Matched: {Count}",
                    sw.ElapsedMilliseconds,
                    resourceIds?.Count.ToString() ?? "all (fallback)");

                // If index returned empty, no matches
                if (resourceIds is { Count: 0 })
                {
                    _logger.LogInformation("[Search Perf] Total: {Ms}ms (no matches from index)", totalSw.ElapsedMilliseconds);
                    return model.BuildResponse(new List<Resource>(), 0);
                }
            }

            // If index is not ready or returned null, fallback to legacy search
            if (resourceIds == null && model.Group != null && !model.Group.Disabled)
            {
                sw.Restart();
                List<Resource> allResources;
                using (MiniProfiler.Current.Step("GetAll (fallback)"))
                {
                    allResources = await GetAll();
                }
                _logger.LogInformation("[Search Perf] GetAll (fallback): {Ms}ms, Count: {Count}", sw.ElapsedMilliseconds, allResources.Count);

                sw.Restart();
                using (MiniProfiler.Current.Step("LegacySearch (fallback)"))
                {
                    resourceIds = await _legacySearchService.SearchAsync(allResources, model.Group, model.Tags?.ToList());
                }
                _logger.LogInformation("[Search Perf] LegacySearch (fallback): {Ms}ms, MatchedCount: {Count}",
                    sw.ElapsedMilliseconds, resourceIds?.Count ?? allResources.Count);
            }

            sw.Restart();
            SearchResponse<ResourceDbModel> resources;
            using (MiniProfiler.Current.Step("OrmSearch"))
            {
                Expression<Func<ResourceDbModel, bool>>? exp = resourceIds == null
                    ? null
                    : r => resourceIds.Contains(r.Id);

                ResourceTag? tagsValue = model.Tags?.Any() == true ? (ResourceTag)model.Tags.Sum(x => (int)x) : null;
                if (tagsValue.HasValue)
                {
                    exp = exp == null
                        ? r => (r.Tags & tagsValue.Value) == tagsValue.Value
                        : exp.And(r => (r.Tags & tagsValue.Value) == tagsValue.Value);
                }

                var ordersForSearch = model.Orders.BuildForSearch();
                resources = await _orm.Search(exp?.Compile(), model.PageIndex, model.PageSize,
                    ordersForSearch,
                    asNoTracking);
            }
            _logger.LogInformation("[Search Perf] OrmSearch: {Ms}ms, ResultCount: {ResultCount}, TotalCount: {TotalCount}",
                sw.ElapsedMilliseconds, resources.Data?.Count ?? 0, resources.TotalCount);

            sw.Restart();
            // Use the specified additionalItems parameter
            var dtoList = await ToDomainModel(resources.Data!.ToArray(), additionalItems, asNoTracking);
            _logger.LogInformation("[Search Perf] ToDomainModel: {Ms}ms", sw.ElapsedMilliseconds);

            _logger.LogInformation("[Search Perf] Total: {Ms}ms", totalSw.ElapsedMilliseconds);

            return model.BuildResponse(dtoList, resources.TotalCount);
        }

        public async Task<int[]> GetAllIds(ResourceSearch model)
        {
            HashSet<int>? resourceIds = null;

            // Try to use inverted index first for property filters
            if (model.Group != null && !model.Group.Disabled)
            {
                resourceIds = await ResourceSearchIndexService.SearchResourceIdsAsync(model.Group);

                // If index returned empty, no matches
                if (resourceIds is { Count: 0 })
                {
                    return [];
                }
            }

            // If index is not ready or returned null, fallback to legacy search
            if (resourceIds == null && model.Group != null && !model.Group.Disabled)
            {
                var allResources = await GetAll();
                resourceIds = await _legacySearchService.SearchAsync(allResources, model.Group, model.Tags?.ToList());
            }

            // Apply ResourceTag filter
            ResourceTag? tagsValue = model.Tags?.Any() == true ? (ResourceTag)model.Tags.Sum(x => (int)x) : null;
            if (tagsValue.HasValue && resourceIds != null)
            {
                var dbModels = await _orm.GetAll(r => resourceIds.Contains(r.Id), asNoTracking: false);
                resourceIds = dbModels
                    .Where(r => (r.Tags & tagsValue.Value) == tagsValue.Value)
                    .Select(r => r.Id)
                    .ToHashSet();
            }

            // If no filter applied, return all resource IDs
            if (resourceIds == null)
            {
                return await GetAllResourceIds();
            }

            return resourceIds.ToArray();
        }

        public async Task<int[]> GetAllResourceIds()
        {
            return (await _orm.GetAll(null, false)).Select(r => r.Id).ToArray();
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

        private async Task<Resource> ToDomainModel(ResourceDbModel resource,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None,
            bool asNoTracking = true)
        {
            return (await ToDomainModel([resource], additionalItems, asNoTracking)).FirstOrDefault()!;
        }

        private async Task<List<Resource>> ToDomainModel(ResourceDbModel[] resources,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None,
            bool asNoTracking = true)
        {
            using (MiniProfiler.Current.Step($"ToDomainModel ({resources.Length} resources)"))
            {
                List<Resource> doList;
                using (MiniProfiler.Current.Step("Basic conversion"))
                {
                    doList = resources.Select(r => r.ToDomainModel()).ToList();
                }

                var resourceIds = resources.Select(a => a.Id).ToList();

                // Always populate SourceLinks (required for source icon display)
                using (MiniProfiler.Current.Step("SourceLinks (mandatory)"))
                {
                    var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();
                    var linksGrouped = await sourceLinkService.GetByResourceIdsGrouped(resourceIds.ToArray());
                    foreach (var r in doList)
                    {
                        r.SourceLinks = linksGrouped.GetValueOrDefault(r.Id) ?? [];
                    }
                }

                // Pre-fetch unified profile data if needed (optimization to avoid multiple index service calls)
                Dictionary<int, ResourceProfileEffectiveData>? unifiedProfileData = null;
                var needsNameTemplate = additionalItems.HasFlag(ResourceAdditionalItem.DisplayName);
                var needsPropertyOptions = additionalItems.HasFlag(ResourceAdditionalItem.Properties);

                if (needsNameTemplate || needsPropertyOptions)
                {
                    using (MiniProfiler.Current.Step("Pre-fetch unified profile data"))
                    {
                        unifiedProfileData = await ResourceProfileService.GetEffectiveDataForResources(
                            resourceIds.ToArray(),
                            includeNameTemplate: needsNameTemplate,
                            includePropertyOptions: needsPropertyOptions);
                    }
                }

                foreach (var i in SpecificEnumUtils<ResourceAdditionalItem>.Values.OrderBy(x => x))
                {
                    if (additionalItems.HasFlag(i))
                    {
                        using (MiniProfiler.Current.Step($"AdditionalItem.{i}"))
                        {
                            switch (i)
                            {
                                case ResourceAdditionalItem.Properties:
                                {
                                    using (MiniProfiler.Current.Step("Properties"))
                                    {
                                        // 并行执行所有独立的数据查询 (使用 Parallel.ForEachAsync 以利用线程池并行执行内存缓存操作)
                                        List<ReservedPropertyValue>? reservedPropertyValues = null;
                                        List<Property>? reservedProperties = null;
                                        List<CustomPropertyValue>? customPropertyValues = null;
                                        List<CustomProperty>? customPropertiesResult = null;

                                        using (MiniProfiler.Current.Step("Execute parallel queries"))
                                        {
                                            var scopeFactory = GetRequiredService<IServiceScopeFactory>();

                                            // Define all query operations with independent scopes to avoid DbContext concurrency issues
                                            var queryOperations = new Func<Task>[]
                                            {
                                                async () =>
                                                {
                                                    await using var scope = scopeFactory.CreateAsyncScope();
                                                    var service = scope.ServiceProvider.GetRequiredService<IReservedPropertyValueService>();
                                                    reservedPropertyValues = await service.GetAll(x => resourceIds.Contains(x.ResourceId), asNoTracking);
                                                },
                                                async () =>
                                                {
                                                    await using var scope = scopeFactory.CreateAsyncScope();
                                                    var service = scope.ServiceProvider.GetRequiredService<IPropertyService>();
                                                    reservedProperties = await service.GetProperties(PropertyPool.Reserved);
                                                },
                                                async () =>
                                                {
                                                    await using var scope = scopeFactory.CreateAsyncScope();
                                                    var service = scope.ServiceProvider.GetRequiredService<ICustomPropertyValueService>();
                                                    customPropertyValues = await service.GetAll(x => resourceIds.Contains(x.ResourceId), CustomPropertyValueAdditionalItem.None, asNoTracking);
                                                },
                                                async () =>
                                                {
                                                    await using var scope = scopeFactory.CreateAsyncScope();
                                                    var service = scope.ServiceProvider.GetRequiredService<ICustomPropertyService>();
                                                    customPropertiesResult = await service.GetAll(null, CustomPropertyAdditionalItem.None, asNoTracking);
                                                }
                                            };

                                            // Execute queries in parallel with limited parallelism
                                            var parallelOptions = GetParallelOptions();
                                            await Parallel.ForEachAsync(
                                                queryOperations,
                                                parallelOptions,
                                                async (operation, ct) => await operation());
                                        }

                                        Dictionary<int, List<ReservedPropertyValue>> reservedPropertyValueMap;
                                        Dictionary<int, Property> reservedPropertyMap;

                                        using (MiniProfiler.Current.Step("Build reserved property maps"))
                                        {
                                            reservedPropertyValueMap = reservedPropertyValues!
                                                .GroupBy(d => d.ResourceId).ToDictionary(d => d.Key, d => d.ToList());
                                            reservedPropertyMap = reservedProperties!.ToDictionary(d => d.Id, d => d);
                                        }

                                        // 使用并行查询的结果
                                        Dictionary<int, Dictionary<int, List<CustomPropertyValue>>> customPropertiesValuesMap;
                                        Dictionary<int, ResourceProfilePropertyOptions> resourceProfilePropertyOptions;
                                        Dictionary<int, CustomProperty> customPropertyMap;

                                        // Pre-build descriptor and property caches to avoid repeated lookups in inner loop
                                        Dictionary<int, IPropertyDescriptor?> descriptorCache;
                                        Dictionary<int, Property> propertyCache;

                                        using (MiniProfiler.Current.Step("Build custom property maps"))
                                        {
                                            customPropertiesValuesMap = customPropertyValues!
                                                .GroupBy(x => x.ResourceId).ToDictionary(x => x.Key,
                                                    x => x.GroupBy(y => y.PropertyId).ToDictionary(y => y.Key, y => y.ToList()));
                                            // Extract property options from unified profile data
                                            resourceProfilePropertyOptions = unifiedProfileData?
                                                .Where(kv => kv.Value.PropertyOptions != null)
                                                .ToDictionary(kv => kv.Key, kv => kv.Value.PropertyOptions!) ?? new Dictionary<int, ResourceProfilePropertyOptions>();
                                            customPropertyMap = customPropertiesResult!.ToDictionary(d => d.Id, d => d);

                                            // Cache descriptors and Property objects to avoid repeated lookups
                                            descriptorCache = customPropertyMap.ToDictionary(
                                                kv => kv.Key,
                                                kv => PropertySystem.Property.TryGetDescriptor(kv.Value.Type));
                                            propertyCache = customPropertyMap.ToDictionary(
                                                kv => kv.Key,
                                                kv => kv.Value.ToProperty());
                                        }

                                        // Process all properties in parallel (each resource is independent)
                                        using (MiniProfiler.Current.Step("Process properties (parallel)"))
                                        {
                                            var parallelOptions = GetParallelOptions();
                                            // Pre-fetch scope priority map to avoid repeated calls in parallel tasks
                                            var scopePriorityMap = GetScopePriorityMap();

                                            await Parallel.ForEachAsync(doList, parallelOptions, (r, ct) =>
                                            {
                                                // Initialize Properties dictionary for this resource
                                                r.Properties ??= new Dictionary<int, Dictionary<int, Resource.Property>>(2);

                                                // Process reserved properties
                                                var reservedProperties = r.Properties.GetOrAdd((int)PropertyPool.Reserved, _ => []);
                                                var dbReservedProperties = reservedPropertyValueMap.GetValueOrDefault(r.Id);

                                                reservedProperties[(int)ResourceProperty.Rating] = new Resource.Property(
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Rating)?.Name,
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Rating)?.Type ?? default,
                                                    dbReservedProperties?.Select(s =>
                                                        new Resource.Property.PropertyValue(s.Scope, s.Rating, s.Rating, s.Rating)).ToList(),
                                                    true);

                                                reservedProperties[(int)ResourceProperty.Introduction] = new Resource.Property(
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Introduction)?.Name,
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Introduction)?.Type ?? default,
                                                    dbReservedProperties?.Select(s =>
                                                        new Resource.Property.PropertyValue(s.Scope, s.Introduction, s.Introduction, s.Introduction)).ToList(),
                                                    true);

                                                reservedProperties[(int)ResourceProperty.Cover] = new Resource.Property(
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Cover)?.Name,
                                                    reservedPropertyMap.GetValueOrDefault((int)ResourceProperty.Cover)?.Type ?? default,
                                                    dbReservedProperties?.Select(s =>
                                                    {
                                                        var coverPaths = s.CoverPaths;
                                                        return new Resource.Property.PropertyValue(s.Scope, coverPaths, coverPaths, coverPaths);
                                                    }).ToList(),
                                                    true);

                                                // Process custom properties
                                                var customProperties = r.Properties.GetOrAdd((int)PropertyPool.Custom, _ => []);

                                                var propertyIds = new List<int>();
                                                if (resourceProfilePropertyOptions.TryGetValue(r.Id, out var propOptions))
                                                {
                                                    propertyIds.AddRange(propOptions.Properties?
                                                        .Where(p => p.Pool == PropertyPool.Custom)
                                                        .Select(p => p.Id) ?? []);
                                                }

                                                var boundPropertyIds = propertyIds.ToHashSet();
                                                propertyIds = propertyIds.Distinct().ToList();

                                                customPropertiesValuesMap.TryGetValue(r.Id, out var pValues);
                                                if (pValues != null)
                                                {
                                                    propertyIds.AddRange(pValues.Keys.Except(propertyIds).OrderBy(x =>
                                                        customPropertyMap.GetValueOrDefault(x)?.Order ?? int.MaxValue));
                                                }

                                                var propertyOrderMap = new Dictionary<int, int>(propertyIds.Count);
                                                for (var j = 0; j < propertyIds.Count; j++)
                                                {
                                                    propertyOrderMap[propertyIds[j]] = j;
                                                }

                                                foreach (var pId in propertyIds)
                                                {
                                                    var property = customPropertyMap.GetValueOrDefault(pId);
                                                    if (property == null) continue;

                                                    var values = pValues?.GetValueOrDefault(pId);
                                                    var visible = boundPropertyIds.Contains(pId);

                                                    var p = customProperties.GetOrAdd(pId,
                                                        _ => new Resource.Property(property.Name, property.Type,
                                                            [], visible, propertyOrderMap[pId]));

                                                    if (values != null)
                                                    {
                                                        p.Values ??= new List<Resource.Property.PropertyValue>(values.Count);
                                                        var cpd = descriptorCache.GetValueOrDefault(pId);
                                                        var cachedProperty = propertyCache.GetValueOrDefault(pId);
                                                        foreach (var v in values)
                                                        {
                                                            var bizValue = (cpd != null && cachedProperty != null)
                                                                ? cpd.GetBizValue(cachedProperty, v.Value)
                                                                : v.Value;
                                                            p.Values.Add(new Resource.Property.PropertyValue(v.Scope, v.Value, bizValue, bizValue));
                                                        }
                                                    }
                                                }

                                                // Sort property values by scope for this resource (with per-property overrides)
                                                resourceProfilePropertyOptions.TryGetValue(r.Id, out var profilePropOptions);
                                                SortPropertyValuesByScope(r, scopePriorityMap, profilePropOptions);

                                                return ValueTask.CompletedTask;
                                            });
                                        }
                                    }

                                    break;
                                }
                                case ResourceAdditionalItem.Alias:
                                    break;
                                case ResourceAdditionalItem.None:
                                    break;
                                case ResourceAdditionalItem.HasChildren:
                                {
                                    break;
                                }
                                case ResourceAdditionalItem.DisplayName:
                                {
                                    using (MiniProfiler.Current.Step("DisplayName"))
                                    {
                                        var wrappers = (await _specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper))
                                            .Select(x => (Left: x.Value1, Right: x.Value2!)).ToArray();

                                        // Extract name templates from unified profile data (already fetched)
                                        Dictionary<int, string?> templateMap;
                                        using (MiniProfiler.Current.Step("Extract name templates from unified data"))
                                        {
                                            templateMap = unifiedProfileData?
                                                .Where(kv => kv.Value.NameTemplate != null)
                                                .ToDictionary(kv => kv.Key, kv => kv.Value.NameTemplate) ?? new Dictionary<int, string?>();
                                        }

                                        // Pre-cache builtin property name mappings to avoid repeated _propertyLocalizer calls
                                        var builtinPropertyKeyMap = SpecificEnumUtils<BuiltinPropertyForDisplayName>.Values
                                            .ToDictionary(
                                                b => b,
                                                b => $"{{{_propertyLocalizer.BuiltinPropertyName((ResourceProperty)b)}}}");

                                        using (MiniProfiler.Current.Step("BuildDisplayName foreach"))
                                        {
                                            foreach (var resource in doList)
                                            {
                                                if (templateMap.TryGetValue(resource.Id, out var tpl) && !string.IsNullOrEmpty(tpl))
                                                {
                                                    resource.DisplayName = BuildDisplayNameForResourceOptimized(resource, tpl, wrappers, builtinPropertyKeyMap);
                                                }
                                            }
                                        }

                                        // Fill display names from resolvers for resources that still have empty display names
                                        using (MiniProfiler.Current.Step("DisplayName from resolvers"))
                                        {
                                            var resourcesWithEmptyDisplayName = doList.Where(r => string.IsNullOrEmpty(r.DisplayName)).ToList();
                                            if (resourcesWithEmptyDisplayName.Count > 0)
                                            {
                                                var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();
                                                var linksGrouped = await sourceLinkService.GetByResourceIdsGrouped(
                                                    resourcesWithEmptyDisplayName.Select(r => r.Id).ToArray());

                                                // Group source keys by source type
                                                var sourceKeysBySource = new Dictionary<ResourceSource, HashSet<string>>();
                                                var resourceSourceLinks = new Dictionary<int, List<ResourceSourceLink>>();
                                                foreach (var resource in resourcesWithEmptyDisplayName)
                                                {
                                                    if (linksGrouped.TryGetValue(resource.Id, out var links))
                                                    {
                                                        resourceSourceLinks[resource.Id] = links;
                                                        foreach (var link in links)
                                                        {
                                                            if (!sourceKeysBySource.TryGetValue(link.Source, out var keys))
                                                            {
                                                                keys = [];
                                                                sourceKeysBySource[link.Source] = keys;
                                                            }

                                                            keys.Add(link.SourceKey);
                                                        }
                                                    }
                                                }

                                                // Batch fetch display names from each resolver
                                                var resolvers = GetRequiredService<IEnumerable<IResourceResolver>>();
                                                var resolverMap = resolvers.ToDictionary(r => r.Source);
                                                var displayNamesBySource = new Dictionary<ResourceSource, Dictionary<string, string>>();

                                                foreach (var (source, keys) in sourceKeysBySource)
                                                {
                                                    if (resolverMap.TryGetValue(source, out var resolver))
                                                    {
                                                        displayNamesBySource[source] = await resolver.GetDefaultDisplayNames(keys);
                                                    }
                                                }

                                                // Apply display names (prefer non-filesystem sources)
                                                foreach (var resource in resourcesWithEmptyDisplayName)
                                                {
                                                    if (!resourceSourceLinks.TryGetValue(resource.Id, out var links)) continue;

                                                    // Try non-filesystem sources first, then filesystem
                                                    foreach (var link in links.OrderBy(l => l.Source == ResourceSource.PathMark ? 1 : 0))
                                                    {
                                                        if (displayNamesBySource.TryGetValue(link.Source, out var names) &&
                                                            names.TryGetValue(link.SourceKey, out var name))
                                                        {
                                                            resource.DisplayName = name;
                                                            break;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    break;
                                }
                                case ResourceAdditionalItem.All:
                                    break;
                                case ResourceAdditionalItem.MediaLibraryName:
                                {
                                    // Use MediaLibraryResourceMappingService with index for efficient O(1) lookups
                                    var mlResourceIds = doList.Select(d => d.Id).ToArray();
                                    var resourceMediaLibraryIdsMap = await MediaLibraryResourceMappingService.GetMediaLibraryIdsByResourceIds(mlResourceIds);

                                    var allMediaLibraryIds = resourceMediaLibraryIdsMap.Values.SelectMany(x => x).Distinct().ToHashSet();
                                    var mediaLibraryV2Map = allMediaLibraryIds.Count > 0
                                        ? (await MediaLibraryV2Service.GetByKeys(allMediaLibraryIds.ToArray())).ToDictionary(
                                            d => d.Id, d => d)
                                        : new Dictionary<int, MediaLibraryV2>();

                                    foreach (var resource in doList)
                                    {
                                        var mlIds = resourceMediaLibraryIdsMap.GetValueOrDefault(resource.Id);
                                        if (mlIds is { Count: > 0 })
                                        {
                                            var libraries = mlIds
                                                .Select(id => mediaLibraryV2Map.GetValueOrDefault(id))
                                                .Where(ml => ml != null)
                                                .Select(ml => new Resource.MediaLibraryInfo(ml!.Id, ml.Name, ml.Color))
                                                .ToList();

                                            resource.MediaLibraries = libraries;

                                            // Keep backward compatibility for deprecated fields
                                            var firstLibrary = libraries.FirstOrDefault();
                                            if (firstLibrary != null)
                                            {
                                                resource.MediaLibraryName = firstLibrary.Name;
                                                resource.MediaLibraryColor = firstLibrary.Color;
                                            }
                                        }
                                    }

                                    break;
                                }
                                case ResourceAdditionalItem.Cover:
                                {
                                    // Load Cache internally (needed by LocalFileCoverProvider)
                                    await EnsureCacheLoaded(doList, resourceIds);

                                    // Properties are loaded via dependency (Cover depends on Properties)
                                    var coverProviders = GetRequiredService<IEnumerable<ICoverProvider>>()
                                        .OrderBy(p => p.Priority).ToList();

                                    foreach (var r in doList)
                                    {
                                        foreach (var cp in coverProviders)
                                        {
                                            if (!cp.AppliesTo(r)) continue;
                                            if (cp.GetStatus(r) != DataStatus.Ready) continue;
                                            var covers = cp.GetCoversAsync(r, CancellationToken.None).GetAwaiter().GetResult();
                                            if (covers is { Count: > 0 })
                                            {
                                                r.Covers = covers;
                                                break;
                                            }
                                        }

                                        // Populate Cover DataStates
                                        r.DataStates ??= [];
                                        foreach (var cp in coverProviders.Where(cp => cp.AppliesTo(r)))
                                            r.DataStates.Add(new ResourceDataState
                                            {
                                                ResourceId = r.Id,
                                                DataType = ResourceDataType.Cover,
                                                Origin = cp.Origin,
                                                Status = cp.GetStatus(r)
                                            });
                                    }

                                    break;
                                }
                                case ResourceAdditionalItem.PlayableItem:
                                {
                                    // Load Cache internally (needed by LocalFilePlayableItemProvider)
                                    await EnsureCacheLoaded(doList, resourceIds);

                                    var playableItemProviders = GetRequiredService<IEnumerable<IPlayableItemProvider>>()
                                        .OrderBy(p => p.Priority).ToList();

                                    foreach (var r in doList)
                                    {
                                        var allItems = new List<PlayableItem>();
                                        foreach (var pp in playableItemProviders)
                                        {
                                            if (!pp.AppliesTo(r)) continue;
                                            if (pp.GetStatus(r) != DataStatus.Ready) continue;
                                            var result = pp.GetPlayableItemsAsync(r, CancellationToken.None).GetAwaiter().GetResult();
                                            allItems.AddRange(result.Items);
                                        }

                                        r.PlayableItems = allItems.Count > 0 ? allItems : null;

                                        // Populate PlayableItem DataStates
                                        r.DataStates ??= [];
                                        foreach (var pp in playableItemProviders.Where(pp => pp.AppliesTo(r)))
                                            r.DataStates.Add(new ResourceDataState
                                            {
                                                ResourceId = r.Id,
                                                DataType = ResourceDataType.PlayableItem,
                                                Origin = pp.Origin,
                                                Status = pp.GetStatus(r)
                                            });
                                    }

                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }
                }

                if (additionalItems.HasFlag(ResourceAdditionalItem.Alias))
                {
                    using (MiniProfiler.Current.Step("ReplaceWithPreferredAlias"))
                    {
                        await ReplaceWithPreferredAlias(doList);
                    }
                }

                return doList;
            }
        }

        /// <summary>
        /// Loads Cache for resources that don't have it yet.
        /// Uses dict-based batch loading for O(N) complexity.
        /// </summary>
        private async Task EnsureCacheLoaded(List<Resource> resources, List<int> resourceIds)
        {
            // Skip if all resources already have cache loaded
            if (resources.All(r => r.Cache != null))
                return;

            var cacheMap = (await _resourceCacheOrm.GetAll(x => resourceIds.Contains(x.ResourceId)))
                .ToDictionary(d => d.ResourceId, d => d);

            foreach (var r in resources)
            {
                r.Cache ??= cacheMap.GetValueOrDefault(r.Id)?.ToDomainModel();
            }
        }

        /// <summary>
        /// Gets the scope priority map for sorting property values.
        /// </summary>
        private Dictionary<int, int> GetScopePriorityMap() =>
            _optionsManager.Value.PropertyValueScopePriority.Cast<int>()
                .Select((x, i) => (Scope: x, Index: i)).ToDictionary(d => d.Scope, d => d.Index);

        private static Dictionary<int, int> BuildScopePriorityMap(PropertyValueScope[] scopes) =>
            scopes.Cast<int>().Select((x, i) => (Scope: x, Index: i)).ToDictionary(d => d.Scope, d => d.Index);

        private void SortPropertyValuesByScope(List<Resource> resources)
        {
            var scopePriorityMap = GetScopePriorityMap();
            foreach (var resource in resources)
            {
                SortPropertyValuesByScope(resource, scopePriorityMap);
            }
        }

        private void SortPropertyValuesByScope(Resource resource) =>
            SortPropertyValuesByScope(resource, GetScopePriorityMap());

        private static void SortPropertyValuesByScope(Resource resource, Dictionary<int, int> globalScopePriorityMap,
            ResourceProfilePropertyOptions? propertyOptions = null)
        {
            if (resource.Properties == null) return;

            // Build per-property scope priority map lookup if available
            Dictionary<(int Pool, int Id), Dictionary<int, int>>? perPropertyMaps = null;
            if (propertyOptions?.Properties != null)
            {
                foreach (var prop in propertyOptions.Properties)
                {
                    if (prop.ScopePriority is { Length: > 0 })
                    {
                        perPropertyMaps ??= new Dictionary<(int Pool, int Id), Dictionary<int, int>>();
                        perPropertyMaps[((int)prop.Pool, prop.Id)] = BuildScopePriorityMap(prop.ScopePriority);
                    }
                }
            }

            foreach (var (pool, ps) in resource.Properties)
            {
                foreach (var (propertyId, p) in ps)
                {
                    var effectiveMap = perPropertyMaps?.GetValueOrDefault((pool, propertyId))
                                      ?? globalScopePriorityMap;

                    p.Values?.Sort((a, b) =>
                        effectiveMap.GetValueOrDefault(a.Scope, int.MaxValue) -
                        effectiveMap.GetValueOrDefault(b.Scope, int.MaxValue));
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
            var parents = resources.Select(a => a.Parent)
                .Where(a => a != null && !string.IsNullOrEmpty(a!.Path))
                .GroupBy(a => a!.Path)
                .Select(a => a.FirstOrDefault()).ToList();
            if (parents.Any())
            {
                await AddOrPutRange(parents!);
            }

            await _addOrUpdateLock.WaitAsync();
            try
            {
                // Resource - maintain index correspondence for ID writeback
                var dbModels = resources.Select(a => a.ToDbModel()).ToList();
                var existedModels = new List<ResourceDbModel>();
                var newModelIndices = new List<int>();
                var newModels = new List<ResourceDbModel>();

                for (var i = 0; i < dbModels.Count; i++)
                {
                    if (dbModels[i].Id > 0)
                        existedModels.Add(dbModels[i]);
                    else
                    {
                        newModelIndices.Add(i);
                        newModels.Add(dbModels[i]);
                    }
                }

                await _orm.UpdateRange(existedModels);
                var addedModels = (await _orm.AddRange(newModels)).Data!.ToList();

                // Write back auto-generated IDs using index correspondence
                for (var i = 0; i < addedModels.Count; i++)
                {
                    resources[newModelIndices[i]].Id = addedModels[i].Id;
                }

                var dbResources = addedModels.Concat(existedModels).ToList();

                // Alias
                await _aliasService.SaveByResources(resources);

                // Built-in properties
                await _reservedPropertyValueService.PutByResources(resources);

                // Custom properties
                await _customPropertyValueService.SaveByResources(resources);

                // Source links
                var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();
                foreach (var resource in resources)
                {
                    if (resource.SourceLinks is { Count: > 0 })
                    {
                        await sourceLinkService.EnsureLinks(resource.Id, resource.SourceLinks);
                    }
                }

                // Publish resource data changed event (triggers index updates via event subscription)
                var allChangedIds = dbResources.Select(r => r.Id).ToArray();
                ResourceDataChangeEventPublisher.PublishResourcesChanged(allChangedIds);

                return [new DataChangeViewModel("Resource", newModels.Count, existedModels.Count, 0)];
            }
            finally
            {
                _addOrUpdateLock.Release();
            }
        }

        public async Task RefreshParentTag()
        {
            var allResources = await _orm.GetAll(null, false);
            var parentIds = allResources.Select(r => r.ParentId).Where(r => r.HasValue).OfType<int>().ToHashSet();

            var changedResources = new List<ResourceDbModel>();

            foreach (var resource in allResources)
            {
                var isParent = parentIds.Contains(resource.Id);
                var hasTag = resource.Tags.HasFlag(ResourceTag.IsParent);

                switch (isParent)
                {
                    case true when !hasTag:
                        resource.Tags |= ResourceTag.IsParent;
                        changedResources.Add(resource);
                        break;
                    case false when hasTag:
                        resource.Tags &= ~ResourceTag.IsParent;
                        changedResources.Add(resource);
                        break;
                }
            }

            if (changedResources.Count > 0)
            {
                await _orm.UpdateRange(changedResources);
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


        public async Task<List<PlayableItem>> DiscoverPlayableItems(int id, CancellationToken ct)
        {
            var r = await Get(id, ResourceAdditionalItem.PlayableItem);
            if (r == null)
                return [];

            var providerService = GetRequiredService<IPlayableItemProviderService>();
            var result = await providerService.GetPlayableItemsAsync(r, ct);
            return result.Items;
        }

        public async Task<BaseResponse> PlayRandomResource()
        {
            var playableCaches = await _resourceCacheOrm.GetAll(x => !string.IsNullOrEmpty(x.PlayableFilePaths));
            if (playableCaches.Count == 0)
            {
                return BaseResponseBuilder.BuildBadRequest("No playable resource was found.");
            }

            var randomIndex = Random.Shared.Next(playableCaches.Count);
            var cache = playableCaches[randomIndex];
            var playableFiles = cache.PlayableFilePaths?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString);
            var file = playableFiles?.FirstOrDefault();
            if (file == null)
            {
                return BaseResponseBuilder.BuildBadRequest("No playable file was found.");
            }

            return await PlayItem(cache.ResourceId, DataOrigin.FileSystem, file);
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
                resource.Tags = toResource.Tags;
                resource.Path = toResource.Path;
                resource.IsFile = toResource.IsFile;
                resource.ParentId = toResource.ParentId;
                resource.UpdatedAt = DateTime.Now;

                changedResources.Add(resource);
                if (deleteSource)
                {
                    discardResourceIds.Add(fromResource.Id);
                }
            }

            await AddOrPutRange(changedResources);
            await DeleteByKeys(discardResourceIds.ToArray());

            // Handle media library mappings for keepMediaLibrary
            foreach (var item in model.Items)
            {
                var keepMediaLibrary = item.KeepMediaLibrary || model.KeepMediaLibraryForAll;
                if (!keepMediaLibrary)
                {
                    // Transfer media library mappings from source to target
                    var fromMappings = await MediaLibraryResourceMappingService.GetByResourceId(item.FromId);
                    var toMlIds = fromMappings.Select(m => m.MediaLibraryId).ToList();
                    if (toMlIds.Any())
                    {
                        await MediaLibraryResourceMappingService.ReplaceMappings(item.ToId, toMlIds);
                    }
                }
            }
        }

        public async Task SaveCover(int id, byte[] imageBytes, CoverSaveMode mode)
        {
            var rpv = await _reservedPropertyValueService.GetFirst(x =>
                x.Scope == (int) PropertyValueScope.Manual && x.ResourceId == id);
            var currentCovers = rpv?.CoverPaths ?? [];
            var index = currentCovers.Count;
            var image = await Image.LoadAsync(new MemoryStream(imageBytes));
            var outputFilePath = _fileManager.BuildAbsolutePath("user-saved", "cover", $"{id}-{index}.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
            await image.SaveAsJpegAsync(outputFilePath);

            if (mode == CoverSaveMode.Replace)
            {
                currentCovers.Clear();
            }

            currentCovers.Insert(0, outputFilePath);

            if (rpv == null)
            {
                rpv = new ReservedPropertyValue
                {
                    ResourceId = id,
                    Scope = (int) PropertyValueScope.Manual,
                    CoverPaths = currentCovers
                };
                await _reservedPropertyValueService.Add(rpv);
            }
            else
            {
                rpv.CoverPaths = currentCovers;
                await _reservedPropertyValueService.Update(rpv);
            }

            // Publish cover changed event to invalidate cache
            ResourceDataChangeEventPublisher.PublishResourceCoverChanged([id]);
        }

        public async Task<CacheOverviewViewModel> GetCacheOverview()
        {
            var cacheMap = (await _resourceCacheOrm.GetAll(null, false)).ToDictionary(d => d.ResourceId, d => d);
            var resources = await GetAllDbModels(null, false);
            var mediaLibrariesV2 = await MediaLibraryV2Service.GetAll(null, MediaLibraryV2AdditionalItem.None);

            // Get resource -> mediaLibraryIds mapping
            var resourceIds = resources.Select(r => r.Id).ToList();
            var resourceMediaLibraryMap = await MediaLibraryResourceMappingService.GetMediaLibraryIdsByResourceIds(resourceIds);

            // Build mediaLibraryId -> resourceIds mapping
            var mediaLibraryIdResourceIdsMap = new Dictionary<int, HashSet<int>>();
            var unassociatedResourceIds = new HashSet<int>();

            foreach (var resource in resources)
            {
                var mediaLibraryIds = resourceMediaLibraryMap.GetValueOrDefault(resource.Id);
                if (mediaLibraryIds == null || mediaLibraryIds.Count == 0)
                {
                    unassociatedResourceIds.Add(resource.Id);
                }
                else
                {
                    foreach (var mlId in mediaLibraryIds)
                    {
                        if (!mediaLibraryIdResourceIdsMap.TryGetValue(mlId, out var set))
                        {
                            set = [];
                            mediaLibraryIdResourceIdsMap[mlId] = set;
                        }
                        set.Add(resource.Id);
                    }
                }
            }

            // Build cache maps
            var mediaLibraryIdCachesMap = mediaLibrariesV2.ToDictionary(
                ml => ml.Id,
                ml => mediaLibraryIdResourceIdsMap.GetValueOrDefault(ml.Id)?
                    .Select(rid => cacheMap.GetValueOrDefault(rid))
                    .OfType<ResourceCacheDbModel>()
                    .ToList() ?? []);

            var unassociatedCaches = unassociatedResourceIds
                .Select(rid => cacheMap.GetValueOrDefault(rid))
                .OfType<ResourceCacheDbModel>()
                .ToList();

            return new CacheOverviewViewModel
            {
                MediaLibraryCaches = mediaLibrariesV2.Select(ml => new CacheOverviewViewModel.MediaLibraryCacheViewModel
                {
                    MediaLibraryId = ml.Id,
                    MediaLibraryName = ml.Name,
                    ResourceCacheCountMap = SpecificEnumUtils<ResourceCacheType>.Values.ToDictionary(d => (int)d,
                        d => mediaLibraryIdCachesMap.GetValueOrDefault(ml.Id)?.Count(x => x.CachedTypes.HasFlag(d)) ?? 0),
                    ResourceCount = mediaLibraryIdResourceIdsMap.GetValueOrDefault(ml.Id)?.Count ?? 0
                }).ToList(),
                UnassociatedCaches = unassociatedResourceIds.Count > 0
                    ? new CacheOverviewViewModel.UnassociatedCacheViewModel
                    {
                        ResourceCacheCountMap = SpecificEnumUtils<ResourceCacheType>.Values.ToDictionary(d => (int)d,
                            d => unassociatedCaches.Count(x => x.CachedTypes.HasFlag(d))),
                        ResourceCount = unassociatedResourceIds.Count
                    }
                    : null
            };
        }

        public async Task<ResourceFileSystemCache?> GetResourceCache(int id)
        {
            return (await _resourceCacheOrm.GetByKey(id))?.ToDomainModel();
        }

        public async Task InvalidateResourceCovers(int resourceId)
        {
            // Clear the cover cache so it gets re-resolved from the new source covers
            await _resourceCacheOrm.UpdateAll(c => c.ResourceId == resourceId, x =>
            {
                x.CoverPaths = null;
                x.CachedTypes &= ~ResourceCacheType.Covers;
            });
        }

        public async Task DeleteResourceCacheByResourceIdAndCacheType(int resourceId, ResourceCacheType type)
        {
            await _resourceCacheOrm.UpdateAll(c => c.ResourceId == resourceId, x =>
            {
                x.CachedTypes &= ~type;
            });
            _prepareCacheTrigger.RequestTrigger();
        }

        public async Task DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId, ResourceCacheType type)
        {
            var resourceIds = await MediaLibraryResourceMappingService.GetResourceIdsByMediaLibraryId(mediaLibraryId);
            if (resourceIds.Count == 0) return;

            await _resourceCacheOrm.UpdateAll(c => resourceIds.Contains(c.ResourceId),
                x => { x.CachedTypes &= ~type; });
            _prepareCacheTrigger.RequestTrigger();
        }

        public async Task DeleteResourceCacheByResourceIdsAndCacheType(IEnumerable<int> resourceIds, ResourceCacheType type)
        {
            var ids = resourceIds.ToHashSet();
            if (ids.Count == 0) return;

            await _resourceCacheOrm.UpdateAll(c => ids.Contains(c.ResourceId),
                x =>
                {
                    x.CachedTypes &= ~type;
                    switch (type)
                    {
                        case ResourceCacheType.Covers:
                            x.CoverPaths = null;
                            break;
                        case ResourceCacheType.PlayableFiles:
                            x.PlayableFilePaths = null;
                            break;
                        case ResourceCacheType.ResourceMarkers:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(type), type, null);
                    }
                    
                });
            _prepareCacheTrigger.RequestTrigger();
        }

        public async Task DeleteUnassociatedResourceCacheByCacheType(ResourceCacheType type)
        {
            // Get all resources
            var allResources = await GetAllDbModels(null, false);
            var allResourceIds = allResources.Select(r => r.Id).ToList();

            // Get resource -> mediaLibraryIds mapping
            var resourceMediaLibraryMap = await MediaLibraryResourceMappingService.GetMediaLibraryIdsByResourceIds(allResourceIds);

            // Find unassociated resource IDs
            var unassociatedResourceIds = allResources
                .Where(r => !resourceMediaLibraryMap.TryGetValue(r.Id, out var mlIds) || mlIds.Count == 0)
                .Select(r => r.Id)
                .ToHashSet();

            if (unassociatedResourceIds.Count == 0) return;

            await _resourceCacheOrm.UpdateAll(c => unassociatedResourceIds.Contains(c.ResourceId),
                x => { x.CachedTypes &= ~type; });
            _prepareCacheTrigger.RequestTrigger();
        }

        public async Task<ResourceFileSystemCache?> RefreshResourceCache(int resourceId, CancellationToken ct)
        {
            var resource = await Get(resourceId, ResourceAdditionalItem.None);
            if (resource == null) return null;

            // Clear existing cache
            var cache = await _resourceCacheOrm.GetByKey(resourceId, true);
            var isNew = cache == null;
            cache ??= new ResourceCacheDbModel { ResourceId = resourceId };

            var uiResource = _uiOptions.Value.Resource;

            // Refresh covers if enabled
            if (!uiResource.DisableCoverCache)
            {
                cache.CoverPaths = null;
                cache.CachedTypes &= ~ResourceCacheType.Covers;

                // Check for existing covers from ReservedPropertyValue first
                var existingCovers = (await _reservedPropertyValueService.GetAll(
                        x => x.ResourceId == resourceId && x.CoverPaths != null && x.CoverPaths.Any()))
                    .OrderBy(x => x.Scope)
                    .FirstOrDefault()?.CoverPaths;

                if (existingCovers?.Any() == true)
                {
                    cache.CoverPaths = new ListStringValueBuilder(existingCovers).Value
                        ?.SerializeAsStandardValue(StandardValueType.ListString);
                }
                else
                {
                    var coverProviders = GetRequiredService<IEnumerable<ICoverProvider>>();
                    var localCoverProvider = coverProviders.FirstOrDefault(p => p.Origin == DataOrigin.FileSystem);
                    string? coverPath = null;
                    var coverResource = await Get(resourceId, ResourceAdditionalItem.Cover);
                    if (coverResource != null && localCoverProvider != null && localCoverProvider.AppliesTo(coverResource))
                    {
                        var covers = await localCoverProvider.GetCoversAsync(coverResource, ct);
                        coverPath = covers?.FirstOrDefault();
                    }
                    cache.CoverPaths = coverPath.IsNotEmpty()
                        ? new ListStringValueBuilder([coverPath]).Value
                            ?.SerializeAsStandardValue(StandardValueType.ListString)
                        : null;
                }

                cache.CachedTypes |= ResourceCacheType.Covers;
            }

            // Refresh playable files if enabled
            if (!uiResource.DisablePlayableFileCache)
            {
                cache.PlayableFilePaths = null;
                cache.CachedTypes &= ~ResourceCacheType.PlayableFiles;

                var playableProviders = GetRequiredService<IEnumerable<IPlayableItemProvider>>();
                var localPlayableProvider = playableProviders.FirstOrDefault(p => p.Origin == DataOrigin.FileSystem);
                var playableResource = await Get(resourceId, ResourceAdditionalItem.PlayableItem);
                if (playableResource != null && localPlayableProvider != null && localPlayableProvider.AppliesTo(playableResource))
                {
                    var playableResult = await localPlayableProvider.GetPlayableItemsAsync(playableResource, ct);
                    var playableFiles = playableResult.Items.Select(i => i.Key).ToList();
                    cache.PlayableFilePaths = playableFiles.Any()
                        ? new ListStringValueBuilder(playableFiles).Value
                            ?.SerializeAsStandardValue(StandardValueType.ListString)
                        : null;
                }

                cache.CachedTypes |= ResourceCacheType.PlayableFiles;
            }

            if (isNew)
            {
                await _resourceCacheOrm.Add(cache);
            }
            else
            {
                await _resourceCacheOrm.Update(cache);
            }

            return await GetResourceCache(resourceId);
        }

        public async Task MarkAsNotPlayed(int id)
        {
            await _orm.UpdateByKey(id, r => r.PlayedAt = null);
            ResourceDataChangeEventPublisher.PublishResourceChanged(id);
        }

        public async Task<Resource[]> GetAllGeneratedByMediaLibraryV2(int[]? ids = null, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            HashSet<int> resourceIds;
            if (ids?.Any() == true)
            {
                resourceIds = await MediaLibraryResourceMappingService.GetResourceIdsByMediaLibraryIds(ids);
            }
            else
            {
                // Get all resources that have at least one media library mapping
                var allMappings = await MediaLibraryResourceMappingService.GetAll();
                resourceIds = allMappings.Select(m => m.ResourceId).ToHashSet();
            }

            if (resourceIds.Count == 0) return [];

            return (await GetAll(r => resourceIds.Contains(r.Id), additionalItems)).ToArray();
        }

        public async Task<List<Resource>> GetByMediaLibraryId(int mediaLibraryId, ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var mappingService = GetRequiredService<IMediaLibraryResourceMappingService>();
            var mappings = await mappingService.GetByMediaLibraryId(mediaLibraryId);
            var resourceIds = mappings.Select(m => m.ResourceId).Distinct().ToArray();

            if (resourceIds.Length == 0)
            {
                return [];
            }

            return await GetByKeys(resourceIds, additionalItems);
        }

        public async Task<BaseResponse> SetMediaLibraries(int[] resourceIds, int[] mediaLibraryIds)
        {
            if (resourceIds.Length == 0)
            {
                return BaseResponseBuilder.Ok;
            }

            var resources = (await GetByKeys(resourceIds)).ToList();
            if (!resources.Any())
            {
                return BaseResponseBuilder.BuildBadRequest($"Resources [{string.Join(',', resourceIds)}] are not found");
            }

            // Validate all media library IDs exist
            if (mediaLibraryIds.Length > 0)
            {
                var mediaLibraries = await MediaLibraryV2Service.GetAll();
                var existingIds = mediaLibraries.Select(m => m.Id).ToHashSet();
                var invalidIds = mediaLibraryIds.Where(id => !existingIds.Contains(id)).ToList();
                if (invalidIds.Any())
                {
                    return BaseResponseBuilder.BuildBadRequest($"Invalid media library IDs: [{string.Join(',', invalidIds)}]");
                }
            }

            // Replace mappings for each resource
            foreach (var resource in resources)
            {
                await MediaLibraryResourceMappingService.ReplaceMappings(resource.Id, mediaLibraryIds);
            }

            return BaseResponseBuilder.Ok;
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
                    case ResourceProperty.Cover:
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

                        switch (property)
                        {
                            case ResourceProperty.Introduction:
                                scopeValue.Introduction =
                                    model.Value?.DeserializeAsStandardValue<string>(StandardValueType.String);
                                    break;
                            case ResourceProperty.Rating:
                                scopeValue.Rating =
                                    model.Value?.DeserializeAsStandardValue<decimal>(StandardValueType.Decimal);
                                    break;
                            case ResourceProperty.Cover:
                                scopeValue.CoverPaths =
                                    model.Value?.DeserializeDbValueAsStandardValue<List<string>>(
                                        PropertyType.Attachment);
                                    break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        var result = noValue
                            ? await _reservedPropertyValueService.Add(scopeValue)
                            : await _reservedPropertyValueService.Update(scopeValue);

                        if (property == ResourceProperty.Cover)
                        {
                            // Publish cover changed event to invalidate cache
                            ResourceDataChangeEventPublisher.PublishResourceCoverChanged([resourceId]);
                        }

                        return result;
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

        public async Task<BaseResponse> BulkPutPropertyValue(int[] resourceIds, ResourcePropertyValuePutInputModel model)
        {
            if (resourceIds.Length == 0)
            {
                return BaseResponseBuilder.Ok;
            }

            if (model.IsCustomProperty)
            {
                // Get all existing values for these resources and property
                var existingValues = await _customPropertyValueService.GetAllDbModels(x =>
                    resourceIds.Contains(x.ResourceId) && x.PropertyId == model.PropertyId &&
                    x.Scope == (int) PropertyValueScope.Manual);

                var existingByResourceId = existingValues.ToDictionary(v => v.ResourceId);

                var toAdd = new List<CustomPropertyValue>();
                var toUpdate = new List<CustomPropertyValue>();

                foreach (var resourceId in resourceIds)
                {
                    if (existingByResourceId.TryGetValue(resourceId, out var existing))
                    {
                        // Update existing
                        toUpdate.Add(new CustomPropertyValue
                        {
                            Id = existing.Id,
                            ResourceId = resourceId,
                            PropertyId = model.PropertyId,
                            Value = model.Value,
                            Scope = (int) PropertyValueScope.Manual
                        });
                    }
                    else
                    {
                        // Add new
                        toAdd.Add(new CustomPropertyValue
                        {
                            ResourceId = resourceId,
                            PropertyId = model.PropertyId,
                            Value = model.Value,
                            Scope = (int) PropertyValueScope.Manual
                        });
                    }
                }

                if (toAdd.Count > 0)
                {
                    var addResult = await _customPropertyValueService.AddRange(toAdd);
                    if (addResult.Code != 0)
                    {
                        return addResult;
                    }
                }

                if (toUpdate.Count > 0)
                {
                    var updateResult = await _customPropertyValueService.UpdateRange(toUpdate);
                    if (updateResult.Code != 0)
                    {
                        return updateResult;
                    }
                }

                return BaseResponseBuilder.Ok;
            }
            else
            {
                // Internal or Reserved properties
                var property = (ResourceProperty) model.PropertyId;
                switch (property)
                {
                    case ResourceProperty.MediaLibraryV2Multi:
                    {
                        var mediaLibraryIds = model.Value?.DeserializeAsStandardValue<List<string>>(StandardValueType.ListString)?
                            .Select(int.Parse).ToArray() ?? [];
                        return await SetMediaLibraries(resourceIds, mediaLibraryIds);
                    }
                    case ResourceProperty.Introduction:
                    case ResourceProperty.Cover:
                    case ResourceProperty.Rating:
                    {
                        // Get all existing scope values for these resources
                        var existingValues = await _reservedPropertyValueService.GetAll(x =>
                            resourceIds.Contains(x.ResourceId) && x.Scope == (int) PropertyValueScope.Manual);

                        var existingByResourceId = existingValues.ToDictionary(v => v.ResourceId);

                        var toAdd = new List<ReservedPropertyValue>();
                        var toUpdate = new List<ReservedPropertyValue>();

                        foreach (var resourceId in resourceIds)
                        {
                            ReservedPropertyValue scopeValue;
                            bool isNew = false;

                            if (existingByResourceId.TryGetValue(resourceId, out var existing))
                            {
                                scopeValue = existing;
                            }
                            else
                            {
                                scopeValue = new ReservedPropertyValue
                                {
                                    ResourceId = resourceId,
                                    Scope = (int) PropertyValueScope.Manual
                                };
                                isNew = true;
                            }

                            switch (property)
                            {
                                case ResourceProperty.Introduction:
                                    scopeValue.Introduction =
                                        model.Value?.DeserializeAsStandardValue<string>(StandardValueType.String);
                                    break;
                                case ResourceProperty.Rating:
                                    scopeValue.Rating =
                                        model.Value?.DeserializeAsStandardValue<decimal>(StandardValueType.Decimal);
                                    break;
                                case ResourceProperty.Cover:
                                    scopeValue.CoverPaths =
                                        model.Value?.DeserializeDbValueAsStandardValue<List<string>>(
                                            PropertyType.Attachment);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            if (isNew)
                            {
                                toAdd.Add(scopeValue);
                            }
                            else
                            {
                                toUpdate.Add(scopeValue);
                            }
                        }

                        if (toAdd.Count > 0)
                        {
                            var addResult = await _reservedPropertyValueService.AddRange(toAdd);
                            if (addResult.Code != 0)
                            {
                                return addResult;
                            }
                        }

                        if (toUpdate.Count > 0)
                        {
                            var updateResult = await _reservedPropertyValueService.UpdateRange(toUpdate);
                            if (updateResult.Code != 0)
                            {
                                return updateResult;
                            }
                        }

                        if (property == ResourceProperty.Cover)
                        {
                            // Publish cover changed event to invalidate cache (only for actually changed resources)
                            var changedResourceIds = toAdd.Concat(toUpdate).Select(v => v.ResourceId).ToArray();
                            if (changedResourceIds.Length > 0)
                            {
                                ResourceDataChangeEventPublisher.PublishResourceCoverChanged(changedResourceIds);
                            }
                        }

                        return BaseResponseBuilder.Ok;
                    }
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

        // public async Task<BaseResponse> Patch(int id, ResourceUpdateRequestModel model)
        // {
        //     throw new NotImplementedException();
        // }

        public async Task<BaseResponse> Play(int resourceId, string file)
        {
            var resource = await Get(resourceId, ResourceAdditionalItem.None);
            if (resource == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            var playedByCustomPlayer = false;

            // Use ResourceProfile to get effective player options
            var playerOptions = await ResourceProfileService.GetEffectivePlayerOptions(resource);
            if (playerOptions?.Players != null && playerOptions.Players.Count > 0)
            {
                var fileExtension = Path.GetExtension(file);
                var player =
                    playerOptions.Players.FirstOrDefault(p => p.Extensions?.Contains(fileExtension, StringComparer.OrdinalIgnoreCase) == true) ??
                    playerOptions.Players.FirstOrDefault(x => x.Extensions?.Any() != true);
                if (player != null)
                {
                    var cmd = player.Command;
                    _ = Task.Run(async () =>
                    {
                        // Replace placeholders with proper escaping
                        // If placeholder is already quoted (e.g., "{0}" or '{0}'), only escape inner quotes
                        // Otherwise, add quotes around the value
                        var template = string.IsNullOrEmpty(cmd) ? "{0}" : cmd;
                        var escapedFile = file.Replace("\"", "\\\"");
                        var args = Regex.Replace(template, @"([""']?)\{(\d+)\}([""']?)", match =>
                        {
                            var prefix = match.Groups[1].Value;
                            var suffix = match.Groups[3].Value;
                            var alreadyQuoted = (prefix == "\"" && suffix == "\"") || (prefix == "'" && suffix == "'");
                            return alreadyQuoted
                                ? $"{prefix}{escapedFile}{suffix}"
                                : $"\"{escapedFile}\"";
                        });
                        await Cli.Wrap(player.ExecutablePath)
                            .WithArguments(args)
                            .ExecuteAsync();
                    });
                    playedByCustomPlayer = true;
                }
            }

            if (!playedByCustomPlayer)
            {
                await _systemPlayer.Play(file);
            }

            var now = DateTime.Now;
            await _orm.UpdateByKey(resourceId, r => r.PlayedAt = now);
            await _playHistoryService.Add(new PlayHistoryDbModel
                {ResourceId = resourceId, Item = file, PlayedAt = now});

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> PlayItem(int resourceId, DataOrigin origin, string key)
        {
            var resource = await Get(resourceId, ResourceAdditionalItem.None);
            if (resource == null)
                return BaseResponseBuilder.NotFound;

            var playableItemProviderService = GetRequiredService<IPlayableItemProviderService>();

            try
            {
                await playableItemProviderService.PlayAsync(resource, origin, key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play item from {Origin} for resource {ResourceId}", origin, resourceId);
                return BaseResponseBuilder.BuildBadRequest($"Failed to play: {ex.Message}");
            }

            var now = DateTime.Now;
            await _orm.UpdateByKey(resourceId, r => r.PlayedAt = now);
            await _playHistoryService.Add(new PlayHistoryDbModel
                { ResourceId = resourceId, Item = $"{origin}:{key}", PlayedAt = now });

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> ChangeMediaLibrary(int[] ids, int mediaLibraryId, Dictionary<int, string>? newPaths = null)
        {
            // Verify media library exists
            var library = await MediaLibraryV2Service.Get(mediaLibraryId);
            if (library == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            // Update paths if provided
            if (newPaths?.Any() == true)
            {
                var resources = await _orm.GetByKeys(ids);
                foreach (var resource in resources)
                {
                    var newPath = newPaths.GetValueOrDefault(resource.Id);
                    if (newPath.IsNotEmpty())
                    {
                        resource.Path = newPath.StandardizePath()!;
                    }
                }
                await _orm.UpdateRange(resources);
            }

            // Update media library mappings using the new service
            foreach (var resourceId in ids)
            {
                await MediaLibraryResourceMappingService.ReplaceMappings(resourceId, [mediaLibraryId]);
            }

            await MediaLibraryV2Service.RefreshResourceCount(mediaLibraryId);

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> ChangePath(int[] ids, Dictionary<int, string> newPaths)
        {
            var resources = await _orm.GetByKeys(ids);
            if (resources == null)
            {
                return BaseResponseBuilder.NotFound;
            }

            var resourcesToBeChanged = new List<ResourceDbModel>();
            
            foreach (var resource in resources)
            {
                if (newPaths.TryGetValue(resource.Id, out var newPath) && newPath.IsNotEmpty())
                {
                    var standardizedPath = newPath.StandardizePath()!;
                    if (resource.Path != standardizedPath)
                    {
                        resourcesToBeChanged.Add(resource);
                    }
                }
            }

            if (!resourcesToBeChanged.Any())
            {
                return BaseResponseBuilder.Ok;
            }

            foreach (var resource in resourcesToBeChanged)
            {
                if (newPaths.TryGetValue(resource.Id, out var newPath) && newPath.IsNotEmpty())
                {
                    resource.Path = newPath.StandardizePath()!;
                }
            }

            await _orm.UpdateRange(resourcesToBeChanged);
            ResourceDataChangeEventPublisher.PublishResourcesChanged(resourcesToBeChanged.Select(r => r.Id));

            return BaseResponseBuilder.Ok;
        }

        public async Task Pin(int id, bool pin)
        {
            await _orm.UpdateByKey(id, r =>
            {
                if (pin)
                {
                    r.Tags |= ResourceTag.Pinned;
                }
                else
                {
                    r.Tags &= ~ResourceTag.Pinned;
                }
            });
            ResourceDataChangeEventPublisher.PublishResourceChanged(id);
        }

        public async Task<List<int>> GetConflictingResourceIds(int resourceId)
        {
            var conflictIds = new HashSet<int>();

            // 1. Find conflicts by overlapping source links
            var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();
            var sourceLinkConflicts = await sourceLinkService.FindConflictingResourceIds(resourceId);
            foreach (var id in sourceLinkConflicts)
            {
                conflictIds.Add(id);
            }

            // 2. Find conflicts by same Path
            var resource = await _orm.GetByKey(resourceId);
            if (resource != null && !string.IsNullOrEmpty(resource.Path))
            {
                var allResources = await _orm.GetAll(r => r.Id != resourceId, false);
                var pathConflicts = allResources
                    .Where(r => !string.IsNullOrEmpty(r.Path) &&
                                string.Equals(r.Path, resource.Path, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Id);
                foreach (var id in pathConflicts)
                {
                    conflictIds.Add(id);
                }
            }

            return conflictIds.ToList();
        }

        public async Task MergeResources(ResourceMergeInputModel model)
        {
            var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();

            // 1. Collect all source links from merge-source resources and add to target
            var mergeSourceIds = model.SourceResourceIds.Concat([model.TargetResourceId]).Distinct().ToArray();
            var sourceLinks = await sourceLinkService.GetByResourceIds(mergeSourceIds);
            await sourceLinkService.EnsureLinks(model.TargetResourceId, sourceLinks);

            // 2. Transfer media library mappings from source resources to target
            var mappingService = MediaLibraryResourceMappingService;
            var sourceMappings = await mappingService.GetByResourceIds(model.SourceResourceIds);
            if (sourceMappings.Count > 0)
            {
                var targetMappings = sourceMappings
                    .Select(m => (model.TargetResourceId, m.MediaLibraryId))
                    .Distinct()
                    .ToList();
                await mappingService.EnsureMappingsRange(targetMappings);
            }

            // 3. Delete source resources and explicitly-deleted resources
            // Note: Property value selections should be applied via separate PutPropertyValue calls
            // before calling merge.
            var idsToDelete = model.SourceResourceIds
                .Concat(model.DeleteResourceIds)
                .Where(id => id != model.TargetResourceId)
                .Distinct()
                .ToArray();

            if (idsToDelete.Length > 0)
            {
                await DeleteByKeys(idsToDelete);
            }
        }

        private async Task DeleteRelatedData(List<int> ids)
        {
            await _customPropertyValueService.RemoveAll(x => ids.Contains(x.ResourceId));
            var sourceLinkService = GetRequiredService<IResourceSourceLinkService>();
            await sourceLinkService.DeleteByResourceIds(ids);
        }

        public string BuildDisplayNameForResource(Resource resource, string template,
            (string Left, string Right)[] wrappers)
        {
            var segments = BuildDisplayNameSegmentsForResource(resource, template, wrappers);
            var displayName = string.Join("", segments.Select(a => a.Text));
            return displayName.IsNullOrEmpty() ? resource.FileName : displayName;
        }

        public Segment[] BuildDisplayNameSegmentsForResource(Resource resource, string template, (string Left, string Right)[] wrappers)
        {
            var matcherPropertyMap = resource.Properties?.GetValueOrDefault((int)PropertyPool.Custom)?.Values
                .GroupBy(d => d.Name)
                .ToDictionary(d => $"{{{d.Key}}}", d => d.First()) ?? [];

            var replacements = matcherPropertyMap.ToDictionary(d => d.Key,
                d =>
                {
                    var value = d.Value.Values?.FirstOrDefault()?.BizValue;
                    if (value != null)
                    {
                        var stdValueHandler = StandardValueSystem.GetHandler(d.Value.BizValueType);
                        return stdValueHandler.BuildDisplayValue(value);
                    }

                    return null;
                });

            foreach (var b in SpecificEnumUtils<BuiltinPropertyForDisplayName>.Values)
            {
                var name = _propertyLocalizer.BuiltinPropertyName((ResourceProperty)b);
                var key = $"{{{name}}}";
                replacements[key] = b switch
                {
                    BuiltinPropertyForDisplayName.Filename => resource.FileName,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            var segments =
                ResourceUtils.SplitDisplayNameTemplateIntoSegments(template, replacements, wrappers);

            return segments;
        }

        /// <summary>
        /// Optimized version that accepts pre-cached builtin property key map
        /// </summary>
        private string BuildDisplayNameForResourceOptimized(
            Resource resource,
            string template,
            (string Left, string Right)[] wrappers,
            Dictionary<BuiltinPropertyForDisplayName, string> builtinPropertyKeyMap)
        {
            var matcherPropertyMap = resource.Properties?.GetValueOrDefault((int)PropertyPool.Custom)?.Values
                .GroupBy(d => d.Name)
                .ToDictionary(d => $"{{{d.Key}}}", d => d.First()) ?? [];

            var replacements = new Dictionary<string, string?>(matcherPropertyMap.Count + builtinPropertyKeyMap.Count);

            foreach (var kv in matcherPropertyMap)
            {
                var value = kv.Value.Values?.FirstOrDefault()?.BizValue;
                if (value != null)
                {
                    var stdValueHandler = StandardValueSystem.GetHandler(kv.Value.BizValueType);
                    replacements[kv.Key] = stdValueHandler.BuildDisplayValue(value);
                }
                else
                {
                    replacements[kv.Key] = null;
                }
            }

            foreach (var (b, key) in builtinPropertyKeyMap)
            {
                replacements[key] = b switch
                {
                    BuiltinPropertyForDisplayName.Filename => resource.FileName,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }

            var segments = ResourceUtils.SplitDisplayNameTemplateIntoSegments(template, replacements, wrappers);
            var displayName = string.Join("", segments.Select(a => a.Text));
            return displayName.IsNullOrEmpty() ? resource.FileName : displayName;
        }

        public async Task<(List<Resource> Ancestors, int ChildrenCount)> GetHierarchyContext(int resourceId)
        {
            // First, get the target resource to find its parent chain
            var resource = await _orm.GetByKey(resourceId);
            if (resource == null)
            {
                return ([], 0);
            }

            // Collect all ancestor IDs by traversing the parent chain
            var ancestorIds = new List<int>();
            var visitedIds = new HashSet<int> { resourceId }; // Prevent infinite loops
            var currentParentId = resource.ParentId;

            // First pass: collect all parent IDs
            while (currentParentId.HasValue && !visitedIds.Contains(currentParentId.Value))
            {
                ancestorIds.Add(currentParentId.Value);
                visitedIds.Add(currentParentId.Value);

                var parent = await _orm.GetByKey(currentParentId.Value);
                currentParentId = parent?.ParentId;
            }

            // Batch fetch all ancestors in one query
            List<Resource> ancestors = [];
            if (ancestorIds.Count > 0)
            {
                var ancestorDbModels = await _orm.GetByKeys(ancestorIds.ToArray());
                var ancestorMap = ancestorDbModels.ToDictionary(a => a.Id);

                // Convert to domain models (minimal, no additional items needed)
                var domainModels = await ToDomainModel(ancestorDbModels, ResourceAdditionalItem.None);
                var domainMap = domainModels.ToDictionary(d => d.Id);

                // Build ordered ancestor list (from root to immediate parent)
                ancestors = ancestorIds
                    .Select(id => domainMap.GetValueOrDefault(id))
                    .Where(a => a != null)
                    .Reverse()
                    .ToList()!;
            }

            // Count children
            var childrenCount = await _orm.Count(r => r.ParentId == resourceId);

            return (ancestors, childrenCount);
        }
    }
}