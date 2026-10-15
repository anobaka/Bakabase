using System;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// The mark path is the origin for dynamic values in both previews and synchronization.
/// Callers supply cached path segments and regexes when processing many resources.
/// </summary>
internal static class PathMarkDynamicValueExtractor
{
    public static string? Extract(
        string markDirectoryName,
        string[] markSegments,
        string[]? resourceSegments,
        PathMatchMode matchMode,
        int? valueLayer,
        string? valueRegex,
        Func<string, Regex> getRegex)
    {
        if (matchMode == PathMatchMode.Layer)
        {
            var layer = valueLayer ?? 0;
            var segments = layer > 0 ? resourceSegments : markSegments;
            var targetIndex = markSegments.Length - 1 + layer;
            return segments != null && targetIndex >= 0 && targetIndex < segments.Length
                ? segments[targetIndex]
                : null;
        }

        if (matchMode == PathMatchMode.Regex && !string.IsNullOrEmpty(valueRegex))
        {
            try
            {
                var match = getRegex(valueRegex).Match(markDirectoryName);
                return match.Success ? match.Groups.Count > 1 ? match.Groups[1].Value : match.Value : null;
            }
            catch
            {
                // Preserve the synchronization fallback for existing invalid value regexes.
                return markDirectoryName;
            }
        }

        return null;
    }
}
