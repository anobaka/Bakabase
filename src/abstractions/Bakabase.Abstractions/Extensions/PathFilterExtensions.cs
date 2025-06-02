using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using System.Text.RegularExpressions;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class PathFilterExtensions
{
    public static Dictionary<string, ResourcePathInfo> Filter(this List<PathFilter> filters, string rootPath,
        string[]? subPaths = null)
    {
        subPaths ??= Directory.GetFileSystemEntries(rootPath, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        });
        var subRelativePaths =
            subPaths.Select(x => x.Replace(rootPath, null).Trim(InternalOptions.DirSeparator)).ToList();
        var subPathRelativeSegments = subRelativePaths.Select(x => x.Split(InternalOptions.DirSeparator)).ToList();
        var resourcePathInfoMap = new Dictionary<string, ResourcePathInfo>();
        foreach (var rf in filters)
        {
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
                        }
                    }

                    break;
                }
                case PathPositioner.Regex:
                {
                    if (rf.Regex.IsNotEmpty())
                    {
                        for (var i = 0; i < subRelativePaths.Count; i++)
                        {
                            var relativePath = subRelativePaths[i];
                            var match = Regex.Match(relativePath, rf.Regex);
                            if (match.Success)
                            {
                                var len = match.Value.Split(InternalOptions.DirSeparator,
                                    StringSplitOptions.RemoveEmptyEntries).Length;
                                var segments = subPathRelativeSegments[i];
                                var relativeSegments = segments.Take(len).ToArray();
                                var path = string.Join(InternalOptions.DirSeparator, rootPath, relativePath);
                                var insidePaths = subPaths.Where(x => x != path && x.StartsWith(path)).ToArray();
                                resourcePathInfoMap[path] =
                                    new ResourcePathInfo(path, relativePath, relativeSegments, insidePaths);
                            }
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return resourcePathInfoMap;
    }
}