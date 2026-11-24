using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace Bakabase.InsideWorld.Business.Components.Artifacts;

/// <summary>Optional Linux process counters for commands with a known local payload destination.</summary>
public static class LocalFileProcessIo
{
    internal static async Task<T> AwaitAsync<T>(int pid, Task<T> command, bool write)
    {
        var observe = ObserveArchiveAsync(pid, command, write);
        try { return await command.ConfigureAwait(false); }
        finally { await observe.ConfigureAwait(false); }
    }

    internal static Task ObserveArchiveAsync(int pid, Task completion, bool write) => ObserveAsync(pid, completion, write, false);

    /// <summary>Estimate only kernel-accounted writes from one direct-to-file child. A network
    /// process can read network and pipe bytes, so its logical counters are not used here.
    /// Optional activity only reports increases after the first sample from the live process.</summary>
    public static Task ObserveStorageWritesAsync(int pid, Task completion, Action? onWrite = null) =>
        ObserveAsync(pid, completion, true, true, onWrite);

    private static async Task ObserveAsync(int pid, Task completion, bool write, bool storageWritesOnly,
        Action? onWrite = null)
    {
        if (!OperatingSystem.IsLinux() || !Environment.Is64BitProcess ||
            (!FileIoObservationScope.Active && onWrite == null) || completion.IsCompleted) return;
        try
        {
            // Holding the proc descriptor keeps later samples bound to this process even if its PID is reused.
            using var descriptor = File.OpenHandle($"/proc/{pid}/io", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (completion.IsCompleted) return;
            var buffer = new byte[1024];
            var snapshot = ReadCounters(descriptor, buffer, storageWritesOnly);
            if (snapshot == null) return;
            using var counter = FileIoObservationScope.Begin(read: !storageWritesOnly, write, externalProcess: true);
            long previousRead = 0, previousWritten = 0;
            var firstSample = true;
            while (snapshot is { } current && current.Read >= previousRead && current.Written >= previousWritten)
            {
                counter?.Add(current.Read - previousRead, write ? current.Written - previousWritten : 0);
                if (!firstSample && current.Written > previousWritten && !completion.IsCompleted) onWrite?.Invoke();
                firstSample = false;
                (previousRead, previousWritten) = current;
                if (completion.IsCompleted) break;
                await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
                snapshot = ReadCounters(descriptor, buffer, storageWritesOnly);
            }
        }
        catch (IOException) { /* Exiting child or unavailable proc I/O. */ }
        catch (UnauthorizedAccessException) { /* Optional diagnostics cannot change extraction. */ }
    }

    private static (long Read, long Written)? ReadCounters(SafeFileHandle descriptor, byte[] buffer, bool storageWritesOnly)
    {
        // proc seq_file needs the real file offset rewound, which a managed FileStream
        // seek and positional read can leave unchanged on the kernel descriptor.
        if (Seek(descriptor, 0, 0) < 0) return null;
        var length = Read(descriptor, buffer, (nuint)buffer.Length);
        if (length <= 0) return null;
        using var reader = new StringReader(Encoding.ASCII.GetString(buffer, 0, (int)length));
        return ReadCounters(reader, storageWritesOnly);
    }

    internal static (long Read, long Written)? ReadCounters(TextReader reader, bool storageWritesOnly = false)
    {
        const string readKey = "rchar:";
        var writtenKey = storageWritesOnly ? "write_bytes:" : "wchar:";
        // Archive commands run only against local files. Their standard streams carry tool
        // messages instead of payload, but these Linux logical counters are still approximate.
        long? read = storageWritesOnly ? 0 : null, written = null;
        while (reader.ReadLine() is { } line)
        {
            if (!storageWritesOnly && line.StartsWith(readKey, StringComparison.Ordinal) && TryReadValue(line, readKey.Length, out var r)) read = r;
            if (line.StartsWith(writtenKey, StringComparison.Ordinal) && TryReadValue(line, writtenKey.Length, out var w)) written = w;
            if (read.HasValue && written.HasValue) return (read.Value, written.Value);
        }
        return null;
    }

    private static bool TryReadValue(string line, int offset, out long value) =>
        long.TryParse(line.AsSpan(offset).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    [DllImport("libc", EntryPoint = "lseek")]
    private static extern long Seek(SafeFileHandle file, long offset, int origin);

    [DllImport("libc", EntryPoint = "read")]
    private static extern nint Read(SafeFileHandle file, [Out] byte[] buffer, nuint length);
}
