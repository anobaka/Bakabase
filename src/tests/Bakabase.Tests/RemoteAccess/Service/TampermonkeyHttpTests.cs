using System.Net;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests.RemoteAccess.Service;

[TestClass]
public class TampermonkeyHttpTests
{
    private sealed class ScriptSource : HttpMessageHandler, IHttpClientFactory
    {
        public int Requests { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            Assert.AreEqual(TampermonkeyService.ScriptCdnUrl, request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("// ==UserScript==\n// @grant GM_getValue\n// @grant GM_setValue\n" +
                                            "// ==/UserScript==\n(function(){const stored=GM_getValue('api_base_url','');})();")
            });
        }
    }

    private static Task<ServiceGateHost> Start(ScriptSource source, ServerKind kind = ServerKind.Headless) =>
        ServiceGateHost.StartAsync([typeof(TampermonkeyController)], services =>
        {
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => kind));
            services.AddSingleton(sp => new TampermonkeyService(null!, sp.GetRequiredService<AppContext>(), source));
        });

    [TestMethod]
    [DataRow(ServerKind.Headless, false)]
    [DataRow(ServerKind.Headless, true)]
    [DataRow(ServerKind.Desktop, true)]
    [DataRow(ServerKind.Unknown, false)]
    public async Task Browser_install_uses_the_browser_origin_without_launching_on_the_host(ServerKind kind, bool remote)
    {
        using var source = new ScriptSource();
        await using var host = await Start(source, kind);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Get, "/Tampermonkey/install");
        if (remote) request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(html, "window.location.origin");
        StringAssert.Contains(html, "window.location.replace(url.href)");
        StringAssert.Contains(html, "/script/bakabase.user.js");
        Assert.AreEqual(0, source.Requests);
    }

    [TestMethod]
    public async Task Script_defaults_to_the_requested_host_and_published_port()
    {
        using var source = new ScriptSource();
        await using var host = await Start(source);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = host.Request(HttpMethod.Get, "/Tampermonkey/script/bakabase.user.js");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        request.Headers.Host = "192.168.3.23:45678";
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/javascript", response.Content.Headers.ContentType!.MediaType);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "\"http://192.168.3.23:45678\"");
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        Assert.AreEqual(1, source.Requests);
    }

    [TestMethod]
    [DataRow("https://media.example:8443")]
    [DataRow("http://127.0.0.1:34568")]
    [DataRow("http://[::1]:34568")]
    public async Task Explicit_browser_or_relay_origin_survives_an_internal_http_host(string origin)
    {
        using var source = new ScriptSource();
        await using var host = await Start(source);
        using var response = await host.SendAsync(HttpMethod.Get,
            "/Tampermonkey/script/bakabase.user.js?apiEndpoint=" + Uri.EscapeDataString(origin));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), System.Text.Json.JsonSerializer.Serialize(origin));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("javascript:alert(1)")]
    [DataRow("https://user:password@example.com")]
    [DataRow("https://example.com/path")]
    [DataRow("https://example.com/?query=1")]
    [DataRow("https://example.com/#fragment")]
    public async Task Invalid_explicit_origin_is_rejected_before_downloading(string origin)
    {
        using var source = new ScriptSource();
        await using var host = await Start(source);
        using var response = await host.SendAsync(HttpMethod.Get,
            "/Tampermonkey/script/bakabase.user.js?apiEndpoint=" + Uri.EscapeDataString(origin));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, source.Requests);
    }

    [TestMethod]
    [DataRow("/Tampermonkey/install")]
    [DataRow("/Tampermonkey/script/bakabase.user.js")]
    public async Task Unpaired_remote_callers_still_need_management_access(string path)
    {
        using var source = new ScriptSource();
        await using var host = await Start(source);
        host.Remote.Mode = RemoteAccessMode.Enabled;
        using var request = host.Request(HttpMethod.Get, path);
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.3.100");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, source.Requests);
    }

    [TestMethod]
    public async Task Failed_download_is_reported_instead_of_installing_an_unconfigured_script()
    {
        using var source = new ScriptSource {Status = HttpStatusCode.BadGateway};
        await using var host = await Start(source);
        using var response = await host.SendAsync(HttpMethod.Get, "/Tampermonkey/script/bakabase.user.js");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.IsNull(response.Headers.Location);
    }
}
