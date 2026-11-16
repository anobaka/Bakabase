using Bakabase.Modules.Downloader.Models;
using MonoTorrent.Client;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public class TorrentFileExceptionTests
{
    [DataTestMethod]
    [DataRow("missing", "BT_LOCAL_FILE_MISSING")]
    [DataRow("directory", "BT_LOCAL_FILE_MISSING")]
    [DataRow("readOnly", "BT_LOCAL_DISK_READ_ONLY")]
    [DataRow("full", "LOCAL_STORAGE_FULL")]
    [DataRow("denied", "LOCAL_STORAGE_ACCESS_DENIED")]
    [DataRow("read", "BT_LOCAL_FILE_READ_FAILED")]
    [DataRow("write", "BT_LOCAL_FILE_WRITE_FAILED")]
    public void UsesEngineOperationAndTypedCauseWithoutLeakingDetails(string mode, string expected)
    {
        const string secret = "LOCAL_STORAGE_FULL: /private/name https://user:secret@tracker.invalid";
        Exception cause = mode switch
        {
            "missing" => new FileNotFoundException(secret),
            "directory" => new DirectoryNotFoundException(secret),
            "readOnly" => new IOException(secret, unchecked((int)0x80070013)),
            "full" => new IOException(secret, unchecked((int)0x80070070)),
            "denied" => new UnauthorizedAccessException(secret),
            _ => new IOException(secret)
        };
        var reason = mode == "write" ? Reason.WriteFailure : Reason.ReadFailure;
        var safe = new TorrentFileException(new Error(reason, new IOException(secret, cause)));
        Assert.AreEqual(expected, safe.Message);
        Assert.IsNull(safe.InnerException);
    }

    [TestMethod]
    public void PosixReadOnlyErrnoIsOnlyRecognizedOnUnix()
    {
        var error = new Error(Reason.WriteFailure, new IOException("untrusted", 30));
        Assert.AreEqual(OperatingSystem.IsWindows() ? "BT_LOCAL_FILE_WRITE_FAILED" : "BT_LOCAL_DISK_READ_ONLY",
            new TorrentFileException(error).Message);
    }
}
