using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;

namespace Bakabase.Modules.StandardValue.Abstractions.Configurations
{
    public class ExpectedConversions
    {
        #region String

        public static List<(string? FromValue, string? ToValue)> StringToString =
        [
            (null, null),
            (string.Empty, null),
            ("abc", "abc"),
            ("abc ", "abc"),
        ];

        public static List<(string? FromValue, List<string>? ToValue)> StringToListString =
        [
            (null, null),
            (string.Empty, null),
            ("abc", ["abc"]),
            ("abc, def,,", ["abc", "def"]),
        ];

        public static List<(string? FromValue, decimal? ToValue)> StringToDecimal =
        [
            (null, null),
            (string.Empty, null),
            ("abc666 ", null),
            ("666 ", 666),
            (" 666.666", 666.666m),
        ];

        public static List<(string? FromValue, LinkValue? ToValue)> StringToLink =
        [
            (null, null),
            (string.Empty, null),
            ("abc ", new LinkValue("abc", null)),
            ("[abc ] (http://abc.com)  ", new LinkValue("abc", "http://abc.com")),
            ("[abc ] http://abc.com  ", new LinkValue("[abc ] http://abc.com", null)),
        ];

        public static List<(string? FromValue, bool? ToValue)> StringToBoolean =
        [
            (null, null),
            (string.Empty, null),
            (" ", null),
            ("abc", true),
            ("true", true),
            ("True", true),
            ("TrUe", true),
            ("false", false),
            ("False", false),
            ("0", false),
            ("1", true),
            ("100", true),
        ];

        public static List<(string? FromValue, DateTime? ToValue)> StringToDateTime =
        [
            (null, null),
            (string.Empty, null),
            (" ", null),
            (" 2024-10-01", new DateTime(2024, 10, 1)),
            ("2024-10-01 20:23:44 ", new DateTime(2024, 10, 1, 20, 23, 44)),
            ("2024-10-01 20:23:44.567\n", new DateTime(2024, 10, 1, 20, 23, 44, 567)),
        ];

        public static List<(string? FromValue, TimeSpan? ToValue)> StringToTime =
        [
            (null, null),
            (string.Empty, null),
            (" ", null),
            ("12:20:30", new TimeSpan(12, 20, 30)),
            ("12:20:30.333 ", new TimeSpan(0, 12, 20, 30, 333)),
            ("5.12:20:30.333\n", new TimeSpan(5, 12, 20, 30, 333)),
        ];

        public static List<(string? FromValue, List<List<string>>? ToValue)> StringToListListString =
        [
            (null, null),
            (string.Empty, null),
            (" ", null),
            ("abc ", [["abc"]]),
            ("abc /def, ,,ghi", [["abc", "def"], ["ghi"]]),
        ];

        public static List<(string? FromValue, List<TagValue>? ToValue)> StringToListTag =
        [
            (null, null),
            (string.Empty, null),
            (" ", null),
            ("abc ", [new TagValue(null, "abc")]),
            ("group :name", [new TagValue("group", "name")]),
            ("abc: def:ghi", [new TagValue("abc", "def:ghi")]),
            ("abc:def ,ghi\n", [new TagValue("abc", "def"), new TagValue(null, "ghi")]),
        ];

        #endregion

        #region ListString

        public static List<(List<string>? FromValue, string? ToValue)> ListStringToString =
        [
            (null, null),
            ([], null),
            ([string.Empty], null),
            ([" "], null),
            (["abc ", string.Empty], "abc"),
            (["abc", " "], "abc"),
            (["abc\n", "  def"], "abc,def"),
        ];

        public static List<(List<string>? FromValue, List<string>? ToValue)> ListStringToListString =
        [
            (null, null),
            ([], null),
            ([" "], null),
            ([string.Empty], null),
            (["abc "], ["abc"]),
            (["abc ", string.Empty, " ", "def \n"], ["abc", "def"]),
        ];

        public static List<(List<string>? FromValue, decimal? ToValue)> ListStringToDecimal =
        [
            (null, null),
            ([], null),
            ([string.Empty], null),
            ([" "], null),
            (["abc", "def"], null),
            (["abc ", "4.5 "], 4.5m),
            (["5.0\n", "4.5"], 5.0m),
        ];

        public static List<(List<string>? FromValue, LinkValue? ToValue)> ListStringToLink =
        [
            (null, null),
            ([], null),
            ([string.Empty], null),
            ([" "], null),
            (["abc \n"], new LinkValue("abc", null)),
            (["abc", " def"], new LinkValue("abc,def", null)),
            (["[abc](http://abc.com)", " "], new LinkValue("abc", "http://abc.com")),
            (["[abc](http://abc.com)", " def"], new LinkValue("[abc](http://abc.com),def", null)),
        ];

        public static List<(List<string>? FromValue, bool? ToValue)> ListStringToBoolean =
        [
            (null, null),
            ([], null),
            ([string.Empty], null),
            ([" "], null),
            (["1 "], true),
            (["0"], false),
            (["falSe"], false),
            (["True\n"], true),
            (["abc"], true),
            (["abc", string.Empty], true),
            (["0", "abc"], true),
            (["0", "0"], true),
            (["0", "1"], true),
            (["true", "false"], true),
            (["True", "false"], true),
            (["false", "abc"], true),
        ];

        public static List<(List<string>? FromValue, DateTime? ToValue)> ListStringToDateTime =
        [
            (null, null),
            ([string.Empty], null),
            ([" "], null),
            (["123", "2024-10-01"], new DateTime(2024, 10, 1)),
            (["abc", "2024-10-01 20:23:44 "], new DateTime(2024, 10, 1, 20, 23, 44)),
            (["abc", "2024-10-01 20:23:44.567 ", "2024-01-01 \n"], new DateTime(2024, 10, 1, 20, 23, 44, 567)),
        ];

        public static List<(List<string>? FromValue, TimeSpan? ToValue)> ListStringToTime =
        [
            (null, null),
            ([string.Empty], null),
            ([" "], null),
            (["12:20:30", "15:06:07"], new TimeSpan(12, 20, 30)),
            ([string.Empty, "abc", "12:20:30.333"], new TimeSpan(0, 12, 20, 30, 333)),
            (["5.12:20:30.333"], new TimeSpan(5, 12, 20, 30, 333)),
        ];

        public static List<(List<string>? FromValue, List<List<string>>? ToValue)> ListStringToListListString =
        [
            (null, null),
            ([string.Empty], null),
            ([" "], null),
            (["abc", "def", string.Empty], [["abc"], ["def"]]),
            (["a/ bb/ c", "\ndef", string.Empty], [["a", "bb", "c"], ["def"]]),
        ];

        public static List<(List<string>? FromValue, List<TagValue>? ToValue)> ListStringToListTag =
        [
            (null, null),
            ([string.Empty], null),
            ([" "], null),
            (["abc", string.Empty], [new TagValue(null, "abc")]),
            (["abc", "def:ghi", "jkl:mno:pqr"],
                [new TagValue(null, "abc"), new TagValue("def", "ghi"), new TagValue("jkl", "mno:pqr")]),
        ];


        #endregion

        #region Decimal

        public static List<(decimal? FromValue, string? ToValue)> DecimalToString =
        [
            (null, null),
            (1.5m, "1.5"),
        ];

        public static List<(decimal? FromValue, List<string>? ToValue)> DecimalToListString =
        [
            (null, null),
            (2m, ["2"]),
        ];

        public static List<(decimal? FromValue, decimal? ToValue)> DecimalToDecimal =
        [
            (null, null),
            (5, 5),
        ];

        public static List<(decimal? FromValue, LinkValue? ToValue)> DecimalToLink =
        [
            (null, null),
            (66, new LinkValue("66", null)),
        ];

        public static List<(decimal? FromValue, bool? ToValue)> DecimalToBoolean =
        [
            (null, null),
            (0.1m, true),
            (0, false),
        ];

        public static List<(decimal? FromValue, DateTime? ToValue)> DecimalToDateTime =
        [
            (null, null),
            (5m, null),
        ];

        public static List<(decimal? FromValue, TimeSpan? ToValue)> DecimalToTime =
        [
            (null, null),
            (5m, null),
        ];

        public static List<(decimal? FromValue, List<List<string>>? ToValue)> DecimalToListListString =
        [
            (null, null),
            (5m, [["5"]]),
        ];

        public static List<(decimal? FromValue, List<TagValue>? ToValue)> DecimalToListTag =
        [
            (null, null),
            (5m, [new TagValue(null, "5")]),
        ];

        #endregion

        #region Link

        public static List<(LinkValue? FromValue, string? ToValue)> LinkToString =
        [
            (null, null),
            (new LinkValue(null, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue("abc", null), "abc"),
            (new LinkValue(null, " http://abc.com"), "http://abc.com"),
            (new LinkValue("abc ", "http://abc.com"), "[abc](http://abc.com)"),
            ..StringToString.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, List<string>? ToValue)> LinkToListString =
        [
            (null, null),
            (new LinkValue(null, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue("abc ", null), ["abc"]),
            (new LinkValue(null, "http://abc.com"), ["http://abc.com"]),
            (new LinkValue("abc", "\nhttp://abc.com"), ["[abc](http://abc.com)"]),
            ..StringToListString.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, decimal? ToValue)> LinkToDecimal =
        [
            (null, null),
            (new LinkValue(string.Empty, " "), null),
            ..StringToDecimal.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, LinkValue? ToValue)> LinkToLink =
        [
            (null, null),
            (new LinkValue(null, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue(null, string.Empty), null),
            (new LinkValue("abc", "http://abc.com "), new LinkValue("abc", "http://abc.com")),
        ];

        public static List<(LinkValue? FromValue, bool? ToValue)> LinkToBoolean =
        [
            (null, null),
            (new LinkValue(null, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue(string.Empty, null), null),
            ..StringToBoolean.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, DateTime? ToValue)> LinkToDateTime =
        [
            (null, null),
            (new LinkValue(string.Empty, null), null),
            (new LinkValue(string.Empty, " "), null),
            ..StringToDateTime.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, TimeSpan? ToValue)> LinkToTime =
        [
            (null, null),
            (new LinkValue(string.Empty, null), null),
            (new LinkValue(string.Empty, " "), null),
            ..StringToTime.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, List<List<string>>? ToValue)> LinkToListListString =
        [
            (null, null),
            (new LinkValue(null, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue("abc", null), [["abc"]]),
            (new LinkValue(null, "http://abc.com"), [["http://abc.com"]]),
            (new LinkValue("abc", "http://abc.com"), [["[abc](http://abc.com)"]]),
            ..StringToListListString.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        public static List<(LinkValue? FromValue, List<TagValue>? ToValue)> LinkToListTag =
        [
            (null, null),
            (new LinkValue(string.Empty, null), null),
            (new LinkValue(string.Empty, " "), null),
            (new LinkValue("abc", null), [new TagValue(null, "abc")]),
            (new LinkValue("group:name", "http://abc.com"), [new TagValue("group", "name")]),
            (new LinkValue("abc:edf:ghi", null), [new TagValue("abc", "edf:ghi")]),
            ..StringToListTag.Select(x => (new LinkValue(x.FromValue, null), x.ToValue))
        ];

        #endregion

        #region Boolean

        public static List<(bool? FromValue, string? ToValue)> BooleanToString =
        [
            (null, null),
            (true, "1"),
            (false, "0"),
        ];

        public static List<(bool? FromValue, List<string>? ToValue)> BooleanToListString =
        [
            (null, null),
            (true, ["1"]),
            (false, ["0"]),
        ];

        public static List<(bool? FromValue, decimal? ToValue)> BooleanToDecimal =
        [
            (null, null),
            (true, 1),
            (false, 0),
        ];

        public static List<(bool? FromValue, LinkValue? ToValue)> BooleanToLink =
        [
            (null, null),
            (true, new LinkValue("1", null)),
            (false, new LinkValue("0", null)),
        ];

        public static List<(bool? FromValue, bool? ToValue)> BooleanToBoolean =
        [
            (null, null),
            (true, true),
            (false, false),
        ];

        public static List<(bool? FromValue, DateTime? ToValue)> BooleanToDateTime =
        [
            (null, null),
            (true, null),
            (false, null),
        ];

        public static List<(bool? FromValue, TimeSpan? ToValue)> BooleanToTime =
        [
            (null, null),
            (true, null),
            (false, null),
        ];

        public static List<(bool? FromValue, List<List<string>>? ToValue)> BooleanToListListString =
        [
            (null, null),
            (true, [["1"]]),
            (false, [["0"]]),
        ];

        public static List<(bool? FromValue, List<TagValue>? ToValue)> BooleanToListTag =
        [
            (null, null),
            (true, [new TagValue(null, "1")]),
            (false, [new TagValue(null, "0")]),
        ];

        #endregion

        #region DateTime

        public static List<(DateTime? FromValue, string? ToValue)> DateTimeToString =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), "2024-10-10 02:05:01"),
        ];

        public static List<(DateTime? FromValue, List<string>? ToValue)> DateTimeToListString =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), ["2024-10-10 02:05:01"]),
        ];

        public static List<(DateTime? FromValue, decimal? ToValue)> DateTimeToDecimal =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), null),
        ];

        public static List<(DateTime? FromValue, LinkValue? ToValue)> DateTimeToLink =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), new LinkValue("2024-10-10 02:05:01", null)),
        ];

        public static List<(DateTime? FromValue, bool? ToValue)> DateTimeToBoolean =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), true),
        ];

        public static List<(DateTime? FromValue, DateTime? ToValue)> DateTimeToDateTime =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), new DateTime(2024, 10, 10, 02, 05, 1)),
        ];

        public static List<(DateTime? FromValue, TimeSpan? ToValue)> DateTimeToTime =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), new TimeSpan(2, 5, 1)),
        ];

        public static List<(DateTime? FromValue, List<List<string>>? ToValue)> DateTimeToListListString =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), [["2024-10-10 02:05:01"]]),
        ];

        public static List<(DateTime? FromValue, List<TagValue>? ToValue)> DateTimeToListTag =
        [
            (null, null),
            (new DateTime(2024, 10, 10, 02, 05, 1), [new TagValue(null, "2024-10-10 02:05:01")]),
        ];

        #endregion

        #region Time

        public static List<(TimeSpan? FromValue, string? ToValue)> TimeToString =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), "2:05:01"),
        ];

        public static List<(TimeSpan? FromValue, List<string>? ToValue)> TimeToListString =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), ["2:05:01"]),
        ];

        public static List<(TimeSpan? FromValue, decimal? ToValue)> TimeToDecimal =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), null),
        ];

        public static List<(TimeSpan? FromValue, LinkValue? ToValue)> TimeToLink =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), new LinkValue("2:05:01", null)),
        ];

        public static List<(TimeSpan? FromValue, bool? ToValue)> TimeToBoolean =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), true),
        ];

        public static List<(TimeSpan? FromValue, DateTime? ToValue)> TimeToDateTime =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), DateTime.Today.Add(new TimeSpan(02, 05, 1))),
        ];

        public static List<(TimeSpan? FromValue, TimeSpan? ToValue)> TimeToTime =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), new TimeSpan(2, 5, 1)),
        ];

        public static List<(TimeSpan? FromValue, List<List<string>>? ToValue)> TimeToListListString =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), [["2:05:01"]]),
        ];

        public static List<(TimeSpan? FromValue, List<TagValue>? ToValue)> TimeToListTag =
        [
            (null, null),
            (new TimeSpan(02, 05, 1), [new TagValue(null, "2:05:01")]),
        ];

        #endregion

        #region ListListString

        public static List<(List<List<string>>? FromValue, string? ToValue)> ListListStringToString =
        [
            (null, null),
            ([["abc"]], "abc"),
            ([["abc", string.Empty, "def"]], "abc/def"),
            ([["abc", string.Empty, " def"], ["ghi", " jkl"]], "abc/def,ghi/jkl"),
        ];

        public static List<(List<List<string>>? FromValue, List<string>? ToValue)> ListListStringToListString =
        [
            (null, null),
            ([["ab c", string.Empty], ["def", "ghi ", string.Empty]], ["ab c", "def/ghi"]),
        ];

        public static List<(List<List<string>>? FromValue, decimal? ToValue)> ListListStringToDecimal =
        [
            (null, null),
            ([["4.5"]], 4.5m),
            ([["abc", "5"], ["4.5", "def"]], 5),
        ];

        public static List<(List<List<string>>? FromValue, LinkValue? ToValue)> ListListStringToLink =
        [
            (null, null),
            ([["abc"]], new LinkValue("abc", null)),
            ([["abc", string.Empty, "def"]], new LinkValue("abc/def", null)),
            ([["abc", string.Empty, "def"], ["ghi", "jkl"]], new LinkValue("abc/def,ghi/jkl", null)),
            ..StringToLink.Where(x => x.FromValue != null).Select(x =>
                (new List<List<string>> {new() {x.FromValue!}}, x.ToValue))
        ];

        public static List<(List<List<string>>? FromValue, bool? ToValue)> ListListStringToBoolean =
        [
            (null, null),
            ([[string.Empty]], null),
            ([[]], null),
            ([[" "]], null),
            ([["abc"]], true),
            ([["0"]], false),
            ([["a", "false"]], true),
            ([["a"], ["false"]], true),
            ..StringToBoolean.Where(x => x.FromValue != null).Select(x =>
                (new List<List<string>> {new() {x.FromValue!}}, x.ToValue))
        ];

        public static List<(List<List<string>>? FromValue, DateTime? ToValue)> ListListStringToDateTime =
        [
            (null, null),
            ([[string.Empty]], null),
            ([["2024-10-01"]], new DateTime(2024, 10, 1)),
            ([["abc", string.Empty], ["def", "2024-10-01 20:23:44"]], new DateTime(2024, 10, 1, 20, 23, 44)),
            ([["2024-10-01 20:23:44.567"], ["2020-01-01"]], new DateTime(2024, 10, 1, 20, 23, 44, 567)),
            ..StringToDateTime.Where(x => x.FromValue != null).Select(x =>
                (new List<List<string>> {new() {x.FromValue!, "abc "}}, x.ToValue))
        ];

        public static List<(List<List<string>>? FromValue, TimeSpan? ToValue)> ListListStringToTime =
        [
            (null, null),
            ([[string.Empty]], null),
            ([["12:20:30"]], new TimeSpan(12, 20, 30)),
            ([["abc"], ["12:20:30.333"]], new TimeSpan(0, 12, 20, 30, 333)),
            ([["5.12:20:30.333", "01:01:01"]], new TimeSpan(5, 12, 20, 30, 333)),
            ..StringToTime.Where(x => x.FromValue != null).Select(x =>
                (new List<List<string>> {new() {x.FromValue!, "abc "}}, x.ToValue))
        ];

        public static List<(List<List<string>>? FromValue, List<List<string>>? ToValue)>
            ListListStringToListListString =
            [
                (null, null),
                ([[string.Empty]], null),
                ([["a", "b", string.Empty], [string.Empty, "c"]], [["a", "b"], ["c"]]),
            ];

        public static List<(List<List<string>>? FromValue, List<TagValue>? ToValue)> ListListStringToListTag =
        [
            (null, null),
            ([["abc", "def"], ["ghi", string.Empty, "jkl"], ["x", "y", "z"]],
                [new TagValue("abc", "def"), new TagValue("ghi", "jkl"), new TagValue("x", "y/z")]),
        ];

        #endregion

        #region ListTag

        public static List<(List<TagValue>? FromValue, string? ToValue)> ListTagToString =
        [
            (null, null),
            ([new TagValue(null, "abc")], "abc"),
            ([new TagValue("abc ", "def")], "abc:def"),
            ([new TagValue("abc", "def "), new TagValue("ghi", "jkl")], "abc:def,ghi:jkl"),
        ];

        public static List<(List<TagValue>? FromValue, List<string>? ToValue)> ListTagToListString =
        [
            (null, null),
            ([new TagValue(string.Empty, "abc "), new TagValue("def", "ghi")], ["abc", "def:ghi"]),
        ];

        public static List<(List<TagValue>? FromValue, decimal? ToValue)> ListTagToDecimal =
        [
            (null, null),
            ([new TagValue(string.Empty, " 4.5")], 4.5m),
            ([new TagValue(string.Empty, "5"), new TagValue(string.Empty, "4.5")], 5),
            ..StringToDecimal.Where(x => x.FromValue != null)
                .Select(x => (new List<TagValue> {new TagValue(null, x.FromValue!)}, x.ToValue)),
        ];

        public static List<(List<TagValue>? FromValue, LinkValue? ToValue)> ListTagToLink =
        [
            (null, null),
            ([new TagValue(null, "abc")], new LinkValue("abc", null)),
            ([new TagValue("abc", "def")], new LinkValue("abc:def", null)),
            ([new TagValue(string.Empty, "abc"), new TagValue("def", "ghi")], new LinkValue("abc,def:ghi", null)),
            ..StringToLink.Where(x => x.FromValue != null)
                .Select(x => (new List<TagValue> {new TagValue(null, x.FromValue!)}, x.ToValue)),
        ];

        public static List<(List<TagValue>? FromValue, bool? ToValue)> ListTagToBoolean =
        [
            (null, null),
            ([], null),
            ([new TagValue(null, "abc")], true),
            ([new TagValue("true", "false")], false),
            ..StringToBoolean.Where(x => x.FromValue != null)
                .Select(x => (new List<TagValue> {new TagValue(null, x.FromValue!)}, x.ToValue)),
        ];

        public static List<(List<TagValue>? FromValue, DateTime? ToValue)> ListTagToDateTime =
        [
            (null, null),
            ([], null),
            ([new TagValue(null, "2024-10-01")], new DateTime(2024, 10, 1)),
            ([new TagValue(string.Empty, "abc"), new TagValue("def", "2024-10-01 20:23:44")],
                new DateTime(2024, 10, 1, 20, 23, 44)),
            ([new TagValue(string.Empty, "2024-10-01 20:23:44.567"), new TagValue("def", "2020-01-01")],
                new DateTime(2024, 10, 1, 20, 23, 44, 567)),
            ..StringToDateTime.Where(x => x.FromValue != null)
                .Select(x => (new List<TagValue> {new TagValue(null, x.FromValue!)}, x.ToValue)),
        ];

        public static List<(List<TagValue>? FromValue, TimeSpan? ToValue)> ListTagToTime =
        [
            (null, null),
            ([], null),
            ([new TagValue(null, "12:20:30")], new TimeSpan(12, 20, 30)),
            ([new TagValue(null, "abc"), new TagValue(null, "12:20:30.333")], new TimeSpan(0, 12, 20, 30, 333)),
            ([new TagValue("abc", "5.12:20:30.333"), new TagValue(null, "01:01:01")], new TimeSpan(5, 12, 20, 30, 333)),
            ..StringToTime.Where(x => x.FromValue != null)
                .Select(x => (new List<TagValue> {new TagValue(null, x.FromValue!)}, x.ToValue)),
        ];

        public static List<(List<TagValue>? FromValue, List<List<string>>? ToValue)> ListTagToListListString =
        [
            (null, null),
            ([], null),
            ([new TagValue("a", "b"), new TagValue(null, "c")], [["a", "b"], ["c"]]),
        ];

        public static List<(List<TagValue>? FromValue, List<TagValue>? ToValue)> ListTagToListTag =
        [
            (null, null),
            ([new TagValue(string.Empty, "abc"), new TagValue("def ", "ghi")],
                [new TagValue(null, "abc"), new TagValue("def", "ghi")]),
        ];

        #endregion
    }
}