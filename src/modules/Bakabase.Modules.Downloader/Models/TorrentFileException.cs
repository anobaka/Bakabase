using Bakabase.Abstractions.Components.FileSystem;
using MonoTorrent.Client;

namespace Bakabase.Modules.Downloader.Models;

/// <summary>A filesystem error reported by the torrent engine, safe to persist without its paths or URLs.</summary>
public sealed class TorrentFileException : IOException
{
    internal TorrentFileException(Error error) : base(Classify(error)) {}

    private static string Classify(Error error)
    {
        for (Exception? cause = error.Exception; cause != null; cause = cause.InnerException)
        {
            if (cause is FileNotFoundException or DirectoryNotFoundException) return "BT_LOCAL_FILE_MISSING";
            if (cause is IOException && (cause.HResult == unchecked((int)0x80070013) ||
                !OperatingSystem.IsWindows() && cause.HResult == 30)) return "BT_LOCAL_DISK_READ_ONLY";
        }
        return (error.Exception == null ? null : LocalStorageFailures.Code(error.Exception)) ??
               (error.Reason == Reason.ReadFailure ? "BT_LOCAL_FILE_READ_FAILED" : "BT_LOCAL_FILE_WRITE_FAILED");
    }
}
