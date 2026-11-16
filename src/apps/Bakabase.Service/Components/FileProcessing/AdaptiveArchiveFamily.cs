using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.Compression;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// An AI extract operation names a single entry volume. The executor needs the complete set
/// of registered members, each of which participates in validation and replacement receipts.
/// </summary>
internal sealed record AdaptiveArchiveFamily(IReadOnlyList<string> Paths, bool Consecutive)
{
    private static readonly Regex Numeric = new(@"\.(?<n>[0-9]{3,})$", RegexOptions.CultureInvariant);
    private static readonly Regex RarPart = new(@"\.part(?<n>[0-9]+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Legacy = new(@"\.(?<family>[rz])(?<n>[0-9]{2,})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static IReadOnlyList<AdaptiveArchiveFamily> From(IReadOnlyList<string> paths)
    {
        var result = new List<AdaptiveArchiveFamily>();
        var sfx = CompressedFileHelper.DetectSfxMultipartCandidates(paths);
        result.AddRange(sfx.Select(candidate => new AdaptiveArchiveFamily(candidate.Files,
            candidate.IsComplete && !candidate.IsAmbiguous)));
        var sfxPaths = sfx.SelectMany(candidate => candidate.Files).ToHashSet(StringComparer.Ordinal);
        foreach (var group in CompressedFileHelper.DetectCompressedFileGroups(
                     paths.Where(path => !sfxPaths.Contains(path)).ToArray(), inspectFileSizes: false))
        {
            // These are established archive families. Generic .001 application assets have
            // no archive extension and do not acquire an extraction requirement by their name.
            if (group.Extension?.ToLowerInvariant() is not (".7z" or ".rar" or ".zip" or ".tar" or ".tar.gz" or ".tar.bz2" or ".tar.xz"))
                continue;
            var names = group.Files.Select(path => Path.GetFileName(path)).ToArray();
            var legacy = names.Select(name => Legacy.Match(name)).FirstOrDefault(match => match.Success);
            var rule = legacy != null ? Legacy : names.Any(name => Numeric.IsMatch(name)) ? Numeric : RarPart;
            var numbers = names.Select(name => rule.Match(name)).Where(match => match.Success)
                .Select(match => int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var number) ? number : -1).OrderBy(number => number).ToArray();
            if (numbers.Length == 0) continue;

            var first = legacy?.Groups["family"].Value.Equals("r", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1;
            var completeShape = legacy == null ? numbers.Length == names.Length && names.Length >= 2 :
                numbers.Length == names.Length - 1 && names.Any(name =>
                    Path.GetExtension(name).Equals(group.Extension, StringComparison.OrdinalIgnoreCase));
            result.Add(new(group.Files, completeShape && numbers.SequenceEqual(Enumerable.Range(first, numbers.Length))));
        }
        return result;
    }

    internal string? ExactSelector(string entry, IReadOnlyList<string> manifestPaths)
    {
        if (!Consecutive || entry != Paths[0]) return null;
        var prefix = Paths.Aggregate((left, right) =>
        {
            var n = 0;
            while (n < left.Length && n < right.Length && left[n] == right[n]) n++;
            return left[..n];
        });
        var selector = prefix + "*";
        // Same matching semantics as FileProcessingPlanExecutor.Matches. Reject patterns
        // that also capture a patch, explanation, or a namesake in another directory.
        var selected = manifestPaths.Where(path => FileSystemName.MatchesSimpleExpression(selector,
            selector.Contains('/') ? path : Path.GetFileName(path), OperatingSystem.IsWindows()));
        return Paths.ToHashSet(StringComparer.Ordinal).SetEquals(selected) ? selector : null;
    }
}
