using CsQuery;

namespace Bakabase.Modules.ThirdParty.Helpers;

/// <summary>
/// Helpers for safely extracting text from CsQuery sets that may match more
/// than one DOM element (e.g. mobile + desktop variants of the same panel
/// rendered in the same response). Calling .Text() on such a set silently
/// concatenates the duplicate innerText into "NameName"; use these helpers
/// to read each element's innerText separately and join distinct values.
/// </summary>
internal static class CsQueryAnchorHelper
{
    public static string JoinDistinctText(this CQ set, string separator = ",") =>
        Join(set.Select(e => e.InnerText), separator);

    public static string JoinDistinctText(this IEnumerable<string?> values, string separator = ",") =>
        Join(values, separator);

    private static string Join(IEnumerable<string?> values, string separator) =>
        string.Join(separator, values
            .Select(s => s?.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct());
}
