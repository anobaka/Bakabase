using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Extensions;
using DotNext.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

public class FileSystemEntryGroupingService(ILogger<FileSystemEntryGroupingService> logger)
{
    private record Candidate(string FullPath, string Name, bool IsDirectory)
    {
        public string Key => IsDirectory ? Name : Path.GetFileNameWithoutExtension(Name);
    }

    private record InternalGroup(string CanonicalKey, List<Candidate> Members);

    public List<FileSystemEntryGroupResultViewModel> Preview(FileSystemEntryGroupInputModel model)
    {
        var batches = BuildBatches(model);
        var results = new List<FileSystemEntryGroupResultViewModel>();

        foreach (var (rootPath, candidates) in batches)
        {
            var groups = model.StrategyType switch
            {
                FileSystemEntryGroupStrategyType.Similarity => GroupBySimilarity(candidates, model.SimilarityThreshold),
                FileSystemEntryGroupStrategyType.KeyExtraction => GroupByKeyExtraction(candidates, model.KeyExtractionRegex),
                FileSystemEntryGroupStrategyType.Affix => GroupByAffix(candidates, model.AffixDirection, model.AffixMinLength),
                _ => []
            };

            var groupedSet = new HashSet<string>(groups.SelectMany(g => g.Members.Select(m => m.FullPath)));
            var untouched = candidates.Where(c => !groupedSet.Contains(c.FullPath)).ToArray();

            var vm = new FileSystemEntryGroupResultViewModel
            {
                RootPath = rootPath.StandardizePath()!,
                Groups = groups.Select(g => BuildGroupViewModel(g, model)).ToArray(),
                UntouchedEntries = untouched.Select(c => new FileSystemEntryGroupResultViewModel.EntryViewModel
                {
                    Name = c.Name,
                    IsDirectory = c.IsDirectory,
                }).ToArray(),
            };
            results.Add(vm);
        }

        return results;
    }

    public List<decimal> ComputeSimilarityBreakpoints(FileSystemEntryGroupInputModel model)
    {
        var batches = BuildBatches(model);
        var breakpoints = new SortedSet<decimal> { 0m, 1m };

        foreach (var (_, candidates) in batches)
        {
            var keys = candidates.Select(c => c.Key).Where(k => k.IsNotEmpty()).Distinct().ToArray();
            for (var i = 0; i < keys.Length; i++)
            {
                for (var j = i + 1; j < keys.Length; j++)
                {
                    breakpoints.Add(NormalizedSimilarity(keys[i], keys[j]));
                }
            }
        }

        return breakpoints.ToList();
    }

    private Dictionary<string, List<Candidate>> BuildBatches(FileSystemEntryGroupInputModel model)
    {
        var batches = new Dictionary<string, List<Candidate>>();

        if (model.GroupInternal)
        {
            foreach (var rootPath in model.Paths)
            {
                if (File.Exists(rootPath) || !Directory.Exists(rootPath)) continue;

                var list = new List<Candidate>();
                foreach (var file in Directory.GetFiles(rootPath))
                {
                    list.Add(new Candidate(file, Path.GetFileName(file)!, false));
                }
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    list.Add(new Candidate(dir, new DirectoryInfo(dir).Name, true));
                }
                batches[rootPath] = list;
            }
        }
        else
        {
            foreach (var pg in model.Paths.GroupBy(Path.GetDirectoryName))
            {
                if (pg.Key.IsNullOrEmpty()) continue;

                foreach (var path in pg)
                {
                    if (File.Exists(path))
                    {
                        batches.GetOrAdd(pg.Key, _ => []).Add(new Candidate(path, Path.GetFileName(path)!, false));
                    }
                    else if (Directory.Exists(path))
                    {
                        batches.GetOrAdd(pg.Key, _ => []).Add(new Candidate(path, new DirectoryInfo(path).Name, true));
                    }
                }
            }
        }

        return batches;
    }

    private static List<InternalGroup> GroupBySimilarity(List<Candidate> candidates, decimal threshold)
    {
        var groups = new List<InternalGroup>();
        foreach (var c in candidates)
        {
            if (c.Key.IsNullOrEmpty()) continue;

            InternalGroup? matched = null;
            if (threshold == 1.0m)
            {
                matched = groups.FirstOrDefault(g => string.Equals(g.CanonicalKey, c.Key, StringComparison.Ordinal));
            }
            else
            {
                matched = groups.FirstOrDefault(g => g.CanonicalKey.IsSimilarTo(c.Key, threshold));
            }

            if (matched != null)
            {
                matched.Members.Add(c);
            }
            else
            {
                groups.Add(new InternalGroup(c.Key, [c]));
            }
        }

        // canonical name = longest key in the group (most descriptive)
        return groups
            .Where(g => g.Members.Count > 1)
            .Select(g =>
            {
                var canonical = g.Members.Select(m => m.Key).OrderByDescending(k => k.Length).First();
                return new InternalGroup(canonical, g.Members);
            })
            .ToList();
    }

    private List<InternalGroup> GroupByKeyExtraction(List<Candidate> candidates, string? pattern)
    {
        if (pattern.IsNullOrEmpty()) return [];

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Invalid key extraction regex: {Pattern}", pattern);
            return [];
        }

        var byKey = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        foreach (var c in candidates)
        {
            string? extracted;
            try
            {
                var m = regex.Match(c.Key);
                if (!m.Success) continue;
                extracted = m.Groups.Count > 1 ? m.Groups[1].Value : m.Value;
            }
            catch (RegexMatchTimeoutException)
            {
                continue;
            }
            if (extracted.IsNullOrEmpty()) continue;
            byKey.GetOrAdd(extracted, _ => []).Add(c);
        }

        return byKey
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => new InternalGroup(kv.Key, kv.Value))
            .ToList();
    }

    private static List<InternalGroup> GroupByAffix(
        List<Candidate> candidates,
        FileSystemEntryGroupAffixDirection direction,
        int minLength)
    {
        var includePrefix = direction is FileSystemEntryGroupAffixDirection.Prefix or FileSystemEntryGroupAffixDirection.Both;
        var includeSuffix = direction is FileSystemEntryGroupAffixDirection.Suffix or FileSystemEntryGroupAffixDirection.Both;

        var validCandidates = candidates.Where(c => c.Key.Length >= minLength).ToList();
        if (validCandidates.Count < 2) return [];

        var groups = new List<InternalGroup>();
        var assigned = new HashSet<string>();

        foreach (var seed in validCandidates)
        {
            if (assigned.Contains(seed.FullPath)) continue;

            var members = new List<Candidate> { seed };
            assigned.Add(seed.FullPath);

            foreach (var other in validCandidates)
            {
                if (assigned.Contains(other.FullPath)) continue;
                if (SharesAffix(seed.Key, other.Key, includePrefix, includeSuffix, minLength))
                {
                    members.Add(other);
                    assigned.Add(other.FullPath);
                }
            }

            if (members.Count > 1)
            {
                var canonical = DeriveAffixCanonical(members.Select(m => m.Key).ToList(), includePrefix, includeSuffix, minLength);
                groups.Add(new InternalGroup(canonical, members));
            }
            else
            {
                assigned.Remove(seed.FullPath);
            }
        }

        return groups;
    }

    private FileSystemEntryGroupResultViewModel.GroupViewModel BuildGroupViewModel(
        InternalGroup group, FileSystemEntryGroupInputModel model)
    {
        var canonical = group.CanonicalKey;

        // Resolution order: reuse an exact-name folder, else rename the first folder member, else create new.
        string targetName;
        string? existingFolderTarget = null;
        string? renamedSourceName = null;
        string? consumedEntryName = null;

        var matchingFolder = group.Members.FirstOrDefault(m =>
            m.IsDirectory && string.Equals(m.Name, canonical, StringComparison.OrdinalIgnoreCase));
        if (matchingFolder != null)
        {
            targetName = matchingFolder.Name;
            existingFolderTarget = matchingFolder.Name;
            consumedEntryName = matchingFolder.Name;
        }
        else
        {
            var firstFolder = group.Members.FirstOrDefault(m => m.IsDirectory);
            if (firstFolder != null)
            {
                targetName = canonical;
                renamedSourceName = firstFolder.Name;
                consumedEntryName = firstFolder.Name;
            }
            else
            {
                targetName = canonical;
            }
        }

        var visibleMembers = consumedEntryName == null
            ? group.Members
            : group.Members.Where(m => !string.Equals(m.Name, consumedEntryName, StringComparison.OrdinalIgnoreCase));

        return new FileSystemEntryGroupResultViewModel.GroupViewModel
        {
            DirectoryName = targetName,
            ExistingFolderTarget = existingFolderTarget,
            RenamedSourceName = renamedSourceName,
            Entries = visibleMembers.Select(c => new FileSystemEntryGroupResultViewModel.EntryViewModel
            {
                Name = c.Name,
                IsDirectory = c.IsDirectory,
                MatchSpans = ComputeMatchSpans(c, canonical, model),
            }).ToArray(),
        };
    }

    private static FileSystemEntryGroupResultViewModel.MatchSpan[] ComputeMatchSpans(
        Candidate candidate,
        string canonical,
        FileSystemEntryGroupInputModel model)
    {
        var name = candidate.Name;
        var keyOffset = candidate.IsDirectory ? 0 : 0;

        switch (model.StrategyType)
        {
            case FileSystemEntryGroupStrategyType.Similarity:
            {
                var lcs = LongestCommonSubstring(candidate.Key, canonical);
                if (lcs.Length == 0) return [];
                var idx = name.IndexOf(lcs, StringComparison.Ordinal);
                return idx < 0 ? [] : [new FileSystemEntryGroupResultViewModel.MatchSpan { Start = idx, Length = lcs.Length }];
            }
            case FileSystemEntryGroupStrategyType.KeyExtraction:
            {
                if (model.KeyExtractionRegex.IsNullOrEmpty()) return [];
                try
                {
                    var rx = new Regex(model.KeyExtractionRegex, RegexOptions.Compiled, TimeSpan.FromSeconds(1));
                    var m = rx.Match(candidate.Key);
                    if (!m.Success) return [];
                    var grp = m.Groups.Count > 1 ? m.Groups[1] : (Group)m;
                    return [new FileSystemEntryGroupResultViewModel.MatchSpan { Start = grp.Index + keyOffset, Length = grp.Length }];
                }
                catch
                {
                    return [];
                }
            }
            case FileSystemEntryGroupStrategyType.Affix:
            {
                var spans = new List<FileSystemEntryGroupResultViewModel.MatchSpan>();
                var includePrefix = model.AffixDirection is FileSystemEntryGroupAffixDirection.Prefix or FileSystemEntryGroupAffixDirection.Both;
                var includeSuffix = model.AffixDirection is FileSystemEntryGroupAffixDirection.Suffix or FileSystemEntryGroupAffixDirection.Both;
                if (includePrefix)
                {
                    var pl = CommonPrefixLength(candidate.Key, canonical);
                    if (pl >= model.AffixMinLength) spans.Add(new FileSystemEntryGroupResultViewModel.MatchSpan { Start = 0, Length = pl });
                }
                if (includeSuffix)
                {
                    var sl = CommonSuffixLength(candidate.Key, canonical);
                    if (sl >= model.AffixMinLength) spans.Add(new FileSystemEntryGroupResultViewModel.MatchSpan { Start = candidate.Key.Length - sl, Length = sl });
                }
                return spans.ToArray();
            }
            default:
                return [];
        }
    }

    private static decimal NormalizedSimilarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        if (max == 0) return 1m;
        var dis = a.GetLevenshteinDistance(b);
        return (decimal)(max - dis) / max;
    }

    private static string LongestCommonSubstring(string a, string b)
    {
        if (a.IsNullOrEmpty() || b.IsNullOrEmpty()) return string.Empty;
        var dp = new int[a.Length + 1, b.Length + 1];
        var maxLen = 0;
        var endIdx = 0;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                    if (dp[i, j] > maxLen)
                    {
                        maxLen = dp[i, j];
                        endIdx = i;
                    }
                }
            }
        }
        return maxLen == 0 ? string.Empty : a.Substring(endIdx - maxLen, maxLen);
    }

    private static int CommonPrefixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int CommonSuffixLength(string a, string b)
    {
        var n = Math.Min(a.Length, b.Length);
        var i = 0;
        while (i < n && a[a.Length - 1 - i] == b[b.Length - 1 - i]) i++;
        return i;
    }

    private static bool SharesAffix(string a, string b, bool prefix, bool suffix, int minLength)
    {
        if (prefix && CommonPrefixLength(a, b) >= minLength) return true;
        if (suffix && CommonSuffixLength(a, b) >= minLength) return true;
        return false;
    }

    private static string DeriveAffixCanonical(List<string> keys, bool prefix, bool suffix, int minLength)
    {
        if (prefix)
        {
            var p = keys.Aggregate((acc, s) => acc.Substring(0, CommonPrefixLength(acc, s)));
            if (p.Length >= minLength) return p.TrimEnd('-', '_', '.', ' ');
        }
        if (suffix)
        {
            var first = keys[0];
            var sLen = keys.Skip(1).Aggregate(first.Length, (acc, s) => Math.Min(acc, CommonSuffixLength(first, s)));
            if (sLen >= minLength) return first.Substring(first.Length - sLen).TrimStart('-', '_', '.', ' ');
        }
        return keys.OrderByDescending(k => k.Length).First();
    }

    public void Execute(FileSystemEntryGroupInputModel model, List<FileSystemEntryGroupResultViewModel> previews)
    {
        foreach (var batch in previews)
        {
            foreach (var group in batch.Groups)
            {
                string targetDir;
                if (group.ExistingFolderTarget.IsNotEmpty())
                {
                    targetDir = Path.Combine(batch.RootPath, group.ExistingFolderTarget);
                    // Folder already exists with the canonical name — reuse as-is.
                }
                else if (group.RenamedSourceName.IsNotEmpty())
                {
                    var oldPath = Path.Combine(batch.RootPath, group.RenamedSourceName);
                    var newPath = Path.Combine(batch.RootPath, group.DirectoryName);
                    if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
                    {
                        Directory.Move(oldPath, newPath);
                    }
                    targetDir = newPath;
                }
                else
                {
                    targetDir = Path.Combine(batch.RootPath, group.DirectoryName);
                    Directory.CreateDirectory(targetDir);
                }

                foreach (var entry in group.Entries)
                {
                    var sourcePath = Path.Combine(batch.RootPath, entry.Name);
                    var destPath = Path.Combine(targetDir, entry.Name);
                    if (entry.IsDirectory)
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    else
                    {
                        File.Move(sourcePath, destPath, false);
                    }
                }
            }
        }
    }
}
