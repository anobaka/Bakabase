using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aliyun.OSS;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Extensions;
using JetBrains.Annotations;

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public class CompressedFileHelper
    {
        private static readonly Regex PartExtRegex = new Regex(@"^\.(?<prefix>[a-zA-Z]{0,5})?(?<index>\d{0,5})$");

        [NotNull]
        public static List<CompressedFileGroup> Group(string[] fileOrFullNames)
        {
            // dir - key name - ext info list
            var allDirGroups = new Dictionary<string, Dictionary<string, List<ExtInfo>>>();
            foreach (var f in fileOrFullNames.OrderBy(t => t))
            {
                var ext1 = Path.GetExtension(f);
                var extInfo = new ExtInfo { Raw = ext1 };
                var ext1IsKeyExt = BusinessConstants.CompressedFileExtensions.Contains(ext1);
                if (ext1IsKeyExt)
                {
                    extInfo.TypeKey = ext1;
                }
                else
                {
                    var match = PartExtRegex.Match(ext1);
                    if (match.Success)
                    {
                        extInfo.PartPrefix = match.Groups["prefix"].Value;
                        extInfo.PartIndex = match.Groups["index"].Value;
                    }
                    else
                    {
                        continue;
                    }
                }

                var keyName = Path.GetFileNameWithoutExtension(f);
                var ext2 = Path.GetExtension(keyName);
                if (ext2.IsNotEmpty())
                {
                    if (ext1IsKeyExt)
                    {
                        var match = PartExtRegex.Match(ext2);
                        if (match.Success)
                        {
                            extInfo.PartPrefix = match.Groups["prefix"].Value;
                            extInfo.PartIndex = match.Groups["index"].Value;
                            extInfo.LeadingPart = true;

                            keyName = Path.GetFileNameWithoutExtension(keyName);
                            extInfo.Raw = ext2 + extInfo.Raw;
                        }
                    }
                    else
                    {
                        if (BusinessConstants.CompressedFileExtensions.Contains(ext2))
                        {
                            extInfo.TypeKey = ext2;

                            keyName = Path.GetFileNameWithoutExtension(keyName);
                            extInfo.Raw = ext2 + extInfo.Raw;
                        }
                    }
                }

                var dir = Path.GetDirectoryName(f);

                if (!allDirGroups.TryGetValue(dir, out var d))
                {
                    d = allDirGroups[dir] = new Dictionary<string, List<ExtInfo>>();
                }

                if (!d.TryGetValue(keyName, out var extList))
                {
                    extList = d[keyName] = new List<ExtInfo>();
                }

                extList.Add(extInfo);
            }

            var result = new List<CompressedFileGroup>();
            foreach (var dirGroups in allDirGroups)
            {
                foreach (var (kn, extInfoList) in dirGroups.Value)
                {
                    var extGroups = new List<List<ExtInfo>>();
                    var restExtInfoList = extInfoList.ToList();

                    var possibleEntries = extInfoList
                        .Where(t => t.PartIndex.IsNullOrEmpty() && t.PartPrefix.IsNullOrEmpty())
                        .OrderByDescending(t => t.Raw)
                        .ToList();
                    // .rar, .r01, ...
                    // .zip, .z01, ...
                    var unknownExtInfosWithPartPrefix = extInfoList
                        .Where(t => t.TypeKey.IsNullOrEmpty() && t.PartPrefix.IsNotEmpty()).ToList();
                    if (unknownExtInfosWithPartPrefix.Any())
                    {
                        var usedPes = new List<ExtInfo>();
                        foreach (var pe in possibleEntries)
                        {
                            var firstTargetPartIndex =
                                unknownExtInfosWithPartPrefix.FindIndex(t =>
                                    t.PartPrefix.FirstOrDefault() == pe.TypeKey[1]);
                            if (firstTargetPartIndex > -1)
                            {
                                var g = new List<ExtInfo> { pe };
                                var parts = unknownExtInfosWithPartPrefix.Skip(firstTargetPartIndex).ToArray();
                                g.AddRange(parts);
                                extGroups.Add(g);

                                restExtInfoList.RemoveAll(g.Contains);
                                usedPes.Add(pe);
                                unknownExtInfosWithPartPrefix.RemoveRange(firstTargetPartIndex, parts.Length);
                            }
                        }

                        // We don't know what format they are, so just ignore them.
                        restExtInfoList.RemoveAll(unknownExtInfosWithPartPrefix.Contains);
                        possibleEntries.RemoveAll(usedPes.Contains);
                    }

                    var purePartIndexExtInfoList = restExtInfoList
                        .Where(t => t.PartPrefix.IsNullOrEmpty() && t.TypeKey.IsNullOrEmpty()).ToArray();
                    // .rar, .01, .02...
                    if (purePartIndexExtInfoList.Any() && possibleEntries.Count == 1)
                    {
                        var e = possibleEntries.FirstOrDefault();
                        var g = new List<ExtInfo> { e }.Concat(purePartIndexExtInfoList).ToList();
                        extGroups.Add(g);
                        restExtInfoList.RemoveAll(g.Contains);
                        possibleEntries.Remove(e);
                    }

                    // .rar
                    var independents =
                        restExtInfoList.Where(t => t.PartIndex.IsNullOrEmpty() && t.PartPrefix.IsNullOrEmpty())
                            .ToArray();
                    foreach (var i in independents)
                    {
                        restExtInfoList.Remove(i);
                        extGroups.Add(new List<ExtInfo> { i });
                    }

                    // .rar.part1
                    // .part1.rar
                    foreach (var groups in restExtInfoList.GroupBy(t => t.LeadingPart))
                    {
                        foreach (var subGroups in groups.GroupBy(a => a.TypeKey))
                        {
                            var g = subGroups;
                            extGroups.Add(g.ToList());
                        }
                    }

                    result.AddRange(extGroups.Select(g => new CompressedFileGroup
                    {
                        Extension = g.FirstOrDefault(a => a.TypeKey.IsNotEmpty())?.TypeKey,
                        Files = g.Select(t => Path.Combine(dirGroups.Key, $"{kn}{t.Raw}").StandardizePath()).ToList(),
                        KeyName = kn?.Trim()
                    }));
                }
            }

            return result;
        }

        private class ExtInfo
        {
            public string TypeKey { get; set; }
            public string PartPrefix { get; set; }
            public string PartIndex { get; set; }
            public string Raw { get; set; }
            public bool LeadingPart { get; set; }
        }

        public class CompressedFileGroup
        {
            public string KeyName { get; set; }
            public List<string> Files { get; set; } = new();
            public string Extension { get; set; }
            public bool MissEntry => Extension.IsNullOrEmpty();

            public static T FromSingleFile<T>(string path) where T : CompressedFileGroup, new() => new()
            {
                KeyName = Path.GetFileNameWithoutExtension(path).Trim(),
                Extension = Path.GetExtension(path),
                Files = new List<string> { path }
            };

            public override string ToString()
            {
                var noEntry = Extension.IsNullOrEmpty();
                var segments = new[]
                {
                    $"[Key]{KeyName}",
                    $"[Entry]{(noEntry ? "Not found" : Files.FirstOrDefault())}",
                }.Concat(Files.Skip(noEntry ? 0 : 1).Select(t => $"[Part]{t}"));
                return string.Join(Environment.NewLine, segments);
            }
        }
    }
}