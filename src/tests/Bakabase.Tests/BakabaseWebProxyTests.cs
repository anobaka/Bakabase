using System;
using System.Collections.Generic;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Implementations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class BakabaseWebProxyTests
{
    private static readonly Uri Destination = new("https://example.com/x");

    private const string HomeProxyId = "home";
    private const string WorkProxyId = "work";

    private static NetworkOptions BuildOptions(
        NetworkOptions.ProxyModel global,
        Dictionary<int, NetworkOptions.ProxyModel>? perSource = null) => new()
    {
        Proxy = global,
        ThirdPartyProxies = perSource,
        CustomProxies =
        [
            new NetworkOptions.ProxyOptions {Id = HomeProxyId, Address = "http://127.0.0.1:1080"},
            new NetworkOptions.ProxyOptions {Id = WorkProxyId, Address = "http://127.0.0.1:2080"}
        ]
    };

    private static BakabaseWebProxy Build(NetworkOptions options) =>
        new(new TestBOptions<NetworkOptions>(options));

    private static NetworkOptions.ProxyModel Custom(string id) => new()
    {
        Mode = NetworkOptions.ProxyMode.UseCustom,
        CustomProxyId = id
    };

    private static NetworkOptions.ProxyModel None => new() {Mode = NetworkOptions.ProxyMode.DoNotUse};

    [TestMethod]
    public void GlobalProxy_IsUsedWhenSourceHasNoOverride()
    {
        var proxy = Build(BuildOptions(Custom(HomeProxyId)));

        var resolved = proxy.ForThirdParty(ThirdPartyId.ExHentai).GetProxy(Destination);

        Assert.AreEqual("http://127.0.0.1:1080/", resolved?.ToString());
    }

    [TestMethod]
    public void SourceOverride_WinsOverGlobal()
    {
        var options = BuildOptions(Custom(HomeProxyId), new Dictionary<int, NetworkOptions.ProxyModel>
        {
            [(int) ThirdPartyId.ExHentai] = Custom(WorkProxyId)
        });
        var proxy = Build(options);

        Assert.AreEqual("http://127.0.0.1:2080/",
            proxy.ForThirdParty(ThirdPartyId.ExHentai).GetProxy(Destination)?.ToString());
    }

    [TestMethod]
    public void SourceOverride_DoesNotLeakToOtherSources()
    {
        var options = BuildOptions(Custom(HomeProxyId), new Dictionary<int, NetworkOptions.ProxyModel>
        {
            [(int) ThirdPartyId.ExHentai] = Custom(WorkProxyId)
        });
        var proxy = Build(options);

        Assert.AreEqual("http://127.0.0.1:1080/",
            proxy.ForThirdParty(ThirdPartyId.Pixiv).GetProxy(Destination)?.ToString());
    }

    [TestMethod]
    public void SourceCanOptOutWhileGlobalProxyIsOn()
    {
        // The point of a per-source override: one downloader goes direct while the rest stay proxied.
        var options = BuildOptions(Custom(HomeProxyId), new Dictionary<int, NetworkOptions.ProxyModel>
        {
            [(int) ThirdPartyId.Bilibili] = None
        });
        var proxy = Build(options);

        Assert.IsNull(proxy.ForThirdParty(ThirdPartyId.Bilibili).GetProxy(Destination));
        Assert.IsNotNull(proxy.ForThirdParty(ThirdPartyId.ExHentai).GetProxy(Destination));
    }

    [TestMethod]
    public void UnknownCustomProxyId_ResolvesToNoProxyRatherThanThrowing()
    {
        // A proxy can be deleted while a source still references it.
        var proxy = Build(BuildOptions(Custom("deleted")));

        Assert.IsNull(proxy.ForThirdParty(ThirdPartyId.ExHentai).GetProxy(Destination));
    }

    [TestMethod]
    public void GlobalBehaviour_IsUnchangedForCallersThatDoNotScopeBySource()
    {
        var proxy = Build(BuildOptions(Custom(HomeProxyId), new Dictionary<int, NetworkOptions.ProxyModel>
        {
            [(int) ThirdPartyId.ExHentai] = Custom(WorkProxyId)
        }));

        // The shared instance keeps answering for the global setting; only ForThirdParty scopes.
        Assert.AreEqual("http://127.0.0.1:1080/", proxy.GetProxy(Destination)?.ToString());
    }

    [TestMethod]
    public void ChangingOptionsTakesEffectWithoutRebuildingTheProxy()
    {
        // Handlers are kept alive for 30 days, so the override has to be read per call.
        var options = BuildOptions(Custom(HomeProxyId));
        var proxy = Build(options);
        var scoped = proxy.ForThirdParty(ThirdPartyId.ExHentai);

        Assert.AreEqual("http://127.0.0.1:1080/", scoped.GetProxy(Destination)?.ToString());

        options.ThirdPartyProxies = new Dictionary<int, NetworkOptions.ProxyModel>
        {
            [(int) ThirdPartyId.ExHentai] = Custom(WorkProxyId)
        };

        Assert.AreEqual("http://127.0.0.1:2080/", scoped.GetProxy(Destination)?.ToString());
    }
}
