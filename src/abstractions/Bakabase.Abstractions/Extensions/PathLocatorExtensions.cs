using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Extensions;

namespace Bakabase.Abstractions.Extensions;

public static class PathLocatorExtensions
{
    public static string[]? LocateValues(this PathLocator pl, string rootFilename, ResourcePathInfo rpi)
    {
        List<string>? pvs = null;
        switch (pl.Positioner)
        {
            case PathPositioner.Layer:
            {
                if (pl.Layer.HasValue)
                {
                    switch (pl.Layer.Value)
                    {
                        case 0:
                        {
                            (pvs ??= []).Add(rootFilename);
                            break;
                        }
                        default:
                        {
                            if (pl.Layer > 0)
                            {
                                if (pl.Layer <= rpi.RelativePathSegments.Length)
                                {
                                    (pvs ??= []).Add(rpi.RelativePathSegments[pl.Layer.Value - 1]);
                                }
                            }
                            else
                            {
                                var len = Math.Abs(pl.Layer.Value);
                                if (len <= rpi.RelativePathSegments.Length)
                                {
                                    (pvs ??= []).Add(rpi.RelativePathSegments[^len]);
                                }
                            }

                            break;
                        }
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