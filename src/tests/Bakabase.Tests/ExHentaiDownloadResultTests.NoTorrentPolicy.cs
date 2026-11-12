using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;

namespace Bakabase.Tests;

public sealed partial class ExHentaiDownloadResultTests
{
    [DataTestMethod]
    [DataRow(ExHentaiDownloadMode.TorrentFile, true)]
    [DataRow(ExHentaiDownloadMode.TorrentFile, false)]
    [DataRow(ExHentaiDownloadMode.Torrent, true)]
    [DataRow(ExHentaiDownloadMode.Torrent, false)]
    [DataRow(ExHentaiDownloadMode.Direct, false)]
    public async Task ExplicitNoTorrentFallback_ProducesReusableFilesWithoutCreatingBtWork(
        ExHentaiDownloadMode mode, bool probeTorrent)
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler);
        async Task Run() => await RunProducer(producer, "https://exhentai.org/g/12345/abcdef0123/",
            _ => Task.CompletedTask, preferTorrent: probeTorrent, resultWorkflowId: null,
            downloadMode: mode, fallbackToDirect: false, noTorrentAction: ExHentaiNoTorrentAction.DirectDownload);
        await Run();
        var result = (await _results.GetByTaskAsync(10)).Single();
        Assert.AreEqual(DownloadResultKind.LocalFiles, result.Kind);
        Assert.IsFalse(result.RequiresContentsDownload);
        Assert.AreEqual(1, handler.ImageRequests);
        Assert.AreEqual(0, handler.TorrentRequests);
        handler.RejectRequests = true;
        await Run();
        Assert.AreEqual(1, (await _results.GetByTaskAsync(10)).Count);
        Assert.AreEqual(1, handler.ImageRequests, "A resumed fallback must reuse its verified final files.");
    }

    [TestMethod]
    public async Task ExplicitFallbackDoesNotTreatMissingAdvertisedTorrentLinksAsNoTorrent()
    {
        var handler = new GalleryHandler {TorrentBytes = _metadata, EmptyTorrentList = true};
        using var producer = await BuildProducer(handler);
        await Assert.ThrowsExceptionAsync<Exception>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            downloadMode: ExHentaiDownloadMode.TorrentFile, noTorrentAction: ExHentaiNoTorrentAction.DirectDownload));
        Assert.AreEqual(0, handler.ImageRequests);
        Assert.AreEqual(0, (await _results.GetByTaskAsync(10)).Count);
    }

    [TestMethod]
    public async Task ExplicitFailRetainsRestrictedBehaviorEvenWithLegacyFallbackEnabled()
    {
        var handler = new GalleryHandler();
        using var producer = await BuildProducer(handler);
        await Assert.ThrowsExceptionAsync<DownloadPolicyException>(() => RunProducer(producer,
            "https://exhentai.org/g/12345/abcdef0123/", _ => Task.CompletedTask,
            downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: true, noTorrentAction: ExHentaiNoTorrentAction.Fail));
        Assert.AreEqual(0, handler.ImageRequests);
    }

    [TestMethod]
    public void ExplicitLegacyFallbackOptOutOverridesNewGlobalDefault()
    {
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
            "{\"downloadMode\":2,\"fallbackToDirect\":false}", true, null,
            noTorrentActionDefault: ExHentaiNoTorrentAction.DirectDownload), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiNoTorrentAction.Fail, options.NoTorrentAction);
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, options.EffectiveDownloadMode);
    }

    [TestMethod]
    public void ExplicitNewFallbackPolicyOverridesLegacyFlag()
    {
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
            "{\"downloadMode\":2,\"fallbackToDirect\":false,\"noTorrentAction\":1}", true, null,
            noTorrentActionDefault: ExHentaiNoTorrentAction.Fail), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiNoTorrentAction.DirectDownload, options.NoTorrentAction);
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, options.EffectiveDownloadMode);
    }

    [TestMethod]
    public void ExplicitLegacyPreferTorrentKeepsSeedOnlyInsteadOfStartingBtContents()
    {
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
            "{\"preferTorrent\":true}", true, null, ExHentaiDownloadMode.Torrent,
            noTorrentActionDefault: ExHentaiNoTorrentAction.DirectDownload), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, options.EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiNoTorrentAction.Fail, options.NoTorrentAction);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CopiedSeedOnlySnapshotDoesNotAdoptFallbackUnlessExplicitlyRequested(bool adoptNewPolicy)
    {
        var raw = adoptNewPolicy
            ? "{\"preferTorrent\":true,\"downloadMode\":3,\"fallbackToDirect\":true,\"noTorrentAction\":1}"
            : "{\"preferTorrent\":true,\"downloadMode\":3,\"fallbackToDirect\":true}";
        var options = JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
            raw, true, null, noTorrentActionDefault: ExHentaiNoTorrentAction.DirectDownload), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, options.EffectiveDownloadMode);
        Assert.AreEqual(adoptNewPolicy ? ExHentaiNoTorrentAction.DirectDownload : ExHentaiNoTorrentAction.Fail,
            options.NoTorrentAction);
    }

    [TestMethod]
    public void NewTaskDefaultsAreExplicitAndDoNotRewriteExistingSnapshots()
    {
        static ExHentaiTaskOptions Create(string? raw = null, ExHentaiDownloadMode? mode = null) =>
            JsonSerializer.Deserialize<ExHentaiTaskOptions>(ExHentaiDownloaderHelper.ApplyDefaultTaskOptions(
                raw, true, null, mode), JsonSerializerOptions.Web)!;
        Assert.AreEqual(ExHentaiDownloadMode.Torrent, Create().EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiNoTorrentAction.DirectDownload, Create().NoTorrentAction);
        Assert.IsFalse(Create().SaveTorrentFile);
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, Create(mode: ExHentaiDownloadMode.TorrentFile).EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiNoTorrentAction.DirectDownload, Create(mode: ExHentaiDownloadMode.TorrentFile).NoTorrentAction);
        Assert.AreEqual(ExHentaiDownloadMode.Direct, Create("{\"preferTorrent\":false}").EffectiveDownloadMode);
        Assert.AreEqual(ExHentaiNoTorrentAction.Fail, Create("{\"noTorrentAction\":2}").NoTorrentAction);
        var existing = JsonSerializer.Deserialize<ExHentaiTaskOptions>(
            "{\"downloadMode\":3,\"fallbackToDirect\":true}", JsonSerializerOptions.Web)!;
        Assert.IsNull(existing.NoTorrentAction);
        Assert.AreEqual(ExHentaiDownloadMode.TorrentFile, existing.EffectiveDownloadMode);
    }
}
