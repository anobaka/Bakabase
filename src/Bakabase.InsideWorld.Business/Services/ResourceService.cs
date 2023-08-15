using Bakabase.InsideWorld.Business.Components.Resource.Nfo;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Humanizer.Localisation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.Resource;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bootstrap.Components.Configuration.Abstractions;
using Xabe.FFmpeg;
using SearchOption = System.IO.SearchOption;
using Volume = Bakabase.InsideWorld.Models.Models.Entities.Volume;
using System.Drawing.Imaging;
using System.Drawing;
using System.Runtime.InteropServices;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Models.Configs;
using CliWrap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ResourceService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int> _orm;
        private readonly SpecialTextService _specialTextService;
        private readonly PublisherService _publisherService;
        private readonly AliasService _aliasService;
        private readonly VolumeService _volumeService;
        private readonly SeriesService _serialService;
        private readonly OriginalService _originalService;
        private readonly TagService _tagService;
        private readonly PublisherResourceMappingService _publisherMappingService;
        private readonly OriginalResourceMappingService _originalMappingService;
        private readonly MediaLibraryService _mediaLibraryService;
        private readonly ResourceCategoryService _categoryService;
        private readonly ResourceTagMappingService _resourceTagMappingService;
        private readonly CustomResourcePropertyService _customResourcePropertyService;
        private readonly ILogger<ResourceService> _logger;
        private readonly SemaphoreSlim _addOrUpdateLock = new(1, 1);
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly BackgroundTaskHelper _backgroundTaskHelper;
        private readonly TagGroupService _tagGroupService;
        private readonly FavoritesResourceMappingService _favoritesResourceMappingService;
        private static readonly SemaphoreSlim FindCoverInVideoSm = new SemaphoreSlim(2, 2);
        private readonly IBOptionsManager<ResourceOptions> _optionsManager;
        private readonly IBOptions<ThirdPartyOptions> _thirdPartyOptions;
        private readonly FFMpegHelper _ffMpegHelper;

        public ResourceService(IServiceProvider serviceProvider, SpecialTextService specialTextService,
            PublisherService publisherService, AliasService aliasService,
            VolumeService volumeService, SeriesService serialService, OriginalService originalService,
            TagService tagService, PublisherResourceMappingService publisherMappingService,
            OriginalResourceMappingService originalMappingService, MediaLibraryService mediaLibraryService,
            ResourceCategoryService categoryService,
            ResourceTagMappingService resourceTagMappingService, ILogger<ResourceService> logger,
            CustomResourcePropertyService customResourcePropertyService, BackgroundTaskManager backgroundTaskManager,
            BackgroundTaskHelper backgroundTaskHelper, FavoritesResourceMappingService favoritesResourceMappingService,
            TagGroupService tagGroupService, IBOptionsManager<ResourceOptions> optionsManager,
            IBOptions<ThirdPartyOptions> thirdPartyOptions, FFMpegHelper ffMpegHelper)
        {
            _specialTextService = specialTextService;
            _publisherService = publisherService;
            _aliasService = aliasService;
            _volumeService = volumeService;
            _serialService = serialService;
            _originalService = originalService;
            _tagService = tagService;
            _publisherMappingService = publisherMappingService;
            _originalMappingService = originalMappingService;
            _mediaLibraryService = mediaLibraryService;
            _categoryService = categoryService;
            _resourceTagMappingService = resourceTagMappingService;
            _logger = logger;
            _customResourcePropertyService = customResourcePropertyService;
            _backgroundTaskManager = backgroundTaskManager;
            _backgroundTaskHelper = backgroundTaskHelper;
            _favoritesResourceMappingService = favoritesResourceMappingService;
            _tagGroupService = tagGroupService;
            _optionsManager = optionsManager;
            _thirdPartyOptions = thirdPartyOptions;
            _ffMpegHelper = ffMpegHelper;
            _orm = new FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int>(serviceProvider);
        }

        public InsideWorldDbContext DbContext => _orm.DbContext;

        public async Task<BaseResponse> Patch(int id, ResourceUpdateRequestModel model)
        {
            var entity = await _orm.GetByKey(id);
            if (model.Rate > 0)
            {
                entity.Rate = model.Rate.Value;
            }

            if (model.Name.IsNotEmpty())
            {
                entity.Name = model.Name;
            }

            if (model.ReleaseDt.HasValue)
            {
                entity.ReleaseDt = model.ReleaseDt.Value;
            }

            if (model.Language.HasValue)
            {
                entity.Language = model.Language.Value;
            }

            if (model.Introduction.IsNotEmpty())
            {
                entity.Introduction = model.Introduction;
            }

            if (model.Series != null)
            {
                var volume = (await _volumeService.GetByResourceKeys(new List<int> {id})).Values.FirstOrDefault();
                if (model.Series.IsNotEmpty())
                {
                    var series =
                        (await _serialService.AddRange(new List<string> {model.Series})).Data.Values.FirstOrDefault();
                    if (volume == null)
                    {
                        await _volumeService.AddRange(new List<Volume>
                        {
                            new Volume
                            {
                                ResourceId = id,
                                SerialId = series.Id
                            }
                        });
                    }
                    else
                    {
                        if (volume.SerialId != series.Id)
                        {
                            await _volumeService.UpdateByKey(volume.ResourceId, t => t.SerialId = series.Id);
                        }
                    }
                }
                else
                {
                    if (volume != null)
                    {
                        await _volumeService.UpdateByKey(volume.ResourceId, t => t.SerialId = 0);
                    }
                }
            }

            if (model.Originals != null)
            {
                await _originalMappingService.UpdateResourceOriginals(id, model.Originals);
            }

            if (model.Publishers != null)
            {
                await _publisherMappingService.UpdateResourcePublishers(id, model.Publishers);
            }

            if (model.Tags != null)
            {
                var tags = await _tagService.AddRangeByNameAndGroupName(model.Tags, false);
                await _resourceTagMappingService.UpdateRange(new Dictionary<int, int[]>
                    {{id, tags.Select(t => t.Id).ToArray()}});
            }

            await _orm.Update(entity);

            var category = await _categoryService.GetByKey(entity.CategoryId);
            if (category.GenerateNfo)
            {
                var dto = await GetByKey(id, ResourceAdditionalItem.All, true);
                await SaveNfo(dto, true);
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateRawName(int id, string rawName)
        {
            if (rawName.IsNotEmpty() && rawName.RemoveInvalidFileNameChars() != rawName)
            {
                return BaseResponseBuilder.BuildBadRequest("Invalid characters are found in filename");
            }

            var resource = await _orm.GetByKey(id);
            if (resource.RawName != rawName)
            {
                var currentFullname = resource.RawFullname;
                resource.RawName = rawName;
                Directory.Move(currentFullname, resource.RawFullname);
                await _orm.Update(resource);
                // await Resync(id);
            }

            return BaseResponseBuilder.Ok;
        }

        public Task RemoveByMediaLibraryIdsNotIn(int[] ids)
        {
            return _orm.RemoveAll(r => !ids.Contains(r.MediaLibraryId));
        }

        public Task RemoveByKey(int id, bool removeFiles)
        {
            return RemoveByKeys(new[] {id}, removeFiles);
        }

        public async Task RemoveByKeys(int[] ids, bool removeFiles)
        {
            if (removeFiles)
            {
                var resources = await _orm.GetByKeys(ids);
                foreach (var resource in resources)
                {
                    RemoveAllRelativeFiles(resource);
                }
            }

            await _orm.RemoveByKeys(ids);
        }

        public void RemoveAllRelativeFiles(Resource resource)
        {
            if (resource.RawFullname.IsNotEmpty())
            {
                if (File.Exists(resource.RawFullname))
                {
                    FileUtils.Delete(resource.RawFullname, true, true);
                }

                if (Directory.Exists(resource.RawFullname))
                {
                    DirectoryUtils.Delete(resource.RawFullname, true, true);
                }
            }
        }

        public async Task<List<ResourceDto>> GetAll(
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var data = await _orm.GetAll();
            var dtoList = await ToDto(data.ToArray(), additionalItems);
            return dtoList;
        }

        public async Task<SearchResponse<ResourceDto>> Search(ResourceSearchDto model, bool returnCopy)
        {
            Func<Resource, bool> exp = a =>
                (!model.ReleaseStartDt.HasValue || model.ReleaseStartDt <= a.ReleaseDt) &&
                (!model.ReleaseEndDt.HasValue || model.ReleaseEndDt >= a.ReleaseDt) &&

                (!model.AddStartDt.HasValue || model.AddStartDt <= a.CreateDt) &&
                (!model.AddEndDt.HasValue || model.AddEndDt >= a.CreateDt) &&

                (!model.FileCreateStartDt.HasValue || model.FileCreateStartDt <= a.FileCreateDt) &&
                (!model.FileCreateEndDt.HasValue || model.FileCreateEndDt >= a.FileCreateDt) &&

                (!model.FileModifyStartDt.HasValue || model.FileModifyStartDt <= a.FileModifyDt) &&
                (!model.FileModifyEndDt.HasValue || model.FileModifyEndDt >= a.FileModifyDt) &&

                (!model.MinRate.HasValue || model.MinRate <= a.Rate) &&

                (!model.ParentId.HasValue || model.ParentId.Value == a.ParentId) &&

                (!model.HideChildren || !a.ParentId.HasValue);

            if (model.FavoritesIds?.Any() == true)
            {
                var resourceIds =
                    (await _favoritesResourceMappingService.GetAll(t => model.FavoritesIds.Contains(t.FavoritesId)))
                    .Select(t => t.ResourceId).Distinct().ToArray();
                var exp1 = exp;
                exp = a => exp1(a) && resourceIds.Contains(a.Id);
            }

            if (model.Languages?.Any() == true)
            {
                var exp1 = exp;
                exp = a => exp1(a) && model.Languages.Contains(a.Language);
            }

            if (model.MediaLibraryIds?.Any() == true)
            {
                var exp1 = exp;
                exp = a => exp1(a) && model.MediaLibraryIds.Contains(a.MediaLibraryId);
            }

            var everyThingSegments = (model.Everything ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var regexEverythingSegments = everyThingSegments.Where(t => t.StartsWith("reg:")).ToHashSet();
            var plainEverythingSegments = everyThingSegments.Except(regexEverythingSegments).ToList();
            var regexs = regexEverythingSegments.Select(t => Regex.Replace(t, @"^reg\:", string.Empty)).ToHashSet();

            var names = new[] {model.Name, model.Publisher, model.Original}.Concat(plainEverythingSegments)
                .Where(a => a.IsNotEmpty())
                .ToArray();

            if (names.Any() || regexs.Any())
            {
                var relatedAliases = await _aliasService.GetByNames(names, true, true);
                var aliasesMap =
                    names.ToDictionary(a => a, a => relatedAliases[a].SelectMany(b => b.AllNames).ToHashSet());
                if (model.Name.IsNotEmpty())
                {
                    var exp1 = exp;
                    var list = aliasesMap[model.Name];
                    exp = a => exp1(a) && list.Contains(a.Name);
                }

                if (model.Publisher.IsNotEmpty())
                {
                    var publisherIds = await _publisherService.GetAllIdsByNames(aliasesMap[model.Publisher], true);
                    var resourceIds = (await _publisherMappingService.GetAll(a => publisherIds.Contains(a.PublisherId)))
                        .Select(a => a.ResourceId).Distinct().ToList();
                    var exp1 = exp;
                    exp = a => exp1(a) && resourceIds.Contains(a.Id);
                }

                if (model.Original.IsNotEmpty())
                {
                    var originalIds = await _originalService.GetAllIdsByNames(aliasesMap[model.Original], true);
                    var resourceIds = (await _originalMappingService.GetAll(a => originalIds.Contains(a.OriginalId)))
                        .Select(a => a.ResourceId).Distinct().ToList();
                    var exp1 = exp;
                    exp = a => exp1(a) && resourceIds.Contains(a.Id);
                }

                if (everyThingSegments.Any())
                {
                    var aliasNames = plainEverythingSegments.SelectMany(t => aliasesMap[t]).ToHashSet();
                    var publisherIds = await _publisherService.GetAllIdsByNames(aliasNames, true);
                    var originalIds = await _originalService.GetAllIdsByNames(aliasNames, true);

                    Func<Resource, bool> rawNameExp = null;
                    foreach (var plainEverything in plainEverythingSegments)
                    {
                        if (rawNameExp == null)
                        {
                            rawNameExp = a => a.RawName.Contains(plainEverything);
                        }
                        else
                        {
                            var keywordExp = rawNameExp;
                            rawNameExp = a =>
                                keywordExp(a) && a.RawName.Contains(plainEverything);
                        }
                    }

                    if (regexs.Any())
                    {
                        var regexedPublisherIds = await _publisherService.GetAllIdsByRegexs(regexs);
                        publisherIds = publisherIds.Intersect(regexedPublisherIds).ToList();

                        var regexedOriginalIds = await _originalService.GetAllIdsByRegexs(regexs);
                        originalIds = originalIds.Intersect(regexedOriginalIds).Distinct().ToList();

                        // todo: Bad design: this will be broken if orm is changed to local db
                        foreach (var r in regexs)
                        {
                            if (rawNameExp == null)
                            {
                                rawNameExp = a => Regex.IsMatch(a.RawName, r);
                            }
                            else
                            {
                                var rawNameExp1 = rawNameExp;
                                rawNameExp = a => rawNameExp1(a) && Regex.IsMatch(a.RawName, r);
                            }
                        }
                    }

                    var publisherRelatedResourceIds =
                        (await _publisherMappingService.GetAll(a => publisherIds.Contains(a.PublisherId)))
                        .Select(a => a.ResourceId).Distinct().ToList();
                    var originalRelatedResourceIds =
                        (await _originalMappingService.GetAll(a => originalIds.Contains(a.OriginalId)))
                        .Select(a => a.ResourceId).Distinct().ToList();
                    var customPropertyRelatedResourceIds =
                        await _customResourcePropertyService.SearchResourceIdsByEverything(model.Everything);

                    var resourceIds = publisherRelatedResourceIds.Concat(originalRelatedResourceIds)
                        .Concat(customPropertyRelatedResourceIds).ToHashSet();
                    var exp1 = exp;


                    if (rawNameExp == null)
                    {
                        exp = a => exp1(a) && (resourceIds.Contains(a.Id) || aliasNames.Contains(a.Name));
                    }
                    else
                    {
                        exp = a => exp1(a) && (resourceIds.Contains(a.Id) || aliasNames.Contains(a.Name) ||
                                               rawNameExp(a));
                    }
                }
            }

            if (model.TagIds?.Any() == true)
            {
                var resourceIds = (await _resourceTagMappingService.GetAll(a => model.TagIds.Contains(a.TagId)))
                    .Select(a => a.ResourceId).Distinct().ToList();
                var exp1 = exp;
                exp = a => exp1(a) && resourceIds.Contains(a.Id);
            }

            if (model.ExcludedTagIds?.Any() == true)
            {
                var invalidResourceIds =
                    (await _resourceTagMappingService.GetAll(a => model.ExcludedTagIds.Contains(a.TagId)))
                    .Select(a => a.ResourceId).Distinct().ToList();
                var exp1 = exp;
                exp = a => exp1(a) && !invalidResourceIds.Contains(a.Id);
            }

            if (model.CustomProperties?.Any() == true)
            {
                var resourceIds = await _customResourcePropertyService.SearchResourceIds(model.CustomProperties);
                var exp1 = exp;
                exp = a => exp1(a) && resourceIds.Contains(a.Id);
            }

            var orders = new List<(Func<Resource, object> SelectKey, bool Asc)>();
            if (model.Orders != null)
            {
                orders.AddRange(from om in model.Orders
                    let o = om.Order
                    let a = om.Asc
                    let s = (Func<Resource, object>) (o switch
                    {
                        ResourceSearchOrder.AddDt => x => x.CreateDt,
                        ResourceSearchOrder.ReleaseDt => x => x.ReleaseDt,
                        ResourceSearchOrder.Rate => x => x.Rate,
                        ResourceSearchOrder.Category => x => x.CategoryId,
                        ResourceSearchOrder.MediaLibrary => x => x.MediaLibraryId,
                        ResourceSearchOrder.Name => x => x.Name,
                        ResourceSearchOrder.FileCreateDt => x => x.FileCreateDt,
                        ResourceSearchOrder.FileModifyDt => x => x.FileModifyDt,
                        ResourceSearchOrder.Filename => x => x.RawName,
                        _ => throw new ArgumentOutOfRangeException()
                    })
                    select (s, a));
            }

            if (!orders.Any())
            {
                orders.Add((t => t.RawName, true));
            }

            var resources = await _orm.Search(exp, model.PageIndex, model.PageSize, orders.ToArray(), returnCopy);
            var dtoList = await ToDto(resources.Data.ToArray(), ResourceAdditionalItem.All);

            if (model.Save)
            {
                await _optionsManager.SaveAsync(a => a.LastSearch = model.ToOptions());
            }

            return model.BuildResponse(dtoList, resources.TotalCount);
        }

        public async Task<List<Resource>> GetAll(Expression<Func<Resource, bool>> selector = null,
            bool returnCopy = true) => await _orm.GetAll(selector, returnCopy);

        public async Task<Resource> GetByKey(int id, bool returnCopy)
        {
            var resource = await _orm.GetByKey(id, returnCopy);
            return resource;
        }

        public async Task<ResourceDto?> GetByKey(int id, ResourceAdditionalItem additionalItems, bool returnCopy)
        {
            var resource = await _orm.GetByKey(id, returnCopy);
            return await ToDto(resource, additionalItems);
        }

        public async Task<List<ResourceDto>> GetByKeys(int[] ids)
        {
            var resources = await _orm.GetByKeys(ids);
            var dtoList = await ToDto(resources, ResourceAdditionalItem.All);
            return dtoList;
        }

        public async Task<ResourceDto> ToDto(Resource resource,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            return (await ToDto(new[] {resource}, ResourceAdditionalItem.All)).FirstOrDefault();
        }

        public async Task<List<ResourceDto>> ToDto(Resource[] resources,
            ResourceAdditionalItem additionalItems = ResourceAdditionalItem.None)
        {
            var resourceIds = resources.Select(a => a.Id).ToList();

            Dictionary<int, List<PublisherDto>> publisherPool = null;
            Dictionary<int, VolumeDto> volumePool = null;
            Dictionary<int, SeriesDto> seriesPool = null;
            Dictionary<int, List<OriginalDto>> originalPool = null;
            Dictionary<int, List<TagDto>> tagPool = null;
            Dictionary<int, List<CustomResourceProperty>> customPropertyPool = null;

            foreach (var i in SpecificEnumUtils<ResourceAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(i))
                {
                    switch (i)
                    {
                        case ResourceAdditionalItem.Publishers:
                            publisherPool = await _publisherService.GetByResourceIds(resourceIds);
                            break;
                        case ResourceAdditionalItem.Volume:
                            volumePool = await _volumeService.GetByResourceKeys(resourceIds);
                            break;
                        case ResourceAdditionalItem.Serial:
                            if (volumePool != null)
                            {
                                var serialIds = volumePool?.Values.Where(a => a != null).Select(a => a.SerialId)
                                    .Distinct()
                                    .ToList();
                                var serials = await _serialService.GetByKeys(serialIds);
                                seriesPool = resourceIds.ToDictionary(a => a,
                                    a => volumePool.TryGetValue(a, out var v) && v != null &&
                                         serials.TryGetValue(v.SerialId, out var s)
                                        ? s
                                        : null);
                            }

                            break;
                        case ResourceAdditionalItem.Originals:
                            originalPool = await _originalService.GetByResourceIds(resourceIds);
                            break;
                        case ResourceAdditionalItem.Tags:
                            tagPool = await _tagService.GetByResourceIds(resourceIds);
                            break;
                        case ResourceAdditionalItem.CustomProperties:
                            customPropertyPool =
                                (await _customResourcePropertyService.GetAll(t => resourceIds.Contains(t.ResourceId)))
                                .GroupBy(t => t.ResourceId).ToDictionary(t => t.Key, t => t.ToList());
                            break;
                        case ResourceAdditionalItem.Alias:
                            break;
                        case ResourceAdditionalItem.None:
                        case ResourceAdditionalItem.All:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            // HasChildren
            var children = await _orm.GetAll(a => a.ParentId.HasValue && resourceIds.Contains(a.ParentId.Value));
            var parentIds = children.Select(a => a.ParentId.Value).ToHashSet();

            var dtoList = resources.Select(a =>
            {
                var dto = a.ToDto();
                if (publisherPool?.TryGetValue(dto.Id, out var ps) == true)
                {
                    dto.Publishers = ps;
                }

                if (originalPool?.TryGetValue(dto.Id, out var os) == true)
                {
                    dto.Originals = os;
                }

                if (seriesPool?.TryGetValue(dto.Id, out var s) == true)
                {
                    dto.Series = s;
                }

                if (volumePool?.TryGetValue(dto.Id, out var v) == true)
                {
                    dto.Volume = v;
                }

                if (tagPool?.TryGetValue(dto.Id, out var ts) == true)
                {
                    dto.Tags = ts;
                }

                if (customPropertyPool?.TryGetValue(dto.Id, out var cps) == true)
                {
                    dto.CustomProperties = cps.GroupBy(t => t.Key).ToDictionary(t => t.Key, t => t.ToList());
                }

                dto.HasChildren = parentIds.Contains(dto.Id);

                return dto;
            }).ToList();

            if (additionalItems.HasFlag(ResourceAdditionalItem.Alias))
            {
                await ReplaceWithPreferredAlias(dtoList);
            }

            return dtoList;
        }

        public async Task<List<Resource>> GetAllEntities(Expression<Func<Resource, bool>> selector = null,
            bool returnCopy = true)
        {
            return await _orm.GetAll(selector, returnCopy);
        }

        // /// <summary>
        // /// Save core data only. Properties will not be saved.
        // /// </summary>
        // /// <param name="resources"></param>
        // /// <returns></returns>
        // public async Task SaveOnSynchronization(IEnumerable<Resource> resources)
        // {
        //     var array = resources.ToArray();
        //     var updates = array.Where(t => t.Id > 0).ToArray();
        //     var @new = array.Except(updates).ToList();
        //     await _orm.UpdateRange(updates);
        //     await _orm.AddRange(@new);
        // }

        /// <summary>
        /// <para>Save all resources and properties at once.</para>
        /// <para>If resource exists, its core properties will be updated, and other properties will be kept or replaced by their identity information such as Name.</para>
        /// <para>We do not update other properties by their id and name, because it's hard to ensure all occurrences of a property have been modified identically.</para>
        /// </summary>
        /// <param name="srs"></param>
        /// <returns></returns>
        public async Task<ResourceRangeAddOrUpdateResult> AddOrUpdateRange(List<ResourceDto> srs)
        {
            var tmpResources = srs?.ToList() ?? new List<ResourceDto>();
            var simpleResourceMap = new Dictionary<string, ResourceDto>();
            foreach (var s in tmpResources)
            {
                s.Clean();
                if (!simpleResourceMap.ContainsKey(s.RawFullname))
                {
                    simpleResourceMap[s.RawFullname] = s;
                }
            }

            var parents = tmpResources.Select(a => a.Parent).Where(a => a != null).GroupBy(a => a.RawFullname)
                .Select(a => a.FirstOrDefault()).ToList();
            if (parents.Any())
            {
                await AddOrUpdateRange(parents);
            }

            await _addOrUpdateLock.WaitAsync();
            try
            {
                // Aliases
                var names = tmpResources.Select(a => a.Name).Where(a => a.IsNotEmpty()).ToHashSet();
                var publisherNames = tmpResources.SelectMany(a => a.Publishers.GetNames()).Distinct().ToList();
                var originalNames = tmpResources.Where(a => a.Originals != null)
                    .SelectMany(a => a.Originals.Select(b => b.Name))
                    .Distinct().ToList();
                var allNames = names.Concat(publisherNames).Concat(originalNames).ToList();
                var aliasAddResult = await _aliasService.AddRange(allNames);

                // Resources
                var resources = tmpResources.Select(a => a.ToEntity()).ToList();
                var existedResources = resources.Where(a => a.Id > 0).ToList();
                var newResources = resources.Except(existedResources).ToList();
                await _orm.UpdateRange(existedResources);
                resources = (await _orm.AddRange(newResources)).Data.Concat(existedResources).ToList();
                resources.ForEach(a => { simpleResourceMap[a.RawFullname].Id = a.Id; });

                // Publishers
                // Changed items will be replaced instead of being updated. 
                var resourcesWithPublishers = tmpResources.Where(a => a.Publishers != null).ToList();
                var publisherRangeAddResult =
                    await _publisherService.AddAll(resourcesWithPublishers.SelectMany(a => a.Publishers).ToList());
                var publisherIds = publisherRangeAddResult.Data.ToDictionary(a => a.Key, a => a.Value.Id);
                var publisherMappings = resourcesWithPublishers.SelectMany(s =>
                {
                    s.Publishers.PopulateId(publisherIds);
                    return s.Publishers.BuildMappings(s.Id);
                }).ToList();
                await _publisherMappingService.AddRange(publisherMappings);

                // Series
                // Changed items will be replaced instead of being updated.
                var allSeries = tmpResources.Select(a => a.Series).Where(a => a != null).ToArray();
                var seriesNames = allSeries.Select(t => t.Name).Where(t => t.IsNotEmpty()).ToList();
                if (seriesNames.Any())
                {
                    var seriesRangeAddResult = await _serialService.AddRange(seriesNames);
                    foreach (var s in allSeries)
                    {
                        s.Id = seriesRangeAddResult.Data[s.Name].Id;
                    }
                }

                // Volumes
                var allVolumes = new List<Volume>();
                foreach (var r in tmpResources)
                {
                    if (r.Volume != null || r.Series != null)
                    {
                        r.Volume ??= new VolumeDto();
                        r.Volume.ResourceId = r.Id;
                        if (r.Series != null)
                        {
                            r.Volume.SerialId = r.Series.Id;
                        }

                        allVolumes.Add(r.Volume.ToResource());
                    }
                }

                var volumeRangeAddResult = await _volumeService.AddRange(allVolumes);

                // Originals
                var originalRangeAddResult = await _originalService.AddRange(originalNames);
                var originalMappings = tmpResources.Where(a => a.Originals != null).SelectMany(a =>
                    a.Originals.Select(b =>
                        new OriginalResourceMapping
                        {
                            OriginalId = originalRangeAddResult.Data[b.Name].Id,
                            ResourceId = a.Id
                        })).ToList();
                await _originalMappingService.AddRange(originalMappings);

                // Tags
                // Cares name and group name only
                var tags = tmpResources.Where(a => a.Tags != null).SelectMany(a => a.Tags).Where(a => a.Id == 0)
                    .Distinct(TagGroupNameAndNameEqualityComparer.Instance).ToArray();
                var savedTags = await _tagService.AddRangeByNameAndGroupName(tags, false);
                var resourceTagsMap = tmpResources.Where(a => a.Tags != null).ToDictionary(a => a.Id,
                    a => a.Tags.Select(b =>
                    {
                        if (b.Id > 0)
                        {
                            return b.Id;
                        }

                        var tag = savedTags.FirstOrDefault(t =>
                            TagGroupNameAndNameEqualityComparer.Instance.Equals(b, t));
                        if (tag == null)
                        {
#if DEBUG
                            Debugger.Break();
#endif
                            _logger.LogWarning(
                                $"Tag [{b.GroupName}:{b.Name}] is not found in saved tags: {string.Join(',', savedTags.Select(t => $"{t.GroupName}:{t.Name}"))}");
                        }

                        return tag?.Id;
                    }).Where(b => b.HasValue).Select(b => b.Value).ToArray());
                await _resourceTagMappingService.UpdateRange(resourceTagsMap);

                // Custom Properties
                var resourceIds = tmpResources.Select(t => t.Id).ToList();
                var existedCustomProperties =
                    (await _customResourcePropertyService.GetAll(t => resourceIds.Contains(t.ResourceId)))
                    .GroupBy(t => t.ResourceId).ToDictionary(t => t.Key,
                        t => t.GroupBy(a => a.Key).ToDictionary(a => a.Key, a => a.ToList()));
                var invalidProperties = new List<CustomResourceProperty>();
                var newProperties = new List<CustomResourceProperty>();
                var changedProperties = new List<CustomResourceProperty>();
                foreach (var r in tmpResources)
                {
                    r.CustomProperties ??= new Dictionary<string, List<CustomResourceProperty>>();
                    if (!existedCustomProperties.TryGetValue(r.Id, out var existedProperties))
                    {
                        existedProperties = new();
                    }

                    // Invalid
                    var invalidKeys = new HashSet<string>();
                    foreach (var (key, list) in existedProperties)
                    {
                        if (!r.CustomProperties.ContainsKey(key))
                        {
                            invalidProperties.AddRange(list);
                            invalidKeys.Add(key);
                        }
                    }

                    foreach (var (key, list) in r.CustomProperties)
                    {
                        if (!invalidKeys.Contains(key))
                        {
                            if (existedProperties.TryGetValue(key, out var exists))
                            {
                                var sortedList = list.OrderBy(t => t.Index).ToList();
                                var sortedExists = exists.OrderBy(t => t.Index).ToList();
                                for (var i = 0; i < sortedList.Count; i++)
                                {
                                    if (sortedExists.Count > i)
                                    {
                                        // Update
                                        sortedList[i].Id = sortedExists[i].Id;
                                        changedProperties.Add(sortedList[i]);
                                    }
                                    else
                                    {
                                        // New
                                        sortedList[i].ResourceId = r.Id;
                                        newProperties.Add(sortedList[i]);
                                    }
                                }
                            }
                            else
                            {
                                list.ForEach(l => l.ResourceId = r.Id);
                                newProperties.AddRange(list);
                            }
                        }
                    }
                }

                await _customResourcePropertyService.RemoveRange(invalidProperties);
                await _customResourcePropertyService.AddRange(newProperties);
                await _customResourcePropertyService.UpdateRange(changedProperties);

                return new ResourceRangeAddOrUpdateResult
                {
                    AliasCount = aliasAddResult.Count,
                    NewAliasCount = aliasAddResult.AddedCount,

                    ResourceCount = resources.Count,
                    NewResourceCount = newResources.Count,

                    PublisherCount = publisherRangeAddResult.Count,
                    NewPublisherCount = publisherRangeAddResult.AddedCount,

                    VolumeCount = volumeRangeAddResult.Count,
                    NewVolumeCount = volumeRangeAddResult.AddedCount,

                    OriginalCount = originalRangeAddResult.Count,
                    NewOriginalCount = originalRangeAddResult.AddedCount,
                };
            }
            finally
            {
                _addOrUpdateLock.Release();
            }
        }

        /// <summary>
        /// 包含**当前**组内部比对，可能会缩减组数
        /// </summary>
        /// <returns></returns>

        private async Task ReplaceWithPreferredAlias(IReadOnlyCollection<ResourceDto> resources)
        {
            var names = resources.SelectMany(a =>
            {
                var l = new List<string> {a.Name};
                l.AddRange(a.Publishers.Extract().Select(b => b.Name));
                l.Add(a.Volume?.Name);
                l.Add(a.Series?.Name);
                if (a.Originals != null)
                {
                    l.AddRange(a.Originals.Select(b => b.Name));
                }

                if (a.Tags != null)
                {
                    l.AddRange(a.Tags.Select(b => b.Name).Concat(a.Tags.Select(b => b.GroupName)));
                }

                return l;
            }).Where(a => a.IsNotEmpty()).ToHashSet();
            var preferredAliases = await _aliasService.GetPreferredNames(names);
            foreach (var dto in resources)
            {
                if (dto.Name.IsNotEmpty())
                {
                    dto.Name = preferredAliases[dto.Name];
                }

                dto.Publishers.ReplaceNames(preferredAliases);
                if (dto.Volume?.Name.IsNotEmpty() == true)
                {
                    dto.Volume.Name = preferredAliases[dto.Volume.Name];
                }

                if (dto.Series?.Name.IsNotEmpty() == true)
                {
                    dto.Series.Name = preferredAliases[dto.Series.Name];
                }

                dto.Originals?.ForEach(o => o.Name = preferredAliases[o.Name]);

                dto.Tags?.ForEach(a =>
                {
                    a.PreferredAlias = preferredAliases[a.Name];
                    if (a.GroupName.IsNotEmpty())
                    {
                        a.GroupNamePreferredAlias = preferredAliases[a.GroupName];
                    }
                });
            }
        }

        public async Task DiscardCoverAndFindAnotherOne(
            ResourceCoverDiscardAndAutomaticallyFindAnotherOneRequestModel model)
        {
            var categories = (await _categoryService.GetAllDto()).ToDictionary(a => a.Id, a => a);
            var resources = await _orm.GetByKeys(model.Ids);
            var libraries =
                (await _mediaLibraryService.GetByKeys(resources.Select(a => a.MediaLibraryId).ToHashSet()))
                .ToDictionary(a => a.Id, a => a);
            foreach (var resource in resources.Where(a => !a.IsSingleFile))
            {
                var coverSelectionOrder = CoverSelectOrder.FilenameAscending;
                if (categories.TryGetValue(resource.CategoryId, out var c))
                {
                    coverSelectionOrder = c.CoverSelectionOrder;
                }

                var coverDiscoverResult = await DiscoverCover(resource.RawFullname, new CancellationToken(),
                    coverSelectionOrder, null, false);

                if (coverDiscoverResult?.Type == CoverDiscoverResultType.LocalFile)
                {
                    FileUtils.Delete(coverDiscoverResult.FileFullname, true, true);
                }
            }
        }

        public async Task<CoverDiscoverResult?> DiscoverAndPopulateCoverStream(int id, CancellationToken ct)
        {
            var r = await GetByKey(id, ResourceAdditionalItem.None, false);
            if (r != null)
            {
                var options = _optionsManager.Value;
                var c = await _categoryService.GetByKey(r.CategoryId, ResourceCategoryAdditionalItem.None);
                var cr = await DiscoverCover(r.RawFullname, ct,
                    c?.CoverSelectionOrder ?? CoverSelectOrder.FilenameAscending,
                    options.AdditionalCoverDiscoveringSources, true);
                return cr;
            }

            return null;
        }

        public async Task<string[]> GetPlayableFiles(int id, CancellationToken ct)
        {
            var r = await GetByKey(id, ResourceAdditionalItem.None, false);
            if (r != null)
            {
                var selector = await _categoryService.GetFirstComponent<IPlayableFileSelector>(r.CategoryId,
                    ComponentType.PlayableFileSelector);
                if (selector.Data != null)
                {
                    var files = await selector.Data.GetStartFiles(r.RawFullname, ct);
                    return files.Select(f => f.StandardizePath()!).ToArray();
                }
            }

            return null;
        }

        public async Task<Dictionary<string, string[]>> GetAllReservedPropertiesAndCandidates()
        {
            var resourceIds = (await _orm.GetAll(null, false)).Select(t => t.Id).ToHashSet();
            var data = new Dictionary<ReservedResourceProperty, string[]>();

            foreach (var property in SpecificEnumUtils<ReservedResourceProperty>.Values)
            {
                switch (property)
                {
                    case ReservedResourceProperty.ReleaseDt:
                        break;
                    case ReservedResourceProperty.Publisher:
                    {
                        var publisherMappings = await _publisherMappingService.GetAll();
                        var publisherIds = publisherMappings.Where(t => resourceIds.Contains(t.ResourceId))
                            .Select(t => t.PublisherId).ToArray();
                        var publishers = await _publisherService.GetNamesByIds(publisherIds);
                        data[property] = publishers;
                        break;
                    }
                    case ReservedResourceProperty.Name:
                    {
                        var names = (await _orm.GetAll(null, false)).Select(t => t.Name).Distinct().ToArray();
                        data[property] = names;
                        break;
                    }
                    case ReservedResourceProperty.Language:
                        break;
                    case ReservedResourceProperty.Volume:
                        break;
                    case ReservedResourceProperty.Original:
                    {
                        var originalMappings = await _originalMappingService.GetAll();
                        var originalIds = originalMappings.Where(t => resourceIds.Contains(t.ResourceId))
                            .Select(t => t.OriginalId).ToArray();
                        var originals = await _originalService.GetNamesByIds(originalIds);
                        data[property] = originals;
                        break;
                    }
                    case ReservedResourceProperty.Series:
                    {
                        var seriesIds = (await _volumeService.GetByResourceKeys(resourceIds.ToList())).Values
                            .Select(t => t?.SerialId).Where(t => t.HasValue).Select(t => t.Value).Distinct().ToArray();
                        var series = await _serialService.GetNamesByIds(seriesIds);
                        data[property] = series;
                        break;
                    }
                    case ReservedResourceProperty.Tag:
                        break;
                    case ReservedResourceProperty.Introduction:
                        break;
                    case ReservedResourceProperty.Rate:
                        break;
                    default:
                        break;
                }
            }

            return data.ToDictionary(t => t.Key.ToString(), t => t.Value);
        }

        public async Task<CoverDiscoverResult> DiscoverCover(string path, CancellationToken ct, CoverSelectOrder order,
            AdditionalCoverDiscoveringSource[] additionalSources, bool populateCoverStream)
        {
            var imageExtensions = BusinessConstants.ImageExtensions;

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return null;
            }

            var attr = File.GetAttributes(path);
            var files = attr.HasFlag(FileAttributes.Directory)
                ? new DirectoryInfo(path).GetFiles("*.*", SearchOption.AllDirectories)
                : new[] {new FileInfo(path)};
            switch (order)
            {
                case CoverSelectOrder.FileModifyDtDescending:
                    files = files.OrderByDescending(a => a.LastWriteTime).ToArray();
                    break;
                case CoverSelectOrder.FilenameAscending:
                default:
                    break;
            }

            // Find cover.{ext}
            var coverImg = files.FirstOrDefault(t =>
                imageExtensions.Any(e => t.Name.Equals($"cover{e}", StringComparison.OrdinalIgnoreCase)));
            if (coverImg != null)
            {
                return CoverDiscoverResult.FromLocalFile(path, coverImg.FullName, populateCoverStream);
            }

            // Find image first
            var firstImage =
                files.FirstOrDefault(a => imageExtensions.Contains(a.Extension, StringComparer.OrdinalIgnoreCase));
            if (firstImage != null)
            {
                return CoverDiscoverResult.FromLocalFile(path, firstImage.FullName, populateCoverStream);
            }

            // additional sources
            if (additionalSources != null)
            {
                foreach (var @as in additionalSources)
                {
                    switch (@as)
                    {
                        case AdditionalCoverDiscoveringSource.Video:
                        {
                            if (string.IsNullOrEmpty(_thirdPartyOptions.Value.FFmpeg?.BinDirectory))
                            {
                                continue;
                            }

                            var firstVideoFile = files.FirstOrDefault(t =>
                                BusinessConstants.VideoExtensions.Contains(Path.GetExtension(t.Name)));
                            if (firstVideoFile != null)
                            {
                                await FindCoverInVideoSm.WaitAsync(ct);
                                try
                                {
                                    var durationSeconds = await _ffMpegHelper.GetDuration(firstVideoFile.FullName, ct);

                                    var duration = TimeSpan.FromSeconds(durationSeconds);
                                    const string ssExt = ".png";
                                    // var duration = info.Duration;
                                    var screenshotTime = duration * 0.2;
                                    var isTmp = (attr & FileAttributes.Directory) == 0;
                                    var tmpFile = !isTmp
                                        ? Path.Combine(path, $"cover{ssExt}")
                                        : Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{ssExt}");
                                    await _ffMpegHelper.CaptureFrame(firstVideoFile.FullName, screenshotTime, tmpFile,
                                        ct);
                                    var ms = new MemoryStream();
                                    var fs = File.OpenRead(tmpFile);
                                    await fs.CopyToAsync(ms, ct);
                                    await fs.DisposeAsync();
                                    if (isTmp)
                                    {
                                        try
                                        {
                                            File.Delete(tmpFile);
                                        }
                                        catch (Exception e)
                                        {
                                            _logger.LogError(e, "An error occurred during deleting temp cover file");
                                        }
                                    }

                                    ms.Seek(0, SeekOrigin.Begin);
                                    return CoverDiscoverResult.FromAdditionalSource(path, tmpFile,
                                        $"{screenshotTime:hh\\-mm\\-ss\\-fff}{ssExt}", ms);
                                }
                                catch (Exception e)
                                {
                                    _logger.LogError(e, "An error occurred during capture a frame from video file");

                                }
                                finally
                                {
                                    FindCoverInVideoSm.Release();
                                }
                            }

                            break;
                        }
                        case AdditionalCoverDiscoveringSource.CompressedFile:
                        {
                            // todo: catch exception
                            const int maxTryTimes = 1;
                            var tryTimes = 0;
                            for (var i = 0; i < files.Length && tryTimes < maxTryTimes; i++)
                            {
                                var file = files[i];
                                if (file.Length > 0)
                                {
                                    if (BusinessConstants.CompressedFileExtensions.Contains(file.Extension))
                                    {
                                        try
                                        {
                                            MemoryStream imageStream = null;
                                            string key = null;

                                            if (BusinessConstants.SevenZipCompressedFileExtension.Equals(file.Extension,
                                                    StringComparison.OrdinalIgnoreCase))
                                            {
                                                var archive = SevenZipArchive.Open(file.FullName);
                                                var imageFile = archive.Entries
                                                    .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                                                    .FirstOrDefault(a =>
                                                        imageExtensions.Contains(Path.GetExtension(a.Key)));
                                                if (imageFile != null)
                                                {
                                                    key = imageFile.Key;
                                                    if (populateCoverStream)
                                                    {
                                                        await using var s = imageFile.OpenEntryStream();
                                                        await s.CopyToAsync(imageStream = new MemoryStream(), ct);
                                                        imageStream.Seek(0, SeekOrigin.Begin);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                string firstFileKey = null;
                                                await using (Stream stream = file.OpenRead())
                                                {
                                                    // reader.MoveToNextEntry will be broken after reader.OpenEntryStream being called,
                                                    // so we do not store entry stream there.
                                                    using (var reader = ReaderFactory.Open(stream))
                                                    {
                                                        while (reader.MoveToNextEntry())
                                                        {
                                                            if (imageExtensions.Contains(
                                                                    Path.GetExtension(reader.Entry.Key)))
                                                            {
                                                                if (firstFileKey == null ||
                                                                    string.Compare(reader.Entry.Key, firstFileKey,
                                                                        StringComparison.OrdinalIgnoreCase) < 0)
                                                                {
                                                                    firstFileKey = reader.Entry.Key;
                                                                }
                                                            }
                                                        }
                                                    }
                                                }

                                                if (firstFileKey != null)
                                                {
                                                    key = firstFileKey;
                                                    if (populateCoverStream)
                                                    {
                                                        await using Stream stream = file.OpenRead();
                                                        using var reader = ReaderFactory.Open(stream);
                                                        while (reader.MoveToNextEntry())
                                                        {
                                                            if (reader.Entry.Key == firstFileKey)
                                                            {
                                                                await using var entryStream = reader.OpenEntryStream();
                                                                await entryStream.CopyToAsync(
                                                                    imageStream = new MemoryStream(), ct);
                                                                imageStream.Seek(0, SeekOrigin.Begin);
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            if (!populateCoverStream || imageStream != null)
                                            {
                                                return CoverDiscoverResult.FromAdditionalSource(path, file.FullName,
                                                    key,
                                                    imageStream);
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            _logger.LogError(e,
                                                $"An error occurred during discovering covers from compressed files: {e.Message}");
                                        }
                                        finally
                                        {
                                            tryTimes++;
                                        }

                                    }
                                }
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            using var icon = File.Exists(path) ? Icon.ExtractAssociatedIcon(path) : DefaultIcons.GetStockIcon(3, 0x04);

            if (icon != null)
            {
                return CoverDiscoverResult.FromIcon(icon);
            }

            return null;
        }

        /// <summary>
        /// todo: move to LazyMortal when it's ready
        /// </summary>
        public static class DefaultIcons
        {
            private static Icon? folderIcon;

            public static Icon FolderLarge => folderIcon ??= GetStockIcon(SHSIID_FOLDER, SHGSI_LARGEICON);

            public static Icon GetStockIcon(uint type, uint size)
            {
                var info = new SHSTOCKICONINFO();
                info.cbSize = (uint) Marshal.SizeOf(info);

                SHGetStockIconInfo(type, SHGSI_ICON | size, ref info);

                var icon = (Icon) Icon.FromHandle(info.hIcon)
                    .Clone(); // Get a copy that doesn't use the original handle
                DestroyIcon(info.hIcon); // Clean up native icon to prevent resource leak

                return icon;
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct SHSTOCKICONINFO
            {
                public uint cbSize;
                public IntPtr hIcon;
                public int iSysIconIndex;
                public int iIcon;

                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
                public string szPath;
            }

            [DllImport("shell32.dll")]
            public static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);

            [DllImport("user32.dll")]
            public static extern bool DestroyIcon(IntPtr handle);

            private const uint SHSIID_FOLDER = 0x3;
            private const uint SHGSI_ICON = 0x100;
            private const uint SHGSI_LARGEICON = 0x0;
            private const uint SHGSI_SMALLICON = 0x1;
        }

        public async Task<List<ResourceDto>> GetNfoGenerationNeededResources(int[] resourceIds)
        {
            var categories = await _categoryService.GetAll(t => t.GenerateNfo, true);
            var categoryIds = categories.Select(t => t.Id).ToHashSet();
            var resources = await GetByKeys(resourceIds);
            return resources.Where(t => categoryIds.Contains(t.CategoryId)).ToList();
        }

        public async Task BatchUpdateTags(ResourceTagUpdateRequestModel model)
        {
            await _resourceTagMappingService.UpdateRange(model.ResourceTagIds);
            await RunBatchSaveNfoBackgroundTask(model.ResourceTagIds.Keys.ToArray(),
                $"{nameof(ResourceService)}:BatchUpdateTags", true);
        }

        public async Task SaveNfo(ResourceDto resource, bool overwrite, CancellationToken ct = new())
        {
            var nfoFullname = ResourceNfoService.GetFullname(resource);
            if (!resource.EnoughToGenerateNfo())
            {
                if (File.Exists(nfoFullname))
                {
                    File.Delete(nfoFullname);
                }

                return;
            }

            if (!overwrite)
            {
                if (File.Exists(nfoFullname))
                {
                    return;
                }
            }

            var directory = Path.GetDirectoryName(nfoFullname);
            if (!Directory.Exists(directory))
            {
                return;
            }

            var xml = ResourceNfoService.Serialize(resource);
            await using (var fs = new FileStream(nfoFullname, FileMode.OpenOrCreate))
            {
                fs.Seek(0, SeekOrigin.Begin);
                await using (TextWriter tw = new StreamWriter(fs, Encoding.UTF8, 1024, true))
                {
                    await tw.WriteAsync(xml);
                }

                fs.SetLength(fs.Position);
            }

            File.SetAttributes(nfoFullname, File.GetAttributes(nfoFullname) | FileAttributes.Hidden);
        }

        private const string NfoGenerationTaskName = $"{nameof(ResourceService)}:NfoGeneration";

        public async Task TryToGenerateNfoInBackground()
        {
            if (!_backgroundTaskManager.IsRunningByName(NfoGenerationTaskName))
            {
                var categories = await _categoryService.GetAll(t => t.GenerateNfo, true);
                if (categories.Any())
                {
                    _backgroundTaskHelper.RunInNewScope<ResourceService>(NfoGenerationTaskName,
                        async (service, task) => await service.StartGeneratingNfo(task));
                }
            }
        }

        public async Task RunBatchSaveNfoBackgroundTask(int[] resourceIds, string backgroundTaskName, bool overwrite)
        {
            var resources = await GetNfoGenerationNeededResources(resourceIds);
            if (resources.Any())
            {
                _backgroundTaskHelper.RunInNewScope<ResourceService>(backgroundTaskName, async (service, task) =>
                {
                    for (var i = 0; i < resources.Count; i++)
                    {
                        var resource = resources[i];
                        await service.SaveNfo(resource, overwrite, task.Cts.Token);
                        task.Percentage = (i + 1) * 100 / resources.Count;
                    }

                    return BaseResponseBuilder.Ok;
                }, BackgroundTaskLevel.Critical);
            }
        }

        public async Task<BaseResponse> StartGeneratingNfo(BackgroundTask task)
        {
            var categories = await _categoryService.GetAll();
            var totalCount = 0;
            var doneCount = 0;
            foreach (var c in categories)
            {
                task.Cts.Token.ThrowIfCancellationRequested();
                var category = await _categoryService.GetByKey(c.Id);
                if (category.GenerateNfo)
                {
                    var resources = (await Search(new ResourceSearchDto
                    {
                        CategoryId = c.Id,
                        PageSize = int.MaxValue
                    }, true)).Data;
                    totalCount += resources.Count;
                    foreach (var r in resources)
                    {
                        task.Cts.Token.ThrowIfCancellationRequested();
                        await SaveNfo(r, false, task.Cts.Token);
                        doneCount++;
                        task.Percentage = doneCount * 100 / totalCount;
                    }
                }
            }

            await _optionsManager.SaveAsync(t => t.LastNfoGenerationDt = DateTime.Now);
            return BaseResponseBuilder.Ok;
        }

        public async Task PopulateStatistics(DashboardStatistics statistics)
        {
            var categories = (await _categoryService.GetAll()).ToDictionary(a => a.Id, a => a.Name);
            var allEntities = await GetAllEntities();

            var totalCount = allEntities.GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.NameAndCount
                {
                    Name = categories.TryGetValue(a.Key, out var c) ? c : string.Empty,
                    Count = a.Count()
                }).ToArray();

            var today = DateTime.Today;
            var todayCount = allEntities.Where(a => a.CreateDt >= today).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.NameAndCount
                {
                    Name = categories.TryGetValue(a.Key, out var c) ? c : string.Empty,
                    Count = a.Count()
                }).ToArray();

            var weekdayDiff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var monday = today.AddDays(-1 * weekdayDiff);
            var thisWeekCount = allEntities.Where(a => a.CreateDt >= monday).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.NameAndCount
                {
                    Name = categories.TryGetValue(a.Key, out var c) ? c : string.Empty,
                    Count = a.Count()
                }).ToArray();

            statistics.TotalResourceCounts = totalCount;
            statistics.ResourceAddedCountsToday = todayCount;
            statistics.ResourceAddedCountsThisWeek = thisWeekCount;
        }
    }
}