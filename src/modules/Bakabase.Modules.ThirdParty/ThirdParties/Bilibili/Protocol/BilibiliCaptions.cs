using System.Globalization;
using System.Text;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>One subtitle to save next to the video.</summary>
/// <param name="Url">Normalized (https) subtitle URL. Signed and short-lived: never log it unredacted.</param>
/// <param name="FileSuffix">"" → <c>{name}.srt</c>; ".en-us" → <c>{name}.en-us.srt</c>. Provisional for
/// <paramref name="IsAi"/> plans: call <see cref="BilibiliCaptions.ResolveFileSuffix"/> with the downloaded body.</param>
/// <param name="IsAi">An AI track, whose label is only trusted once the body's <c>lang</c> confirms it.</param>
public sealed record BilibiliSubtitlePlan(DmView.TSubtitleItem Item, string Url, string FileSuffix, bool IsAi = false);

/// <summary>Subtitle selection, naming and SRT conversion.</summary>
public static class BilibiliCaptions
{
    private const int MaxLanguageLength = 16;

    /// <summary>
    /// Human tracks: the primary (<c>{name}.srt</c>, as the previous downloader saved it) is
    /// <c>zh-CN</c>/<c>zh-Hans</c>, else another <c>zh*</c>, else the first; every other human language is an extra (<c>.{lan}</c>, one per language).
    /// Without any human track, one AI track (<c>ai-zh</c>, else the first) is planned with
    /// <see cref="BilibiliSubtitlePlan.IsAi"/>; other AI tracks are dropped (their labels can be wrong).
    /// Items without a URL are ignored.
    /// </summary>
    public static IReadOnlyList<BilibiliSubtitlePlan> PlanSubtitles(IEnumerable<DmView.TSubtitleItem>? items)
    {
        var usable = (items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.SubtitleUrl)).ToList();
        var humans = usable.Where(i => !IsAi(i)).ToList();
        if (humans.Count == 0)
        {
            var ai = usable.FirstOrDefault(i => string.Equals(i.Lan, "ai-zh", StringComparison.OrdinalIgnoreCase)) ??
                     usable.FirstOrDefault();
            return ai == null ? [] : [new BilibiliSubtitlePlan(ai, NormalizeUrl(ai.SubtitleUrl!), "", true)];
        }

        var primary = humans.FirstOrDefault(i => i.Lan is { } l &&
                                                 (l.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) ||
                                                  l.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase))) ??
                      humans.FirstOrDefault(i => i.Lan?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true) ??
                      humans[0];

        var plans = new List<BilibiliSubtitlePlan> {new(primary, NormalizeUrl(primary.SubtitleUrl!), "")};
        var languages = new HashSet<string>(StringComparer.Ordinal) {SanitizeLanguage(primary.Lan)};
        foreach (var item in humans)
        {
            if (ReferenceEquals(item, primary))
            {
                continue;
            }

            var language = SanitizeLanguage(item.Lan);
            if (languages.Add(language))
            {
                plans.Add(new BilibiliSubtitlePlan(item, NormalizeUrl(item.SubtitleUrl!), "." + language));
            }
        }

        return plans;
    }

    /// <summary>
    /// The final suffix: a human plan keeps its own; an AI plan becomes the primary ("") only when the body's
    /// <c>lang</c> matches its label (<c>ai-zh</c> ↔ <c>zh*</c>), otherwise <c>.ai-{lang}</c>.
    /// </summary>
    public static string ResolveFileSuffix(BilibiliSubtitlePlan plan, SubtitleBody body)
    {
        if (!plan.IsAi)
        {
            return plan.FileSuffix;
        }

        return AiLanguageMatches(plan.Item.Lan, body.Lang) ? "" : ".ai-" + SanitizeLanguage(body.Lang);
    }

    /// <summary>
    /// SRT text: non-blank lines, stably sorted by start, numbered from 1, <c>HH:MM:SS,mmm</c> (hours may exceed
    /// 24), end clamped to ≥ start. <c>location</c>/<c>sid</c> are ignored. Write it as UTF-8 with a BOM.
    /// </summary>
    public static string ToSrt(SubtitleBody body)
    {
        var lines = (body.Body ?? [])
            .Where(l => !string.IsNullOrWhiteSpace(l.Content))
            .OrderBy(l => l.From)
            .ToList();
        var sb = new StringBuilder();
        var n = 0;
        foreach (var line in lines)
        {
            var from = ToMilliseconds(line.From);
            var to = Math.Max(ToMilliseconds(line.To), from);
            sb.Append(++n).Append('\n')
                .Append(FormatTimestamp(from)).Append(" --> ").Append(FormatTimestamp(to)).Append('\n')
                .Append(line.Content!.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n')).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>"//h/p" → "https://h/p"; "http://…" → "https://…".</summary>
    public static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return "https:" + trimmed;
        }

        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + trimmed["http://".Length..]
            : trimmed;
    }

    /// <summary>[A-Za-z0-9-] only, lower case, at most 16 characters; "und" when nothing is left.</summary>
    public static string SanitizeLanguage(string? lan)
    {
        var sb = new StringBuilder();
        foreach (var c in lan ?? "")
        {
            if (sb.Length >= MaxLanguageLength)
            {
                break;
            }

            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        var result = sb.ToString().Trim('-');
        return result.Length == 0 ? "und" : result;
    }

    private static bool IsAi(DmView.TSubtitleItem item) =>
        item.Type == 1 || item.Lan?.StartsWith("ai-", StringComparison.OrdinalIgnoreCase) == true;

    private static bool AiLanguageMatches(string? label, string? lang)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var labelLanguage = label.StartsWith("ai-", StringComparison.OrdinalIgnoreCase) ? label[3..] : label;
        return string.Equals(PrimarySubtag(labelLanguage), PrimarySubtag(lang), StringComparison.OrdinalIgnoreCase);
    }

    private static string PrimarySubtag(string language)
    {
        var trimmed = language.Trim();
        var end = trimmed.IndexOfAny(['-', '_']);
        return end < 0 ? trimmed : trimmed[..end];
    }

    private static long ToMilliseconds(double seconds) =>
        double.IsFinite(seconds) ? Math.Max(0, (long) Math.Round(seconds * 1000)) : 0;

    private static string FormatTimestamp(long milliseconds)
    {
        var hours = milliseconds / 3_600_000;
        var minutes = milliseconds / 60_000 % 60;
        var seconds = milliseconds / 1000 % 60;
        var ms = milliseconds % 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{hours:00}:{minutes:00}:{seconds:00},{ms:000}");
    }
}
