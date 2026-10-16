using System;
using System.IO;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Extensions;

namespace Bakabase.InsideWorld.Business.Services;

internal readonly record struct PathMarkExtractedValue(string? Value, string? SourcePath);

/// <summary>Shared modern value extraction for previews, effects and media-library creation.</summary>
internal static class PathMarkDynamicValueExtractor
{
    public static PathMarkExtractedValue Extract(
        string markPath, string? resourcePath, PathMarkValueBasePath basePath,
        PathMatchMode mode, int? valueLayer, string? valueRegex, SyncContext ctx,
        bool invalidRegexUsesSourceName = false)
    {
        var sourcePath = basePath == PathMarkValueBasePath.ResourcePath ? resourcePath : markPath;
        if (string.IsNullOrEmpty(sourcePath)) return default;
        sourcePath = ctx.GetStandardizedPath(sourcePath);

        if (mode == PathMatchMode.Layer)
        {
            var layer = valueLayer ?? 0;
            if (basePath == PathMarkValueBasePath.ResourcePath && layer > 0)
                throw new ArgumentException("Resource-relative value layers must be zero or negative.");
            var baseSegments = ctx.GetPathSegments(sourcePath);
            if (layer > 0)
            {
                if (string.IsNullOrEmpty(resourcePath)) return default;
                var resourceSegments = ctx.GetPathSegments(resourcePath);
                var targetIndex = (long)baseSegments.Length - 1 + layer;
                if (targetIndex < 0 || targetIndex >= resourceSegments.Length) return default;
                sourcePath = Path.Combine(sourcePath,
                    Path.Combine(resourceSegments[baseSegments.Length..((int)targetIndex + 1)])).StandardizePath()!;
                return new PathMarkExtractedValue(resourceSegments[targetIndex], sourcePath);
            }

            var index = (long)baseSegments.Length - 1 + layer;
            if (index < 0 || index >= baseSegments.Length) return default;
            for (var i = 0L; i < -(long)layer; i++)
            {
                sourcePath = Path.GetDirectoryName(sourcePath)?.StandardizePath();
                if (sourcePath == null) return default;
            }
            return new PathMarkExtractedValue(baseSegments[index], sourcePath);
        }

        if (mode == PathMatchMode.Regex && !string.IsNullOrEmpty(valueRegex))
        {
            var name = Path.GetFileName(sourcePath);
            try
            {
                var match = ctx.GetOrCreateRegex(valueRegex).Match(name);
                return new PathMarkExtractedValue(
                    match.Success ? match.Groups.Count > 1 ? match.Groups[1].Value : match.Value : null,
                    sourcePath);
            }
            catch when (invalidRegexUsesSourceName)
            {
                // Historical configs use the source name when their value regex is invalid.
                return new PathMarkExtractedValue(name, sourcePath);
            }
        }
        return default;
    }
}
