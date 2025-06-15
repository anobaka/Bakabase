using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using System.Text.RegularExpressions;
using Bootstrap.Extensions;
using Bootstrap.Components.Tasks;
using System;
using Bakabase.Abstractions.Components.Tasks;
using Bootstrap.Components.Storage;

namespace Bakabase.Abstractions.Extensions;

public static class PathFilterExtensions
{
    public static async Task<Dictionary<string, ResourcePathInfo>> Filter(this List<PathFilter> filters,
        string rootPath,
        string[]? subPaths = null, Func<int, Task>? onProgressChange = null, Func<string, Task>? onProcessChange = null,
        PauseToken pt = default,
        CancellationToken ct = default)
    {
        const decimal progressForScanning = 50m;

        rootPath = rootPath.StandardizePath()!;
        subPaths ??= Directory.GetFileSystemEntries(rootPath, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        }).Select(x => x.StandardizePath()!).ToArray();
        var subRelativePaths =
            subPaths.Select(x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator)).ToList();
        var subPathRelativeSegments = subRelativePaths.Select(x => x.Split(InternalOptions.DirSeparator)).ToList();
        var resourcePathInfoMap = new Dictionary<string, ResourcePathInfo>();
        if (onProgressChange != null)
        {
            var progress = (int)(progressForScanning);
            await onProgressChange(progress);
        }

        var currentProgress = progressForScanning;
        const decimal progressForFiltering = 100m - progressForScanning;
        var progressPerFilter = filters.Count == 0 ? 0 : (100m - progressForFiltering) / filters.Count;
        var progressPerPath = subRelativePaths.Count == 0 ? 0 : progressForFiltering / subRelativePaths.Count;
        for (var index = 0; index < filters.Count; index++)
        {
            var rf = filters[index];
            switch (rf.Positioner)
            {
                case PathPositioner.Layer:
                {
                    if (rf.Layer.HasValue)
                    {
                        foreach (var segments in subPathRelativeSegments)
                        {
                            var len = rf.Layer.Value;
                            if (len >= 0 && len < segments.Length)
                            {
                                var relativeSegments = segments.Take(len).ToArray();
                                var relativePath = string.Join(InternalOptions.DirSeparator, relativeSegments);
                                var path = string.Join(InternalOptions.DirSeparator, rootPath, relativePath);
                                var insidePaths = subPaths.Where(x => x != path && x.StartsWith(path)).ToArray();
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, relativeSegments, insidePaths);
                            }

                            currentProgress = await onProgressChange.TriggerWithStep1(currentProgress, progressPerPath);
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        for (var j = 0; j < subRelativePaths.Count; j++)
                        {
                            var relativePath = subRelativePaths[j];
                            var match = Regex.Match(relativePath, rf.Regex);
                            if (match.Success)
                            {
                                var len = match.Value.Split(InternalOptions.DirSeparator,
                                    StringSplitOptions.RemoveEmptyEntries).Length;
                                var segments = subPathRelativeSegments[j];
                                var relativeSegments = segments.Take(len).ToArray();
                                var path = string.Join(InternalOptions.DirSeparator, rootPath, relativePath);
                                var insidePaths = subPaths.Where(x => x != path && x.StartsWith(path)).ToArray();
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, relativeSegments, insidePaths);
                            }

                            currentProgress = await onProgressChange.TriggerWithStep1(currentProgress, progressPerPath);
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            currentProgress = progressPerFilter;
            if (onProgressChange != null)
            {
                await onProgressChange((int)currentProgress);
            }
        }

        return resourcePathInfoMap;
    }
}