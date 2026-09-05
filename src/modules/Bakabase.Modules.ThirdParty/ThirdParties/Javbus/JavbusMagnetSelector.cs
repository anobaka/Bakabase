using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Javbus;

/// <summary>
/// Picks one magnet out of a code's candidates.
///
/// Size decides first: everything within <c>sizeTolerance</c> of the largest
/// file counts as the same quality tier, and only inside that tier do the
/// subtitle hints break the tie. Ranking by tag alone would happily trade a
/// 6GB rip for an 800MB one just because its name mentions 字幕.
/// </summary>
public static class JavbusMagnetSelector
{
    /// <summary>Beyond this the "same quality tier" idea stops meaning anything.</summary>
    public const decimal MaxSizeTolerance = 0.9m;

    private static readonly Regex SizeRegex =
        new(@"([\d.]+)\s*([KMGT])?B", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SubtitleKeywordRegex =
        new("字幕|中字|中文|繁中|简中", RegexOptions.Compiled);

    // -C / -UC suffix, but only when nothing alphanumeric follows it, so that
    // -CHN and -COMPLETE aren't mistaken for the subtitled-release marker.
    private static readonly Regex SubtitleSuffixRegex =
        new(@"-U?C(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ChineseRegex = new(@"[一-鿿]", RegexOptions.Compiled);

    private static readonly Dictionary<char, long> Multipliers = new()
    {
        ['K'] = 1024L,
        ['M'] = 1024L * 1024,
        ['G'] = 1024L * 1024 * 1024,
        ['T'] = 1024L * 1024 * 1024 * 1024
    };

    /// <summary>Turns <c>4.35GB</c> into bytes. Returns 0 for anything unparsable.</summary>
    public static long ParseSize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var match = SizeRegex.Match(text);
        if (!match.Success ||
            !decimal.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return 0;
        }

        var unit = match.Groups[2].Value.ToUpperInvariant();
        var multiplier = unit.Length == 1 && Multipliers.TryGetValue(unit[0], out var m) ? m : 1L;

        return (long) (value * multiplier);
    }

    public static JavbusMagnetTag DetectTag(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return JavbusMagnetTag.Plain;
        }

        if (SubtitleKeywordRegex.IsMatch(name))
        {
            return JavbusMagnetTag.SubtitleKeyword;
        }

        if (SubtitleSuffixRegex.IsMatch(name))
        {
            return JavbusMagnetTag.SubtitleSuffix;
        }

        return ChineseRegex.IsMatch(name) ? JavbusMagnetTag.Chinese : JavbusMagnetTag.Plain;
    }

    /// <param name="sizeTolerance">
    /// 0.3 means "within 30% of the largest file is the same quality tier".
    /// Clamped to [0, <see cref="MaxSizeTolerance"/>].
    /// </param>
    public static JavbusMagnet? Select(IReadOnlyCollection<JavbusMagnet>? magnets, decimal sizeTolerance)
    {
        if (magnets is not {Count: > 0})
        {
            return null;
        }

        var tolerance = Math.Clamp(sizeTolerance, 0m, MaxSizeTolerance);
        var maxBytes = magnets.Max(m => m.SizeInBytes);
        var threshold = (long) (maxBytes * (1 - tolerance));

        // Sizeless rows only compete when nothing else does — otherwise an
        // unparsable size would silently win the tier on its tag alone.
        var tier = magnets.Where(m => m.SizeInBytes > 0 && m.SizeInBytes >= threshold).ToList();
        var pool = tier.Count > 0 ? tier : magnets;

        return pool.OrderByDescending(m => (int) m.Tag).ThenByDescending(m => m.SizeInBytes).First();
    }
}
