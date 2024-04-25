using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Resource.Components;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations.Models.Db;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Cryptography;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Humanizer.Localisation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Enhancement = Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures.Enhancement;
using Resource = Bakabase.Abstractions.Models.Db.Resource;

namespace Bakabase.InsideWorld.Business.Services
{
    public class EnhancementRecordService : ResourceService<InsideWorldDbContext, EnhancementRecord, int>
    {
        protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();
        protected InsideWorldLocalizer InsideWorldLocalizer => GetRequiredService<InsideWorldLocalizer>();
        protected ResourceService ResourceService => GetRequiredService<ResourceService>();
        protected TagGroupService TagGroupService => GetRequiredService<TagGroupService>();
        protected TagService TagService => GetRequiredService<TagService>();
        protected LogService LogService => GetRequiredService<LogService>();
        protected BackgroundTaskHelper BackgroundTaskHelper => GetRequiredService<BackgroundTaskHelper>();
        protected TempFileManager TempFileManager => GetRequiredService<TempFileManager>();

        protected CustomResourcePropertyService CustomResourcePropertyService =>
            GetRequiredService<CustomResourcePropertyService>();

        protected IBOptions<ResourceOptions> ResourceOptions => GetRequiredService<IBOptions<ResourceOptions>>();

        protected BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
        protected ComponentService ComponentService => GetRequiredService<ComponentService>();

        public EnhancementRecordService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public const string EnhanceBackgroundTaskName = "EnhancementService:Enhance";

        public async Task<EnhancementRecord[]> GetAllEntities(Expression<Func<EnhancementRecord, bool>> exp = null)
        {
            return (await base.GetAll(exp)).ToArray();
        }

        public async Task<EnhancementRecordDto[]> GetAll(Expression<Func<EnhancementRecord, bool>> exp = null)
        {
            var data = await base.GetAll(exp);
            return data.Select(d => d.ToDto()).ToArray();
        }

        public async Task<SearchResponse<EnhancementRecordDto>> SearchDto(EnhancementRecordSearchRequestModel model)
        {
            var data = await base.Search(t =>
                    (!model.ResourceId.HasValue || model.ResourceId == t.ResourceId) &&
                    (!model.Success.HasValue || model.Success == t.Success) &&
                    (string.IsNullOrEmpty(model.EnhancerDescriptorId) ||
                     t.EnhancerDescriptorId.Contains(model.EnhancerDescriptorId))
                , model.PageIndex, model.PageSize,
                t => t.CreateDt);
            var dtoList = data.Data.Select(d => d.ToDto()).ToArray();
            return model.BuildResponse(dtoList, data.TotalCount);
        }

        public async Task StartEnhancingInBackgroundTask()
        {
            if (BackgroundTaskManager.IsRunningByName(EnhanceBackgroundTaskName))
            {
                return;
            }

            var resources = await BuildEnhancementTasks();
            if (resources.Any())
            {
                BackgroundTaskHelper.RunInNewScope<EnhancementRecordService>(EnhanceBackgroundTaskName,
                    async (service, task) =>
                    {
                        await service._enhance(resources, async p => { task.Percentage = p; });
                        return BaseResponseBuilder.Ok;
                    });
            }
        }

        public class EnhancementTask
        {
            public int ResourceId { get; init; }
            public IEnhancer[] OrderedEnhancers { get; init; }
            public Dictionary<string, List<IEnhancer>> Orders { get; init; }
            public Dictionary<IEnhancer, ComponentDescriptor> EnhancerDescriptors { get; init; }
            public Dictionary<string, string> RuleIdMap { get; init; }
        }

        private async Task<EnhancementTask[]> BuildEnhancementTasks(IReadOnlyCollection<Resource> resources = null)
        {
            // Category validation
            var categoryIds = resources?.Select(d => d.CategoryId).Distinct().ToArray();
            var categories = await ResourceCategoryService.GetAllDto(
                categoryIds == null ? null : a => categoryIds.Contains(a.Id),
                ResourceCategoryAdditionalItem.Validation);
            categories.RemoveAll(a => a.ComponentsData?.Any(x => x.ComponentType == ComponentType.Enhancer) != true);
            var invalidCategories = categories.Where(a => !a.IsValid).ToArray();
            if (invalidCategories.Any())
            {
                await LogService.Log(nameof(EnhancementRecordService), LogLevel.Error, "CategoryValidationFailed",
                    InsideWorldLocalizer.Category_Invalid(invalidCategories.Select(a => (a.Name, a.Message))
                        .ToArray()));
            }

            categories.RemoveAll(invalidCategories.Contains);

            var tasks = new List<EnhancementTask>();
            var invalidEnhancementRecords = new List<EnhancementRecord>();

            var enhancerRuleIdMap = new Dictionary<string, string>();

            foreach (var c in categories)
            {
                var resourceIds = resources?.Where(a => a.CategoryId == c.Id).Select(a => a.Id).ToArray() ??
                                  (await ResourceService.GetAllEntities(t => t.CategoryId == c.Id, false))
                                  .Select(t => t.Id).ToArray();

                if (resourceIds.Any())
                {
                    var resourceEnhancements =
                        (await GetAllEntities(t => resourceIds.Contains(t.ResourceId)))
                        .GroupBy(t => t.ResourceId)
                        .ToDictionary(t => t.Key, t => t.ToList());

                    // Key - Enhancer Types
                    var enhancementOptions = c.EnhancementOptions!;
                    var enhancerComponentKeys = c.ComponentsData.Where(a => a.ComponentType == ComponentType.Enhancer)
                        .Select(a => a.ComponentKey).ToArray();
                    var enhancers = await ComponentService.CreateInstances<IEnhancer>(enhancerComponentKeys);
                    var orderedEnhancers =
                        (enhancementOptions.DefaultPriority?
                            .Select(a => (IEnhancer?)enhancers.FirstOrDefault(e => e.Value.Id == a).Key)
                            .Where(a => a != null)
                         ??
                        enhancers.Keys).ToList();

                    var orders = enhancers.Values.Cast<EnhancerDescriptor>()
                        .SelectMany(a => a?.Targets ?? Array.Empty<string>()).Distinct().ToDictionary(a => a,
                            a =>
                            {
                                var definedOrders =
                                    enhancementOptions.EnhancementPriorities?.TryGetValue(a, out var tOrders) == true
                                        ? tOrders
                                        : new List<string>();
                                var unknownEnhancers = enhancers.Where(e => !definedOrders.Contains(e.Value.Id));

                                return definedOrders.Select(b => enhancers.FirstOrDefault(x => x.Value.Id == b).Key)
                                    .Concat(unknownEnhancers.Select(x => x.Key)).Where(x => x != null).ToList();
                            });

                    foreach (var rId in resourceIds)
                    {
                        if (!resourceEnhancements.TryGetValue(rId, out var doneEnhancements))
                        {
                            doneEnhancements = new List<EnhancementRecord>();
                        }

                        var validEnhancements = new List<EnhancementRecord>();
                        var orderedRestEnhancers = new List<IEnhancer>();
                        foreach (var enhancer in orderedEnhancers)
                        {
                            try
                            {
                                var cd1 = enhancers[enhancer!];
                            }
                            catch (Exception e)
                            {

                            }
                            var cd = enhancers[enhancer];
                            var ruleId = enhancerRuleIdMap.TryGetValue(cd.Id, out var tmpRuleId)
                                ? tmpRuleId
                                : enhancerRuleIdMap[cd.Id] = EnhancementRecord.BuildRuleId(cd.AssemblyQualifiedTypeName, cd.Version, cd.DataVersion);

                            var doneEnhancement = doneEnhancements.FirstOrDefault(t => t.Success && t.RuleId == ruleId);
                            if (doneEnhancement != null)
                            {
                                validEnhancements.Add(doneEnhancement);
                            }
                            else
                            {
                                orderedRestEnhancers.Add(enhancer);
                            }
                        }

                        var invalidEnhancements = doneEnhancements.Except(validEnhancements);
                        invalidEnhancementRecords.AddRange(invalidEnhancements);

                        if (orderedRestEnhancers.Any())
                        {
                            var task = new EnhancementTask
                            {
                                OrderedEnhancers = orderedRestEnhancers.ToArray(),
                                Orders = orders,
                                ResourceId = rId,
                                EnhancerDescriptors = enhancers.ToDictionary(a => a.Key as IEnhancer, a => a.Value),
                                RuleIdMap = enhancerRuleIdMap
                            };
                            tasks.Add(task);
                        }
                    }
                }
            }

            if (invalidEnhancementRecords.Any())
            {
                await RemoveRange(invalidEnhancementRecords);
            }

            return tasks.ToArray();
        }

        public async Task EnhanceByResourceId(int resourceId)
        {
            var resource = await ResourceService.GetByKey(resourceId, true);
            var tasks = await BuildEnhancementTasks(new[] {resource});
            await _enhance(tasks);
        }

        public async Task EnhanceByCategoryId(int categoryId)
        {
            var resource = await ResourceService.GetAll(r => r.CategoryId == categoryId, true);
            var tasks = await BuildEnhancementTasks(resource);
            await _enhance(tasks);
        }

        public async Task EnhanceByMediaLibraryId(int mediaLibraryId)
        {
            var resource = await ResourceService.GetAll(r => r.MediaLibraryId == mediaLibraryId, true);
            var tasks = await BuildEnhancementTasks(resource);
            await _enhance(tasks);
        }

        private async Task _enhance(IReadOnlyCollection<EnhancementTask> enhancementTasks,
            Func<int, Task> onProgress = null)
        {
            var doneCount = 0;
            foreach (var et in enhancementTasks)
            {
                var rId = et.ResourceId;
                var enhancementOrders = et.Orders;
                var tasks = new List<Task>();
                var records = new ConcurrentBag<EnhancementRecord>();
                var enhancerEnhancements = new ConcurrentDictionary<IEnhancer, Enhancement[]>();
                var resource = (await ResourceService.GetByKey(rId, ResourceAdditionalItem.All, true))!;
                foreach (var enhancer in et.OrderedEnhancers)
                {
                    var cd = et.EnhancerDescriptors[enhancer];
                    var n = new EnhancementRecord
                    {
                        EnhancerDescriptorId = cd.Id,
                        EnhancerName = cd.Name,
                        ResourceId = rId,
                        RuleId = et.RuleIdMap[cd.Id],
                        ResourceRawFullName = resource.Path
                    };
                    records.Add(n);
                    var enhancements1 = enhancerEnhancements;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var enhancements = await enhancer.Enhance(resource);
                            if (enhancements.Any())
                            {
                                enhancements1[enhancer] = enhancements;
                                n.Enhancement = string.Join(Environment.NewLine,
                                    enhancements.Select(t => t.ToString()));
                            }

                            n.Success = true;
                        }
                        catch (Exception e)
                        {
                            n.Success = false;
                            n.Message = e.BuildFullInformationText();
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                // EnhancementKey - List<(Enhancer, Enhancement)>
                var keyEnhancerEnhancementPairs =
                    new Dictionary<string, List<(IEnhancer Enhancer, Enhancement Enhancement)>>();
                foreach (var (enhancer, es) in enhancerEnhancements)
                {
                    if (es?.Any() == true)
                    {
                        foreach (var e in es)
                        {
                            if (!keyEnhancerEnhancementPairs.TryGetValue(e.Key, out var list))
                            {
                                keyEnhancerEnhancementPairs[e.Key] =
                                    list =
                                        new List<(IEnhancer Enhancer, Enhancement Enhancement)>();
                            }

                            list.Add((enhancer, e));
                        }
                    }
                }

                // Key - May be array properties
                var customResourceProperties = new Dictionary<string, List<CustomResourceProperty>>();

                foreach (var (enhancementKey, tmpCandidates) in keyEnhancerEnhancementPairs)
                {
                    // Apply orders
                    var candidates = tmpCandidates;
                    if (enhancementOrders.TryGetValue(enhancementKey, out var orderedEnhancers))
                    {
                        candidates = tmpCandidates.OrderBy(t => orderedEnhancers.IndexOf(t.Enhancer)).ToList();
                    }

                    var candidate = candidates.FirstOrDefault().Enhancement;
                    var type = candidates.FirstOrDefault().Enhancement.Type;
                    var keyWithoutType = enhancementKey.Split(':', StringSplitOptions.RemoveEmptyEntries)[1];

                    switch (type)
                    {
                        case EnhancementType.Property:
                        {
                            if (Enum.TryParse<ReservedResourceProperty>(keyWithoutType, true,
                                    out var reservedProperty))
                            {
                                switch (reservedProperty)
                                {
                                    case ReservedResourceProperty.ReleaseDt:
                                        resource.ReleaseDt ??= candidate.Data as DateTime?;
                                        break;
                                    case ReservedResourceProperty.Publisher:
                                        if (resource.Publishers?.Any() != true)
                                        {
                                            resource.Publishers =
                                                (candidate.Data as IEnumerable<PublisherDto>)?
                                                .ToList();
                                        }

                                        break;
                                    case ReservedResourceProperty.Name:
                                        resource.Name ??= candidate.Data?.ToString();
                                        break;
                                    case ReservedResourceProperty.Volume:
                                        resource.Volume ??= candidate.Data as VolumeDto;
                                        break;
                                    case ReservedResourceProperty.Original:
                                        if (resource.Originals?.Any() != true)
                                        {
                                            resource.Originals =
                                                ((IEnumerable<OriginalDto>) candidate.Data)
                                                .ToList();
                                        }

                                        break;
                                    case ReservedResourceProperty.Series:
                                        resource.Series ??= (SeriesDto) candidate.Data;
                                        break;
                                    case ReservedResourceProperty.Introduction:
                                        resource.Introduction ??= (string) candidate.Data;
                                        break;
                                    case ReservedResourceProperty.Rate:
                                        if (resource.Rate == 0)
                                        {
                                            resource.Rate = (decimal) candidate.Data;
                                        }

                                        break;
                                    case ReservedResourceProperty.Tag:
                                    {
                                        var tags = candidates.SelectMany(t => t.Enhancement.Data as IEnumerable<TagDto>)
                                            .Distinct(TagDto.BizComparer).ToList();
                                        var newTags = tags.Except(resource.Tags ?? new List<TagDto>(),
                                                TagDto.BizComparer)
                                            .ToArray();
                                        if (newTags.Any())
                                        {
                                            resource.Tags ??= new List<TagDto>();
                                            resource.Tags.AddRange(newTags);
                                        }

                                        break;
                                    }
                                    case ReservedResourceProperty.Language:
                                        if (resource.Language == ResourceLanguage.NotSet &&
                                            candidate.Data is ResourceLanguage l)
                                        {
                                            resource.Language = l;
                                        }

                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                            else
                            {
                                var e = candidate as CustomEnhancement;
                                if (e.IsArray)
                                {
                                    if (e.Data is IEnumerable ie)
                                    {
                                        var enumerator = ie.GetEnumerator();
                                        customResourceProperties[e.Key] =
                                            new List<CustomResourceProperty>();
                                        var index = 0;
                                        while (enumerator.MoveNext())
                                        {
                                            if (enumerator.Current != null)
                                            {
                                                customResourceProperties[e.Key].Add(
                                                    new CustomResourceProperty
                                                    {
                                                        Value = enumerator.Current?.ToString(),
                                                        ValueType = e.DataType,
                                                        Key = e.Key,
                                                        ResourceId = rId,
                                                        Index = index++
                                                    });
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    customResourceProperties[e.Key] =
                                        new List<CustomResourceProperty>
                                        {
                                            new CustomResourceProperty
                                            {
                                                Value = e.Data.ToString(),
                                                ValueType = e.DataType,
                                                Key = e.Key,
                                                ResourceId = rId
                                            }
                                        };
                                }
                            }

                            break;
                        }
                        case EnhancementType.File:
                        {
                            var files = candidate.Data is EnhancementFile tf
                                ? new List<EnhancementFile> {tf}
                                : (candidate.Data as IEnumerable<EnhancementFile>).ToList();

                            if (Enum.TryParse<ReservedResourceFileType>(keyWithoutType,
                                    out var fileType))
                            {
                                switch (fileType)
                                {
                                    case ReservedResourceFileType.Cover:
                                    {
                                        await ResourceService.SaveCover(resource.Id, null, false,
                                            () => files.FirstOrDefault()!.Data, new CancellationToken());
                                        break;
                                    }
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                            else
                            {
                                foreach (var file in files)
                                {
                                    var fullname = resource.IsFile
                                        ? Path.Combine(resource.Directory, file.RelativePath)
                                        : Path.Combine(resource.Path, file.RelativePath);
                                    if (!File.Exists(fullname))
                                    {
                                        await File.WriteAllBytesAsync(fullname, file.Data);
                                    }
                                }
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (customResourceProperties.Any())
                {
                    resource.CustomProperties ??=
                        new Dictionary<string, List<CustomResourceProperty>>();

                    foreach (var cp in customResourceProperties.Where(cp =>
                                 !resource.CustomProperties.ContainsKey(cp.Key)))
                    {
                        resource.CustomProperties[cp.Key] = cp.Value;
                    }
                }

                await ResourceService.AddOrPatchRange(new List<Models.Domain.Resource> {resource});
                await AddRange(records);

                doneCount++;

                if (onProgress != null)
                {
                    var percentage = (int) ((decimal) doneCount * 100 / enhancementTasks.Count);
                    await onProgress.Invoke(percentage);
                }
            }
        }
    }
}