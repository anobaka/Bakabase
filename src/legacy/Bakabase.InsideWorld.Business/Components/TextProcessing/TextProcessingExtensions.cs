using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.TextProcessing
{
    public static class TextProcessingExtensions
    {
        public static string? Process(this string? raw, TextProcessValue tpv)
        {
            switch (tpv.Operation)
            {
                case TextProcessOperation.Remove:
                    return null;
                case TextProcessOperation.SetWithFixedValue:
                    return tpv.Value;
                case TextProcessOperation.AddToStart:
                    return tpv.Value + raw;
                case TextProcessOperation.AddToEnd:
                    return raw + tpv.Value;
                case TextProcessOperation.AddToAnyPosition:
                {
                    if (tpv is {Reverse: not null, Position: not null} && !string.IsNullOrEmpty(tpv.Value) &&
                        !string.IsNullOrEmpty(raw))
                    {
                        var length = raw.Length;
                        var position = tpv.Reverse.Value ? length - tpv.Position.Value : tpv.Position.Value;
                        return raw.Insert(position, tpv.Value);
                    }

                    break;
                }
                case TextProcessOperation.RemoveFromStart:
                {
                    if (!string.IsNullOrEmpty(raw) && tpv.Count < raw.Length)
                    {
                        return raw[tpv.Count.Value..];
                    }

                    break;
                }
                case TextProcessOperation.RemoveFromEnd:
                {
                    if (!string.IsNullOrEmpty(raw) && tpv.Count < raw.Length)
                    {
                        return raw[..^tpv.Count.Value];
                    }

                    break;
                }
                case TextProcessOperation.RemoveFromAnyPosition:
                {
                    if (!string.IsNullOrEmpty(raw) && tpv is
                            {Count: not null, RemoveBefore: not null, Position: not null, Reverse: not null})
                    {
                        var length = raw.Length;
                        var index = tpv.Reverse.Value ? length - tpv.Position.Value : tpv.Position.Value;
                        if (tpv.RemoveBefore.Value)
                        {
                            var nIndex = index - tpv.Count.Value + 1;
                            if (nIndex < 0)
                            {
                                return raw.Remove(0, tpv.Count.Value + nIndex);
                            }

                            index = Math.Max(0, index - tpv.Count.Value);
                        }

                        return raw.Remove(index, tpv.Count.Value);
                    }

                    break;
                }
                case TextProcessOperation.ReplaceFromStart:
                {
                    if (!string.IsNullOrEmpty(tpv.Find) && !string.IsNullOrEmpty(raw))
                    {
                        if (raw[..tpv.Find.Length] == tpv.Find)
                        {
                            return tpv.Replace + raw[..tpv.Find.Length];
                        }
                    }

                    break;
                }
                case TextProcessOperation.ReplaceFromEnd:
                {
                    if (!string.IsNullOrEmpty(tpv.Find) && !string.IsNullOrEmpty(raw))
                    {
                        if (raw[^tpv.Find.Length..] == tpv.Find)
                        {
                            return raw[..^tpv.Find.Length] + tpv.Replace;
                        }
                    }

                    break;
                }
                case TextProcessOperation.ReplaceFromAnyPosition:
                {
                    if (!string.IsNullOrEmpty(tpv.Find))
                    {
                        return raw?.Replace(tpv.Find, tpv.Replace);
                    }

                    break;
                }
                case TextProcessOperation.ReplaceWithRegex:
                {
                    if (!string.IsNullOrEmpty(tpv.Find) && !string.IsNullOrEmpty(raw) &&
                        !string.IsNullOrEmpty(tpv.Replace))
                    {
                        return Regex.Replace(raw, tpv.Find, tpv.Replace);
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(tpv.Operation), tpv.Operation, null);
            }

            return raw;
        }
    }
}