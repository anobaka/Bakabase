using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class RemoteTranscodePolicyTests
{
    private static RemoteAccessContext Context(bool loopback, RemoteAccessMode mode) =>
        new() {IsLoopback = loopback, Mode = mode};

    [TestMethod]
    public void LoopbackCallers_AreNeverRefused()
    {
        Assert.IsFalse(RemoteTranscodePolicy.ShouldRefuse(
            Context(loopback: true, RemoteAccessMode.Enabled), allowLiveTranscode: false));
    }

    [TestMethod]
    public void UnrestrictedMode_IsNeverRefused()
    {
        // The remote browser belongs to the operator there — Docker's default.
        Assert.IsFalse(RemoteTranscodePolicy.ShouldRefuse(
            Context(loopback: false, RemoteAccessMode.Unrestricted), allowLiveTranscode: false));
    }

    [TestMethod]
    public void RemoteCallers_AreRefused_UntilTheHostOptsIn()
    {
        var remote = Context(loopback: false, RemoteAccessMode.Enabled);

        Assert.IsTrue(RemoteTranscodePolicy.ShouldRefuse(remote, allowLiveTranscode: false));
        Assert.IsFalse(RemoteTranscodePolicy.ShouldRefuse(remote, allowLiveTranscode: true));
    }

    [TestMethod]
    public void AMissingContext_MeansAnInProcessCall_AndIsNotRefused()
    {
        Assert.IsFalse(RemoteTranscodePolicy.ShouldRefuse(null, allowLiveTranscode: false));
    }
}
