using System;
using System.IO;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.Tests;

[TestClass]
public class DiskWriteExceptionTests
{
    [TestMethod]
    [DataRow(unchecked((int) 0x80070070), DisplayName = "ERROR_DISK_FULL")]
    [DataRow(unchecked((int) 0x80070027), DisplayName = "ERROR_HANDLE_DISK_FULL")]
    [DataRow(28, DisplayName = "ENOSPC")]
    public void DiskFullHResults(int hresult)
    {
        var e = DiskWriteException.From("/data/v.m4s.part", new IOException("write failed", hresult));
        Assert.IsTrue(e.IsDiskFull);
        Assert.AreEqual("/data/v.m4s.part", e.Path);
    }

    [TestMethod]
    public void OtherWriteErrors_AreNotDiskFull_ButStillFatal()
    {
        var e = DiskWriteException.From("/data/v.m4s.part", new IOException("The process cannot access the file."));
        Assert.IsFalse(e.IsDiskFull);
        Assert.IsInstanceOfType<IUserActionableException>(e);
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
        StringAssert.Contains(e.Message, "The process cannot access the file.");
    }

    [TestMethod]
    public void FromKeepsAnExistingDiskWriteException()
    {
        var original = new DiskWriteException("/a", true);
        Assert.AreSame(original, DiskWriteException.From("/b", original));
    }

    [TestMethod]
    [DataRow("av_interleaved_write_frame(): No space left on device", true)]
    [DataRow("There is not enough space on the disk.", true)]
    [DataRow("Could not find tag for codec flac", false)]
    [DataRow(null, false)]
    public void DiskFullMessages(string? text, bool expected)
    {
        Assert.AreEqual(expected, DiskWriteException.IsDiskFullMessage(text));
    }

    [TestMethod]
    public void IsDiskFullError_WalksInnerExceptions()
    {
        Assert.IsTrue(DiskWriteException.IsDiskFullError(
            new InvalidOperationException("outer", new IOException("x", unchecked((int) 0x80070070)))));
        Assert.IsFalse(DiskWriteException.IsDiskFullError(new InvalidOperationException("outer")));
    }
}
