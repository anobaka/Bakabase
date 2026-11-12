using System.Diagnostics;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.TestKit.Implementations;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduBridgeProxyTests
{
    private static readonly string[] ProxyEnvironment =
        ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy"];

    private static NetworkOptions.ProxyModel Custom(string id) => new()
        {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = id};

    private static NetworkOptions Settings() => new()
    {
        Proxy = Custom("global"),
        ThirdPartyProxies = new() {[(int) ThirdPartyId.BaiduNetdisk] = Custom("source")},
        CustomProxies =
        [
            new() {Id = "global", Address = "http://global.invalid:8080"},
            new() {Id = "source", Address = "http://source.invalid:8081"},
            new() {Id = "tool", Address = "socks5://tool.invalid:1080"}
        ]
    };

    [TestMethod]
    public void BaiduBridgeUsesLiveToolThenSourceThenGlobalSelectionForBothHttpAndHttps()
    {
        var options = Settings();
        var resolver = new BakabaseWebProxy(new TestBOptions<NetworkOptions>(options));
        var start = new ProcessStartInfo("bridge");
        start.ArgumentList.Add("fixture-argument");
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.BaiduPcsGo] = Custom("tool")};
        BaiduPcsBridgeClient.ConfigureProxy(start, resolver);
        AssertRouting(start, "socks5://tool.invalid:1080/");

        options.DownloadToolProxies.Clear();
        BaiduPcsBridgeClient.ConfigureProxy(start, resolver);
        AssertRouting(start, "http://source.invalid:8081/");

        options.ThirdPartyProxies!.Clear();
        BaiduPcsBridgeClient.ConfigureProxy(start, resolver);
        AssertRouting(start, "http://global.invalid:8080/");
        CollectionAssert.AreEqual(new[] {"fixture-argument"}, start.ArgumentList.ToArray());
    }

    [DataTestMethod]
    [DataRow("tool")]
    [DataRow("source")]
    [DataRow("global")]
    public void ExplicitDirectAtTheEffectiveScopeClearsAmbientProxyAndBypassVariables(string scope)
    {
        var options = Settings();
        var direct = new NetworkOptions.ProxyModel {Mode = NetworkOptions.ProxyMode.DoNotUse};
        if (scope == "tool") options.DownloadToolProxies = new() {[DownloadToolProxyTargets.BaiduPcsGo] = direct};
        else if (scope == "source") options.ThirdPartyProxies![(int) ThirdPartyId.BaiduNetdisk] = direct;
        else { options.ThirdPartyProxies = null; options.Proxy = direct; }
        var start = new ProcessStartInfo("bridge");
        foreach (var key in ProxyEnvironment) start.Environment[key] = "ambient-routing-canary";

        BaiduPcsBridgeClient.ConfigureProxy(start, new(new TestBOptions<NetworkOptions>(options)));

        foreach (var key in ProxyEnvironment) Assert.IsFalse(start.Environment.ContainsKey(key), key);
        Assert.AreEqual(0, start.ArgumentList.Count);
    }

    [DataTestMethod]
    [DataRow("http")]
    [DataRow("https")]
    [DataRow("socks5")]
    [DataRow("socks5h")]
    public void EveryDeclaredBaiduProxyProtocolReachesTheChildWithoutAnAmbientNoProxyBypass(string scheme)
    {
        var options = Settings();
        options.CustomProxies!.Single(p => p.Id == "tool").Address = scheme + "://tool.invalid:1080";
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.BaiduPcsGo] = Custom("tool")};
        var start = new ProcessStartInfo("bridge");
        start.Environment["NO_PROXY"] = "*";

        BaiduPcsBridgeClient.ConfigureProxy(start, new(new TestBOptions<NetworkOptions>(options)));

        AssertRouting(start, scheme + "://tool.invalid:1080/");
        Assert.IsFalse(start.Environment.ContainsKey("NO_PROXY"));
        Assert.IsTrue(DownloadToolProxyTargets.Find(DownloadToolProxyTargets.BaiduPcsGo)!
            .SupportedProxySchemes.Contains(scheme));
    }

    [TestMethod]
    public void UnsupportedBaiduProxyProtocolFailsRatherThanIgnoringTheSelection()
    {
        var options = Settings();
        options.CustomProxies!.Single(p => p.Id == "tool").Address = "socks4://tool.invalid:1080";
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.BaiduPcsGo] = Custom("tool")};
        var start = new ProcessStartInfo("bridge");

        Assert.ThrowsException<InvalidOperationException>(() =>
            BaiduPcsBridgeClient.ConfigureProxy(start, new(new TestBOptions<NetworkOptions>(options))));
        Assert.IsFalse(start.Environment.ContainsKey("HTTPS_PROXY"));
    }

    private static void AssertRouting(ProcessStartInfo start, string expected)
    {
        foreach (var name in new[] {"HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"})
            Assert.AreEqual(expected, start.Environment[name], name);
        foreach (var name in new[] {"NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy"})
            Assert.IsFalse(start.Environment.ContainsKey(name), name);
    }
}
