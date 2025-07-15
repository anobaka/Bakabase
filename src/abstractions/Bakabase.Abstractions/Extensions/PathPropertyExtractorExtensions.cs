using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class PathPropertyExtractorExtensions
{
    public static string[]? ExtractValues(this PathPropertyExtractor pl, string rootFilename, ResourcePathInfo rpi)
    {
        List<string>? pvs = null;
        switch (pl.Positioner)
        {
            case PathPositioner.Layer:
            {
                if (pl.Layer.HasValue)
                {
                    var totalSegmentCount = rpi.MediaLibraryPathSegments.Length + rpi.RelativePathSegments.Length;
                    var targetIndex = -1;
                    switch (pl.BasePathType)
                    {
                        case PathPropertyExtractorBasePathType.MediaLibrary:
                        {
                            targetIndex = rpi.MediaLibraryPathSegments.Length - 1 + pl.Layer.Value;
                            break;
                        }
                        case PathPropertyExtractorBasePathType.Resource:
                        {
                            targetIndex = totalSegmentCount - 1 + pl.Layer.Value;
                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (targetIndex > -1 && targetIndex < totalSegmentCount)
                    {
                        var segments = rpi.MediaLibraryPathSegments;
                        if (targetIndex >= rpi.MediaLibraryPathSegments.Length)
                        {
                            targetIndex -= rpi.MediaLibraryPathSegments.Length;
                            segments = rpi.RelativePathSegments;
                        }

                        (pvs ??= []).Add(segments[targetIndex]);

                    }
                }

                break;
            }
            case PathPositioner.Regex:
            {
                if (pl.Regex.IsNotEmpty())
                {
                    var matches = Regex.Matches(rpi.RelativePath, pl.Regex);
                    if (matches.Any())
                    {
                        var groups = matches
                            .SelectMany(a => a.Groups.Values.Skip(1).Select(b => b.Value))
                            .Where(x => x.IsNotEmpty())
                            .ToHashSet();
                        if (groups.Any())
                        {
                            (pvs ??= []).AddRange(groups.ToArray());
                        }
                    }
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        return pvs?.ToArray();
    }
}