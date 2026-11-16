using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>A name-based candidate only. IsComplete means the supplied volume numbers
/// are consecutive from one, not that an unknown final volume or payload was verified.</summary>
public sealed record SfxMultipartArchiveCandidate(string EntryFile, string KeyName,
    IReadOnlyList<string> Files, string ExpectedFormat, bool IsComplete, bool IsAmbiguous);

public static partial class CompressedFileHelper
{
    private static readonly Regex SfxPartName = new(@"^(?<stem>.+)\.part(?<n>[0-9]+)\.(?<ext>exe|rar)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RarPartName = new(@"\.part(?<n>[0-9]+)\.rar$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Only the supplied paths participate. No directory enumeration, format
    /// discovery or executable launch occurs here. Callers must probe the entry's format.</summary>
    public static IReadOnlyList<SfxMultipartArchiveCandidate> DetectSfxMultipartCandidates(IReadOnlyList<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new List<SfxMultipartArchiveCandidate>();
        foreach (var directory in paths.GroupBy(path => Path.GetDirectoryName(path) ?? "", comparer))
        {
            var matches = directory.Select(path => (path, match: SfxPartName.Match(Path.GetFileName(path))))
                .Where(item => item.match.Success)
                .GroupBy(item => item.match.Groups["stem"].Value, comparer);
            foreach (var family in matches)
            {
                var ordered = family.Select(item => (item.path, number: Number(item.match),
                        exe: item.match.Groups["ext"].Value.Equals("exe", StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(item => item.number).ThenBy(item => item.path, StringComparer.Ordinal).ToArray();
                var executables = ordered.Where(item => item.exe).ToArray();
                if (executables.Length == 0 || ordered.All(item => item.exe)) continue;
                var ambiguous = executables.Length != 1 || ordered.Any(item => item.number < 1) ||
                    ordered.Select(item => item.number).Distinct().Count() != ordered.Length;
                var complete = !ambiguous && executables[0].number == 1 &&
                    ordered.Select(item => item.number).SequenceEqual(Enumerable.Range(1, ordered.Length));
                result.Add(new(executables[0].path, family.Key, ordered.Select(item => item.path).ToArray(),
                    ".rar", complete, ambiguous));
            }
        }
        return result;
    }

    public static bool SfxFormatMatches(SfxMultipartArchiveCandidate candidate, string? format) =>
        candidate.ExpectedFormat == ".rar" && (format?.TrimStart('.').Equals("rar", StringComparison.OrdinalIgnoreCase) == true ||
            format?.TrimStart('.').Equals("rar5", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Returns one exact, consecutively named multipart family, not a promise
    /// of content integrity. Only a successful extraction can justify deleting its inputs.</summary>
    public static CompressedFileGroup? GetCompleteMultipartArchiveGroup(IReadOnlyList<string> files,
        string? confirmedSfxFormat = null)
    {
        if (files.Count < 2 || files.Distinct(StringComparer.Ordinal).Count() != files.Count) return null;
        var sfx = DetectSfxMultipartCandidates(files);
        IReadOnlyDictionary<string, string>? confirmations = null;
        if (sfx.Count > 0)
        {
            var candidate = sfx.Count == 1 ? sfx[0] : null;
            if (candidate is not {IsComplete: true, IsAmbiguous: false} ||
                candidate.Files.Count != files.Count || candidate.EntryFile != files[0] ||
                !SfxFormatMatches(candidate, confirmedSfxFormat)) return null;
            confirmations = new Dictionary<string, string>(StringComparer.Ordinal) {{candidate.EntryFile, confirmedSfxFormat!}};
        }
        var groups = DetectCompressedFileGroups(files.ToArray(), ignoreUnknownExtensions: false,
            inspectFileSizes: false, confirmedSfxFormats: confirmations);
        if (groups.Count != 1 || groups[0].Files.Count != files.Count || groups[0].Files[0] != files[0]) return null;
        var group = groups[0];
        if (confirmations != null) return group;
        var names = group.Files.Select(Path.GetFileName).ToArray();
        var numbered = names.Select(name => NumericSplitPattern.Match(name!)).ToArray();
        if ((group.Extension?.ToLowerInvariant() is ".7z" or ".zip" or ".rar" or ".tar" or ".tar.gz" or ".tar.bz2" or ".tar.xz") &&
            numbered.All(match => match.Success) && Consecutive(numbered, 1)) return group;
        var rar = names.Select(name => RarPartName.Match(name!)).ToArray();
        if (group.Extension?.Equals(".rar", StringComparison.OrdinalIgnoreCase) == true &&
            rar.All(match => match.Success) && Consecutive(rar, 1)) return group;
        var tails = names.Skip(1).Select(name => TailingXNumberPattern.Match(name!)).ToArray();
        var expected = group.Extension?.ToLowerInvariant() switch {".rar" => "r", ".zip" => "z", _ => null};
        if (expected != null && Path.GetExtension(names[0]).Equals(group.Extension, StringComparison.OrdinalIgnoreCase) &&
            tails.All(match => match.Success && match.Groups["family"].Value.Equals(expected, StringComparison.OrdinalIgnoreCase)) &&
            Consecutive(tails, expected == "r" ? 0 : 1)) return group;
        return null;
    }

    public static string GetExtractionDirectoryName(IReadOnlyList<string> files, string? confirmedSfxFormat = null) =>
        GetCompleteMultipartArchiveGroup(files, confirmedSfxFormat)?.KeyName ?? Path.GetFileNameWithoutExtension(files[0]);

    private static bool Consecutive(IEnumerable<Match> matches, int first)
    {
        var numbers = matches.Select(Number).Order().ToArray();
        return numbers.Length > 0 && numbers.SequenceEqual(Enumerable.Range(first, numbers.Length));
    }

    private static int Number(Match match) => int.TryParse(
        match.Groups["n"].Success ? match.Groups["n"].Value : match.Groups["index"].Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1;

    private static IEnumerable<string> OrderArchiveVolumes(IEnumerable<string> files) => files
        .OrderBy(path =>
        {
            var name = Path.GetFileName(path);
            foreach (var pattern in new[] {NumericSplitPattern, TailingXNumberPattern, RarPartName, TrailingPartPattern})
            {
                var match = pattern.Match(name);
                if (match.Success) return Number(match);
            }
            return -1;
        }).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ThenBy(path => path, StringComparer.Ordinal);
}
