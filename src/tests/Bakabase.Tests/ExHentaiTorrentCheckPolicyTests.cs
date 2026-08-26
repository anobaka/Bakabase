using System;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ExHentaiTorrentCheckPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Local);

    [TestMethod]
    public void NeverChecked_IsNotFresh()
    {
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(null, 24, Now));
    }

    [TestMethod]
    public void NoValidityConfigured_KeepsProbingEveryTime()
    {
        // Null is the default, and must preserve the previous always-probe behaviour.
        Assert.IsFalse(
            ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddMinutes(-1), null, Now));
    }

    [TestMethod]
    public void ZeroOrNegativeValidity_DisablesCaching()
    {
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddMinutes(-1), 0, Now));
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddMinutes(-1), -5, Now));
    }

    [TestMethod]
    public void WithinWindow_IsFresh()
    {
        Assert.IsTrue(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddHours(-23), 24, Now));
    }

    [TestMethod]
    public void ExactlyAtWindow_IsExpired()
    {
        // Boundary is exclusive: a verdict exactly as old as the window is re-probed.
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddHours(-24), 24, Now));
    }

    [TestMethod]
    public void BeyondWindow_IsExpired()
    {
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddHours(-25), 24, Now));
    }

    [TestMethod]
    public void FutureTimestamp_IsRejected()
    {
        // A verdict stamped in the future means the clock moved backwards; trusting it would
        // suppress probing until the clock caught up.
        Assert.IsFalse(ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(Now.AddHours(1), 24, Now));
    }
}
