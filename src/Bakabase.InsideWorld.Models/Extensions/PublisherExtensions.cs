using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class PublisherExtensions
    {
        public static void ReplaceNames(this IEnumerable<PublisherDto> publishers,
            IDictionary<string, string> replacements)
        {
            if (publishers != null)
            {
                foreach (var p in publishers)
                {
                    p.Name = replacements[p.Name];
                    p.SubPublishers.ReplaceNames(replacements);
                }
            }
        }

        public static List<PublisherDto> AnalyzePublishers(this string publisherString, int level = 1)
        {
            if (!string.IsNullOrEmpty(publisherString))
            {
                // 只需要分割出A(...)、B(...)、C即可，...交给下级分割
                var dotLayer = 0;
                // 补头
                var dotPositions = new List<int> {-1};
                for (var i = 0; i < publisherString.Length; i++)
                {
                    var c = publisherString[i];
                    switch (c)
                    {
                        // 分隔符
                        case '、':
                            if (dotLayer == 0)
                            {
                                dotPositions.Add(i);
                            }

                            break;
                        // 多级
                        case '(':
                        {
                            dotLayer++;
                            break;
                        }
                        // 多级
                        case ')':
                        {
                            dotLayer--;
                            break;
                        }
                    }
                }

                // 补尾
                dotPositions.Add(publisherString.Length);

                var list = new List<PublisherDto>();
                for (var i = 0; i < dotPositions.Count - 1; i++)
                {
                    var sp = new PublisherDto();
                    var name = publisherString.Substring(dotPositions[i] + 1,
                        dotPositions[i + 1] - dotPositions[i] - 1);
                    var lb = name.IndexOf('(');
                    var rb = name.LastIndexOf(')');
                    var length = rb - lb - 1;
                    if (length > 0 && lb > -1)
                    {
                        // (circle)author
                        if (lb == 0)
                        {
                            // (circle)
                            if (rb == name.Length - 1)
                            {
                                name = name.Substring(lb + 1, length);
                            }
                            // (circle)author
                            else
                            {
                                var sub = name[(rb + 1)..];
                                name = name.Substring(lb + 1, length);
                                sp.SubPublishers = sub.AnalyzePublishers(level + 1);
                            }
                        }
                        // circle(author)
                        else
                        {
                            var sub = name.Substring(lb + 1, length);
                            name = name[..lb];
                            sp.SubPublishers = sub.AnalyzePublishers(level + 1);
                        }
                    }

                    sp.Name = name;
                    list.Add(sp);
                }

                return list;
            }

            return null;
        }

        public static string BuildPublisherString(this List<PublisherDto> publishers,
            bool includeContainer = true)
        {
            if (publishers?.Any() == true)
            {
                publishers = publishers.ToList();
                var publisherStrings = publishers.Select(t =>
                {
                    var name = t.Name;
                    if (t.SubPublishers?.Any() == true)
                    {
                        name += $"({string.Join("、", t.SubPublishers.Select(a => a.Name))})";
                    }

                    return name;
                });
                var str = string.Join("、", publisherStrings);
                if (includeContainer)
                {
                    str = $"[{str}]";
                }

                return str;
            }

            return null;
        }

        public static List<PublisherDto> Extract(this List<PublisherDto> publishers)
        {
            var list = new List<PublisherDto>();
            if (publishers != null)
            {
                list.AddRange(publishers);
                publishers.ForEach(t => { list.AddRange(t.SubPublishers.Extract()); });
            }

            return list;
        }

        /// <summary>
        /// 只获取名称
        /// </summary>
        /// <param name="publishers"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public static List<string> GetNames(this List<PublisherDto> publishers, Func<PublisherDto, bool> filter = null)
        {
            var names = new List<string>();
            if (publishers != null)
            {
                var targets = filter == null ? publishers : publishers.Where(filter);
                foreach (var t in targets)
                {
                    names.Add(t.Name);
                    names.AddRange(t.SubPublishers.GetNames(filter));
                }
            }

            return names.Distinct().ToList();
        }

        public static string ConvertToFeatureValue(this List<PublisherDto> publishers)
        {
            // 2->([1:2]->([2:1]->([1:1])|[1:1])|[2:3]->([1:1]|[1:1]|[1:1]))
            // [1:2]->([2:1]->([1:0])|[1:0])
            // [2:1]->([1:0])
            // 1Circle(1Author)1Author1Circle(2Author1Circle(1Author))
            if (publishers?.Any() == true)
            {
                var sb = new StringBuilder();
                // if (root)
                // {
                //     sb.Append(publishers.Count);
                // }

                // sb.Append("->(");

                // var typeGroups = publishers.GroupBy(a => a.Type).ToList();

                var publisherTestCaseGroups = publishers.Select(p =>
                {
                    // var str = p.Type.ToString();
                    var str = string.Empty;
                    if (p.SubPublishers?.Any() == true)
                    {
                        str += $"({p.SubPublishers.ConvertToFeatureValue()})";
                    }

                    return str;
                }).GroupBy(a => a).OrderByDescending(t => t.Count()).ToList();

                foreach (var g in publisherTestCaseGroups)
                {
                    sb.Append(g.Count()).Append(g.FirstOrDefault());
                }

                // for (var i = 0; i < publishers.Count; i++)
                // {
                //     var p = publishers[i];
                //     var subCount = p.SubPublishers?.Count ?? 0;
                //     if (i > 0)
                //     {
                //         sb.Append('|');
                //     }
                //
                //     sb.Append('[').Append(p.Type).Append('>').Append(subCount).Append(']');
                //     if (subCount > 0)
                //     {
                //         sb.Append(ConvertToFeatureValue(p.SubPublishers));
                //     }
                // }
                //
                // sb.Append(')');

                return sb.ToString();
            }

            return null;
        }

        public static PublisherDto ToDto(this Publisher publisher)
        {
            return new()
            {
                Favorite = publisher.Favorite,
                Rank = publisher.Rank,
                // Type = publisher.Type,
                Name = publisher.Name,
                Id = publisher.Id,
            };
        }

        public static List<PublisherDto> ToDto(this List<Publisher> publishers, List<PublisherDto> structure) =>
            publishers.ToDictionary(a => a.Name, a => a).ToDto(structure);

        public static List<PublisherDto> ToDto(this Dictionary<string, Publisher> publishers,
            List<PublisherDto> structure)
        {
            return structure.Select(a =>
            {
                var p = publishers[a.Name];
                var dto = p.ToDto();
                if (a.SubPublishers != null)
                {
                    dto.SubPublishers = publishers.ToDto(a.SubPublishers);
                }

                return dto;
            }).ToList();
        }

        public static Publisher ToPublisher(this PublisherDto s)
        {
            if (s == null)
            {
                return null;
            }

            return new()
            {
                Id = s.Id,
                Favorite = false,
                Name = s.Name,
                Rank = s.Rank
                // Type = s.Type
            };
        }

        public static void PopulateId(this IEnumerable<PublisherDto> publishers, Dictionary<string, int> publisherIds)
        {
            foreach (var p in publishers)
            {
                // Use existed id first.
                if (p.Id == 0)
                {
                    p.Id = publisherIds[p.Name];
                }

                p.SubPublishers?.PopulateId(publisherIds);
            }
        }

        public static List<PublisherResourceMapping> BuildMappings(this IEnumerable<PublisherDto> publishers,
            int resourceId,
            int? parentId = null)
        {
            return publishers.SelectMany(a =>
            {
                var mappings = new List<PublisherResourceMapping>
                {
                    new()
                    {
                        ParentPublisherId = parentId,
                        PublisherId = a.Id,
                        ResourceId = resourceId
                    }
                };
                if (a.SubPublishers != null)
                {
                    mappings.AddRange(a.SubPublishers.BuildMappings(resourceId, a.Id));
                }

                return mappings;
            }).ToList();
        }

        public static void RemoveInvalid(this List<PublisherDto> publishers)
        {
            if (publishers != null)
            {
                publishers?.RemoveAll(a => a.Id == 0 && a.Name.IsNullOrEmpty());
                foreach (var p in publishers)
                {
                    p.SubPublishers.RemoveInvalid();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static List<ResourceDiff>? Compare(this List<PublisherDto>? a, List<PublisherDto>? b)
        {
            return ResourceDiff.Build(ResourceDiffProperty.Publisher, a.PairByString(b, x => x.Name),
                PublisherDto.NameComparer, nameof(ResourceDto.Publishers), Compare);
        }

        public static List<ResourceDiff>? Compare(this PublisherDto a, PublisherDto b)
        {
            var nameDiff = ResourceDiff.Build(ResourceDiffProperty.Publisher, a.Name, b.Name,
                StringComparer.OrdinalIgnoreCase, nameof(PublisherDto.Name), null);
            var subPublisherDiff = a?.SubPublishers.Compare(b?.SubPublishers);

            if (nameDiff != null || subPublisherDiff?.Any() == true)
            {
                var diffs = new List<ResourceDiff>();
                if (nameDiff != null)
                {
                    diffs.Add(nameDiff);
                }

                if (subPublisherDiff?.Any() == true)
                {
                    diffs.AddRange(subPublisherDiff);
                }

                return diffs;
            }

            return null;
        }

        public static List<PublisherDto> Merge(this List<PublisherDto> publishersA, List<PublisherDto> publishersB)
        {
            var list = new List<PublisherDto>();

            var nameMapA = publishersA.GroupBy(a => a.Name).ToDictionary(a => a.Key, a => a.First());
            var nameMapB = publishersB.GroupBy(a => a.Name).ToDictionary(a => a.Key, a => a.First());

            foreach (var (name, a) in nameMapA)
            {
                if (nameMapB.TryGetValue(name, out var b))
                {
                    var copy = a with
                    {
                        SubPublishers = new List<PublisherDto>().Merge(a.SubPublishers).Merge(b.SubPublishers)
                    };

                    list.Add(copy);
                }
                else
                {
                    list.Add(a with { });
                }
            }

            foreach (var (name, b) in nameMapB.Where(b => !nameMapA.ContainsKey(b.Key)))
            {
                list.Add(b with { });
            }

            return list;
        }
    }
}