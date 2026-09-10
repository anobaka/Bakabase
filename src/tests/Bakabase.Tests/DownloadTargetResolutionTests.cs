using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Tests;

/// <summary>
/// Reading a link to decide which downloader is meant.
/// <para>
/// This is what lets a chain end in "download it" rather than in "download it from ExHentai". The
/// thing to get right is the refusal: a link nobody here can fetch has to say so, because a task
/// queued against the wrong downloader sits there failing rather than telling anyone.
/// </para>
/// </summary>
[TestClass]
public sealed class DownloadTargetResolutionTests
{
    [TestMethod]
    public void AGalleryLinkIsOneWork()
    {
        foreach (var url in new[]
                 {
                     "https://exhentai.org/g/1234567/abcdef0123",
                     "https://e-hentai.org/g/1234567/abcdef0123/",
                 })
        {
            Assert.IsTrue(DownloadTargetResolver.TryResolve(url, out var target), url);
            Assert.AreEqual(ThirdPartyId.ExHentai, target!.ThirdPartyId);
            Assert.AreEqual((int) ExHentaiDownloadTaskType.SingleWork, target.TaskType);
            Assert.AreEqual(url, target.Key,
                "the token is part of what identifies a gallery, so the link goes through as written");
        }
    }

    /// <summary>
    /// Anything else on the site is a listing — a search, a favourites page, a tag. The list
    /// downloader walks it page by page, and its query is the whole of what it means.
    /// </summary>
    [TestMethod]
    public void AnythingElseOnTheSiteIsAListing()
    {
        const string url = "https://exhentai.org/?f_search=some+artist";

        Assert.IsTrue(DownloadTargetResolver.TryResolve(url, out var target));
        Assert.AreEqual((int) ExHentaiDownloadTaskType.List, target!.TaskType);
        Assert.AreEqual(url, target.Key);
    }

    /// <summary>
    /// The important half. Fanbox, Fantia, Ci-en and Patreon have downloader classes whose task
    /// bodies are still to be written; resolving their links would queue tasks that never move,
    /// which reads as a broken download rather than an absent one.
    /// </summary>
    [TestMethod]
    public void ALinkNobodyCanFetchIsRefused()
    {
        foreach (var url in new[]
                 {
                     "https://www.fanbox.cc/@somebody/posts/1234567",
                     "https://fantia.jp/posts/1234567",
                     "https://pan.example/s/1AbC",
                     "https://store.steampowered.com/app/440",
                 })
        {
            Assert.IsFalse(DownloadTargetResolver.TryResolve(url, out _), url);
        }
    }

    [TestMethod]
    public void SomethingThatIsNotALinkIsNotALink()
    {
        Assert.IsFalse(DownloadTargetResolver.TryResolve(null, out _));
        Assert.IsFalse(DownloadTargetResolver.TryResolve("   ", out _));
        Assert.IsFalse(DownloadTargetResolver.TryResolve("RJ01234567", out _),
            "a work id names a work, not a way of getting it");
    }

    /// <summary>
    /// A near-miss host must not be read as the real one — the pattern anchors the host rather than
    /// searching the whole string for it.
    /// </summary>
    [TestMethod]
    public void ALookalikeHostIsNotTheSite()
    {
        Assert.IsFalse(DownloadTargetResolver.TryResolve("https://exhentai.org.example.com/?f_search=x",
            out _));

        Assert.IsFalse(
            DownloadTargetResolver.TryResolve(
                "https://example.com/redirect?u=https://exhentai.org/g/1234567/abcdef0123", out _),
            "a link that merely quotes a gallery address is not that gallery");
    }
}
