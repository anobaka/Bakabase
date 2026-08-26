using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Text.Components;

/// <summary>
/// Ships the builtin types and their starter entries. Carried over verbatim from the retired
/// <c>SpecialTextPrefabs</c>, plus the shape each type actually uses.
/// </summary>
public static class TextSeedData
{
    public record BuiltinType(
        WellKnownTextType WellKnown,
        string Name,
        TextTypeShape Shape,
        IReadOnlyList<(string Value1, string? Value2)> Entries);

    public static readonly IReadOnlyList<BuiltinType> BuiltinTypes =
    [
        new(WellKnownTextType.Useless, "Useless", TextTypeShape.Values,
        [
            (@"[Cc]\d{2}", null),
            (@"COMIC1☆\d{1,2}", null),
            ("成年コミック", null),
            ("同人誌", null),
            ("DL版", null),
            ("彩頁部分", null),
            ("無修正", null),
            ("文盲組", null),
            (@"\.mp4", null),
            ("ゲームCG", null),
            ("同人CG集", null),
            ("18禁ゲームCG", null)
        ]),
        new(WellKnownTextType.Language, "Language", TextTypeShape.MappingPair,
        [
            ("汉化", "中文"),
            ("中文", "中文"),
            ("中国翻訳", "中文"),
            ("CE家族社", "中文"),
            ("漢化", "中文"),
            ("阿提斯整個車頭的", "中文"),
            ("CN", "中文")
        ]),
        new(WellKnownTextType.Wrapper, "Wrapper", TextTypeShape.DelimiterPair,
        [
            ("(", ")"),
            ("[", "]")
        ]),
        new(WellKnownTextType.Standardization, "Standardization", TextTypeShape.MappingPair,
        [
            ("（", "("),
            ("）", ")"),
            ("【", "["),
            ("】", "]"),
            ("，", "、"),
            ("#", "＃"),
            ("Ａ", "A"),
            ("!", "！"),
            ("～", "~")
        ]),
        new(WellKnownTextType.Volume, "Volume", TextTypeShape.Values,
        [
            (@"\s[A-Za-z]+[\.-]?\d+[\.-]?", null),
            (@"[＃]\d+", null),
            ("上[巻卷]", "1"),
            ("(1st|2nd|3rd)", null),
            (@"\d+限目", null),
            ("第一[話章]", "1"),
            ("下[巻卷]", "2"),
            ("第二[話章]", "2"),
            ("第三[話章]", "3")
        ]),
        new(WellKnownTextType.Trim, "Trim", TextTypeShape.Values, []),
        new(WellKnownTextType.DateTime, "DateTime", TextTypeShape.Values,
        [
            ("yyyyMMddHHmm", null),
            ("yyyyMMdd", null),
            ("yyyy-MM-dd", null),
            ("yyyy-M-d", null),
            ("yyyy年MM月dd日", null),
            ("yyyy年M月d日", null),
            ("yyMMdd", null),
            ("yy年M月d日", null),
            ("MMdd", null),
            ("M月d日", null)
        ])
    ];
}
