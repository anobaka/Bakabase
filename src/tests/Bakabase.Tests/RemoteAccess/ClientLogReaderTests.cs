using System;
using System.IO;
using System.Linq;
using Bakabase.Client.Remoting.Components.Diagnostics;
using Bakabase.Remoting.Components.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Reading the client's log back out of the lines Serilog wrote.
/// </summary>
/// <remarks>
/// The client has no log database — nothing it does is worth one — so its log page
/// parses the file sink's own output. The template is
/// <c>{Timestamp} [{Level}] ({SourceContext}.{Method}) {Message}{NewLine}{Exception}</c>,
/// which means the interesting case is not a well-formed line but everything a message
/// or a stack trace does to the shape of one.
/// </remarks>
[TestClass]
public class ClientLogReaderTests
{
    // The sink's template is {Level}, not {Level:u3}, so the level is spelled out.
    // Taken from a file this app actually wrote rather than from the template.
    private const string Head = "2026-09-10 03:56:04.123 +08:00 [Information] (Bakabase.Client.Foo.Bar) ";

    [TestMethod]
    public void A_plain_line_is_split_into_its_parts()
    {
        var entry = ClientLogReader.Parse([Head + "Connected to Desk-PC"]).Single();

        Assert.AreEqual("2026-09-10 03:56:04.123 +08:00", entry.Timestamp);
        Assert.AreEqual("Information", entry.Level);
        Assert.AreEqual("Bakabase.Client.Foo.Bar", entry.Source);
        Assert.AreEqual("Connected to Desk-PC", entry.Message);
    }

    [TestMethod]
    public void A_stack_trace_stays_with_the_line_that_threw()
    {
        // The one thing a log reader must not do is drop the part that says what went
        // wrong. Serilog writes the exception on the lines after the message, and none
        // of them look like the head of an entry.
        var entries = ClientLogReader.Parse([
            "2026-09-10 03:56:04.123 +08:00 [Error] (Bakabase.Client.Foo.Bar) Could not start a player",
            "System.ComponentModel.Win32Exception (2): No such file or directory",
            "   at System.Diagnostics.Process.Start()",
            "   at Bakabase.Client.LocalPlayback.PlayAsync()"
        ]);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Error", entries[0].Level);
        StringAssert.Contains(entries[0].Message, "Could not start a player");
        StringAssert.Contains(entries[0].Message, "Win32Exception");
        StringAssert.Contains(entries[0].Message, "LocalPlayback.PlayAsync");
    }

    [TestMethod]
    public void Newest_comes_first()
    {
        // The page is a tail. Oldest-first would put the line someone is looking for at
        // the bottom of a two-thousand-line list.
        var entries = ClientLogReader.Parse([
            Head + "first",
            Head + "second",
            Head + "third"
        ]);

        CollectionAssert.AreEqual(new[] {"third", "second", "first"},
            entries.Select(e => e.Message).ToArray());
    }

    [TestMethod]
    public void A_file_that_starts_mid_entry_is_still_read()
    {
        // What a rolled file looks like when an exception straddled the boundary.
        // Dropping the orphaned lines would hide the tail of the very stack trace that
        // caused the roll.
        var entries = ClientLogReader.Parse([
            "   at Something.That.Threw()",
            Head + "carrying on"
        ]);

        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual("carrying on", entries[0].Message);
        Assert.IsNull(entries[1].Level, "an orphaned continuation has no head to take a level from");
        StringAssert.Contains(entries[1].Message, "Something.That.Threw");
    }

    [TestMethod]
    public void A_message_that_looks_like_a_timestamp_does_not_start_a_new_entry()
    {
        // Messages quote paths and other logs. Only the full template head counts, and
        // a bare date in the middle of a message is not one.
        var entries = ClientLogReader.Parse([
            Head + "Wrote 2026-09-10 03:56:04.123 to the index"
        ]);

        Assert.AreEqual(1, entries.Count);
        StringAssert.Contains(entries[0].Message, "to the index");
    }

    [TestMethod]
    public void Lines_this_application_really_wrote_are_parsed()
    {
        // Copied verbatim from a log file produced by a test run, so the fixture cannot
        // quietly drift into describing a template nobody uses. Note the empty source:
        // Serilog writes "(.)" when neither SourceContext nor Method is set, and a
        // reader that assumed a dotted type name there would match nothing.
        var entries = ClientLogReader.Parse([
            "2026-09-09 02:48:38.000 +00:00 [Information] (.) Environment has been set up. " +
            "AppData anchor: \"/root/.config/Bakabase.Modules.Player.Debugging\""
        ]);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Information", entries[0].Level);
        Assert.AreEqual(".", entries[0].Source);
        Assert.AreEqual("2026-09-09 02:48:38.000 +00:00", entries[0].Timestamp);
        StringAssert.Contains(entries[0].Message, "Environment has been set up");
    }

    [TestMethod]
    public void Nothing_in_is_nothing_out()
    {
        Assert.AreEqual(0, ClientLogReader.Parse([]).Count);
        Assert.AreEqual(0, ClientLogReader.Parse(["", "", ""]).Count);
    }

    [TestMethod]
    public void A_missing_directory_reads_as_empty_rather_than_throwing()
    {
        // A client that has never written a log — the first launch — must not answer
        // the log page with an error.
        Assert.AreEqual(0, ClientLogReader.Files("/no/such/place/at/all").Count);
        Assert.AreEqual(0, ClientLogReader.Read("/no/such/place/at/all", 100).Count);
    }

    [TestMethod]
    public void Rolled_files_are_read_newest_first()
    {
        // The page is a tail across the whole directory, not one file. Reading in
        // directory order would put yesterday's lines above this morning's.
        using var logs = new TempLogs();
        logs.Write("AppLog_20260908.log", DateTime.UtcNow.AddDays(-1), Head + "yesterday");
        logs.Write("AppLog_20260909.log", DateTime.UtcNow, Head + "today");

        var entries = ClientLogReader.Read(logs.Path, 10);

        CollectionAssert.AreEqual(new[] {"today", "yesterday"},
            entries.Select(e => e.Message).ToArray());
    }

    [TestMethod]
    public void A_filter_keeps_looking_in_older_files()
    {
        // The reason to filter by level at all is to find the failure, and the failure is
        // usually not in the newest lines. Stopping once enough *lines* had been read —
        // rather than enough matches — would answer "no errors" with the error one file
        // away.
        using var logs = new TempLogs();
        logs.Write("AppLog_20260908.log", DateTime.UtcNow.AddDays(-1),
            "2026-09-08 03:56:04.123 +08:00 [Error] (Bakabase.Client.Foo) Could not reach the server");
        logs.Write("AppLog_20260909.log", DateTime.UtcNow,
            Enumerable.Range(0, 500).Select(i => Head + $"line {i}").ToArray());

        var errors = ClientLogReader.Read(logs.Path, 100, "Error");

        Assert.AreEqual(1, errors.Count);
        StringAssert.Contains(errors[0].Message, "Could not reach the server");
    }

    [TestMethod]
    public void Take_is_a_ceiling_on_matches()
    {
        using var logs = new TempLogs();
        logs.Write("AppLog_20260909.log", DateTime.UtcNow,
            Enumerable.Range(0, 50).Select(i => Head + $"line {i}").ToArray());

        Assert.AreEqual(5, ClientLogReader.Read(logs.Path, 5).Count);
        Assert.AreEqual(50, ClientLogReader.Read(logs.Path, 500).Count);
    }

    [TestMethod]
    public void A_file_being_written_to_is_still_readable()
    {
        // Serilog holds the current day's file open. Refusing to show the log because it
        // is being written to would make the page useless exactly when it is needed.
        using var logs = new TempLogs();
        var path = Path.Combine(logs.Path, "AppLog_20260909.log");

        using var held = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(held) {AutoFlush = true};
        writer.WriteLine(Head + "written while open");

        var entries = ClientLogReader.Read(logs.Path, 10);

        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("written while open", entries[0].Message);
    }

    /// <summary>A throwaway logs directory shaped like the one the client writes.</summary>
    private sealed class TempLogs : IDisposable
    {
        public string Path { get; } = Directory
            .CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"bakabase-client-log-{Guid.NewGuid():N}"))
            .FullName;

        public void Write(string name, DateTime lastWrite, params string[] lines)
        {
            var file = System.IO.Path.Combine(Path, name);

            File.WriteAllLines(file, lines);
            // Files are ordered by write time, not by name: a rolled file keeps the name
            // it was given and only the timestamp says which is current.
            File.SetLastWriteTimeUtc(file, lastWrite);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A test that left a handle open. Not worth failing the run over.
            }
        }
    }
}
