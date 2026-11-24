using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>Linux logical I/O estimate for a child dedicated to one local archive.</summary>
internal static class ArchiveProcessIo
{
    internal static async Task<T> AwaitAsync<T>(int pid, Task<T> command, bool write)
    {
        var observe = ObserveAsync(pid, command, write);
        try { return await command.ConfigureAwait(false); }
        finally { await observe.ConfigureAwait(false); }
    }

    internal static async Task ObserveAsync(int pid, Task completion, bool write)
    {
        if (!OperatingSystem.IsLinux() || !FileIoObservationScope.Active || completion.IsCompleted) return;
        try
        {
            // Holding the proc descriptor keeps later samples bound to this process even if its PID is reused.
            using var stream = new FileStream($"/proc/{pid}/io", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (completion.IsCompleted) return;
            using var reader = new StreamReader(stream);
            var snapshot = ReadCounters(reader);
            if (snapshot == null) return;
            using var counter = FileIoObservationScope.Begin(read: true, write, externalProcess: true);
            long previousRead = 0, previousWritten = 0;
            while (snapshot is { } current && current.Read >= previousRead && current.Written >= previousWritten)
            {
                // rchar/wchar include tool messages as well as local bytes. This estimate is limited to
                // local archive commands, whose standard streams do not carry the archive payload.
                counter?.Add(current.Read - previousRead, write ? current.Written - previousWritten : 0);
                (previousRead, previousWritten) = current;
                if (completion.IsCompleted) break;
                await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                reader.DiscardBufferedData();
                stream.Position = 0;
                snapshot = ReadCounters(reader);
            }
        }
        catch (IOException) { /* Exiting child or unavailable proc I/O. */ }
        catch (UnauthorizedAccessException) { /* Optional diagnostics cannot change extraction. */ }
    }

    internal static (long Read, long Written)? ReadCounters(TextReader reader)
    {
        const string readKey = "rchar:";
        const string writtenKey = "wchar:";
        long? read = null, written = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith(readKey, StringComparison.Ordinal) && TryReadValue(line, readKey.Length, out var r)) read = r;
            if (line.StartsWith(writtenKey, StringComparison.Ordinal) && TryReadValue(line, writtenKey.Length, out var w)) written = w;
            if (read.HasValue && written.HasValue) return (read.Value, written.Value);
        }
        return null;
    }

    private static bool TryReadValue(string line, int offset, out long value) =>
        long.TryParse(line.AsSpan(offset).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
