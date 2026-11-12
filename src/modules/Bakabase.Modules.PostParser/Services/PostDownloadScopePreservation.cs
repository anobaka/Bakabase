using System.Text.RegularExpressions;
using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>
/// Conservative source wording for titles containing an explicit resource qualifier.
/// It quotes the source title; it never infers package completeness from file size, links or file names,
/// changes content groups, or grants permission to acquire anything outside the extracted scope.
/// Body/comment qualifiers remain semantic extraction work because they may refer to separate groups.
/// </summary>
internal static partial class PostDownloadScopePreservation
{
    [GeneratedRegex(@"(?:仅|只含|仅含|仅提供|仅分享|只有)?(?:字幕|汉化补丁|翻译补丁|补丁|差分|试玩版?|体验版|部分(?:内容|章节))|(?:不含|未含|没有)(?:游戏|音频)?本体|本体(?:不含|另下|自备|需另行获取)|(?<![\p{L}\p{N}])(?:lrc|srt|ass|ssa|vtt|subtitles?(?:[\s_-]+only)?|patch(?:[\s_-]+only)?|demo|trial|partial)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex Qualifiers();

    internal static PostDownloadInfo PreserveTitleEvidence(PostContent content, PostDownloadInfo info)
    {
        var original = content.Title?.Trim() ?? "";
        // Extremely large or unbounded input should not become generated presentation metadata.
        if (original.Length is 0 or > 4096) return info;
        var matches = Qualifiers().Matches(original).ToArray();
        if (matches.Length == 0) return info;
        var clauses = matches.Select(m => SourceClause(original, m.Index, m.Length))
            .Distinct(StringComparer.Ordinal).ToArray();

        // Matching the word "subtitles" cannot prove whether "includes subtitles",
        // "without subtitles" or "subtitles only" survived rewriting. Keep the full
        // source wording, including its negation/composition, instead of substring tests.
        // Acquisition plan titles are limited to 512 characters downstream.
        var title = BoundedTitle(original, clauses, 512);
        var groups = info.Groups;
        // A post title cannot be assigned to one of several packages without semantic evidence.
        // Also leave tool/related groups and any ungrouped resources untouched.
        if (groups.Count == 1 && groups[0].Kind is "main" or "preview" or "supplement" &&
            info.Resources.Count > 0 && info.Resources.All(r => r.GroupId == groups[0].Id))
        {
            var group = groups[0];
            // Do not retain a generated "full edition" claim with a contradictory suffix.
            // Processing instructions remain in extraction; this is presentation only.
            groups = [group with
            {
                Title = BoundedTitle(original, clauses, 160),
                Summary = BoundedTitle(original, clauses, 500),
                Evidence = new[] {BoundedTitle(original, clauses, 300)}.Concat(group.Evidence)
                    .Distinct(StringComparer.Ordinal).Take(8).ToList()
            }];
        }
        return info with {Title = title, Groups = groups};
    }

    private static string BoundedTitle(string original, string[] clauses, int limit)
    {
        if (original.Length <= limit) return original;
        // Quote entire source clauses rather than cutting a negation or quoting a bare
        // keyword. If even those cannot fit, state uncertainty instead of changing scope.
        var quoted = string.Join(" ", clauses.Select(c => "[" + c + "]"));
        return quoted.Length <= limit ? quoted : "资源范围需核对（请查看原帖完整标题）";
    }

    private static string SourceClause(string original, int index, int length)
    {
        const string boundaries = "[]【】()（）|\r\n;；,，";
        var start = index;
        var end = index + length;
        while (start > 0 && !boundaries.Contains(original[start - 1])) start--;
        while (end < original.Length && !boundaries.Contains(original[end])) end++;
        return original[start..end].Trim();
    }
}
