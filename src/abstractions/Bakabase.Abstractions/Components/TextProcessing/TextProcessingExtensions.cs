using System.Text.RegularExpressions;

namespace Bakabase.Abstractions.Components.TextProcessing
{
    public static class TextProcessingExtensions
    {
        public static string? Process(this string? raw, TextProcessingOperation operation, TextProcessingOptions? tpo)
        {
            if (operation == TextProcessingOperation.Delete)
            {
                return null;
            }

            if (tpo == null)
            {
                throw new Exception("Text processing options cannot be null");
            }

            switch (operation)
            {
                case TextProcessingOperation.SetWithFixedValue:
                    return tpo.Value;
                case TextProcessingOperation.AddToStart:
                    return tpo.Value + raw;
                case TextProcessingOperation.AddToEnd:
                    return raw + tpo.Value;
                case TextProcessingOperation.AddToAnyPosition:
                {
                    if (tpo is {IsPositioningDirectionReversed: not null, Index: not null} &&
                        !string.IsNullOrEmpty(tpo.Value) &&
                        !string.IsNullOrEmpty(raw))
                    {
                        var length = raw.Length;
                        var position = tpo.IsPositioningDirectionReversed.Value
                            ? length - tpo.Index.Value
                            : tpo.Index.Value;
                        return raw.Insert(position, tpo.Value);
                    }

                    break;
                }
                case TextProcessingOperation.RemoveFromStart:
                {
                    if (!string.IsNullOrEmpty(raw) && tpo.Count < raw.Length)
                    {
                        return raw[tpo.Count.Value..];
                    }

                    break;
                }
                case TextProcessingOperation.RemoveFromEnd:
                {
                    if (!string.IsNullOrEmpty(raw) && tpo.Count < raw.Length)
                    {
                        return raw[..^tpo.Count.Value];
                    }

                    break;
                }
                case TextProcessingOperation.RemoveFromAnyPosition:
                {
                    if (!string.IsNullOrEmpty(raw) && tpo is
                        {
                            Count: not null, IsOperationDirectionReversed: not null, Index: not null,
                            IsPositioningDirectionReversed: not null
                        })
                    {
                        var length = raw.Length;
                        var index = tpo.IsPositioningDirectionReversed.Value
                            ? length - tpo.Index.Value
                            : tpo.Index.Value;
                        if (tpo.IsOperationDirectionReversed.Value)
                        {
                            var nIndex = index - tpo.Count.Value + 1;
                            if (nIndex < 0)
                            {
                                return raw.Remove(0, tpo.Count.Value + nIndex);
                            }

                            index = Math.Max(0, index - tpo.Count.Value);
                        }

                        return raw.Remove(index, tpo.Count.Value);
                    }

                    break;
                }
                case TextProcessingOperation.ReplaceFromStart:
                {
                    if (!string.IsNullOrEmpty(tpo.Find) && !string.IsNullOrEmpty(raw))
                    {
                        if (raw[..tpo.Find.Length] == tpo.Find)
                        {
                            return tpo.Value + raw[..tpo.Find.Length];
                        }
                    }

                    break;
                }
                case TextProcessingOperation.ReplaceFromEnd:
                {
                    if (!string.IsNullOrEmpty(tpo.Find) && !string.IsNullOrEmpty(raw))
                    {
                        if (raw[^tpo.Find.Length..] == tpo.Find)
                        {
                            return raw[..^tpo.Find.Length] + tpo.Value;
                        }
                    }

                    break;
                }
                case TextProcessingOperation.ReplaceFromAnyPosition:
                {
                    if (!string.IsNullOrEmpty(tpo.Find))
                    {
                        return raw?.Replace(tpo.Find, tpo.Value);
                    }

                    break;
                }
                case TextProcessingOperation.ReplaceWithRegex:
                {
                    if (!string.IsNullOrEmpty(tpo.Find) && !string.IsNullOrEmpty(raw) &&
                        !string.IsNullOrEmpty(tpo.Value))
                    {
                        return Regex.Replace(raw, tpo.Find, tpo.Value);
                    }

                    break;
                }
                case TextProcessingOperation.Delete:
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation), operation, null);
            }

            return raw;
        }
    }
}