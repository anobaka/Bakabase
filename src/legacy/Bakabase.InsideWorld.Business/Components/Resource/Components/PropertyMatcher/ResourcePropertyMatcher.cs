using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PropertyMatcher
{
    public class ResourcePropertyMatcher
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="segments"></param>
        /// <param name="value"></param>
        /// <param name="startIndex">Starts from -1</param>
        /// <param name="endIndex">Ends to <see cref="segments.Length"/></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static MatchResult? Match(string[] segments, PropertyPathSegmentMatcherValue? value, int? startIndex, int? endIndex)
        {
            if (value is not {IsValid: true})
            {
                return null;
            }

            switch (value.ValueType)
            {
                case ResourceMatcherValueType.Layer:
                {
                    if (value.Layer > 0)
                    {
                        if (startIndex >= -1)
                        {
                            var idx = startIndex.Value + value.Layer.Value;
                            if (idx < segments.Length)
                            {
                                return MatchResult.OfLayer(value.Layer.Value, startIndex + value.Layer);
                            }
                        }
                    }
                    else
                    {
                        var idx = endIndex + value.Layer;
                        if (idx >= 0 && idx < segments.Length)
                        {
                            return MatchResult.OfLayer(value.Layer!.Value, endIndex + value.Layer);
                        }
                    }

                    break;
                }
                case ResourceMatcherValueType.Regex:
                {
                    if (startIndex >= -1 && endIndex <= segments.Length && startIndex + 1 < endIndex)
                    {
                        var subPath = string.Join(InternalOptions.DirSeparator,
                            segments.Skip(startIndex.Value + 1).Take((endIndex - startIndex).Value - 1));
                        var matches = Regex.Matches(subPath, value.Regex!);
                        if (matches.Any())
                        {
                            var groups = matches.SelectMany(a => a.Groups.Values.Skip(1).Select(b => b.Value)).Where(x => x.IsNotEmpty())
                                .ToHashSet();
                            if (groups.Any())
                            {
                                return MatchResult.OfRegex(groups.ToArray());
                            }
                            else
                            {
                                var match = matches.FirstOrDefault()!;
                                var matchedValue = subPath.Substring(0, match.Index + match.Value.Length);
                                if (matchedValue.IsNotEmpty())
                                {
                                    // matched value can't starts with unc prefix, so we can split it safely.
                                    var layer = matchedValue.Split(InternalOptions.DirSeparator).Length;
                                    return MatchResult.OfLayer(layer, layer + startIndex.Value);
                                }
                            }
                        }
                    }

                    break;
                }
                case ResourceMatcherValueType.FixedText:
                {
                    if (startIndex >= -1)
                    {
                        var subSegments = segments.Take(startIndex.Value + 1).ToArray();
                        var str = string.Empty;
                        for (var i = 0; i < subSegments.Length; i++)
                        {
                            str += subSegments[i];
                            if (str == value.FixedText)
                            {
                                var layer = i + 1;
                                return MatchResult.OfLayer(layer, startIndex + layer);
                            }

                            str += InternalOptions.DirSeparator;
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return null;
        }
    }
}