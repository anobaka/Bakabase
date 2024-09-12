using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.RequestModels;
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
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Components.Configuration.Abstractions;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Helpers;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;
using Bakabase.InsideWorld.Business.Components.Search;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bootstrap.Models.Constants;
using Newtonsoft.Json;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player.Infrastructures;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Extensions;
using CustomProperty = Bakabase.Modules.CustomProperty.Abstractions.Models.CustomProperty;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ResourceService : IResourceService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.Resource, int>
            _orm;

        private readonly ISpecialTextService _specialTextService;
        private readonly IMediaLibraryService _mediaLibraryService;
        private readonly ICategoryService _categoryService;
        private readonly ILogger<ResourceService> _logger;
        private readonly SemaphoreSlim _addOrUpdateLock = new(1, 1);
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly BackgroundTaskHelper _backgroundTaskHelper;
        private static readonly SemaphoreSlim FindCoverInVideoSm = new SemaphoreSlim(2, 2);
        private readonly IBOptionsManager<ResourceOptions> _optionsManager;
        private readonly IBOptions<ThirdPartyOptions> _thirdPartyOptions;
        private readonly FfMpegService _ffMpegService;
        private readonly InsideWorldLocalizer _localizer;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IResourceSearchContextProcessor _resourceSearchContextProcessor;
        private readonly IAliasService _aliasService;
        private readonly IReservedPropertyValueService _reservedPropertyValueService;
        private readonly Dictionary<int, IStandardValueHandler> _standardValueHandlers;
        private readonly ICustomPropertyDescriptors _customPropertyDescriptors;
        private readonly IFileManager _fileManager;
        private readonly ICoverDiscoverer _coverDiscoverer;
        private readonly IStandardValueHelper _standardValueHelper;
        private readonly ICategoryCustomPropertyMappingService _categoryCustomPropertyMappingService;

        public ResourceService(IServiceProvider serviceProvider, ISpecialTextService specialTextService,
            IAliasService aliasService, IMediaLibraryService mediaLibraryService, ICategoryService categoryService,
            ILogger<ResourceService> logger,
            BackgroundTaskManager backgroundTaskManager, BackgroundTaskHelper backgroundTaskHelper,
            IBOptionsManager<ResourceOptions> optionsManager, IBOptions<ThirdPartyOptions> thirdPartyOptions,
            FfMpegService ffMpegService, InsideWorldLocalizer localizer,
            ICustomPropertyService customPropertyService, ICustomPropertyValueService customPropertyValueService,
            IResourceSearchContextProcessor resourceSearchContextProcessor,
            IReservedPropertyValueService reservedPropertyValueService,
            IEnumerable<IStandardValueHandler> standardValueHandlers,
            ICustomPropertyDescriptors customPropertyDescriptors, IFileManager fileManager,
            ICoverDiscoverer coverDiscoverer, IStandardValueHelper standardValueHelper, ICategoryCustomPropertyMappingService categoryCustomPropertyMappingService)
        {
            _specialTextService = specialTextService;
            _aliasService = aliasService;
            _mediaLibraryService = mediaLibraryService;
            _categoryService = categoryService;
            _logger = logger;
            _backgroundTaskManager = backgroundTaskManager;
            _backgroundTaskHelper = backgroundTaskHelper;
            _optionsManager = optionsManager;
            _thirdPartyOptions = thirdPartyOptions;
            _ffMpegService = ffMpegService;
            _localizer = localizer;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _resourceSearchContextProcessor = resourceSearchContextProcessor;
            _reservedPropertyValueService = reservedPropertyValueService;
            _fileManager = fileManager;
            _coverDiscoverer = coverDiscoverer;
            _standardValueHelper = standardValueHelper;
            _categoryCustomPropertyMappingService = categoryCustomPropertyMappingService;
            _customPropertyDescriptors = customPropertyDescriptors;
            _standardValueHandlers = standardValueHandlers.ToDictionary(d => (int) d.Type, d => d);
            _orm = new FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.Resource, int>(
                serviceProvider);
        }

        public InsideWorldDbContext DbContext => _orm.DbContext;

        public async Task DeleteByKeys(int[] ids)
        {
            await DeleteRelatedData(ids.ToList());
            await _orm.RemoveByKeys(ids);
        }

        public async Task<List<Resource>> GetAll(
            Expression<Func<Abstractions.Models.Db.Resource, bool>>? selector = null,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var data = await _orm.GetAll(selector);
            var dtoList = await ToDomainModel(data.ToArray(), additionalItems);
            return dtoList;
        }

        public async Task<SearchResponse<Resource>> Search(ResourceSearchDto model, bool save, bool asNoTracking)
        {
            var allResources = await GetAll();
            var context = new ResourceSearchContext(allResources);

            var searchModel = model.Copy();

            if (!string.IsNullOrEmpty(model.Keyword))
            {
                var properties = await _customPropertyService.GetAll();
                context.PropertiesDataPool = properties.ToDictionary(d => d.Id, d => d);

                var newGroup = new ResourceSearchFilterGroup
                {
                    Combinator = Combinator.Or, Filters =
                    [
                        new ResourceSearchFilter
                        {
                            DbValue = model.Keyword.SerializeAsStandardValue(StandardValueType.String),
                            Operation = SearchOperation.Contains,
                            PropertyType = ResourcePropertyType.Internal,
                            PropertyId = (int) ResourceProperty.FileName
                        }
                    ]
                };

                foreach (var p in properties)
                {
                    if (_customPropertyDescriptors.TryGet(p.Type, out var pd))
                    {
                        var filter = pd.BuildSearchFilterByKeyword(p, model.Keyword);
                        if (filter != null)
                        {
                            newGroup.Filters.Add(filter);
                        }
                    }
                }

                if (searchModel.Group == null)
                {
                    searchModel.Group = newGroup;
                }
                else
                {
                    searchModel.Group = new ResourceSearchFilterGroup
                    {
                        Combinator = Combinator.And,
                        Groups = [searchModel.Group, newGroup]
                    };
                }
            }

            var resourceIds = await SearchResourceIds(searchModel.Group, context);
            var ordersForSearch = searchModel.Orders.BuildForSearch();

            Func<Abstractions.Models.Db.Resource, bool>? exp = resourceIds == null
                ? null
                : r => resourceIds.Contains(r.Id);

            var resources = await _orm.Search(exp, searchModel.PageIndex, searchModel.PageSize, ordersForSearch,
                asNoTracking);
            var dtoList = await ToDomainModel(resources.Data.ToArray(), ResourceAdditionalItem.All);

            if (save)
            {
                await _optionsManager.SaveAsync(a => a.LastSearchV2 = model);
            }

            return searchModel.BuildResponse(dtoList, resources.TotalCount);
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
        private async Task<HashSet<int>?> SearchResourceIds(ResourceSearchFilterGroup? group,
            ResourceSearchContext context)
        {
            if (group == null)
            {
                return null;
            }

            var steps = new List<Func<Task<HashSet<int>?>>>();

            if (group.Filters?.Any() == true)
            {
                foreach (var filter in group.Filters)
                {
                    steps.Add(async () => await _resourceSearchContextProcessor.Search(filter, context));
                }
            }

            if (group.Groups?.Any() == true)
            {
                foreach (var subGroup in group.Groups)
                {
                    steps.Add(async () => await SearchResourceIds(subGroup, context));
                }
            }

            HashSet<int>? result = null;

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var ids = await step();

                if (ids == null)
                {
                    if (group.Combinator == Combinator.Or)
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
                        if (group.Combinator == Combinator.And)
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
                            if (group.Combinator == Combinator.Or)
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

        public async Task<Resource> ToDomainModel(Abstractions.Models.Db.Resource resource,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            return (await ToDomainModel([resource], additionalItems)).FirstOrDefault()!;
        }

        public async Task<List<Resource>> ToDomainModel(Abstractions.Models.Db.Resource[] resources,
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

                            foreach (var r in doList)
                            {
                                r.Properties ??= [];
                                var reservedProperties =
                                    r.Properties.GetOrAdd((int) ResourcePropertyType.Reserved, () => []);
                                var dbReservedProperties = reservedPropertyValueMap.GetValueOrDefault(r.Id);
                                reservedProperties[(int) ResourceProperty.Rating] = new Resource.Property(null,
                                    StandardValueType.Decimal,
                                    StandardValueType.Decimal,
                                    dbReservedProperties?.Select(s =>
                                        new Resource.Property.PropertyValue(s.Scope, s.Rating, s.Rating,
                                            s.Rating)).ToList(), true);
                                reservedProperties[(int) ResourceProperty.Introduction] = new Resource.Property(null,
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
                                    r.Properties.GetOrAdd((int) ResourcePropertyType.Custom, () => []);

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
                                        () => new Resource.Property(property.Name, property.DbValueType,
                                            property.BizValueType, [], visible));
                                    if (values != null)
                                    {
                                        p.Values ??= [];
                                        _customPropertyDescriptors.TryGet(property.Type, out var cpd);
                                        foreach (var v in values)
                                        {
                                            var bizValue = cpd?.ConvertDbValueToBizValue(property, v.Value) ?? v.Value;
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
                                    @do.Properties?.GetValueOrDefault((int) ResourcePropertyType.Custom);
                                var found = false;
                                if (customPropertyValues != null)
                                {
                                    foreach (var (pId, pvs) in customPropertyValues)
                                    {
                                        if (found)
                                        {
                                            break;
                                        }

                                        if (propertyMap.GetValueOrDefault(pId)?.EnumType ==
                                            CustomPropertyType.Attachment)
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
                                                        @do.CoverPaths = images;
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

        public async Task<List<Abstractions.Models.Db.Resource>> GetAllDbModels(
            Expression<Func<Abstractions.Models.Db.Resource, bool>>? selector = null,
            bool returnCopy = true)
        {
            return await _orm.GetAll(selector, returnCopy);
        }
//
// 		/// <summary>
// 		/// <para>Save all resources and properties at once.</para>
// 		/// <para>If resource exists, its core properties will be updated, and other properties will be kept or replaced by their identity information such as Name.</para>
// 		/// <para>We do not update other properties by their id and name, because it's hard to ensure all occurrences of a property have been modified identically.</para>
// 		/// <para>Null values will be ignored.</para>
// 		/// </summary>
// 		/// <param name="srs"></param>
// 		/// <returns></returns>
// 		[Obsolete($"Use {nameof(AddOrPutRange)} instead, or wait a new {nameof(AddOrPatchRange)} method.")]
// 		public async Task<ResourceRangeAddOrUpdateResult> AddOrPatchRange(List<Resource> srs)
// 		{
// 			var tmpResources = srs?.ToList() ?? new List<Resource>();
// 			var simpleResourceMap = new Dictionary<string, Resource>();
// 			foreach (var s in tmpResources)
// 			{
// 				s.Clean();
// 				simpleResourceMap.TryAdd(s.Path, s);
// 			}
//
// 			var parents = tmpResources.Select(a => a.Parent).Where(a => a != null).GroupBy(a => a!.Path)
// 				.Select(a => a.FirstOrDefault()).ToList();
// 			if (parents.Any())
// 			{
// 				await AddOrPatchRange(parents);
// 			}
//
// 			await _addOrUpdateLock.WaitAsync();
// 			try
// 			{
// 				// Aliases
// 				var names = tmpResources.Select(a => a.Name).Where(a => !string.IsNullOrEmpty(a)).ToHashSet();
// 				var publisherNames = tmpResources.SelectMany(a => a.Publishers.GetNames()).Distinct()
// 					.Where(x => !string.IsNullOrEmpty(x)).ToList();
// 				var originalNames = tmpResources.Where(a => a.Originals != null)
// 					.SelectMany(a => a.Originals!.Select(b => b.Name))
// 					.Distinct().Where(x => !string.IsNullOrEmpty(x)).ToList();
// 				var allNames = names.Concat(publisherNames).Concat(originalNames).ToList();
// 				var aliasAddResult = await _aliasService.AddRange(allNames!);
//
// 				// Resources
// 				var resources = tmpResources.Select(a => a.ToDbModel()!).ToList();
// 				var existedResources = resources.Where(a => a.Id > 0).ToList();
// 				var newResources = resources.Except(existedResources).ToList();
// 				await _orm.UpdateRange(existedResources);
// 				resources = (await _orm.AddRange(newResources)).Data.Concat(existedResources).ToList();
// 				resources.ForEach(a => { simpleResourceMap[a.BuildPath()].Id = a.Id; });
//
// 				// Publishers
// 				// Changed items will be replaced instead of being updated. 
// 				var resourcesWithPublishers = tmpResources.Where(a => a.Publishers != null).ToList();
// 				var publisherRangeAddResult =
// 					await _publisherService.GetOrAddRangeByNames(resourcesWithPublishers.SelectMany(a => a.Publishers!)
// 						.ToList());
// 				var publisherIds = publisherRangeAddResult.Data.ToDictionary(a => a.Key, a => a.Value.Id);
// 				var publisherMappings = resourcesWithPublishers.SelectMany(s =>
// 				{
// 					s.Publishers!.PopulateId(publisherIds);
// 					return s.Publishers!.BuildMappings(s.Id);
// 				}).ToList();
// 				await _publisherMappingService.AddRange(publisherMappings);
//
// 				// Series
// 				// Changed items will be replaced instead of being updated.
// 				var allSeries = tmpResources.Select(a => a.Series).Where(a => a != null).ToArray();
// 				var seriesNames = allSeries.Select(t => t!.Name).Where(t => t.IsNotEmpty()).ToList();
// 				if (seriesNames.Any())
// 				{
// 					var seriesRangeAddResult = await _serialService.GetOrAddRangeByNames(seriesNames);
// 					foreach (var s in allSeries)
// 					{
// 						s.Id = seriesRangeAddResult.Data[s.Name].Id;
// 					}
// 				}
//
// 				// Volumes
// 				var allVolumes = new List<Volume>();
// 				foreach (var r in tmpResources)
// 				{
// 					if (r.Volume != null || r.Series != null)
// 					{
// 						r.Volume ??= new VolumeDto();
// 						r.Volume.ResourceId = r.Id;
// 						if (r.Series != null)
// 						{
// 							r.Volume.SerialId = r.Series.Id;
// 						}
//
// 						allVolumes.Add(r.Volume.ToEntity());
// 					}
// 				}
//
// 				var volumeRangeAddResult = await _volumeService.AddRange(allVolumes);
//
// 				// Originals
// 				var originalRangeAddResult = await _originalService.GetOrAddRangeByNames(originalNames);
// 				var originalMappings = tmpResources.Where(a => a.Originals != null).SelectMany(a =>
// 					a.Originals!.Select(b =>
// 						new OriginalResourceMapping
// 						{
// 							OriginalId = b.Id == 0 ? originalRangeAddResult.Data[b.Name].Id : b.Id,
// 							ResourceId = a.Id
// 						})).ToList();
// 				await _originalMappingService.AddRange(originalMappings);
//
// 				// Tags
// 				// Cares name and group name only
// 				var tags = tmpResources.Where(a => a.Tags != null).SelectMany(a => a.Tags!).Where(a => a.Id == 0)
// 					.Distinct(TagDto.BizComparer).ToArray();
// 				var savedTags = await _tagService.GetOrAddRangeByNameAndGroupName(tags, false);
// 				var resourceTagsMap = tmpResources.Where(a => a.Tags != null).ToDictionary(a => a.Id,
// 					a => a.Tags!.Select(b =>
// 					{
// 						if (b.Id > 0)
// 						{
// 							return b.Id;
// 						}
//
// 						var tag = savedTags.FirstOrDefault(t =>
// 							TagDto.BizComparer.Equals(b, t));
// 						if (tag == null)
// 						{
// #if DEBUG
// 							Debugger.Break();
// #endif
// 							_logger.LogWarning(
// 								$"Tag [{b.GroupName}:{b.Name}] is not found in saved tags: {string.Join(',', savedTags.Select(t => $"{t.GroupName}:{t.Name}"))}");
// 						}
//
// 						return tag?.Id;
// 					}).Where(b => b.HasValue).Select(b => b!.Value).ToArray());
// 				await _resourceTagMappingService.PutRange(resourceTagsMap);
//
// 				// Custom Properties
// 				var resourceIds = tmpResources.Select(t => t.Id).ToList();
// 				var existedCustomProperties =
// 					(await _customResourcePropertyService.GetAll(t => resourceIds.Contains(t.ResourceId)))
// 					.GroupBy(t => t.ResourceId).ToDictionary(t => t.Key,
// 						t => t.GroupBy(a => a.Key).ToDictionary(a => a.Key, a => a.ToList()));
// 				var invalidProperties = new List<CustomResourceProperty>();
// 				var newProperties = new List<CustomResourceProperty>();
// 				var changedProperties = new List<CustomResourceProperty>();
// 				foreach (var r in tmpResources)
// 				{
// 					r.CustomProperties ??= new Dictionary<string, List<CustomResourceProperty>>();
// 					if (!existedCustomProperties.TryGetValue(r.Id, out var existedProperties))
// 					{
// 						existedProperties = new();
// 					}
//
// 					// Invalid
// 					var invalidKeys = new HashSet<string>();
// 					foreach (var (key, list) in existedProperties)
// 					{
// 						if (!r.CustomProperties.ContainsKey(key))
// 						{
// 							invalidProperties.AddRange(list);
// 							invalidKeys.Add(key);
// 						}
// 					}
//
// 					foreach (var (key, list) in r.CustomProperties)
// 					{
// 						if (!invalidKeys.Contains(key))
// 						{
// 							if (existedProperties.TryGetValue(key, out var exists))
// 							{
// 								var sortedList = list.OrderBy(t => t.Index).ToList();
// 								var sortedExists = exists.OrderBy(t => t.Index).ToList();
// 								for (var i = 0; i < sortedList.Count; i++)
// 								{
// 									if (sortedExists.Count > i)
// 									{
// 										// Update
// 										sortedList[i].Id = sortedExists[i].Id;
// 										changedProperties.Add(sortedList[i]);
// 									}
// 									else
// 									{
// 										// New
// 										sortedList[i].ResourceId = r.Id;
// 										newProperties.Add(sortedList[i]);
// 									}
// 								}
// 							}
// 							else
// 							{
// 								list.ForEach(l => l.ResourceId = r.Id);
// 								newProperties.AddRange(list);
// 							}
// 						}
// 					}
// 				}
//
// 				await _customResourcePropertyService.RemoveRange(invalidProperties);
// 				await _customResourcePropertyService.AddRange(newProperties);
// 				await _customResourcePropertyService.UpdateRange(changedProperties);
//
// 				return new ResourceRangeAddOrUpdateResult
// 				{
// 					AliasCount = aliasAddResult.Count,
// 					NewAliasCount = aliasAddResult.AddedCount,
//
// 					ResourceCount = resources.Count,
// 					NewResourceCount = newResources.Count,
//
// 					PublisherCount = publisherRangeAddResult.Count,
// 					NewPublisherCount = publisherRangeAddResult.AddedCount,
//
// 					VolumeCount = volumeRangeAddResult.Count,
// 					NewVolumeCount = volumeRangeAddResult.AddedCount,
//
// 					OriginalCount = originalRangeAddResult.Count,
// 					NewOriginalCount = originalRangeAddResult.AddedCount,
// 				};
// 			}
// 			finally
// 			{
// 				_addOrUpdateLock.Release();
// 			}
// 		}

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
                dbResources = (await _orm.AddRange(newResources)).Data.Concat(existedResources).ToList();
                dbResources.ForEach(a => { resourceDtoMap[a.Path].Id = a.Id; });

                // Alias
                await _aliasService.SaveByResources(resources);

                // Built-in properties
                // todo:

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

        public async Task<(string? Path, byte[]? ImageBytes)?> DiscoverCover(int id, CancellationToken ct)
        {
            return await GetCover(id, true, ct);
        }

        protected async Task<(string? Path, byte[]? ImageBytes)?> GetCover(int id, bool useIconAsFallback,
            CancellationToken ct)
        {
            var r = await Get(id, ResourceAdditionalItem.CustomProperties | ResourceAdditionalItem.Category);
            if (r == null)
            {
                return null;
            }

            string? coverFile = null;
            var customPropertyValues = r.Properties?.GetValueOrDefault((int) ResourcePropertyType.Custom);
            var propertyIds = customPropertyValues?.Keys.ToHashSet();
            if (propertyIds?.Any() == true)
            {
                var propertyMap =
                    (await _customPropertyService.GetByKeys(propertyIds, CustomPropertyAdditionalItem.None))
                    .ToDictionary(
                        d => d.Id, d => d);
                foreach (var (pId, pvs) in customPropertyValues!)
                {
                    if (propertyMap.GetValueOrDefault(pId)?.EnumType == CustomPropertyType.Attachment)
                    {
                        coverFile = _findCoverInAttachmentProperty(pvs);
                        if (!string.IsNullOrEmpty(coverFile))
                        {
                            return (coverFile, null);
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(coverFile))
            {
                var discoveryResult = await _coverDiscoverer.Discover(r.Path,
                    r.Category?.CoverSelectionOrder ?? CoverSelectOrder.FilenameAscending, useIconAsFallback, ct);
                if (discoveryResult != null)
                {
                    if (discoveryResult.Data != null)
                    {
                        return (null, discoveryResult.Data);
                    }
                    else
                    {
                        return (discoveryResult.Path, null);
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
                    var files = await selector.Data.GetStartFiles(r.Path, ct);
                    return files.Select(f => f.StandardizePath()!).ToArray();
                }
            }

            return null;
        }

        public async Task<bool> Any(Func<Abstractions.Models.Db.Resource, bool>? selector = null)
        {
            return await _orm.Any(selector);
        }

        public async Task<List<Abstractions.Models.Db.Resource>> AddAll(
            IEnumerable<Abstractions.Models.Db.Resource> resources)
        {
            return (await _orm.AddRange(resources.ToList())).Data;
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
                    value = new Bakabase.Abstractions.Models.Db.CustomPropertyValue()
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
                                _standardValueHelper.Deserialize<string>(model.Value, StandardValueType.String);
                        }
                        else
                        {
                            scopeValue.Rating =
                                _standardValueHelper.Deserialize<decimal?>(model.Value, StandardValueType.Decimal);
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
            var unknownResources = await GetUnknownResources();

            if (unknownResources.Any())
            {
                var unknownResourceIds = unknownResources.Select(r => r.Id).ToList();
                await DeleteRelatedData(unknownResourceIds);
                await _orm.RemoveRange(unknownResources);
            }
        }

        private async Task<List<Abstractions.Models.Db.Resource>> GetUnknownResources()
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
            var unknownResources = await GetUnknownResources();
            return unknownResources.Count;
        }

        private async Task DeleteRelatedData(List<int> ids)
        {
            await _customPropertyValueService.RemoveAll(x => ids.Contains(x.ResourceId));
        }
    }
}