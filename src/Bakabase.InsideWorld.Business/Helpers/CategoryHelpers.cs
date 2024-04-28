using Bakabase.Abstractions.Models.View.Constants;
using Bakabase.Abstractions.Models.View;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Drawing.Text;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Helpers;

public class CategoryHelpers
{
    public static CategoryResourceDisplayNameViewModel.Segment[] SplitDisplayNameTemplateIntoSegments(
        string template, Dictionary<string, string?> propertyReplacements, (string Left, string Right)[] wrappers)
    {
        var matchers = propertyReplacements.Keys.ToArray();
        var findsRanges = new List<(string Find, int StartIdx, int EndIdx)>();
        foreach (var find in matchers)
        {
            var pointerIdx = 0;
            while (true)
            {
                var idx = template.IndexOf(find, pointerIdx, StringComparison.Ordinal);
                if (idx == -1)
                {
                    break;
                }

                var tailIdx = idx + find.Length - 1;
                if (findsRanges.All(x => idx > x.EndIdx || tailIdx < x.StartIdx))
                {
                    findsRanges.Add((find, idx, idx + find.Length - 1));
                }

                pointerIdx += find.Length;
            }
        }
        var waitingRightWrapperStack = new Stack<(int LeftWrapperIndex, string RightWrapper)>();
        var validWrappers = new List<(string Left, string Right, int StartIdx, int EndIdx, string PairId)>();
        for (var i = 0; i < template.Length; i++)
        {
            var find = findsRanges.FirstOrDefault(x => x.StartIdx == i);
            if (find == default)
            {
                foreach (var (left, right) in wrappers)
                {
                    var leftWrapperIdx = template.IndexOf(left, i, StringComparison.Ordinal);

                    if (leftWrapperIdx != i)
                    {
                        var rightWrapperIdx = template.IndexOf(right, i, StringComparison.Ordinal);
                        if (rightWrapperIdx != i)
                        {
                            continue;
                        }

                        if (waitingRightWrapperStack.Any())
                        {
                            var lastWrapper = waitingRightWrapperStack.Peek();
                            if (lastWrapper.RightWrapper == right)
                            {
                                validWrappers.Add((left, right, lastWrapper.LeftWrapperIndex, i + right.Length - 1,
                                    Guid.NewGuid().ToString("N")));
                                i += right.Length - 1;
                                waitingRightWrapperStack.Pop();
                                break;
                            }
                        }
                    }
                    else
                    {
                        waitingRightWrapperStack.Push((i, right));
                        i += left.Length - 1;
                        break;
                    }
                }
            }
            else
            {
                i = find.EndIdx;
            }
        }

        var orderedWrapperPairs = validWrappers.OrderBy(d => d.StartIdx).ToList();

        var segments = SplitIntoSegments(template, 0, template.Length - 1).ToList();

        // set all property values
        foreach (var segment in segments.Where(segment =>
                     segment.Type == CategoryResourceDisplayNameSegmentType.Property))
        {
            segment.Text = propertyReplacements.GetValueOrDefault(segment.Text) ?? string.Empty;
        }

        // remove wrappers without inner content
        while (true)
        {
            var hasWrappersWithoutInnerContent = false;
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.Type == CategoryResourceDisplayNameSegmentType.LeftWrapper &&
                    !string.IsNullOrEmpty(segment.Text))
                {
                    var rightWrapperSegmentIdx = segments.FindIndex(x =>
                        x.WrapperPairId == segment.WrapperPairId &&
                        x.Type == CategoryResourceDisplayNameSegmentType.RightWrapper);
                    var hasContent = false;
                    for (var j = i + 1; j < rightWrapperSegmentIdx; j++)
                    {
                        if (!string.IsNullOrEmpty(segments[j].Text))
                        {
                            hasContent = true;
                            break;
                        }
                    }

                    if (!hasContent)
                    {
                        segment.Text = string.Empty;
                        segments[rightWrapperSegmentIdx].Text = string.Empty;
                        hasWrappersWithoutInnerContent = true;
                    }
                }
            }

            if (!hasWrappersWithoutInnerContent)
            {
                break;
            }
        }

        var notEmptySegments = segments.Where(x => !string.IsNullOrEmpty(x.Text)).ToArray();

        return notEmptySegments;

        IEnumerable<CategoryResourceDisplayNameViewModel.Segment> SplitIntoSegments(string tpl, int startIdx,
            int endIdx)
        {
            var tmpSegments = new List<CategoryResourceDisplayNameViewModel.Segment>();
            var closestWrapperPair =
                orderedWrapperPairs.FirstOrDefault(x => x.StartIdx >= startIdx && x.EndIdx <= endIdx);
            if (closestWrapperPair == default)
            {
                var find = findsRanges.FirstOrDefault(x => x.StartIdx >= startIdx && x.EndIdx <= endIdx);
                if (find == default)
                {
                    // whole subTemplate is one part
                    tmpSegments.Add(new CategoryResourceDisplayNameViewModel.Segment
                    {
                        Type = CategoryResourceDisplayNameSegmentType.Normal,
                        Text = tpl.Substring(startIdx, endIdx - startIdx + 1)
                    });
                }
                else
                {
                    // split template into segments
                    // left part
                    if (find.StartIdx > startIdx)
                    {
                        tmpSegments.AddRange(SplitIntoSegments(tpl, startIdx, find.StartIdx - 1));
                    }

                    // inner part
                    tmpSegments.Add(new CategoryResourceDisplayNameViewModel.Segment
                    {
                        Text = find.Find,
                        Type = CategoryResourceDisplayNameSegmentType.Property
                    });
                    // right part
                    if (find.EndIdx < endIdx)
                    {
                        tmpSegments.AddRange(SplitIntoSegments(tpl, find.EndIdx + 1, endIdx));
                    }
                }
            }
            else
            {
                // // find largest wrapper pair
                // var largerWrapper = closestWrapperPair;
                // while (true)
                // {
                //     var nextLargerWrapper = orderedWrapperPairs.FirstOrDefault(x =>
                //         x.StartIdx < largerWrapper.StartIdx && x.EndIdx > largerWrapper.EndIdx);
                //     if (nextLargerWrapper == default)
                //     {
                //         break;
                //     }
                //
                //     largerWrapper = nextLargerWrapper;
                // }
                //
                // var closestWrapperPair = largerWrapper;

                // // remove intersected wrapper pairs, remain containing wrapper pairs
                // orderedWrapperPairs.RemoveAll(x =>
                //     (x.StartIdx >= closestWrapperPair.StartIdx && x.StartIdx <= closestWrapperPair.EndIdx &&
                //      x.EndIdx >= closestWrapperPair.EndIdx)
                //     || (x.EndIdx >= closestWrapperPair.StartIdx && x.EndIdx <= closestWrapperPair.EndIdx &&
                //         x.StartIdx <= closestWrapperPair.StartIdx));

                // split template into segments
                // left part
                if (closestWrapperPair.StartIdx > startIdx)
                {
                    tmpSegments.AddRange(SplitIntoSegments(tpl, startIdx, closestWrapperPair.StartIdx - 1));
                }

                // inner part
                tmpSegments.Add(new CategoryResourceDisplayNameViewModel.Segment
                {
                    Text = closestWrapperPair.Left,
                    Type = CategoryResourceDisplayNameSegmentType.LeftWrapper,
                    WrapperPairId = closestWrapperPair.PairId
                });
                tmpSegments.AddRange(SplitIntoSegments(tpl,
                    closestWrapperPair.StartIdx + closestWrapperPair.Left.Length,
                    closestWrapperPair.EndIdx - closestWrapperPair.Right.Length));
                tmpSegments.Add(new CategoryResourceDisplayNameViewModel.Segment
                {
                    Text = closestWrapperPair.Right,
                    Type = CategoryResourceDisplayNameSegmentType.RightWrapper,
                    WrapperPairId = closestWrapperPair.PairId
                });
                // right part
                if (closestWrapperPair.EndIdx < endIdx)
                {
                    tmpSegments.AddRange(SplitIntoSegments(tpl, closestWrapperPair.EndIdx + 1, endIdx));
                }
            }

            return tmpSegments;
        }
    }
}