using System;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Extensions;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

/// <summary>
/// Best-effort single-path twin of PathMarkSyncService.FilterResourcesByMarkConfig, used to
/// preview which property/media-library marks would cover a resource at its post-move path —
/// a path that does not exist yet, so the FS-walking preview cannot answer this.
/// One deliberate approximation: under MatchedAndSubdirectories + Regex, the real sync only
/// pulls in descendants of *resources* whose path matched, while this treats any matching
/// ancestor path as enough. Good for a hint, not for applying values.
/// </summary>
internal static class PathMarkMatchEvaluator
{
    public static bool Matches(PathMatchMode matchMode, int? layer, string? regexPattern,
        PathMarkApplyScope applyScope, string markPath, string candidatePath)
    {
        var mark = markPath.StandardizePath();
        var candidate = candidatePath.StandardizePath();
        if (mark == null || candidate == null || !candidate.IsPathEqualOrUnder(mark))
        {
            return false;
        }

        var includeSubdirectories = applyScope == PathMarkApplyScope.MatchedAndSubdirectories;

        switch (matchMode)
        {
            case PathMatchMode.Layer when layer.HasValue:
            {
                var markSegments = SegmentCount(mark);
                var candidateSegments = SegmentCount(candidate);

                if (layer.Value < 0)
                {
                    var targetSegmentCount = markSegments - Math.Abs(layer.Value);
                    if (targetSegmentCount <= 0)
                    {
                        return false;
                    }

                    return includeSubdirectories
                        ? candidateSegments >= targetSegmentCount
                        : candidateSegments == targetSegmentCount;
                }

                var relativeDepth = candidateSegments - markSegments;
                return includeSubdirectories ? relativeDepth >= layer.Value : relativeDepth == layer.Value;
            }
            case PathMatchMode.Regex when !string.IsNullOrEmpty(regexPattern):
            {
                Regex regex;
                try
                {
                    regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    return false;
                }

                if (regex.IsMatch(RelativeTo(candidate, mark)))
                {
                    return true;
                }

                if (!includeSubdirectories)
                {
                    return false;
                }

                var ancestor = System.IO.Path.GetDirectoryName(candidate).StandardizePath();
                while (ancestor != null && ancestor.IsPathEqualOrUnder(mark))
                {
                    if (regex.IsMatch(RelativeTo(ancestor, mark)))
                    {
                        return true;
                    }

                    if (string.Equals(ancestor, mark, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    ancestor = System.IO.Path.GetDirectoryName(ancestor).StandardizePath();
                }

                return false;
            }
            default:
                return false;
        }
    }

    private static int SegmentCount(string standardizedPath) =>
        standardizedPath.SplitPathIntoSegments().Length;

    private static string RelativeTo(string standardizedPath, string standardizedRoot) =>
        standardizedPath.Length <= standardizedRoot.Length
            ? string.Empty
            : standardizedPath[standardizedRoot.Length..].TrimStart('/');
}
