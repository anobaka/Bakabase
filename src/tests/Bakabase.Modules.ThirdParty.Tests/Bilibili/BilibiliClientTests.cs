using System.Net;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliClientTests
{
    private const string SignedUrl =
        "https://upos-sz-example.bilivideo.com/upgcxcode/00/5001-1-100026-80.m4s?deadline=1900000000&oi=1234567&upsig=00000000000000000000000000000000";

    private static (BilibiliClient Client, FakeBilibiliApi Api, CapturingLoggerFactory Logs) Create(
        Action<FakeBilibiliApi> routes)
    {
        var api = new FakeBilibiliApi();
        routes(api);
        var logs = new CapturingLoggerFactory();
        var client = new BilibiliClient(new StubThirdPartyLocalizer(), new SingleClientFactory(new HttpClient(api)), logs);
        return (client, api, logs);
    }

    private static async Task<TException> Throws<TException>(Func<Task> action) where TException : Exception
    {
        var e = await Assert.ThrowsExceptionAsync<TException>(action);
        BilibiliSecrets.AssertClean(e);
        return e;
    }

    [TestMethod]
    public async Task FavoritesOfASixteenDigitAccount()
    {
        var (client, api, _) = Create(a => a
            .Fixture("/x/space/v2/myinfo", "myinfo-16digit.json")
            .Fixture("/list-all", "favorites-list-all.json"));
        var favorites = await client.GetFavorites();
        CollectionAssert.AreEqual(new long[] {100, 200}, favorites.Select(f => f.Id).ToArray());
        Assert.AreEqual(12, favorites[0].MediaCount);
        Assert.IsTrue(api.Requests.Any(r => r.RequestUri!.Query.Contains("up_mid=3546571234567890")));
    }

    [TestMethod]
    public async Task NoFoldersIsAnEmptyList()
    {
        var (client, _, _) = Create(a => a
            .Fixture("/x/space/v2/myinfo", "myinfo-16digit.json")
            .Fixture("/list-all", "favorites-list-null.json"));
        Assert.AreEqual(0, (await client.GetFavorites()).Count);
    }

    [TestMethod]
    public async Task AnInvalidCookieIsNotLoggedInAndFatal()
    {
        var (client, api, _) = Create(a => a.Fixture("/x/space/v2/myinfo", "myinfo-101.json"));
        var e = await Throws<BilibiliNotLoggedInException>(() => client.GetFavorites());
        Assert.AreEqual(StubThirdPartyLocalizer.CookieInvalid, e.Message);
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
        Assert.AreEqual(1, api.Requests.Count, "no list request without an account");

        var (noProfile, _, _) = Create(a => a.Json("/x/space/v2/myinfo", "{\"code\":0,\"data\":{\"profile\":null}}"));
        await Throws<BilibiliNotLoggedInException>(() => noProfile.GetFavorites());
    }

    [TestMethod]
    public async Task NullMediasBecomeEmpty()
    {
        var (client, _, _) = Create(a => a.Fixture("/fav/resource/list", "fav-items-medias-null.json"));
        var page = await client.GetPostsInFavorites(100, 1, CancellationToken.None);
        Assert.IsNotNull(page.Medias);
        Assert.AreEqual(0, page.Medias.Count);
        Assert.IsFalse(page.HasMore);
    }

    [TestMethod]
    public async Task FavoritesPageItems()
    {
        var (client, api, _) = Create(a => a.Fixture("/fav/resource/list", "fav-items-mixed.json"));
        var page = await client.GetPostsInFavorites(100, 3, CancellationToken.None);
        Assert.AreEqual(13, page.Medias!.Count);
        var query = api.Requests.Single().RequestUri!.Query;
        StringAssert.Contains(query, "media_id=100");
        StringAssert.Contains(query, "pn=3");
        StringAssert.Contains(query, "platform=web");
    }

    [TestMethod]
    public async Task ABusyServiceIsTransient()
    {
        var (client, _, _) = Create(a => a.Fixture("/fav/resource/list", "fav-items-504.json"));
        var e = await Throws<BilibiliTemporarilyUnavailableException>(() =>
            client.GetPostsInFavorites(100, 1, CancellationToken.None));
        Assert.AreEqual(BilibiliTemporaryFailureKind.ServiceBusy, e.Kind);
        Assert.AreEqual(-504, e.Code);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
    }

    [DataTestMethod]
    [DataRow(412, BilibiliTemporaryFailureKind.RiskControl)]
    [DataRow(403, BilibiliTemporaryFailureKind.RiskControl)]
    [DataRow(429, BilibiliTemporaryFailureKind.ServiceBusy)]
    [DataRow(502, BilibiliTemporaryFailureKind.ServiceBusy)]
    public async Task HttpRefusalsFromTheApiAreTransient(int status, BilibiliTemporaryFailureKind kind)
    {
        var (client, _, _) = Create(a => a.Json("/x/web-interface/view", "<html>" + SignedUrl + "</html>",
            (HttpStatusCode) status, "text/html"));
        var e = await Throws<BilibiliTemporarilyUnavailableException>(() => client.GetView(1001, CancellationToken.None));
        Assert.AreEqual(kind, e.Kind);
        Assert.AreEqual(status, e.HttpStatus);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
    }

    [TestMethod]
    public async Task OtherHttpErrorsAreFatal()
    {
        var (client, _, _) = Create(a => a.Json("/x/web-interface/view", "nope", HttpStatusCode.NotFound));
        var e = await Throws<BilibiliProtocolException>(() => client.GetView(1001, CancellationToken.None));
        StringAssert.Contains(e.Message, "HTTP 404");
    }

    [DataTestMethod]
    [DataRow("playurl-352.json")]
    [DataRow("playurl-voucher-code0.json")]
    public async Task RiskControlCodesAndVouchersAreTransient(string fixture)
    {
        var (client, _, _) = Create(a => a.Fixture("/x/player/playurl", fixture));
        var e = await Throws<BilibiliTemporarilyUnavailableException>(() =>
            client.GetPlayUrl(1001, 5001, 127, CancellationToken.None));
        Assert.AreEqual(BilibiliTemporaryFailureKind.RiskControl, e.Kind);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
    }

    [TestMethod]
    public async Task ViewRiskControlWithVoucher()
    {
        var (client, _, logs) = Create(a => a.Fixture("/x/web-interface/view", "view-352-voucher.json"));
        await Throws<BilibiliTemporarilyUnavailableException>(() => client.GetView(1001, CancellationToken.None));
        Assert.IsFalse(logs.Lines.Any(l => l.Contains("voucher_x")), "the captcha token is not logged");
    }

    [TestMethod]
    public async Task NotLoggedInOnAContentEndpointIsFatal()
    {
        var (client, _, _) = Create(a => a.Fixture("/x/web-interface/view", "view-101.json"));
        var e = await Throws<BilibiliNotLoggedInException>(() => client.GetView(1001, CancellationToken.None));
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
    }

    [TestMethod]
    public async Task UnknownCodesAreFatal()
    {
        var (client, _, _) = Create(a => a.Fixture("/x/player/playurl", "playurl-unknown-code.json"));
        var e = await Throws<BilibiliApiException>(() => client.GetPlayUrl(1001, 5001, 127, CancellationToken.None));
        Assert.AreEqual(-12345, e.Code);
        Assert.AreEqual("playurl", e.Endpoint);
        Assert.IsFalse(TransientNetworkError.IsTransient(e));
    }

    [TestMethod]
    public async Task ContentStatesAreReturnedNotThrown()
    {
        var (client, _, _) = Create(a => a
            .Fixture("/x/web-interface/view", "view-62002.json")
            .Fixture("/x/player/playurl", "playurl-87008.json")
            .Fixture("/x/player/pagelist", "pagelist-404.json"));
        Assert.AreEqual(62002, (await client.GetView(1001, CancellationToken.None)).Code);
        Assert.AreEqual(87008, (await client.GetPlayUrl(1001, 5001, 127, CancellationToken.None)).Code);
        Assert.AreEqual(-404, (await client.GetPageList(1001, CancellationToken.None)).Code);
    }

    [TestMethod]
    public async Task ANonJsonBodyIsAProtocolErrorWithoutTheBody()
    {
        var (client, _, logs) = Create(a => a.Json("/x/web-interface/view",
            "<html><a href=\"" + SignedUrl + "\">SESSDATA=" + BilibiliSecrets.CookieSentinel + "</a></html>",
            mediaType: "text/html"));
        var e = await Throws<BilibiliProtocolException>(() => client.GetView(1001, CancellationToken.None));
        Assert.IsNull(e.InnerException);
        Assert.IsFalse(e.Message.Contains("html"));
        foreach (var line in logs.Lines)
        {
            BilibiliSecrets.AssertClean(line.Replace(BilibiliSecrets.CookieSentinel, "").Replace("SESSDATA", ""));
            Assert.IsFalse(line.Contains("?deadline"), line);
        }
    }

    [TestMethod]
    public async Task AnUnexpectedShapeNamesThePathNotTheValue()
    {
        var (client, _, _) = Create(a => a.Json("/x/player/playurl",
            "{\"code\":0,\"message\":\"0\",\"data\":{\"timelength\":\"" + SignedUrl + "\"}}"));
        var e = await Throws<BilibiliProtocolException>(() => client.GetPlayUrl(1001, 5001, 127, CancellationToken.None));
        StringAssert.Contains(e.Message, "timelength");
        Assert.IsNull(e.InnerException);
    }

    [TestMethod]
    public async Task MissingOrNonNumericCodesAreProtocolErrors()
    {
        var (noCode, _, _) = Create(a => a.Json("/x/web-interface/view", "{\"data\":{}}"));
        await Throws<BilibiliProtocolException>(() => noCode.GetView(1001, CancellationToken.None));
        var (badCode, _, _) = Create(a => a.Json("/x/web-interface/view", "{\"code\":\"abc\"}"));
        await Throws<BilibiliProtocolException>(() => badCode.GetView(1001, CancellationToken.None));
        var (array, _, _) = Create(a => a.Json("/x/web-interface/view", "[1,2]"));
        await Throws<BilibiliProtocolException>(() => array.GetView(1001, CancellationToken.None));
    }

    [TestMethod]
    public async Task TheNamingRequestUsesTheLegacyUrlExactly()
    {
        var (client, api, _) = Create(a => a.Fixture("/x/player/playurl", "playurl-16-naming.json"));
        var naming = await client.GetLegacyNamingSource(1001, 5001, CancellationToken.None);
        Assert.AreEqual(0, naming.Code);
        Assert.AreEqual(
            "https://api.bilibili.com/x/player/playurl?avid=1001&cid=5001&bvid=&qn=16&type=&otype=json&fourk=1&fnval=16",
            api.Requests.Single().RequestUri!.OriginalString);
    }

    [TestMethod]
    public async Task EndpointUrls()
    {
        var (client, api, _) = Create(a => a
            .Fixture("/x/player/playurl", "playurl-dash.json")
            .Fixture("/x/v2/dm/view", "dmview-subs.json")
            .Fixture("/x/web-interface/view", "view-ok-2pages.json")
            .Fixture("/x/player/pagelist", "pagelist-ok.json"));
        await client.GetPlayUrl(1001, 5001, 127, CancellationToken.None);
        await client.GetDmView(1001, 5001, CancellationToken.None);
        await client.GetView(1001, CancellationToken.None);
        await client.GetPageList(1001, CancellationToken.None);
        CollectionAssert.AreEqual(new[]
        {
            "https://api.bilibili.com/x/player/playurl?avid=1001&cid=5001&qn=127&fnval=4048&fnver=0&fourk=1&otype=json",
            "https://api.bilibili.com/x/v2/dm/view?type=1&oid=5001&pid=1001",
            "https://api.bilibili.com/x/web-interface/view?aid=1001",
            "https://api.bilibili.com/x/player/pagelist?aid=1001",
        }, api.Requests.Select(r => r.RequestUri!.OriginalString).ToArray());
    }

    [TestMethod]
    public async Task DmViewModel()
    {
        var (client, _, _) = Create(a => a.Fixture("/x/v2/dm/view", "dmview-null.json"));
        var dm = await client.GetDmView(1001, 5001, CancellationToken.None);
        Assert.AreEqual(0, dm.Code);
        Assert.IsNull(dm.Data!.Subtitle);
    }

    [TestMethod]
    public async Task CancellationIsHonoured()
    {
        var (client, _, _) = Create(a => a.Fixture("/x/web-interface/view", "view-ok-2pages.json"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try
        {
            await client.GetView(1001, cts.Token);
            Assert.Fail("a cancelled request must not complete");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    public void ApiDefaultsAreAddedOnlyWhenAbsent()
    {
        var bare = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/x/web-interface/view?aid=1");
        BilibiliRequestDefaults.ApplyApiDefaults(bare);
        BilibiliRequestDefaults.ApplyApiDefaults(bare);
        Assert.AreEqual("https://www.bilibili.com/", bare.Headers.Referrer!.ToString());
        Assert.AreEqual("https://www.bilibili.com", bare.Headers.GetValues("Origin").Single());

        var configured = new HttpRequestMessage(HttpMethod.Get, "https://api.bilibili.com/");
        configured.Headers.Add("Referer", "https://space.bilibili.com/");
        configured.Headers.Add("Origin", "https://space.bilibili.com");
        BilibiliRequestDefaults.ApplyApiDefaults(configured);
        Assert.AreEqual("https://space.bilibili.com/", configured.Headers.Referrer!.ToString());
        Assert.AreEqual("https://space.bilibili.com", configured.Headers.GetValues("Origin").Single());
    }
}
