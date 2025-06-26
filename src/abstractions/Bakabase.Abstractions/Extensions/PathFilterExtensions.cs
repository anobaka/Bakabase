using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using System.Text.RegularExpressions;
using Bootstrap.Extensions;
using Bootstrap.Components.Tasks;
using System;
using Bakabase.Abstractions.Components.Tasks;
using Bootstrap.Components.Storage;
using CsQuery.Engine.PseudoClassSelectors;

namespace Bakabase.Abstractions.Extensions;

public static class PathFilterExtensions
{
    public static async Task<Dictionary<string, ResourcePathInfo>> Filter(this List<PathFilter> filters,
        string rootPath,
        string[]? subPaths = null, Func<int, Task>? onProgressChange = null, Func<string, Task>? onProcessChange = null,
        PauseToken pt = default,
        CancellationToken ct = default,
        int maxThreads = 1)
    {
        const float progressForScanning = 70f;

        rootPath = rootPath.StandardizePath()!;
        var fileEnumerationOnProgress = onProgressChange.ScaleInSubTask(0, progressForScanning);
        HashSet<string>? dirSet = null;
        if (subPaths == null)
        {
            var data = DirectoryUtils.EnumerateFileSystemEntries(rootPath, p => fileEnumerationOnProgress?.Invoke(p))
                .Select(x => x with {Path = x.Path.StandardizePath()!}).ToArray();
            dirSet = data.Where(d => !d.IsFile).Select(d => d.Path).ToHashSet();
            subPaths = data.Select(d => d.Path).ToArray();
        }

        var pathRelativePathMap =
            subPaths.ToDictionary(d => d, x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator));
        var pathRelativeSegmentsMap =
            pathRelativePathMap.ToDictionary(kvp => kvp.Key,
                kvp => kvp.Value.Split(InternalOptions.DirSeparator, StringSplitOptions.RemoveEmptyEntries));
        var resourcePathInfoMap = new Dictionary<string, ResourcePathInfo>();
        if (onProgressChange != null)
        {
            var progress = (int) (progressForScanning);
            await onProgressChange(progress);
        }

        var currentProgress = progressForScanning;
        const float progressForFiltering = 100 - progressForScanning;
        var progressPerFilter = filters.Count == 0 ? 0 : (100 - progressForFiltering) / filters.Count;
        var progressPerPath = subPaths.Length == 0 ? 0 : progressForFiltering / subPaths.Length;

        HashSet<string>? fileSet = null;
        if (filters.Any(f => f.FsType.HasValue))
        {
            dirSet ??= subPaths.Where(Directory.Exists).ToHashSet();
            fileSet = subPaths.Except(dirSet).ToHashSet();
        }

        var cachedInnerPathsMap = new Dictionary<string, string[]>();

        var filterOnProgress = onProgressChange.ScaleInSubTask(progressForScanning, progressForFiltering);
        for (var index = 0; index < filters.Count; index++)
        {
            var rf = filters[index];

            IEnumerable<string> candidatePaths = subPaths;
            if (rf.FsType.HasValue)
            {
                switch (rf.FsType.Value)
                {
                    case PathFilterFsType.File:
                        candidatePaths = fileSet!;
                        break;
                    case PathFilterFsType.Directory:
                        candidatePaths = dirSet!;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            candidatePaths = candidatePaths.Except(resourcePathInfoMap.Keys).ToArray();

            switch (rf.Positioner)
            {
                case PathPositioner.Layer:
                {
                    if (rf.Layer.HasValue)
                    {
                        foreach (var path in candidatePaths)
                        {
                            var segments = pathRelativeSegmentsMap[path];
                            var len = rf.Layer.Value;
                            if (len == segments.Length)
                            {
                                var relativePath = pathRelativePathMap[path];
                                var innerPaths = cachedInnerPathsMap.GetOrAdd(path,
                                    () => subPaths.Where(x => x != path && x.StartsWith(path)).ToArray());
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, segments, innerPaths);
                            }

                            currentProgress =
                                await filterOnProgress.TriggerOnJumpingOver(currentProgress, progressPerPath);
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        var regex = new Regex(rf.Regex, RegexOptions.Compiled);
                        await Parallel.ForEachAsync(candidatePaths,
                            new ParallelOptions {MaxDegreeOfParallelism = maxThreads}, async (path, pct) =>
                            {
                                var relativePath = pathRelativePathMap[path];
                                var match = regex.Match(relativePath);
                                if (match.Success)
                                {
                                    var len = match.Value.Split(InternalOptions.DirSeparator,
                                        StringSplitOptions.RemoveEmptyEntries).Length;
                                    var segments = pathRelativeSegmentsMap[path];
                                    var relativeSegments = segments.Take(len).ToArray();
                                    var resourcePath = string.Join(InternalOptions.DirSeparator, rootPath,
                                        relativePath);
                                    if (!resourcePathInfoMap.ContainsKey(resourcePath))
                                    {
                                        var innerPaths = cachedInnerPathsMap.GetOrAdd(resourcePath,
                                            () => subPaths.Where(x => x != resourcePath && x.StartsWith(resourcePath))
                                                .ToArray());
                                        resourcePathInfoMap[resourcePath] =
                                            new ResourcePathInfo(path, relativePath, relativeSegments, innerPaths);
                                    }
                                }

                                currentProgress =
                                    await filterOnProgress.TriggerOnJumpingOver(currentProgress, progressPerPath);
                            });
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            currentProgress = await filterOnProgress.TriggerOnJumpingOver(currentProgress,
                progressPerFilter * (index + 1) - currentProgress);
        }

        return resourcePathInfoMap;
    }
}