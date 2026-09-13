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
    public static List<SharedListRow> ReadDelimited(string text) =>
        ReadRows(SplitRecords(text.TrimStart('\uFEFF')));

    /// <summary>
    /// Recognised headers make the columns authoritative. Headerless lists retain the original
    /// content-based reading, including lists whose columns are in a different order on each row.
    /// </summary>
    public static List<SharedListRow> ReadRows(
        IEnumerable<(IReadOnlyList<string> Cells, int LineNumber)> records)
    {
        var rows = new List<SharedListRow>();
        Dictionary<string, int>? columns = null;
        var first = true;

        foreach (var (cells, lineNumber) in records)
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            if (first)
            {
                first = false;
                columns = ReadHeader(cells);
                if (columns != null) continue;
            }

            var row = columns == null
                ? FromCells(cells, lineNumber)
                : FromColumns(cells, columns, lineNumber);
            if (row.Title != null || row.Url != null) rows.Add(row);
        }

        return rows;
    }

    private static SharedListRow FromColumns(IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> columns, int lineNumber)
    {
        string? Value(string key) => columns.TryGetValue(key, out var index) && index < cells.Count
            && !string.IsNullOrWhiteSpace(cells[index]) ? cells[index].Trim() : null;

        var title = Value("title");
        var url = Value("url");
        var password = Value("password");

        return new SharedListRow(title, url, password, lineNumber);
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

    private static List<string> SplitCells(string line) =>
        SplitRecords(line, strictQuotes: false).First().Cells.ToList();

    private static IEnumerable<(IReadOnlyList<string> Cells, int LineNumber)> SplitRecords(string text, bool strictQuotes = true)
    {
        // Determine the delimiter from the first record, ignoring punctuation inside quotes.
        var separator = ',';
        var quoted = false;
        var hasContent = false;
        foreach (var c in text)
        {
            if (c == '"') quoted = !quoted;
            if (!quoted && c is '\r' or '\n')
            {
                if (hasContent) break;
                continue;
            }
            if (!quoted && c == '\t') { separator = '\t'; break; }
            if (!char.IsWhiteSpace(c)) hasContent = true;
        }

        var cells = new List<string>();
        var current = new StringBuilder();
        var line = 1;
        var recordLine = 1;
        quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
                continue;
            }

            if (c == separator && !quoted)
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            if (c is '\r' or '\n')
            {
                var newline = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : c.ToString();
                if (newline.Length == 2) i++;
                line++;

                if (quoted) current.Append(newline);
                else
                {
                    cells.Add(current.ToString());
                    yield return (cells, recordLine);
                    cells = [];
                    current.Clear();
                    recordLine = line;
                }
                continue;
            }

            current.Append(c);
        }

        if (quoted && strictQuotes)
            throw new FormatException($"CSV record starting on line {recordLine} has an unclosed quoted field. " +
                                      "Close the quote or save the file as CSV UTF-8 and try again.");

        cells.Add(current.ToString());
        yield return (cells, recordLine);
    }

    private static Dictionary<string, int>? ReadHeader(IReadOnlyList<string> cells)
    {
        var columns = new Dictionary<string, int>();
        for (var i = 0; i < cells.Count; i++)
        {
            var value = cells[i].Trim().TrimStart('\uFEFF').ToLowerInvariant();
            if (value.Length == 0) continue;
            var key = value switch
            {
                "title" or "name" or "标题" or "名称" or "资源名称" => "title",
                "url" or "link" or "download url" or "download link" or "链接" or "地址" or "下载链接" => "url",
                "password" or "archive password" or "code" or "密码" or "解压密码" or "提取码" => "password",
                _ => null
            };
            if (key == null || !columns.TryAdd(key, i)) return null;
        }

        return columns.Count > 0 ? columns : null;
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
