using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Service.Components.CloudDownloads.Baidu;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bakabase.Tests.Notices;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
[DoNotParallelize] // The system-proxy case temporarily replaces HttpClient.DefaultProxy.
public sealed class DownloadToolProxyTests
{
    private static readonly Uri Destination = new("https://drive.google.com/file/d/fixture/view");
    private static NetworkOptions.ProxyModel Custom(string id) => new() {Mode = NetworkOptions.ProxyMode.UseCustom, CustomProxyId = id};
    private static NetworkOptions.ProxyModel Direct => new() {Mode = NetworkOptions.ProxyMode.DoNotUse};
    private static NetworkOptions Settings() => new()
    {
        Proxy = Custom("global"),
        ThirdPartyProxies = new() {[(int)ThirdPartyId.GoogleDrive] = Custom("site")},
        CustomProxies =
        [
            new() {Id = "global", Address = "http://global.invalid:8000"},
            new() {Id = "site", Address = "http://site.invalid:8001"},
            new() {Id = "tool", Address = "socks5://tool.invalid:1080", Credentials = new()
                {Username = "alice@example.test", Password = "p:a@ss/word", Domain = "WORK"}}
        ]
    };
    private static BakabaseWebProxy Resolver(NetworkOptions options) => new(new TestBOptions<NetworkOptions>(options));

    [TestMethod]
    public void ToolSelectionWinsWithoutChangingSourceGlobalOrBtRouting()
    {
        var options = Settings();
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Custom("tool")};
        var resolver = Resolver(options);
        var actual = WebCloudProvider.ResolveDownloadProxy(resolver, "googleDrive", Destination);
        Assert.AreEqual("socks5://tool.invalid:1080", actual!.Url);
        Assert.AreEqual("WORK\\alice@example.test", actual.Username);
        Assert.AreEqual("http://site.invalid:8001/", resolver.ForThirdParty(ThirdPartyId.GoogleDrive).GetProxy(Destination)!.AbsoluteUri);
        Assert.AreEqual("http://global.invalid:8000/", resolver.GetProxy(Destination)!.AbsoluteUri);
        Assert.IsNull(new BtNetworkOptionsProvider(new TestBOptions<NetworkOptions>(options)).GetOptions().ProxyUri);
    }

    [TestMethod]
    public void SourceResolverProxyIsIndependentAndItsOverrideCanBeChangedWithoutRestarting()
    {
        var options = Settings();
        var resolver = Resolver(options);
        var scoped = resolver.ForDownloadTool(DownloadToolProxyTargets.SourceResolver);
        Assert.IsNull(DownloadToolProxyTargets.Find(DownloadToolProxyTargets.SourceResolver)!.ThirdPartyId);
        Assert.AreEqual("global.invalid", scoped.GetProxy(Destination)!.Host);
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.SourceResolver] = Custom("tool")};
        Assert.AreEqual("tool.invalid", scoped.GetProxy(Destination)!.Host);
        Assert.AreEqual("site.invalid", resolver.ForDownloadTool(DownloadToolProxyTargets.Gdown).GetProxy(Destination)!.Host);
        Assert.AreEqual("global.invalid", resolver.GetProxy(Destination)!.Host);
        options.DownloadToolProxies[DownloadToolProxyTargets.SourceResolver] = Direct;
        Assert.IsNull(scoped.GetProxy(Destination));
        Assert.AreEqual("site.invalid", resolver.ForThirdParty(ThirdPartyId.GoogleDrive).GetProxy(Destination)!.Host);
    }

    [TestMethod]
    public void AScopedToolReevaluatesInheritanceAndDirectWithoutBeingRecreated()
    {
        var options = Settings();
        var scoped = Resolver(options).ForDownloadTool(DownloadToolProxyTargets.Gdown);
        Assert.AreEqual("site.invalid", scoped.GetProxy(Destination)!.Host);
        options.ThirdPartyProxies = null;
        Assert.AreEqual("global.invalid", scoped.GetProxy(Destination)!.Host);
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Custom("tool")};
        Assert.AreEqual("tool.invalid", scoped.GetProxy(Destination)!.Host);
        options.DownloadToolProxies[DownloadToolProxyTargets.Gdown] = Direct;
        Assert.IsNull(scoped.GetProxy(Destination));
        options.DownloadToolProxies.Clear();
        Assert.AreEqual("global.invalid", scoped.GetProxy(Destination)!.Host);
    }

    [TestMethod]
    public void InheritedSourceDirectDoesNotFallThroughToGlobal()
    {
        var options = Settings();
        options.ThirdPartyProxies![(int)ThirdPartyId.GoogleDrive] = Direct;
        Assert.IsNull(WebCloudProvider.ResolveDownloadProxy(Resolver(options), "googleDrive", Destination));
        Assert.IsNotNull(Resolver(options).GetProxy(Destination));
    }

    [TestMethod]
    public void ToolUsesTheCallingSourceRatherThanAssumingItsDefaultSource()
    {
        var options = Settings();
        options.ThirdPartyProxies![(int)ThirdPartyId.Gofile] = Direct;
        var resolver = Resolver(options);
        Assert.AreEqual("site.invalid", resolver.ForDownloadTool(DownloadToolProxyTargets.GalleryDl, ThirdPartyId.GoogleDrive).GetProxy(Destination)!.Host);
        Assert.IsNull(resolver.ForDownloadTool(DownloadToolProxyTargets.GalleryDl, ThirdPartyId.Gofile).GetProxy(Destination));
    }

    [TestMethod]
    public void SystemSelectionHonorsBypassAndDoesNotLeakAnEndpointForDirectHosts()
    {
        var previous = HttpClient.DefaultProxy;
        try
        {
            HttpClient.DefaultProxy = new BypassProxy();
            var options = Settings();
            options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = new() {Mode = NetworkOptions.ProxyMode.UseSystem}};
            var scoped = Resolver(options).ForDownloadTool(DownloadToolProxyTargets.Gdown);
            Assert.IsNull(CloudToolProxy.Resolve(scoped, new Uri("https://nas.internal/")));
            Assert.AreEqual("http://system.invalid:8080", CloudToolProxy.Resolve(scoped, Destination)!.Url);
        }
        finally { HttpClient.DefaultProxy = previous; }
    }

    [TestMethod]
    [DataRow("tool")]
    [DataRow("site")]
    [DataRow("global")]
    public void AStaleCustomReferenceCannotSilentlyBecomeDirectOrFallBack(string scope)
    {
        var options = Settings();
        if (scope == "tool") options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Custom("deleted-secret-canary")};
        if (scope == "site") options.ThirdPartyProxies![(int)ThirdPartyId.GoogleDrive] = Custom("deleted-secret-canary");
        if (scope == "global") { options.ThirdPartyProxies = null; options.Proxy = Custom("deleted-secret-canary"); }
        var error = Assert.ThrowsException<InvalidOperationException>(() =>
            WebCloudProvider.ResolveDownloadProxy(Resolver(options), "googleDrive", Destination));
        Assert.IsFalse(error.ToString().Contains("secret-canary", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BaiduCredentialsAreEscapedOnlyInTheChildEnvironmentAndDirectClearsAmbientRouting()
    {
        var options = Settings();
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.BaiduPcsGo] = Custom("tool")};
        var start = CloudToolProcess.StartInfo("bridge", ["--version"], Path.GetTempPath());
        start.Environment["NO_PROXY"] = "*";
        start.Environment["https_proxy"] = "http://ambient.invalid:80";
        BaiduPcsBridgeClient.ConfigureProxy(start, Resolver(options));
        var actual = new Uri(start.Environment["HTTPS_PROXY"]!);
        Assert.AreEqual("tool.invalid", actual.Host);
        Assert.AreEqual("WORK\\alice@example.test:p:a@ss/word", Uri.UnescapeDataString(actual.UserInfo));
        Assert.IsFalse(start.Environment.ContainsKey("NO_PROXY"));
        Assert.IsFalse(start.Environment.ContainsKey("https_proxy"));
        CollectionAssert.AreEqual(new[] {"--version"}, start.ArgumentList.ToArray());
        options.DownloadToolProxies[DownloadToolProxyTargets.BaiduPcsGo] = Direct;
        BaiduPcsBridgeClient.ConfigureProxy(start, Resolver(options));
        foreach (var name in new[] {"HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy"})
            Assert.IsFalse(start.Environment.ContainsKey(name));
    }

    [TestMethod]
    public void PythonProxyCredentialsArePrivateStdinFieldsAndItsDiagnosticStringIsRedacted()
    {
        var options = Settings();
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Custom("tool"), [DownloadToolProxyTargets.GalleryDl] = Direct};
        var settings = WebCloudProvider.ResolveDownloadProxy(Resolver(options), "googleDrive", Destination)!;
        var request = new Bakabase.Modules.Downloader.Cloud.CloudDownloadRequest("operation", new()
            {Id = "source", ProviderId = "googleDrive", Url = Destination.AbsoluteUri}, null, null, Path.GetTempPath());
        using var json = JsonDocument.Parse(WebCloudProvider.SerializeRequest("googleDrive", request, Path.GetTempPath(), settings));
        var proxy = json.RootElement.GetProperty("proxy");
        Assert.AreEqual("p:a@ss/word", proxy.GetProperty("password").GetString());
        Assert.IsFalse(settings.Url.Contains("alice", StringComparison.Ordinal));
        Assert.IsFalse(settings.ToString().Contains("p:a@ss", StringComparison.Ordinal));
        Assert.IsNull(WebCloudProvider.ResolveDownloadProxy(Resolver(options), "gofile", new Uri("https://gofile.io/d/fixture")));
        // Dependency installers deliberately resolve the unscoped global instance.
        Assert.AreEqual("global.invalid", new Uri(CloudToolProxy.Resolve(Resolver(options), new Uri("https://pypi.org"))!.Url).Host);
    }

    [TestMethod]
    public async Task NullPatchLeavesOverridesAndEmptyPatchRestoresInheritanceWithoutChangingBt()
    {
        var manager = new StubOptions<NetworkOptions>(Settings());
        manager.Value.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Direct};
        manager.Value.BtProxy = Custom("site");
        using var services = new ServiceCollection().BuildServiceProvider();
        var controller = Controller(manager, services);
        Assert.AreEqual(0, (await controller.PatchNetworkOptions(new() {BtMaxConcurrentDownloads = 3})).Code);
        Assert.IsNotNull(manager.Value.DownloadToolProxies);
        Assert.AreEqual(NetworkOptions.ProxyMode.DoNotUse, manager.Value.DownloadToolProxies[DownloadToolProxyTargets.Gdown].Mode);
        Assert.AreEqual(0, (await controller.PatchNetworkOptions(new() {DownloadToolProxies = []})).Code);
        Assert.IsNull(manager.Value.DownloadToolProxies);
        Assert.AreEqual("site", manager.Value.BtProxy.CustomProxyId);
        Assert.AreEqual("site.invalid", WebCloudProvider.ResolveDownloadProxy(Resolver(manager.Value), "googleDrive", Destination) is { } selected ? new Uri(selected.Url).Host : null);
    }

    [TestMethod]
    [DataRow("unknown-tool", "tool", 2)]
    [DataRow("gdown", "missing-secret-canary", 2)]
    [DataRow("gdown", "tool", 99)]
    public async Task UnknownTargetsReferencesAndModesAreRejectedBeforeSaveWithoutEchoingInput(string id, string proxyId, int mode)
    {
        var manager = new StubOptions<NetworkOptions>(Settings());
        using var services = new ServiceCollection().BuildServiceProvider();
        var result = await Controller(manager, services).PatchNetworkOptions(new()
        {
            DownloadToolProxies = new() {[id] = new() {Mode = (NetworkOptions.ProxyMode)mode, CustomProxyId = proxyId}}
        });
        Assert.AreNotEqual(0, result.Code);
        Assert.AreEqual(0, manager.SaveCount);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-canary", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("p:a@ss", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("baidu-pcs-go", "http", true)]
    [DataRow("baidu-pcs-go", "https", true)]
    [DataRow("baidu-pcs-go", "socks5", true)]
    [DataRow("baidu-pcs-go", "socks5h", true)]
    [DataRow("baidu-pcs-go", "socks4", false)]
    [DataRow("gdown", "socks4", true)]
    [DataRow("gallery-dl", "socks5h", true)]
    [DataRow("gallery-dl", "ftp", false)]
    [DataRow("sourceResolver", "http", true)]
    [DataRow("sourceResolver", "socks5", true)]
    [DataRow("sourceResolver", "socks5h", false)]
    public async Task SaveAndRuntimeUseTheSameToolProtocolCapabilities(string toolId, string scheme, bool supported)
    {
        var options = Settings();
        options.CustomProxies!.Add(new() {Id = "protocol", Address = scheme + "://proxy.invalid:1080"});
        var manager = new StubOptions<NetworkOptions>(options);
        using var services = new ServiceCollection().BuildServiceProvider();
        var result = await Controller(manager, services).PatchNetworkOptions(new()
            {DownloadToolProxies = new() {[toolId] = Custom("protocol")}});
        Assert.AreEqual(supported, result.Code == 0);
        Assert.AreEqual(supported ? 1 : 0, manager.SaveCount);
        options.DownloadToolProxies = new() {[toolId] = Custom("protocol")};
        if (supported) Assert.AreEqual(scheme, Resolver(options).ForDownloadTool(toolId).GetProxy(Destination)!.Scheme);
        else Assert.ThrowsException<InvalidOperationException>(() => Resolver(options).ForDownloadTool(toolId).GetProxy(Destination));
    }

    [TestMethod]
    public async Task DeletingASharedProxyClearsAllReferencesAndPreservesUnrelatedToolOverrides()
    {
        var options = Settings();
        options.Proxy = Custom("tool"); options.BtProxy = Custom("tool");
        // BT supports this proxy's protocol; it remains independent when that shared proxy is deleted.
        options.ThirdPartyProxies![(int)ThirdPartyId.GoogleDrive] = Custom("tool");
        options.DownloadToolProxies = new() {[DownloadToolProxyTargets.Gdown] = Custom("tool"), [DownloadToolProxyTargets.GalleryDl] = Direct};
        var manager = new StubOptions<NetworkOptions>(options);
        using var services = new ServiceCollection().BuildServiceProvider();
        var response = await Controller(manager, services).PatchNetworkOptions(new()
        {
            CustomProxies = options.CustomProxies!.Where(p => p.Id != "tool").Select(p =>
                new NetworkOptionsPatchInputModel.ProxyOptions {Id = p.Id, Address = p.Address}).ToList()
        });
        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(NetworkOptions.ProxyMode.DoNotUse, manager.Value.Proxy.Mode);
        Assert.AreEqual(NetworkOptions.ProxyMode.DoNotUse, manager.Value.BtProxy.Mode);
        Assert.IsNull(manager.Value.ThirdPartyProxies);
        Assert.IsFalse(manager.Value.DownloadToolProxies!.ContainsKey(DownloadToolProxyTargets.Gdown));
        Assert.AreEqual(NetworkOptions.ProxyMode.DoNotUse, manager.Value.DownloadToolProxies[DownloadToolProxyTargets.GalleryDl].Mode);
        Assert.AreEqual(1, manager.SaveCount);
    }

    private static OptionsController Controller(StubOptions<NetworkOptions> options, IServiceProvider services)
    {
        var pool = new BakabaseOptionsManagerPool(services);
        pool.AllOptionsManagers[typeof(NetworkOptions)] = options;
        return new OptionsController(null!, null!, pool, null!, null!, null!, null!, null!);
    }

    private sealed class BypassProxy : IWebProxy
    {
        public Uri GetProxy(Uri destination) => new("http://system.invalid:8080");
        public bool IsBypassed(Uri host) => host.Host == "nas.internal";
        public ICredentials? Credentials { get; set; }
    }
}
