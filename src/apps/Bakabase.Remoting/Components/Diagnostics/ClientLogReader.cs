using System.Text;
using System.Text.RegularExpressions;

namespace Bakabase.Remoting.Components.Diagnostics;

/// <param name="Timestamp">As written, including the offset. Null for a continuation line.</param>
/// <param name="Level">
/// Serilog's level name, spelled out — <c>Information</c>, <c>Error</c> — because the
/// template uses <c>{Level}</c> rather than <c>{Level:u3}</c>. Not the server's LogLevel:
/// these are Serilog's own names, so <c>Verbose</c> and <c>Fatal</c> appear where the
/// server's table would say Trace and Critical.
/// </param>
/// <param name="Source">The logger and method the line came from.</param>
public sealed record ClientLogEntry(string? Timestamp, string? Level, string? Source, string Message);

/// <summary>
/// Reads the client's own log, which is a set of files rather than a database.
/// </summary>
/// <remarks>
/// <para>
/// The all-in-one's log page reads a SQLite store the server writes alongside its
/// Serilog files. A thin client has no database at all — nothing it does is worth one —
/// so its log is exactly what Serilog put on disk, and this parses that back.
/// </para>
/// <para>
/// It exists because a client that cannot show its own log is a client whose problems
/// can only be diagnosed by asking the user to find a folder. The forwarded log page
/// shows the server's log, which is the wrong machine for every question about the
/// client itself.
/// </para>
/// </remarks>
public static partial class ClientLogReader
{
    /// <summary>
    /// Matches the head of a line written with the sink's output template:
    /// <c>{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] ({SourceContext}.{Method}) </c>.
    /// </summary>
    /// <remarks>
    /// Anything that does not match is a continuation — a stack trace, or a message with
    /// newlines in it — and belongs to the entry above rather than being dropped. Losing
    /// stack traces would defeat the point of having the log at all.
    /// </remarks>
    [GeneratedRegex(
        @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [^\]]*?) \[(?<lvl>\w+)\] \((?<src>[^)]*)\) (?<msg>.*)$",
        RegexOptions.Compiled)]
    private static partial Regex EntryHead();

    /// <summary>The log files, newest first.</summary>
    public static IReadOnlyList<FileInfo> Files(string logsDirectory)
    {
        var directory = new DirectoryInfo(logsDirectory);

        if (!directory.Exists)
        {
            return [];
        }

        return directory.GetFiles("AppLog_*.log")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    /// <summary>
    /// The most recent entries, newest first.
    /// </summary>
    /// <remarks>
    /// Reads whole files rather than seeking, because the sink rolls at 100 MB and a
    /// day's client log is a few hundred kilobytes — the complexity of a reverse reader
    /// would buy nothing. It stops as soon as it has enough, so a directory of rolled
    /// files is not all read to answer a request for the last hundred lines.
    /// </remarks>
    public static IReadOnlyList<ClientLogEntry> Read(string logsDirectory, int take, string? level = null,
        string? contains = null)
    {
        var matched = new List<ClientLogEntry>();

        // Newest first overall: files are walked newest-first and each file's entries come
        // back newest-first, so appending in order is already the right order. Counting
        // matches rather than lines read is what makes a filter work across rolled files —
        // counting lines would stop in today's file and report "no errors" while yesterday's
        // is full of them.
        foreach (var file in Files(logsDirectory))
        {
            foreach (var entry in ParseFile(file))
            {
                if (!Matches(entry, level, contains))
                {
                    continue;
                }

                matched.Add(entry);

                if (matched.Count >= take)
                {
                    return matched;
                }
            }
        }

        return matched;
    }

    private static bool Matches(ClientLogEntry entry, string? level, string? contains)
    {
        if (!string.IsNullOrWhiteSpace(level) &&
            !string.Equals(entry.Level, level, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(contains) ||
               entry.Message.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
               (entry.Source?.Contains(contains, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>Parses one file into entries, newest first.</summary>
    internal static IReadOnlyList<ClientLogEntry> ParseFile(FileInfo file)
    {
        List<string> lines;

        try
        {
            // Shared read: the sink has this file open for writing, and refusing to show
            // the log because it is being written to would be absurd.
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            lines = [];

            while (reader.ReadLine() is {} line)
            {
                lines.Add(line);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return Parse(lines);
    }

    /// <summary>
    /// Turns raw lines into entries, newest first, folding continuation lines into the
    /// entry they belong to.
    /// </summary>
    public static IReadOnlyList<ClientLogEntry> Parse(IReadOnlyList<string> lines)
    {
        var entries = new List<ClientLogEntry>();
        var pending = new StringBuilder();
        string? ts = null, level = null, source = null;
        var started = false;

        void Flush()
        {
            if (started)
            {
                entries.Add(new ClientLogEntry(ts, level, source, pending.ToString().TrimEnd()));
            }
        }

        foreach (var line in lines)
        {
            var match = EntryHead().Match(line);

            if (match.Success)
            {
                Flush();

                ts = match.Groups["ts"].Value;
                level = match.Groups["lvl"].Value;
                source = match.Groups["src"].Value;
                pending.Clear();
                pending.Append(match.Groups["msg"].Value);
                started = true;
            }
            else if (started)
            {
                pending.Append('\n').Append(line);
            }
            else if (line.Length > 0)
            {
                // A file that begins mid-entry, which is what a rolled file looks like
                // when an exception straddled the boundary. Kept rather than dropped.
                ts = null;
                level = null;
                source = null;
                pending.Clear();
                pending.Append(line);
                started = true;
            }
        }

        Flush();

        entries.Reverse();

        return entries;
    }
}
