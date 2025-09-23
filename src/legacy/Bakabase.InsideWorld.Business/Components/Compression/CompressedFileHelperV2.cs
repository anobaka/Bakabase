using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Components.Compression
{
    public static class CompressedFileHelperV2
    {
        private static readonly Regex PartPattern =
            new Regex(@"\.part(?<index>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NumericSplitPattern =
            new Regex(@"\.(?<index>\d{3,})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TailingXNumberPattern =
            new Regex(@"\.[rz](?<index>\d{3,})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<CompressedFileGroup> DetectCompressedFileGroups(
            string[] filePaths,
            bool ignoreUnknownExtensions = true)
        {
            var allGroups = new List<CompressedFileGroup>();

            foreach (var dirGroup in filePaths.GroupBy(d => Path.GetDirectoryName(d)!))
            {
                var groups = new Dictionary<string, CompressedFileGroup>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in dirGroup.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(path);
                    string ext = GetArchiveExtension(fileName);

                    bool isKnown = InternalOptions.CompressedFileExtensions.Contains(ext) || InternalOptions.CompoundCompressedFileExtensions.Contains(ext);
                    bool isPart = PartPattern.IsMatch(fileName) ||
                                  TailingXNumberPattern.IsMatch(fileName)
                                  || NumericSplitPattern.IsMatch(fileName);

                    if (!isKnown && !isPart)
                    {
                        if (ignoreUnknownExtensions) continue;
                        AddToGroup(groups, Path.GetFileNameWithoutExtension(fileName), null, path, fileName);
                        continue;
                    }

                    string keyName;
                    string? mainExt = null;

                    if (isKnown)
                    {
                        keyName = Path.GetFileNameWithoutExtension(fileName[..^ext.Length]);
                        mainExt = ext;
                    }
                    else if (isPart)
                    {
                        var match = PartPattern.Match(fileName);
                        if (match.Success)
                        {
                            var baseName = fileName[..match.Index];
                            keyName = Path.GetFileNameWithoutExtension(baseName);
                            mainExt = GetArchiveExtension(fileName[(match.Index + match.Length)..]);
                        }
                        else if (NumericSplitPattern.IsMatch(fileName))
                        {
                            var baseName = NumericSplitPattern.Replace(fileName, "");
                            keyName = Path.GetFileNameWithoutExtension(baseName);
                            mainExt = GetArchiveExtension(baseName);
                        }
                        else
                        {
                            var baseName = Path.GetFileNameWithoutExtension(fileName);
                            keyName = baseName;
                            mainExt = Path.GetExtension(baseName);
                        }
                    }
                    else
                    {
                        keyName = Path.GetFileNameWithoutExtension(fileName);
                    }

                    mainExt = NormalizeExtension(mainExt);
                    AddToGroup(groups, keyName, mainExt, path, fileName);
                }

                // Finalize groups
                foreach (var g in groups.Values)
                {
                    var ordered = g.Files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

                    // Prefer the file with the main archive extension as the entry
                    if (!string.IsNullOrEmpty(g.Extension))
                    {
                        var entry = ordered.FirstOrDefault(f =>
                            Path.GetFileName(f).EndsWith(g.Extension, StringComparison.OrdinalIgnoreCase));

                        if (entry != null)
                        {
                            ordered.Remove(entry);
                            ordered.Insert(0, entry);
                        }
                    }

                    g.Files = ordered;
                    g.FileSizes = ordered.Select(f => new FileInfo(f).Length).ToList();
                }

                allGroups.AddRange(groups.Values);
            }

            return allGroups;
        }

        private static string GetArchiveExtension(string fileName)
        {
            var lower = fileName.ToLowerInvariant();
            var compound =
                InternalOptions.CompoundCompressedFileExtensions.FirstOrDefault(c => lower.EndsWith(c, StringComparison.OrdinalIgnoreCase));
            return compound ?? Path.GetExtension(fileName);
        }

        private static string? NormalizeExtension(string? ext)
        {
            return string.IsNullOrWhiteSpace(ext) ? null : ext;
        }

        private static void AddToGroup(
            Dictionary<string, CompressedFileGroup> groups,
            string key,
            string? ext,
            string path,
            string fileName)
        {
            var familyKey = GetFamilyKey(key, ext, fileName);

            if (!groups.TryGetValue(familyKey, out var group))
            {
                group = new CompressedFileGroup
                {
                    KeyName = key,
                    Extension = ext
                };
                groups[familyKey] = group;
            }

            if (ext != null && group.Extension == null)
                group.Extension = ext;

            group.Files.Add(path);
        }

        private static string GetFamilyKey(string keyName, string? ext, string fileName)
        {
            // RAR .partN family
            if (PartPattern.IsMatch(fileName) && fileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                return $"{keyName}|rar.part";

            // RAR + .rNN family
            if (ext != null && ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                return $"{keyName}|rar";

            if (Regex.IsMatch(Path.GetExtension(fileName), @"^\.r\d+$", RegexOptions.IgnoreCase))
                return $"{keyName}|rar";

            // ZIP + .zNN family
            if (ext != null && ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                return $"{keyName}|zip";

            if (Regex.IsMatch(Path.GetExtension(fileName), @"^\.z\d+$", RegexOptions.IgnoreCase))
                return $"{keyName}|zip";

            // 7Z family (single archive)
            if (ext != null && ext.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                            && !NumericSplitPattern.IsMatch(fileName))
                return $"{keyName}|7z";

            // 7Z split family (multi-volume .7z.00001, .00002, …)
            if (NumericSplitPattern.IsMatch(fileName) && fileName.Contains(".7z", StringComparison.OrdinalIgnoreCase))
                return $"{keyName}|7z.split";

            // Compound TAR families
            if (InternalOptions.CompoundCompressedFileExtensions.Contains(ext ?? ""))
                return $"{keyName}|{ext}";

            // Plain TAR
            if (ext != null && ext.Equals(".tar", StringComparison.OrdinalIgnoreCase))
                return $"{keyName}|tar";

            // Fallback: key + ext
            return $"{keyName}|{ext}";
        }
    }
}