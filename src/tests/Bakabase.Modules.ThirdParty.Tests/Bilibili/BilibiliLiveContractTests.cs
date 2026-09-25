using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

/// <summary>
/// Checks the decision tables against the live API (anonymously). Run it before blaming Bilibili for a change in
/// behaviour, and after touching anything in <c>Protocol/</c>. The samples are the research's evidence ids.
/// </summary>
[TestClass]
[Ignore("Manual: verifies the live Bilibili API still matches the rules.")]
public class BilibiliLiveContractTests
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", InternalOptions.DefaultHttpUserAgent);
        http.DefaultRequestHeaders.Referrer = new Uri(BilibiliRequestDefaults.Referer);
        return http;
    }

    private static BilibiliClient Client() =>
        new(new StubThirdPartyLocalizer(), new SingleClientFactory(Http), NullLoggerFactory.Instance);

    private static async Task<long> Aid(string bvid)
    {
        var json = JObject.Parse(await Http.GetStringAsync($"https://api.bilibili.com/x/web-interface/view?bvid={bvid}"));
        return json["data"]!.Value<long>("aid");
    }

    private static async Task<(Post View, long Cid)> View(long aid, int part = 1)
    {
        var view = await Client().GetView(aid, CancellationToken.None);
        Assert.AreEqual(BilibiliViewOutcomeKind.Proceed, BilibiliArchiveRules.DecideView(view, aid, 0).Kind);
        return (view.Data!, view.Data!.Pages![part - 1].Cid);
    }

    private static async Task<BilibiliPlayUrlOutcome> PlayUrl(long aid, long cid, bool pgc = false) =>
        BilibiliPlayUrlRules.Decide(
            await Client().GetPlayUrl(aid, cid, BiliBiliApiUrls.DefaultPlayQn, CancellationToken.None), pgc);

    [TestMethod]
    public async Task DurlOnlyArchive()
    {
        var aid = await Aid("BV1nx411u79K");
        var (_, cid) = await View(aid);
        Assert.AreEqual(BilibiliPlayUrlOutcomeKind.Durl, (await PlayUrl(aid, cid)).Kind);
    }

    [TestMethod]
    public async Task DashWithSubtitles()
    {
        var aid = await Aid("BV1GJ411x7h7");
        var (_, cid) = await View(aid);
        Assert.AreEqual(BilibiliPlayUrlOutcomeKind.Dash, (await PlayUrl(aid, cid)).Kind);
        var dm = await Client().GetDmView(aid, cid, CancellationToken.None);
        Assert.IsTrue(BilibiliCaptions.PlanSubtitles(dm.Data?.Subtitle?.Subtitles).Count > 0);
    }

    [TestMethod]
    public async Task LoginOnlyLegacyArchiveFallsBackToPageList()
    {
        var view = await Client().GetView(7903418, CancellationToken.None);
        Assert.AreEqual(BilibiliViewOutcomeKind.PageListFallback, BilibiliArchiveRules.DecideView(view, 7903418, 0).Kind);
        var (pages, skip) = BilibiliArchiveRules.DecidePageListFallback(
            await Client().GetPageList(7903418, CancellationToken.None));
        Assert.IsNull(skip);
        Assert.IsTrue(pages!.Count > 0);
    }

    [TestMethod]
    public async Task SupporterOnlyWithPreview()
    {
        var aid = await Aid("BV1HxXwYEEqt");
        var (view, cid) = await View(aid);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnlyPreview, BilibiliArchiveRules.GateAccess(view)?.Reason);
        Assert.AreEqual(BilibiliSkipReason.PreviewOnly, (await PlayUrl(aid, cid)).Skip?.Reason);
    }

    [TestMethod]
    public async Task SupporterOnlyWithoutPreview()
    {
        var aid = await Aid("BV1bS4cekEFy");
        var (_, cid) = await View(aid);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnly, (await PlayUrl(aid, cid)).Skip?.Reason);
    }

    [TestMethod]
    public async Task Deleted()
    {
        var o = BilibiliArchiveRules.DecideView(await Client().GetView(959106569, CancellationToken.None), 959106569, 0);
        Assert.AreEqual(BilibiliSkipReason.Deleted, o.Skip?.Reason);
    }

    [TestMethod]
    public async Task RegionRestricted()
    {
        var o = BilibiliArchiveRules.DecideView(await Client().GetView(70867620, CancellationToken.None), 70867620, 0);
        Assert.AreEqual(BilibiliViewOutcomeKind.CheckExistence, o.Kind);
        Assert.AreEqual(BilibiliSkipReason.RegionRestrictedOrHidden, BilibiliArchiveRules.DecideAfterNotFound(
            await Client().GetPageList(70867620, CancellationToken.None)).Reason);
    }

    [TestMethod]
    public async Task PgcRedirectPage()
    {
        var (view, cid) = await View(710444604, part: 2);
        Assert.IsTrue(BilibiliArchiveRules.IsPgcRedirect(view.RedirectUrl));
        Assert.AreEqual(BilibiliSkipReason.PgcEpisodeNotSupported, (await PlayUrl(710444604, cid, true)).Skip?.Reason);
    }

    /// <summary>
    /// Evidence for dropping the naming request in phase 2: the legacy name equals the best 4048 format that the
    /// legacy request could have listed.
    /// </summary>
    [DataTestMethod]
    [DataRow("BV1GJ411x7h7")]
    [DataRow("BV1nx411u79K")]
    public async Task NamingParity(string bvid)
    {
        var aid = await Aid(bvid);
        var (_, cid) = await View(aid);
        var legacy = await Client().GetLegacyNamingSource(aid, cid, CancellationToken.None);
        var modern = await Client().GetPlayUrl(aid, cid, BiliBiliApiUrls.DefaultPlayQn, CancellationToken.None);
        int[] notInLegacy = [125, 126, 127, 129];
        Assert.AreEqual(BilibiliQualityNaming.LegacyQualityName(legacy.Data!.SupportFormats),
            BilibiliQualityNaming.LegacyQualityName(modern.Data!.SupportFormats?.Where(f => !notInLegacy.Contains(f.Quality))));
    }
}
