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
using Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Logging.LogService.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
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
using SearchOption = System.IO.SearchOption;

namespace Bakabase.InsideWorld.Business.Services
{
    public class MediaLibraryService : ResourceService<InsideWorldDbContext, MediaLibrary, int>
    {
        private const decimal MinimalFreeSpace = 1_000_000_000;
        protected BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
        protected ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();
        protected ResourceService ResourceService => GetRequiredService<ResourceService>();
        protected TagService TagService => GetRequiredService<TagService>();
        protected BackgroundTaskHelper BackgroundTaskHelper => GetRequiredService<BackgroundTaskHelper>();

        protected ResourceTagMappingService ResourceTagMappingService =>
            GetRequiredService<ResourceTagMappingService>();

        protected FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int> ResourceServiceOrm =>
            GetRequiredService<FullMemoryCacheResourceService<InsideWorldDbContext, Resource, int>>();

        protected LogService LogService => GetRequiredService<LogService>();

        protected InsideWorldOptionsManagerPool InsideWorldAppService =>
            GetRequiredService<InsideWorldOptionsManagerPool>();

        public MediaLibraryService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task StopSync()
        {
            BackgroundTaskManager.StopByName(SyncTaskBackgroundTaskName);
        }

        public BackgroundTaskDto SyncTaskInformation =>
            BackgroundTaskManager.GetByName(SyncTaskBackgroundTaskName).FirstOrDefault();

        public const string SyncTaskBackgroundTaskName = $"MediaLibraryService:Sync";

        private static string[] DiscoverAllResourceFullnameList(string rootPath, MatcherValue resourceMatcherValue)
        {
            rootPath = rootPath.StandardizePath()!;
            if (!rootPath.EndsWith(BusinessConstants.DirSeparator))
            {
                rootPath += BusinessConstants.DirSeparator;
            }

            var list = new List<string>();
            switch (resourceMatcherValue.ValueType)
            {
                case ResourceMatcherValueType.Layer:
                {
                    var currentLayer = 0;
                    var paths = new List<string> {rootPath};
                    while (currentLayer++ < resourceMatcherValue.Layer! - 1)
                    {
                        paths = paths.SelectMany(t => Directory.GetDirectories(t, "*", SearchOption.TopDirectoryOnly))
                            .ToList();
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
                    list.AddRange(allFileEntries.Select(s => s.StandardizePath()));
                    break;
                }
                case ResourceMatcherValueType.Regex:
                {
                    var allEntries = Directory.GetFileSystemEntries(rootPath, "*", SearchOption.AllDirectories)
                        .Select(e => e.StandardizePath()).ToArray();
                    foreach (var e in allEntries)
                    {
                        var relativePath = e[(rootPath.Length + 1)..];
                        if (Regex.IsMatch(relativePath, resourceMatcherValue.Regex!))
                        {
                            list.Add(e);
                        }
                    }

                    break;
                }
                case ResourceMatcherValueType.FixedText:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return list.Where(e => !BusinessConstants.IgnoredFileExtensions.Contains(Path.GetExtension(e))).ToArray();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public void SyncInBackgroundTask()
        {
            BackgroundTaskHelper.RunInNewScope<MediaLibraryService>(SyncTaskBackgroundTaskName,
                async (service, task) => await service.Sync(task));
        }

        private void SetPropertiesByMatchers(ResourceDto pr, string rootPath, List<MatcherValue> matchers,
            Dictionary<string, ResourceDto> parentResources, MediaLibrary library)
        {
            var standardRootPath = rootPath.StandardizePath();
            var otherMatchers = matchers.Where(a =>
                a.Property != ResourceProperty.RootPath && a.Property != ResourceProperty.Resource);
            var rootPathSegments = standardRootPath.SplitPathIntoSegments();

            var segments = pr.RawFullname.SplitPathIntoSegments();
            var matchedValues =
                new Dictionary<ResourceProperty, List<(MatcherValue MatcherValue, string Value)>>();
            foreach (var s in otherMatchers)
            {
                switch (s.Property)
                {
                    case ResourceProperty.ParentResource:
                    case ResourceProperty.ReleaseDt:
                    case ResourceProperty.Name:
                    case ResourceProperty.Volume:
                    case ResourceProperty.Series:
                    case ResourceProperty.Rate:
                    case ResourceProperty.Original:
                    case ResourceProperty.Publisher:
                    case ResourceProperty.Tag:
                    case ResourceProperty.CustomProperty:
                    {
                        var matchResult =
                            ResourcePropertyMatcher.Match(segments, s, rootPathSegments.Length - 1, segments.Length);

                        
                        if (matchResult != null)
                        {
                            var values = matchedValues.GetOrAdd(s.Property, () => new());

                            switch (matchResult.Type)
                            {
                                case MatchResultType.Layer:
                                {
                                    // Value of parent resource should be a complete path
                                    if (s.Property == ResourceProperty.ParentResource)
                                    {
                                        var prPath = string.Join(BusinessConstants.DirSeparator,
                                            segments.Take(matchResult.Index!.Value + 1));
                                        values.Add((s, prPath));
                                    }
                                    else
                                    {
                                        values.Add((s, segments[matchResult.Index!.Value]));

                                    }

                                    break;
                                }
                                case MatchResultType.Regex:
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }

                        break;
                    }
                    case ResourceProperty.Introduction:
                    case ResourceProperty.RootPath:
                    case ResourceProperty.Resource:
                    case ResourceProperty.Language:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            foreach (var (t, values) in matchedValues)
            {
                var (firstMatcher, firstValue) = values[0];
                switch (t)
                {
                    case ResourceProperty.ParentResource:
                    {
                        if (firstValue != pr.RawFullname)
                        {
                            if (!parentResources.TryGetValue(firstValue, out var parent))
                            {
                                parentResources[firstValue] = parent = new ResourceDto
                                {
                                    Directory = Path.GetDirectoryName(firstValue)!.StandardizePath(),
                                    RawName = Path.GetFileName(firstValue),
                                    CategoryId = library.CategoryId,
                                    MediaLibraryId = library.Id,
                                    IsSingleFile = false
                                };
                            }

                            pr.Parent = parent;
                        }

                        break;
                    }
                    case ResourceProperty.ReleaseDt:
                    {
                        DateTime dt = default;
                        if (!(firstMatcher.Key.IsNotEmpty() && DateTime.TryParseExact(firstValue, firstMatcher.Key,
                                null, DateTimeStyles.None, out dt)) || DateTime.TryParse(firstValue, out dt))
                        {
                            pr.ReleaseDt = dt;
                        }

                        break;
                    }
                    case ResourceProperty.Publisher:
                        pr.Publishers = values.Select(a => new PublisherDto {Name = a.Value}).ToList();
                        break;
                    case ResourceProperty.Name:
                        pr.Name = firstValue;
                        break;
                    case ResourceProperty.Volume:
                        pr.Volume = new VolumeDto {Name = firstValue};
                        break;
                    case ResourceProperty.Original:
                        pr.Originals = values.Select(a => new OriginalDto {Name = a.Value}).ToList();
                        break;
                    case ResourceProperty.Series:
                        pr.Series = new SeriesDto {Name = firstValue};
                        break;
                    case ResourceProperty.Tag:
                        pr.Tags.AddRange(values.Select(a => new TagDto {Name = a.Value}));
                        break;
                    case ResourceProperty.Rate:
                        if (decimal.TryParse(firstValue, out var r))
                        {
                            pr.Rate = r;
                        }

                        break;
                    case ResourceProperty.CustomProperty:
                        pr.CustomProperties = values.GroupBy(a => a.MatcherValue.Key).ToDictionary(a => a.Key, a => a
                            .Select(b => new CustomResourceProperty
                            {
                                Value = b.Value,
                                ValueType = CustomDataType.String
                            }).ToList());
                        break;
                    case ResourceProperty.Language:
                    case ResourceProperty.Introduction:
                    case ResourceProperty.RootPath:
                    case ResourceProperty.Resource:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public async Task<BaseResponse> Sync(BackgroundTask task)
        {
            // Make log and error can show in background task info.
            var libraries = await GetAll();
            var librariesMap = libraries.ToDictionary(a => a.Id, a => a);
            var categories = (await ResourceCategoryService.GetAllDto()).ToDictionary(a => a.Id, a => a);

            // Validation
            {
                var ignoredLibraries = new List<MediaLibraryDto>();
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
            var patchingResources = new Dictionary<string, ResourceDto>(StringComparer.OrdinalIgnoreCase);
            var parentResources = new Dictionary<string, ResourceDto>();
            var prevRawFullnameResourcesMap = new Dictionary<string, ResourceDto>();
            var invalidData = new List<ResourceDto>();

            var changedResources = new ConcurrentDictionary<string, ResourceDto>();

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
                                (decimal) 1 / library.PathConfigurations.Length *
                                percentagePerLibrary;
                            for (var j = 0; j < library.PathConfigurations.Length; j++)
                            {
                                var pathConfiguration = library.PathConfigurations[j];
                                var resourceMatcher =
                                    pathConfiguration.RpmValues.FirstOrDefault(m =>
                                        m.Property == ResourceProperty.Resource);
                                if (!Directory.Exists(pathConfiguration.Path) || resourceMatcher == null)
                                {
                                    continue;
                                }

                                var standardRootPath = pathConfiguration.Path.StandardizePath();
                                var resourcePaths =
                                    DiscoverAllResourceFullnameList(pathConfiguration.Path, resourceMatcher);
                                if (!resourcePaths.Any())
                                {
                                    continue;
                                }

                                var otherMatchers = pathConfiguration.RpmValues
                                    .Where(m => m != resourceMatcher && m.IsValid).ToList();
                                var percentagePerItem =
                                    (decimal) 1 / resourcePaths.Length * percentagePerPathConfiguration;
                                var count = 0;
                                foreach (var resourcePath in resourcePaths)
                                {
                                    var pr = new ResourceDto()
                                    {
                                        CategoryId = library.CategoryId,
                                        MediaLibraryId = library.Id,
                                        IsSingleFile = new FileInfo(resourcePath).Exists,
                                        Directory = Path.GetDirectoryName(resourcePath).StandardizePath()!,
                                        RawName = Path.GetFileName(resourcePath),
                                        Tags = new List<TagDto>(),
                                    };

                                    if (pathConfiguration.FixedTagIds?.Any() == true)
                                    {
                                        pr.Tags.AddRange(
                                            pathConfiguration.FixedTagIds.Select(a =>
                                                new TagDto {Id = a}));
                                    }

                                    if (pathConfiguration.RpmValues?.Any() ==
                                        true)
                                    {
                                        SetPropertiesByMatchers(pr, standardRootPath, otherMatchers, parentResources,
                                            library);
                                    }

                                    patchingResources.TryAdd(pr.RawFullname, pr);
                                    task.Percentage = basePercentage + (int) (i * percentagePerLibrary +
                                                                              percentagePerPathConfiguration * j +
                                                                              percentagePerItem * (++count));
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
                            FileSystemInfo fileSystemInfo = patches.IsSingleFile
                                ? new FileInfo(fullname)
                                : new DirectoryInfo(fullname);
                            patches.FileCreateDt =
                                fileSystemInfo.CreationTime.AddTicks(-(fileSystemInfo.CreationTime.Ticks %
                                                                       TimeSpan.TicksPerSecond));
                            patches.FileModifyDt =
                                fileSystemInfo.LastWriteTime.AddTicks(-(fileSystemInfo.LastWriteTime.Ticks %
                                                                        TimeSpan.TicksPerSecond));
                            task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
                        }

                        break;
                    }
                    case MediaLibrarySyncStep.CleanResources:
                    {
                        // Remove resources belonged to unknown libraries
                        await ResourceService.RemoveByMediaLibraryIdsNotIn(librariesMap.Keys.ToArray());
                        var prevResources = await ResourceService.GetAll(ResourceAdditionalItem.All);

                        var prevRawFullnameResourcesList = prevResources
                            .GroupBy(a => a.RawFullname.StandardizePath(), StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(t => t.Key, t => t.ToArray());

                        var duplicatedResources = prevRawFullnameResourcesList.Values.Where(t => t.Length > 0)
                            .SelectMany(t => t.Skip(1)).ToArray();

                        invalidData.AddRange(duplicatedResources);
                        prevRawFullnameResourcesMap =
                            prevRawFullnameResourcesList.ToDictionary(t => t.Key, t => t.Value[0]);

                        // Compare
                        var missingFullnameList = prevRawFullnameResourcesMap.Keys
                            .Except(patchingResources.Keys, StringComparer.OrdinalIgnoreCase)
                            .ToHashSet();
                        // Remove unknown data
                        // Bad directory/raw names will be deleted there.
                        invalidData.AddRange(missingFullnameList.Select(a =>
                        {
                            prevRawFullnameResourcesMap.Remove(a, out var d);
                            return d!;
                        }));
                        // Remove mismatched category/library resources.

                        var invalidMediaLibraryResources = prevRawFullnameResourcesMap.Values
                            .Where(a => !librariesMap.ContainsKey(a.MediaLibraryId)).ToArray();

                        foreach (var r in invalidMediaLibraryResources)
                        {
                            prevRawFullnameResourcesMap.Remove(r.RawFullname);
                            invalidData.Add(r);
                        }

                        await ResourceService.RemoveByKeys(invalidData.Select(a => a.Id).ToArray(), false);
                        break;
                    }
                    case MediaLibrarySyncStep.CompareResources:
                    {
                        var count = 0;
                        foreach (var (fullname, pr) in
                                 patchingResources)
                        {
                            if (!prevRawFullnameResourcesMap.TryGetValue(fullname, out var resource))
                            {
                                resource = pr;
                                changedResources[fullname] = resource;
                            }

                            if (resource.MergeOnSynchronization(pr))
                            {
                                changedResources[fullname] = resource;
                            }

                            task.Percentage = basePercentage + (int) (++count / resourceCountPerPercentage);
                        }

                        foreach (var (_, r) in changedResources)
                        {
                            r.UpdateDt = DateTime.Now;
                        }

                        break;
                    }
                    case MediaLibrarySyncStep.SaveResources:
                    {
                        var resourcesToBeSaved = changedResources.Values.ToList();
                        var newResources = resourcesToBeSaved.Where(a => a.Id == 0).ToArray();
                        await ResourceService.AddOrUpdateRange(resourcesToBeSaved);

                        // #region Tags
                        //
                        // var resourcePathIds = newResources.ToDictionary(t => t.RawFullname, t => t.Id);
                        // foreach (var p in prevRawFullnameResourcesMap.Where(
                        //              p => !resourcePathIds.ContainsKey(p.Key)))
                        // {
                        //     resourcePathIds[p.Key] = p.Value.Id;
                        // }
                        //
                        // var newTagNames = patchingResources.Where(t => t.Value.DynamicTagNames != null)
                        //     .SelectMany(t => t.Value.DynamicTagNames).Where(a => a.IsNotEmpty()).ToHashSet();
                        // var newTagNameIdMap = new Dictionary<string, int>();
                        // if (newTagNames.Any())
                        // {
                        //     newTagNameIdMap = (await TagService.AddRange(new Dictionary<string, string[]>
                        //     {
                        //         {
                        //             string.Empty, newTagNames.ToArray()
                        //         }
                        //     }, true)).GroupBy(t => t.Name).ToDictionary(t => t.Key, t => t.FirstOrDefault()!.Id);
                        // }
                        //
                        // var prevResourceIds = prevRawFullnameResourcesMap.Select(a => a.Value.Id).Where(a => a > 0)
                        //     .Distinct()
                        //     .ToArray();
                        // var prevResourceTagMappings =
                        //     (await ResourceTagMappingService.GetAll(a => prevResourceIds.Contains(a.ResourceId), false))
                        //     .GroupBy(a => a.ResourceId).ToDictionary(a => a.Key,
                        //         a => a.Select(b => b.TagId).Distinct().ToArray());
                        //
                        // var resourceIdTagIdsMap = new Dictionary<int, int[]>();
                        // foreach (var (key, pr) in patchingResources.Where(t =>
                        //              t.Value.DynamicTagNames != null || t.Value.FixedTagIds != null))
                        // {
                        //     var allTagIds = (pr.FixedTagIds ?? new int[] { })
                        //         .Concat((pr.DynamicTagNames ?? new string[] { }).Select(t => newTagNameIdMap[t]))
                        //         .Distinct()
                        //         .ToArray();
                        //     var resourceId = resourcePathIds[key];
                        //     if (prevResourceTagMappings.TryGetValue(resourceId, out var prevTagIds))
                        //     {
                        //         allTagIds = allTagIds.Concat(prevTagIds).ToArray();
                        //     }
                        //
                        //     if (allTagIds.Any())
                        //     {
                        //         resourceIdTagIdsMap[resourceId] = allTagIds.Distinct().ToArray();
                        //     }
                        // }
                        //
                        // await ResourceTagMappingService.UpdateRange(resourceIdTagIdsMap);
                        //
                        // #endregion

                        // Update sync result
                        var libraryResourceCount = patchingResources.GroupBy(a => a.Value.MediaLibraryId)
                            .ToDictionary(a => a.Key, a => a.Count());
                        await UpdateByKeys(libraries.Select(a => a.Id).ToArray(),
                            l => { l.ResourceCount = libraryResourceCount.TryGetValue(l.Id, out var c) ? c : 0; });

                        task.Message = string.Join(
                            Environment.NewLine,
                            $"[Resource] Found: {patchingResources.Count}, New: {newResources.Length} Removed: {invalidData.Count}, Updated: {resourcesToBeSaved.Count - newResources.Length}",
                            $"[Directory]: Found: {patchingResources.Count(a => !a.Value.IsSingleFile)}",
                            $"[SingleFile]: Found: {patchingResources.Count(a => a.Value.IsSingleFile)}"
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

        public async Task<BaseResponse> SetOrders(MediaLibraryChangeOrderRequestModel model)
        {
            var orderedIds = model.OrderBy(a => a.Value).Select(a => a.Key).ToList();
            var orders = orderedIds.ToDictionary(a => a, a => orderedIds.IndexOf(a));
            var wss = await base.GetAll();
            foreach (var ws in wss)
            {
                ws.Order = orders.TryGetValue(ws.Id, out var order) ? order : int.MaxValue;
            }

            return await UpdateRange(wss);
        }

        public static bool CheckPathContaining(IReadOnlyCollection<string> paths)
        {
            var distinct = paths.Distinct().ToArray();
            if (distinct.Length != paths.Count)
            {
                return true;
            }

            var uris = distinct.ToDictionary(a => a, StandardizeForPathComparing);
            if (uris.Any(r => uris.Where(a => a.Key != r.Key).Any(a => a.Value.IsBaseOf(r.Value))))
            {
                return true;
            }

            return false;
        }

        public static Uri StandardizeForPathComparing(string path)
        {
            return new Uri(
                $"{path.TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)}{BusinessConstants.DirSeparator}");
        }

        public static bool CheckPathContaining(IReadOnlyCollection<string> pool, string target)
        {
            var uris = pool.Select(StandardizeForPathComparing).ToList();
            var targetUri = StandardizeForPathComparing(target);
            if (uris.Any(r => targetUri.IsBaseOf(r) || r.IsBaseOf(targetUri)))
            {
                return true;
            }

            return false;
        }

        public async Task<BaseResponse> Add(MediaLibraryCreateRequestModel model)
        {
            var ml = new MediaLibrary
            {
                CategoryId = model.CategoryId,
                Name = model.Name
            };

            if (model.PathConfigurations?.Any() == true)
            {
                var otherMls = await GetAll();
                // foreach (var o in otherMls)
                // {
                //     if (o.PathConfigurations != null)
                //     {
                //         var paths = o.PathConfigurations.Concat(model.PathConfigurations).Select(t => t.Path).ToArray();
                //         if (CheckPathContaining(paths))
                //         {
                //             return BaseResponseBuilder.BuildBadRequest("Relative paths are found.");
                //         }
                //     }
                // }

                foreach (var p in model.PathConfigurations)
                {
                    if (p.Regex.IsNullOrEmpty())
                    {
                        p.Regex = 1.BuildMultiLayerRegexString();
                    }
                }


                ml.PathConfigurationsJson = JsonConvert.SerializeObject(model.PathConfigurations);
            }

            var t = await Add(ml);
            return t;
        }

        private static void CreateDirectories(params string[] rootPaths)
        {
            if (rootPaths != null)
            {
                foreach (var rootPath in rootPaths)
                {
                    Directory.CreateDirectory(rootPath);
                }
            }
        }

        public async Task<List<MediaLibraryDto>> GetAll()
        {
            var categories = (await ResourceCategoryService.GetAllDto()).ToDictionary(a => a.Id, a => a);
            var categoryIds = categories.Keys.ToHashSet();
            var wss = (await base.GetAll(a => categoryIds.Contains(a.CategoryId), true)).OrderBy(a => a.Order).ToList();
            var drives = DriveInfo.GetDrives().ToDictionary(t => t.Name, t => t);
            var dtoList = wss.Select(t => t.ToDto())
                .OrderBy(t => categories.TryGetValue(t.CategoryId, out var c) ? c.Order : 0).ThenBy(t => t.Order)
                .ToArray();
            var fixedTagIds = dtoList
                .SelectMany(t => t.PathConfigurations?.SelectMany(a => a.FixedTagIds ?? new int[] { }) ?? new int[] { })
                .Distinct().ToArray();
            var tags = (await TagService.GetByKeys(fixedTagIds, TagAdditionalItem.None))
                .ToDictionary(t => t.Id, t => t);
            return dtoList.Select(wsd =>
            {
                foreach (var pathConfiguration in wsd.PathConfigurations)
                {
                    var ri = wsd.RootPathInformation[pathConfiguration.Path] ??
                             (wsd.RootPathInformation[pathConfiguration.Path] =
                                 new MediaLibraryDto.SingleMediaLibraryRootPathInformation());
                    var driveRoot = Path.GetPathRoot(pathConfiguration.Path);
                    if (drives.TryGetValue(driveRoot!, out var d))
                    {
                        ri.FreeSpace = d.AvailableFreeSpace;
                        ri.TotalSize = d.TotalSize;
                        if (d.AvailableFreeSpace < MinimalFreeSpace)
                        {
                            ri.Error = MediaLibraryError.FreeSpaceNotEnough;
                        }
                    }
                    else
                    {
                        ri.Error = MediaLibraryError.InvalidVolume;
                    }

                    if (pathConfiguration.FixedTagIds?.Any() == true)
                    {
                        pathConfiguration.FixedTags = pathConfiguration.FixedTagIds
                            .Select(t => tags.TryGetValue(t, out var n) ? n : null).Where(t => t != null)
                            .ToList()!;
                    }
                }

                if (categories.TryGetValue(wsd.CategoryId, out var c))
                {
                    wsd.CategoryName = c.Name;
                }

                return wsd;
            }).ToList();
        }

        public async Task<BaseResponse> Patch(int id, MediaLibraryUpdateRequestModel model)
        {
            var ml = await GetByKey(id);
            if (model.PathConfigurations != null)
            {
                var otherMls = await GetAll();
                otherMls.RemoveAll(t => t.Id == id);
                // foreach (var o in otherMls)
                // {
                //     if (o.PathConfigurations != null)
                //     {
                //         var paths = o.PathConfigurations.Concat(model.PathConfigurations).Select(t => t.Path).ToArray();
                //         if (CheckPathContaining(paths))
                //         {
                //             return BaseResponseBuilder.BuildBadRequest("Relative paths are found.");
                //         }
                //     }
                // }

                foreach (var p in model.PathConfigurations)
                {
                    // if (p.Regex.IsNullOrEmpty())
                    // {
                    //     p.Regex = 1.BuildMultiLayerRegexString();
                    // }

                    if (p.RpmValues?.Any() == true)
                    {
                        foreach (var s in p.RpmValues.Where(s =>
                                     s.Property != ResourceProperty.CustomProperty))
                        {
                            s.Key = null;
                        }
                    }
                }

                ml.PathConfigurationsJson = JsonConvert.SerializeObject(model.PathConfigurations);
            }

            if (ml.Name != model.Name && model.Name.IsNotEmpty())
            {
                ml.Name = model.Name;
            }

            if (model.Order.HasValue)
            {
                ml.Order = model.Order.Value;
            }

            var t = await Update(ml);
            return t;
        }

        public async Task<BaseResponse> Sort(int[] ids)
        {
            var libraries = (await GetByKeys(ids)).ToDictionary(t => t.Id, t => t);
            var changed = new List<MediaLibrary>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (libraries.TryGetValue(id, out var t) && t.Order != i)
                {
                    t.Order = i;
                    changed.Add(t);
                }
            }

            return await UpdateRange(changed);
        }

        public async Task<SingletonResponse<PathConfigurationValidateResult>> Test(MediaLibrary.PathConfiguration pc,
            int maxResourceCount = 1)
        {
            if (pc.Path.IsNullOrEmpty())
            {
                return SingletonResponseBuilder<PathConfigurationValidateResult>.BuildBadRequest(
                    "Root path is required");
            }

            var resourceMatcherValue =
                pc.RpmValues.FirstOrDefault(a => a is
                    {Property: ResourceProperty.Resource, IsValid: true});
            if (resourceMatcherValue == null)
            {
                return SingletonResponseBuilder<PathConfigurationValidateResult>.BuildBadRequest(
                    "A valid resource matcher value is required");
            }

            pc.Path = pc.Path?.StandardizePath();
            var dir = new DirectoryInfo(pc.Path!);
            var entries = new List<PathConfigurationValidateResult.Entry>();
            if (dir.Exists)
            {
                var rootSegments = pc.Path!.SplitPathIntoSegments();
                var fixedTags = pc.FixedTagIds?.Any() == true
                    ? (await TagService.GetByKeys(pc.FixedTagIds,
                        TagAdditionalItem.PreferredAlias | TagAdditionalItem.GroupName)).ToList()
                    : new();
                var filesystemItems = Directory.GetFileSystemEntries(dir.FullName, "*.*", SearchOption.AllDirectories);
                foreach (var f in filesystemItems)
                {
                    var segments = f.SplitPathIntoSegments();
                    var resourceMatchValue = ResourcePropertyMatcher.Match(segments, resourceMatcherValue,
                        rootSegments.Length - 1, segments.Length);
                    if (resourceMatchValue is not {Type: MatchResultType.Layer} || resourceMatchValue.Index < rootSegments.Length)
                    {
                        continue;
                    }

                    var relativeSegments = segments[rootSegments.Length..];
                    var relativePath = string.Join(BusinessConstants.DirSeparator, relativeSegments);

                    var entry = new PathConfigurationValidateResult.Entry(Directory.Exists(f), relativePath)
                    {
                        FixedTags = fixedTags,
                        SegmentAndMatchedValues = relativeSegments
                            .Select(s => new PathConfigurationValidateResult.Entry.SegmentMatchResult(s)).ToList(),
                        IsDirectory = Directory.Exists(f),
                        RelativePath = relativePath
                    };

                    var otherMatchers = pc.RpmValues!.Where(a =>
                        a.Property != ResourceProperty.Resource && a.Property != ResourceProperty.RootPath).ToList();

                    foreach (var m in otherMatchers)
                    {
                        var result = ResourcePropertyMatcher.Match(segments, m, rootSegments.Length - 1, segments.Length);
                        if (result != null)
                        {
                            switch (result.Type)
                            {
                                case MatchResultType.Layer:
                                {
                                    entry.SegmentAndMatchedValues[
                                            result.Layer > 0 ? result.Layer.Value - 1 : ^(1-result.Layer!.Value)]
                                        .Properties
                                        .Add(m.Property);
                                    break;
                                }
                                case MatchResultType.Regex:
                                {
                                    var list = entry.GlobalMatchedValues.GetOrAdd((int)m.Property,
                                        () => new HashSet<string>());
                                    foreach (var t in result.Matches!)
                                    {
                                        list.Add(t);
                                    }

                                    break;
                                }
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }

                    entries.Add(entry);

                    if (entries.Count >= maxResourceCount)
                    {
                        break;
                    }
                }

                return new SingletonResponse<PathConfigurationValidateResult>(
                    new PathConfigurationValidateResult(entries));
            }

            return SingletonResponseBuilder<PathConfigurationValidateResult>.NotFound;
        }
    }
}