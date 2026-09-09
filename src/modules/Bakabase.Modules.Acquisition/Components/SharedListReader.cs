using System.Text;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>One line of somebody's list of things to get.</summary>
/// <param name="Title">What they called it.</param>
/// <param name="Url">Where to get it, when the row says.</param>
/// <param name="Password">An archive password, when the row says.</param>
/// <param name="LineNumber">Which row it came from, so a problem can be pointed at.</param>
public record SharedListRow(string? Title, string? Url, string? Password, int LineNumber);

/// <summary>
/// Reads a list of things somebody wrote down.
/// <para>
/// Lists like this are how sharing actually circulates — a message with twenty lines, a
/// spreadsheet a group keeps. They are never in the same shape twice, so this reads them the way a
/// person does: a link is whatever looks like a link, a password is whatever sits behind the word
/// for it, and the title is what is left.
/// </para>
/// </summary>
public static partial class SharedListReader
{
    /// <summary>
    /// Reads plain text — one entry per line, however the line is arranged. Handles the two
    /// shapes people actually write: separated fields, and a sentence with a link in it.
    /// </summary>
    public static List<SharedListRow> ReadText(string text)
    {
        var rows = new List<SharedListRow>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.Length == 0) continue;

            var row = ReadLine(line, i + 1);

            // A line with neither a name nor a link says nothing. Skipping it beats importing a
            // resource called "---".
            if (row.Title != null || row.Url != null) rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Reads separated values — comma or tab. Quoted fields are honoured because a title with a
    /// comma in it is the commonest thing in one of these files.
    /// </summary>
    public static List<SharedListRow> ReadDelimited(string text)
    {
        var rows = new List<SharedListRow>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.Trim().Length == 0) continue;

            var cells = SplitCells(line);

            if (IsHeader(cells)) continue;

            var row = FromCells(cells, i + 1);

            if (row.Title != null || row.Url != null) rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// A row read from cells that are already separated — a spreadsheet's, or a delimited line's.
    /// </summary>
    public static SharedListRow FromCells(IReadOnlyList<string> cells, int lineNumber)
    {
        string? url = null;
        string? password = null;
        var rest = new List<string>();

        foreach (var raw in cells)
        {
            var cell = raw.Trim();

            if (cell.Length == 0) continue;

            if (url == null && UrlRegex().Match(cell) is {Success: true} match)
            {
                url = match.Value;

                // A whole line is often one cell: "Volume 1 https://… 提取码 abcd". What is left
                // once the link is taken out is still the row's own words, and throwing it away
                // would leave every such row nameless.
                var remainder = cell.Remove(match.Index, match.Length).Trim();

                if (ReadPassword(remainder) is { } code)
                {
                    password ??= code;
                    remainder = PasswordRegex().Replace(remainder, "").Trim();
                }

                if (remainder.Length > 0) rest.Add(remainder);

                continue;
            }

            var asPassword = ReadPassword(cell);

            if (asPassword != null && password == null)
            {
                password = asPassword;

                continue;
            }

            rest.Add(cell);
        }

        var title = rest.Count > 0
            ? string.Join(" ", rest).Trim().Trim('-', '|', ':', '：', ',', '，', ';', '；').Trim()
            : null;

        return new SharedListRow(string.IsNullOrWhiteSpace(title) ? null : title, url, password,
            lineNumber);
    }

    private static SharedListRow ReadLine(string line, int lineNumber)
    {
        // A line that is clearly separated is read as cells; a sentence is read as one cell, and
        // the same extraction finds the link inside it.
        var cells = line.Contains('\t') || line.Count(c => c == ',') >= 2
            ? SplitCells(line)
            : [line];

        return FromCells(cells, lineNumber);
    }

    private static List<string> SplitCells(string line)
    {
        var separator = line.Contains('\t') ? '\t' : ',';
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"')
            {
                // "" inside a quoted field is one quote, which is how a spreadsheet writes it.
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (c == separator && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();

                continue;
            }

            current.Append(c);
        }

        cells.Add(current.ToString());

        return cells;
    }

    /// <summary>
    /// A header row is one whose cells are all field names. Dropping it beats importing a resource
    /// called "Title".
    /// </summary>
    private static bool IsHeader(IReadOnlyList<string> cells)
    {
        var named = 0;

        foreach (var cell in cells)
        {
            var value = cell.Trim().ToLowerInvariant();

            if (value.Length == 0) continue;

            if (value is "title" or "name" or "url" or "link" or "password" or "code"
                or "标题" or "名称" or "链接" or "地址" or "密码" or "提取码")
            {
                named++;
            }
            else
            {
                return false;
            }
        }

        return named > 0;
    }

    /// <summary>
    /// The password in a cell, when the cell says it is one. A bare word is not treated as a
    /// password: a title is a bare word too, and guessing wrong writes nonsense into every row.
    /// </summary>
    private static string? ReadPassword(string cell)
    {
        var match = PasswordRegex().Match(cell);

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    [GeneratedRegex(@"https?://[^\s,;""'）)】\]]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    /// <summary>
    /// The words people put in front of a code, in the two languages these lists are written in.
    /// </summary>
    [GeneratedRegex(
        @"(?:提取码|访问码|密\s*码|解压密码|password|passwd|pwd|code)\s*[:：=]?\s*(?<value>[^\s,;，；]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();
}
