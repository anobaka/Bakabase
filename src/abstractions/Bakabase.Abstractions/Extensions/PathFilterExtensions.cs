using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using System.Text.RegularExpressions;
using Bootstrap.Components.Tasks;
using System;
using System.Collections.Concurrent;
using Bakabase.Abstractions.Components.Tasks;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using CsQuery.Engine.PseudoClassSelectors;
using NPOI.SS.Formula.Functions;
using System.Linq;

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
        await using var progressor = new BProgressor(onProgressChange);
        const float progressForScanning = 70f;

        var scanningProgressor = progressor.CreateNewScope(0, progressForScanning);
        rootPath = rootPath.StandardizePath()!;
        HashSet<string>? dirSet = null;
        if (subPaths == null)
        {
            var data = DirectoryUtils.EnumerateFileSystemEntries(rootPath, p => _ = scanningProgressor.Set(p))
                .Select(x => x with {Path = x.Path.StandardizePath()!}).ToArray();
            dirSet = data.Where(d => !d.IsFile).Select(d => d.Path).ToHashSet();
            subPaths = data.Select(d => d.Path).ToArray();
        }

        var pathRelativePathMap = new ConcurrentDictionary<string, string>(
            subPaths.ToDictionary(d => d, x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator)));
        var pathRelativeSegmentsMap = new ConcurrentDictionary<string, string[]>(pathRelativePathMap.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Split(InternalOptions.DirSeparator, StringSplitOptions.RemoveEmptyEntries)));
        var resourcePathInfoMap = new ConcurrentDictionary<string, ResourcePathInfo>();
        await scanningProgressor.DisposeAsync();

        var filteringProgressor = progressor.CreateNewScope(progressForScanning, 100 - progressForScanning);
        var progressPerFilter = filters.Count == 0 ? 0 : 100f / filters.Count;
        var progressPerPath = subPaths.Length == 0 ? 0 : 100f / subPaths.Length;

        dirSet ??= subPaths.Where(Directory.Exists).ToHashSet();
        HashSet<string>? fileSet = null;
        if (filters.Any(f => f.FsType.HasValue))
        {
            fileSet = subPaths.Except(dirSet).ToHashSet();
        }

        var cachedInnerPathsMap = new ConcurrentDictionary<string, string[]>();

        for (var index = 0; index < filters.Count; index++)
        {
            await using var filterProgressor =
                filteringProgressor.CreateNewScope(progressForScanning + index * progressPerFilter, progressPerFilter);
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
                        candidatePaths = dirSet;
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
                            await pt.WaitWhilePausedAsync(ct);
                            ct.ThrowIfCancellationRequested();
                            var segments = pathRelativeSegmentsMap[path];
                            var len = rf.Layer.Value;
                            if (len == segments.Length)
                            {
                                var relativePath = pathRelativePathMap[path];
                                var innerPaths = cachedInnerPathsMap.GetOrAdd(path,
                                    (_) => subPaths.Where(x => x != path && x.StartsWith(path)).ToArray());
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, segments, innerPaths, !dirSet.Contains(path));
                            }

                            await filterProgressor.Add(progressPerPath);
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        var regex = new Regex(rf.Regex, RegexOptions.Compiled);
                        var lazyMap = new ConcurrentDictionary<string, Lazy<ResourcePathInfo>>();

                        var tasks = new List<Task>();
                        var candidatePathCountPerThread = Math.Max(1, candidatePaths.Count() / maxThreads);
                        for (var i = 0; i < maxThreads; i++)
                        {
                            var threadedCandidatePaths = candidatePaths.Skip(candidatePathCountPerThread * i)
                                .Take(candidatePathCountPerThread).ToArray();
                            if (threadedCandidatePaths.Length == 0)
                            {
                                break;
                            }

                            var fp = filterProgressor;
                            tasks.Add(Task.Run(async () =>
                            {
                                foreach (var path in threadedCandidatePaths)
                                {
                                    await pt.WaitWhilePausedAsync(ct);
                                    ct.ThrowIfCancellationRequested();
                                    var relativePath = pathRelativePathMap[path];
                                    var match = regex.Match(relativePath);
                                    if (match.Success)
                                    {
                                        var fullMatchedPath =
                                            relativePath.Substring(0, match.Index + match.Value.Length);
                                        var len = fullMatchedPath.Split(InternalOptions.DirSeparator,
                                            StringSplitOptions.RemoveEmptyEntries).Length;
                                        var segments = pathRelativeSegmentsMap[path];
                                        var relativeSegments = segments.Take(len).ToArray();
                                        var resourcePath = string.Join(InternalOptions.DirSeparator,
                                            [rootPath, .. relativeSegments]);
                                        resourcePathInfoMap.GetOrAdd(resourcePath, _ =>
                                        {
                                            return lazyMap.GetOrAdd(resourcePath, _ => new Lazy<ResourcePathInfo>(() =>
                                            {
                                                var innerPaths = cachedInnerPathsMap.GetOrAdd(resourcePath,
                                                    _ => subPaths
                                                        .Where(x => x != resourcePath && x.StartsWith(resourcePath))
                                                        .ToArray());
                                                Console.WriteLine(resourcePath);
                                                return new ResourcePathInfo(path, relativePath, relativeSegments,
                                                    innerPaths, !dirSet.Contains(resourcePath));
                                            })).Value;
                                        });
                                    }

                                    await fp.Add(progressPerPath);
                                }
                            }, ct));
                        }

                        await Task.WhenAll(tasks);

                        // await Parallel.ForEachAsync(candidatePaths,
                        //     new ParallelOptions { MaxDegreeOfParallelism = maxThreads, CancellationToken = ct },
                        //     async (path, pct) =>
                        //     {
                        //         await pt.WaitWhilePausedAsync(pct);
                        //         var relativePath = pathRelativePathMap[path];
                        //         var match = regex.Match(relativePath);
                        //         if (match.Success)
                        //         {
                        //             var len = match.Value.Split(InternalOptions.DirSeparator,
                        //                 StringSplitOptions.RemoveEmptyEntries).Length;
                        //             var segments = pathRelativeSegmentsMap[path];
                        //             var relativeSegments = segments.Take(len).ToArray();
                        //             var resourcePath = string.Join(InternalOptions.DirSeparator,
                        //                 [rootPath, ..relativeSegments]);
                        //             resourcePathInfoMap.GetOrAdd(resourcePath, _ =>
                        //             {
                        //                 return lazyMap.GetOrAdd(resourcePath, _ => new Lazy<ResourcePathInfo>(() =>
                        //                 {
                        //                     var innerPaths = cachedInnerPathsMap.GetOrAdd(resourcePath,
                        //                         _ => subPaths
                        //                             .Where(x => x != resourcePath && x.StartsWith(resourcePath))
                        //                             .ToArray());
                        //                     Console.WriteLine(resourcePath);
                        //                     return new ResourcePathInfo(path, relativePath, relativeSegments,
                        //                         innerPaths);
                        //                 })).Value;
                        //             });
                        //         }
                        //
                        //         await filterProgressor.Add(progressPerPath);
                        //     });
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        await filteringProgressor.DisposeAsync();

        return new Dictionary<string, ResourcePathInfo>(resourcePathInfoMap);
    }
}