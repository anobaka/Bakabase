using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Dto;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods.Internal;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using Newtonsoft.Json;
using NPOI.SS.Formula.Functions;
using SharpCompress.Readers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using static Bakabase.Abstractions.Models.Domain.PathConfigurationTestResult.Resource;
using MediaLibrary = Bakabase.Abstractions.Models.Domain.MediaLibrary;
using PathConfiguration = Bakabase.Abstractions.Models.Domain.PathConfiguration;
using SearchOption = System.IO.SearchOption;

namespace Bakabase.InsideWorld.Business.Services
{
    public class MediaLibraryService: BootstrapService,  IMediaLibraryService
    {
        private readonly ResourceService<InsideWorldDbContext,
            Abstractions.Models.Db.MediaLibrary, int> _orm;
        private const decimal MinimalFreeSpace = 1_000_000_000;
        protected BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
        protected ICategoryService ResourceCategoryService => GetRequiredService<ICategoryService>();
        protected IResourceService ResourceService => GetRequiredService<IResourceService>();
        protected BackgroundTaskHelper BackgroundTaskHelper => GetRequiredService<BackgroundTaskHelper>();
        private InsideWorldLocalizer _localizer;
        protected LogService LogService => GetRequiredService<LogService>();
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();
        protected InsideWorldOptionsManagerPool InsideWorldAppService =>
            GetRequiredService<InsideWorldOptionsManagerPool>();
        protected IStandardValueService StandardValueService => GetRequiredService<IStandardValueService>();

        public MediaLibraryService(IServiceProvider serviceProvider, InsideWorldLocalizer localizer,
            ResourceService<InsideWorldDbContext, Abstractions.Models.Db.MediaLibrary, int> orm) : base(serviceProvider)
        {
            _localizer = localizer;
            _orm = orm;
        }

        public async Task<BaseResponse> Add(MediaLibraryAddDto model)
        {
            var dto = new MediaLibrary
            {
                Name = model.Name,
                CategoryId = model.CategoryId,
                PathConfigurations = model.PathConfigurations
            };

            var t = await _orm.Add(dto.ToDbModel()!);
            return t;
        }

        public async Task AddRange(ICollection<MediaLibrary> mls)
        {
            var map = mls.ToDictionary(x => x, x => x.ToDbModel()!);
            await _orm.AddRange(map.Values);
            foreach (var (dto, entity) in map)
            {
                dto.Id = entity.Id;
            }
        }

        public async Task<BaseResponse> Patch(int id, MediaLibraryPatchDto model)
        {
            var ml = (await Get(id, MediaLibraryAdditionalItem.None))!;
            if (model.PathConfigurations != null)
            {
                ml.PathConfigurations = model.PathConfigurations;
            }

            if (ml.Name != model.Name && !string.IsNullOrEmpty(model.Name))
            {
                ml.Name = model.Name;
            }

            if (model.Order.HasValue)
            {
                ml.Order = model.Order.Value;
            }

            var t = await _orm.Update(ml.ToDbModel()!);
            return t;
        }

        public async Task<BaseResponse> Put(MediaLibrary dto)
        {
            return await _orm.Update(dto.ToDbModel()!);
        }

        public async Task<MediaLibrary?> Get(int id, MediaLibraryAdditionalItem additionalItems = MediaLibraryAdditionalItem.None)
        {
            var ml = await _orm.GetByKey(id);
            return (await ToDtoList([ml], additionalItems)).FirstOrDefault();
        }

        public async Task<List<MediaLibrary>> GetAll(
            Expression<Func<Abstractions.Models.Db.MediaLibrary, bool>>? exp = null,
            MediaLibraryAdditionalItem additionalItems = MediaLibraryAdditionalItem.None)
        {
            var wss = (await _orm.GetAll(exp)).OrderBy(a => a.Order).ToList();
            return await ToDtoList(wss, additionalItems);
        }

        public async Task<BaseResponse> DeleteAll(Expression<Func<Abstractions.Models.Db.MediaLibrary, bool>> selector)
        {
            return await _orm.RemoveAll(selector);
        }

        public async Task<BaseResponse> DeleteByKey(int key)
        {
            return await _orm.RemoveByKey(key);
        }

        protected async Task<List<MediaLibrary>> ToDtoList(List<Abstractions.Models.Db.MediaLibrary> mls,
            MediaLibraryAdditionalItem additionalItems)
        {
            var dtoList = mls.Select(ml => ml.ToDomainModel()!).ToList();
            foreach (var ai in SpecificEnumUtils<MediaLibraryAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(ai))
                {
                    switch (ai)
                    {
                        case MediaLibraryAdditionalItem.None:
                            break;
                        case MediaLibraryAdditionalItem.FileSystemInfo:
                        {
                            var drives = DriveInfo.GetDrives().ToDictionary(t => t.Name, t => t);
                            var paths = dtoList.Where(ml => ml.PathConfigurations != null)
                                .SelectMany(ml => ml.PathConfigurations!.Select(pc => pc.Path!))
                                .Where(path => !string.IsNullOrEmpty(path)).Distinct();
                            var fileSystemInfoMap = paths.ToDictionary(p => p, p =>
                            {
                                var ri = new MediaLibraryFileSystemInformation();
                                var driveRoot = Path.GetPathRoot(p);
                                if (drives.TryGetValue(driveRoot!, out var d))
                                {
                                    ri.FreeSpace = d.AvailableFreeSpace;
                                    ri.TotalSize = d.TotalSize;
                                    if (d.AvailableFreeSpace < MinimalFreeSpace)
                                    {
                                        ri.Error = MediaLibraryFileSystemError.FreeSpaceNotEnough;
                                    }
                                }
                                else
                                {
                                    ri.Error = MediaLibraryFileSystemError.InvalidVolume;
                                }

                                return ri;
                            });
                            foreach (var ml in dtoList)
                            {
                                ml.FileSystemInformation = ml.PathConfigurations
                                    ?.Where(p => !string.IsNullOrEmpty(p.Path)).GroupBy(x => x.Path!)
                                    .ToDictionary(p => p.Key, p => fileSystemInfoMap.GetValueOrDefault(p.Key)!);
                            }

                            break;
                        }
                        // case MediaLibraryAdditionalItem.FixedTags:
                        // {
                        //     var fixedTagIds = dtoList.Where(t => t.PathConfigurations != null)
                        //         .SelectMany(t =>
                        //             t.PathConfigurations!.Where(a => a.FixedTagIds != null)
                        //                 .SelectMany(x => x.FixedTagIds!)).ToHashSet();
                        //     var tags = (await TagService.GetByKeys(fixedTagIds, TagAdditionalItem.None))
                        //         .ToDictionary(t => t.Id, t => t);
                        //     foreach (var ml in dtoList.Where(t => t.PathConfigurations != null))
                        //     {
                        //         foreach (var pathConfiguration in ml.PathConfigurations!.Where(pathConfiguration =>
                        //                      pathConfiguration.FixedTagIds?.Any() == true))
                        //         {
                        //             pathConfiguration.FixedTags = pathConfiguration.FixedTagIds!
                        //                 .Select(t => tags.GetValueOrDefault(t)).Where(t => t != null).ToList()!;
                        //         }
                        //     }
                        //
                        //     break;
                        // }
                        case MediaLibraryAdditionalItem.Category:
                        {
                            var categoryIds = dtoList.Select(c => c.CategoryId).ToHashSet();
                            var categories =
                                (await ResourceCategoryService.GetAll(x => categoryIds.Contains(x.Id))).ToDictionary(
                                    a => a.Id, a => a);
                            foreach (var ml in dtoList)
                            {
                                ml.Category = categories.GetValueOrDefault(ml.CategoryId);
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            return dtoList;
        }

        public async Task<BaseResponse> Sort(int[] ids)
        {
            var libraries = (await _orm.GetByKeys(ids)).ToDictionary(t => t.Id, t => t);
            var changed = new List<Abstractions.Models.Db.MediaLibrary>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (libraries.TryGetValue(id, out var t) && t.Order != i)
                {
                    t.Order = i;
                    changed.Add(t);
                }
            }

            return await _orm.UpdateRange(changed);
        }

        public async Task<BaseResponse> DuplicateAllInCategory(int fromCategoryId, int toCategoryId)
        {
            var libraries = await GetAll(x => x.CategoryId == fromCategoryId, MediaLibraryAdditionalItem.None);
            if (libraries.Any())
            {
                var newLibraries = libraries.Select(l => l.Duplicate(toCategoryId)).ToArray();
                await AddRange(newLibraries);
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task StopSyncing()
        {
            BackgroundTaskManager.StopByName(SyncTaskBackgroundTaskName);
        }

        public BackgroundTaskDto? SyncTaskInformation =>
            BackgroundTaskManager.GetByName(SyncTaskBackgroundTaskName).FirstOrDefault();

        public const string SyncTaskBackgroundTaskName = $"MediaLibraryService:Sync";

        private static string[] DiscoverAllResourceFullnameList(string rootPath,
            PropertyPathSegmentMatcherValue resourceMatcherValue,
            int maxCount = int.MaxValue)
        {
            rootPath = rootPath.StandardizePath()!;
            if (!rootPath.EndsWith(InternalOptions.DirSeparator))
            {
                rootPath += InternalOptions.DirSeparator;
            }

            var list = new List<string>();
            switch (resourceMatcherValue.ValueType)
            {
                case ResourceMatcherValueType.Layer:
                {
                    var currentLayer = 0;
                    var paths = new List<string> {rootPath};
                    var nextLayerPaths = new List<string>();
                    while (currentLayer++ < resourceMatcherValue.Layer! - 1)
                    {
                        var isTargetLayer = currentLayer == resourceMatcherValue.Layer - 1;
                        foreach (var path in paths)
                        {
                            try
                            {
                                nextLayerPaths.AddRange(Directory.GetDirectories(path, "*",
                                    SearchOption.TopDirectoryOnly));
                            }
                            catch
                            {
                            }

                            if (nextLayerPaths.Count > maxCount && isTargetLayer)
                            {
                                break;
                            }
                        }

                        paths = nextLayerPaths;
                        nextLayerPaths = new();
                    }

                    var allFileEntries = paths.SelectMany(p =>
                    {
                        try
                        {
                            return Directory.GetFileSystemEntries(p);
                        }
                        catch (Exception e)
                        {
                            return Array.Empty<string>();
                        }
                    });
                    list.AddRange(allFileEntries.Select(s => s.StandardizePath()!));
                    break;
                }
                case ResourceMatcherValueType.Regex:
                {
                    var allEntries = Directory.GetFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
                        .Select(e => e.StandardizePath()).OrderBy(a => a).ToArray();
                    foreach (var e in allEntries)
                    {
                        var relativePath = e![rootPath.Length..];

                        // ignore sub contents
                        if (list.Any(l => e.StartsWith(l + InternalOptions.DirSeparator)))
                        {
                            continue;
                        }

                        var match = Regex.Match(relativePath, resourceMatcherValue.Regex!);
                        if (match.Success)
                        {
                            var length = match.Index + match.Value.Length;
                            var matchedPath = relativePath[..length];

                            if (matchedPath.SplitPathIntoSegments().Length ==
                                relativePath.SplitPathIntoSegments().Length)
                            {
                                list.Add(e);

                                if (list.Count > maxCount)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    break;
                }
                case ResourceMatcherValueType.FixedText:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return list.Where(e => !InternalOptions.IgnoredFileExtensions.Contains(Path.GetExtension(e))).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public void StartSyncing()
        {
            BackgroundTaskHelper.RunInNewScope<MediaLibraryService>(SyncTaskBackgroundTaskName,
                async (service, task) => await service.Sync(task));
        }

        private void SetPropertiesByMatchers(string rootPath, PathConfigurationTestResult.Resource e, Resource pr,
            Dictionary<string, Resource> parentResources, Dictionary<int, CustomProperty> customPropertyMap)
        {
            if (parentResources == null) throw new ArgumentNullException(nameof(parentResources));
            // property - custom key/string.empty - values
            // PropertyId - Values
            // For Property=ParentResource, value will be an absolute path.
            var reservedPropertyValues = new Dictionary<ResourceProperty, List<string>>();
            for (var i = 0; i < e.SegmentAndMatchedValues.Count; i++)
            {
                var t = e.SegmentAndMatchedValues[i];
                if (t.PropertyKeys.Any())
                {
                    foreach (var p in t.PropertyKeys.Where(x => x.IsReserved))
                    {
                        var propertyIdAndValues = reservedPropertyValues.GetOrAdd((ResourceProperty) p.Id, () => new());
                        var v = t.SegmentText;
                        if (p is {IsReserved: true, Id: (int) ResourceProperty.ParentResource})
                        {
                            v = Path.Combine(rootPath,
                                    string.Join(InternalOptions.DirSeparator,
                                        e.SegmentAndMatchedValues.Take(i + 1).Select(a => a.SegmentText)))
                                .StandardizePath()!;
                        }

                        propertyIdAndValues.Add(v);
                    }
                }
            }

            foreach (var t in e.GlobalMatchedValues.Where(p => p.PropertyKey.IsReserved))
            {
                var values = reservedPropertyValues.GetOrAdd((ResourceProperty) t.PropertyKey.Id, () => new());
                values.AddRange(t.TextValues);
            }

            if (reservedPropertyValues.Any())
            {
                foreach (var (propertyId, values) in reservedPropertyValues)
                {
                    var firstValue = values.First();
                    switch (propertyId)
                    {
                        case ResourceProperty.ParentResource:
                        {
                            if (firstValue != pr.Path)
                            {
                                if (!parentResources.TryGetValue(firstValue, out var parent))
                                {
                                    parentResources[firstValue] = parent = new Resource()
                                    {
                                        CategoryId = pr.CategoryId,
                                        MediaLibraryId = pr.MediaLibraryId,
                                        IsFile = false
                                    };
                                }

                                pr.Parent = parent;
                            }

                            break;
                        }
                        case ResourceProperty.RootPath:
                        case ResourceProperty.Resource:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }

            if (e.CustomPropertyIdValueMap.Any())
            {
                foreach (var (pId, rawValue) in e.CustomPropertyIdValueMap)
                {
                    var property = customPropertyMap.GetValueOrDefault(pId);
                    if (property != null)
                    {
                        var propertyMap = (pr.Properties ??= []).GetOrAdd(ResourcePropertyType.Custom, () => [])!;
                        var rp = propertyMap.GetOrAdd(property.Id,
                            () => new Resource.Property(property.Name, property.DbValueType, property.DbValueType, null));
                        rp.Values ??= [];
                        rp.Values.Add(new Resource.Property.PropertyValue((int)PropertyValueScope.Synchronization,
                            rawValue, rawValue, rawValue));
                    }
                }
            }
        }

        public async Task<BaseResponse> Sync(BackgroundTask task)
        {
            // Make log and error can show in background task info.
            var libraries = await GetAll(null, MediaLibraryAdditionalItem.None);
            var librariesMap = libraries.ToDictionary(a => a.Id, a => a);
            var categories = (await ResourceCategoryService.GetAll()).ToDictionary(a => a.Id, a => a);

            // Validation
            {
                var ignoredLibraries = new List<MediaLibrary>();
                foreach (var library in libraries)
                {
                    if (!categories.TryGetValue(library.CategoryId, out var c))
                    {
                        await LogService.Log(SyncTaskBackgroundTaskName, LogLevel.Error, "CategoryValidationFailed",
                            $"Media library [{library.Id}:{library.Name}] will not be synchronized because its category [id:{library.CategoryId}] is not found");
                        ignoredLibraries.Add(library);
                    }
                }

                libraries.RemoveAll(ignoredLibraries.Contains);
                // var paths = libraries.Where(t => t.PathConfigurations != null)
                //     .SelectMany(t => t.PathConfigurations.Select(b => b.Path)).ToArray();
                // if (CheckPathContaining(paths))
                // {
                //     throw new Exception(
                //         "Some paths of media libraries are related, please check them before syncing");
                // }
            }

            // Top level directory/file name - (Filename, IsSingleFile, MediaLibraryId, FixedTagIds, TagNames)
            var patchingResources = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase);
            var parentResources = new Dictionary<string, Resource>();
            var prevPathResourceMap = new Dictionary<string, Resource>();
            var invalidResources = new List<Resource>();

            var changedResources = new ConcurrentDictionary<string, Resource>();

            var step = SpecificEnumUtils<MediaLibrarySyncStep>.Values.FirstOrDefault();

            while (step <= MediaLibrarySyncStep.SaveResources)
            {
                var basePercentage = step.GetBasePercentage();
                var stepPercentage = MediaLibrarySyncStepExtensions.Percentages[step];
                var resourceCountPerPercentage = patchingResources.Any()
                    ? patchingResources.Count / (decimal) stepPercentage
                    : decimal.MaxValue;
                task.Percentage = basePercentage;
                task.CurrentProcess = step.ToString();
                switch (step)
                {
                    case MediaLibrarySyncStep.Filtering:
                    {
                        var percentagePerLibrary =
                            libraries.Count == 0 ? 0 : (decimal) 1 / libraries.Count * stepPercentage;
                        for (var i = 0; i < libraries.Count; i++)
                        {
                            var library = libraries[i];
                            if (library.PathConfigurations?.Any() != true)
                            {
                                continue;
                            }

                            var percentagePerPathConfiguration =
                                (decimal) 1 / library.PathConfigurations.Count *
                                percentagePerLibrary;
                            for (var j = 0; j < library.PathConfigurations.Count; j++)
                            {
                                var pathConfiguration = library.PathConfigurations[j];
                                var resourceMatcher =
                                    pathConfiguration.RpmValues?.FirstOrDefault(m => m.IsResourceProperty);
                                if (!Directory.Exists(pathConfiguration.Path) || resourceMatcher == null)
                                {
                                    continue;
                                }

                                var pscResult = await Test(pathConfiguration, int.MaxValue);
                                if (pscResult.Code == (int) ResponseCode.Success)
                                {
                                    var percentagePerItem =
                                        (decimal) 1 / pscResult.Data.Resources.Count * percentagePerPathConfiguration;
                                    var count = 0;
                                    foreach (var e in pscResult.Data.Resources)
                                    {
                                        var resourcePath =
                                            $"{pscResult.Data.RootPath}{InternalOptions.DirSeparator}{e.RelativePath}";
                                        var pr = new Resource()
                                        {
                                            CategoryId = library.CategoryId,
                                            MediaLibraryId = library.Id,
                                            IsFile = new FileInfo(resourcePath).Exists,
                                        };

                                        if (pathConfiguration.RpmValues?.Any() ==
                                            true)
                                        {
                                            SetPropertiesByMatchers(pscResult.Data.RootPath, e, pr, parentResources, pscResult.Data.CustomPropertyMap);
                                        }

                                        patchingResources.TryAdd(pr.Path, pr);
                                        task.Percentage = basePercentage + (int) (i * percentagePerLibrary +
                                            percentagePerPathConfiguration * j +
                                            percentagePerItem * (++count));
                                    }
                                }
                            }
                        }

                        break;
                    }
                    case MediaLibrarySyncStep.AcquireFileSystemInfo:
                    {
                        resourceCountPerPercentage = patchingResources.Any()
                            ? patchingResources.Count / (decimal) stepPercentage
                            : decimal.MaxValue;
                        var count = 0;
                        foreach (var (fullname, patches) in patchingResources)
                        {
                            FileSystemInfo fileSystemInfo = patches.IsFile
                                ? new FileInfo(fullname)
                                : new DirectoryInfo(fullname);
                            patches.FileCreatedAt =
                                fileSystemInfo.CreationTime.AddTicks(-(fileSystemInfo.CreationTime.Ticks %
                                                                       TimeSpan.TicksPerSecond));
                            patches.FileModifiedAt =
                                fileSystemInfo.LastWriteTime.AddTicks(-(fileSystemInfo.LastWriteTime.Ticks %
                                                                        TimeSpan.TicksPerSecond));
                            task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
                        }

                        break;
                    }
                    case MediaLibrarySyncStep.CleanResources:
                    {
                        var prevResources = await ResourceService.GetAll(null, ResourceAdditionalItem.All);
                        // Delete resources with unknown paths
                        invalidResources.AddRange(prevResources.Where(x => !patchingResources.Keys.Contains(x.Path)));

                        var invalidIds = invalidResources.Select(r => r.Id).ToArray();
                        await ResourceService.DeleteByKeys(invalidIds);
                        prevResources.RemoveAll(x => invalidResources.Contains(x));

                        prevPathResourceMap = prevResources.ToDictionary(t => t.Path);
                        break;
                    }
                    case MediaLibrarySyncStep.CompareResources:
                    {
                        var count = 0;
                        foreach (var (path, pr) in patchingResources)
                        {
                            if (!prevPathResourceMap.TryGetValue(path, out var resource))
                            {
                                resource = pr;
                                changedResources[path] = resource;
                            }

                            if (resource.MergeOnSynchronization(pr))
                            {
                                changedResources[path] = resource;
                            }

                            task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
                        }

                        foreach (var (_, r) in changedResources)
                        {
                            r.UpdatedAt = DateTime.Now;
                        }

                        break;
                    }
                    case MediaLibrarySyncStep.SaveResources:
                    {
                        var resourcesToBeSaved = changedResources.Values.ToList();
                        var newResources = resourcesToBeSaved.Where(a => a.Id == 0).ToArray();
                        await ResourceService.AddOrPutRange(resourcesToBeSaved);
                        // Update sync result
                        var libraryResourceCount = patchingResources.GroupBy(a => a.Value.MediaLibraryId)
                            .ToDictionary(a => a.Key, a => a.Count());
                        await _orm.UpdateByKeys(libraries.Select(a => a.Id).ToArray(),
                            l => { l.ResourceCount = libraryResourceCount.TryGetValue(l.Id, out var c) ? c : 0; });

                        task.Message = string.Join(
                            Environment.NewLine,
                            $"[Resource] Found: {patchingResources.Count}, New: {newResources.Length} Removed: {invalidResources.Count}, Updated: {resourcesToBeSaved.Count - newResources.Length}",
                            $"[Directory]: Found: {patchingResources.Count(a => !a.Value.IsFile)}",
                            $"[File]: Found: {patchingResources.Count(a => a.Value.IsFile)}"
                        );
                        task.Percentage = basePercentage + stepPercentage;

                        await InsideWorldAppService.Resource.SaveAsync(t => t.LastSyncDt = DateTime.Now);

                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException(nameof(step), step, null);
                }

                step++;
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<SingletonResponse<PathConfigurationTestResult>> Test(
            PathConfiguration pc, int maxResourceCount = int.MaxValue)
        {
            if (pc.Path.IsNullOrEmpty())
            {
                return SingletonResponseBuilder<PathConfigurationTestResult>.BuildBadRequest(
                    _localizer.ValueIsNotSet(nameof(pc.Path)));
            }

            var resourceMatcherValue =
                pc.RpmValues?.FirstOrDefault(a => a is
                    {PropertyId: (int) ResourceProperty.Resource, IsReservedProperty: true, IsValid: true});
            if (resourceMatcherValue == null)
            {
                return SingletonResponseBuilder<PathConfigurationTestResult>.BuildBadRequest(
                    "A valid resource matcher value is required");
            }

            pc.Path = pc.Path?.StandardizePath()!;
            var dir = new DirectoryInfo(pc.Path!);
            var entries = new List<PathConfigurationTestResult.Resource>();
            if (dir.Exists)
            {
                var customPropertyIds =
                    pc.RpmValues?.Where(r => !r.IsReservedProperty).Select(r => r.PropertyId).ToHashSet() ?? [];
                var customPropertyMap =
                    (await CustomPropertyService.GetByKeys(customPropertyIds, CustomPropertyAdditionalItem.None))
                    .ToDictionary(d => d.Id, d => d);
                var resourceFullnameList =
                    DiscoverAllResourceFullnameList(pc.Path, resourceMatcherValue, maxResourceCount);

                var rootSegments = pc.Path!.SplitPathIntoSegments();
                foreach (var f in resourceFullnameList)
                {
                    var segments = f.SplitPathIntoSegments();

                    var relativeSegments = segments[rootSegments.Length..];
                    var relativePath = string.Join(InternalOptions.DirSeparator, relativeSegments);

                    var otherMatchers = pc.RpmValues!.Where(a => a.IsSecondaryProperty).ToList();

                    // Index - IsReservedProperty - PropertyId
                    var tmpSegmentProperties = new Dictionary<int, Dictionary<bool, HashSet<int>>>();
                    // IsReservedProperty - PropertyId - Values
                    var tmpGlobalMatchedValues = new Dictionary<bool, Dictionary<int, HashSet<string>>>();

                    foreach (var m in otherMatchers)
                    {
                        var result = ResourcePropertyMatcher.Match(segments, m, rootSegments.Length - 1,
                            segments.Length - 1);
                        if (result != null)
                        {
                            switch (result.Type)
                            {
                                case MatchResultType.Layer:
                                {
                                    var idx = result.Layer > 0
                                        ? result.Layer.Value - 1
                                        : relativeSegments.Length + result.Layer!.Value - 1;

                                    var propertyIds = tmpSegmentProperties
                                        .GetOrAdd(idx, () => new())
                                        .GetOrAdd(m.IsReservedProperty, () => new());
                                    propertyIds.Add(m.PropertyId);

                                    break;
                                }
                                case MatchResultType.Regex:
                                {
                                    var values = tmpGlobalMatchedValues
                                        .GetOrAdd(m.IsReservedProperty, () => new())
                                        .GetOrAdd(m.PropertyId, () => new());
                                    values.AddRange(result.Matches!);

                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }

                    var list = new List<SegmentMatchResult>();

                    for (var i = 0; i < relativeSegments.Length; i++)
                    {
                        var segment = relativeSegments[i];
                        var segmentProperties = tmpSegmentProperties.TryGetValue(i, out var t)
                            ? t.SelectMany(a =>
                            {
                                var (isReserved, pIds) = a;
                                return pIds.Select(b => new SegmentPropertyKey(isReserved, b));
                            }).ToList()
                            : [];

                        var r = new SegmentMatchResult(segment, segmentProperties);
                        list.Add(r);
                    }

                    var globalValues = tmpGlobalMatchedValues.SelectMany(a =>
                        {
                            var (isReserved, pIdAndValues) = a;
                            return pIdAndValues.Select(b =>
                            {
                                var (pId, textValues) = b;
                                return new GlobalMatchedValue(new SegmentPropertyKey(isReserved, pId), textValues);
                            });
                        })
                        .ToList();

                    var propertyIdBizValueMap = new Dictionary<int, HashSet<string>>();
                    foreach (var segment in list)
                    {
                        foreach (var p in segment.PropertyKeys.Where(p => !p.IsReserved))
                        {
                            propertyIdBizValueMap.GetOrAdd(p.Id, () => []).Add(segment.SegmentText);
                        }
                    }

                    foreach (var gv in globalValues.Where(x => !x.PropertyKey.IsReserved))
                    {
                        var set = propertyIdBizValueMap.GetOrAdd(gv.PropertyKey.Id, () => []);
                        foreach (var x in gv.TextValues)
                        {
                            set.Add(x);
                        }
                    }

                    var customPropertyIdValueMap = new Dictionary<int, object?>();
                    foreach (var (pId, listString) in propertyIdBizValueMap)
                    {
                        var property = customPropertyMap.GetValueOrDefault(pId);
                        if (property != null)
                        {
                            
                            customPropertyIdValueMap[property.Id] =
                                (await StandardValueService.CheckConversionLoss(listString.ToList(),
                                    StandardValueType.ListString, property.DbValueType)).NewValue;
                        }
                    }

                    var entry = new PathConfigurationTestResult.Resource(Directory.Exists(f), relativePath)
                    {
                        SegmentAndMatchedValues = list,
                        IsDirectory = Directory.Exists(f),
                        RelativePath = relativePath,
                        GlobalMatchedValues = globalValues,
                        CustomPropertyIdValueMap = customPropertyIdValueMap
                    };

                    entries.Add(entry);

                    if (entries.Count >= maxResourceCount)
                    {
                        break;
                    }
                }

                var relativeCustomPropertyIds = entries.SelectMany(x => x.CustomPropertyIdValueMap.Keys).ToHashSet();

                return new SingletonResponse<PathConfigurationTestResult>(
                    new PathConfigurationTestResult(dir.FullName.StandardizePath()!, entries,
                        customPropertyMap.Where(c => relativeCustomPropertyIds.Contains(c.Key))
                            .ToDictionary(d => d.Key, d => (CustomProperty) d.Value)));
            }

            return SingletonResponseBuilder<PathConfigurationTestResult>.NotFound;
        }

        public static bool CheckPathContaining(IReadOnlyCollection<string> pool, string target)
        {
            var stdPool = pool.Select(p => p.StandardizePath()!).ToList();
            var stdPath = target.StandardizePath()!;
            if (stdPool.Any(r => r.StartsWith(stdPath) || stdPath.StartsWith(r)))
            {
                return true;
            }

            return false;
        }
    }
}