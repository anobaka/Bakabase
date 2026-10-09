using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Tests;

[TestClass]
public sealed class TampermonkeyServiceTests
{
    // The published bundle has optimized the empty DEFAULT_API_URL constant away.
    // Keep that compiled shape here so source-only replacement cannot pass this test.
    private const string Metadata = """
        // ==UserScript==
        // @name         Bakabase integration
        // @version      2.1.0
        // @grant        GM_getValue
        // @grant        GM_setValue
        // @updateURL    https://cdn-public.anobaka.com/app/bakabase/scripts/bakabase.user.js
        // @downloadURL  https://cdn-public.anobaka.com/app/bakabase/scripts/bakabase.user.js
        // ==/UserScript==
        """;

    private const string Bundle = """
        (function () {
          const _GM_getValue = (() => typeof GM_getValue !== "undefined" ? GM_getValue : void 0)();
          function getApiBaseUrl() {
            const stored = _GM_getValue("api_base_url", "");
            if (stored) return stored;
            return "";
          }
          globalThis.bakabaseEndpoint = getApiBaseUrl();
        })();
        """;

    private static readonly string CompiledScript = Metadata + "\n" + Bundle;

    [TestMethod]
    [DataRow("http://192.168.3.23:34567/", "http://192.168.3.23:34567")]
    [DataRow("https://bakabase.example:443/", "https://bakabase.example")]
    [DataRow("http://127.0.0.1:34568", "http://127.0.0.1:34568")]
    public async Task CompiledScriptIsSeededBeforeTheBundleWithoutChangingMetadataOrStoredChoices(
        string requestedEndpoint, string expectedEndpoint)
    {
        using var source = new SourceHandler(CompiledScript);
        var service = Service(source);

        var script = await service.GetScript(requestedEndpoint);

        Assert.IsNotNull(script);
        Assert.IsFalse(CompiledScript.Contains("DEFAULT_API_URL", StringComparison.Ordinal));
        Assert.IsTrue(script.StartsWith(Metadata, StringComparison.Ordinal), "The userscript metadata must stay first and intact.");
        Assert.IsTrue(script.EndsWith("\n" + Bundle, StringComparison.Ordinal), "The compiled bundle must remain intact.");
        var beforeBundle = script[Metadata.Length..^Bundle.Length];
        var guard = beforeBundle.IndexOf("if (!GM_getValue('api_base_url', ''))", StringComparison.Ordinal);
        var seed = Regex.Match(beforeBundle, @"GM_setValue\('api_base_url',\s*(""(?:\\.|[^""\\])*"")\);");
        Assert.IsTrue(guard >= 0 && seed.Success && guard < seed.Index,
            "Only an empty stored choice is seeded, before any bundled code reads it.");
        Assert.AreEqual(expectedEndpoint, JsonSerializer.Deserialize<string>(seed.Groups[1].Value));
        Assert.IsFalse(script.Contains("http://172.20.0.2:34567", StringComparison.Ordinal),
            "The caller's reachable endpoint must replace the container's listening address.");
        CollectionAssert.AreEqual(new[] {TampermonkeyService.ScriptCdnUrl}, source.Requests);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<html>Gateway error</html>")]
    [DataRow("// ==UserScript==\n// @name incomplete")]
    [DataRow("alert('not a userscript');\n// ==/UserScript==")]
    public async Task InvalidSourceTemplateIsNotReturnedAsAnInstallableScript(string template)
    {
        using var source = new SourceHandler(template);

        Assert.IsNull(await Service(source).GetScript("http://localhost:34567"));
    }

    [TestMethod]
    public async Task SourceFetchExceptionReturnsNullWithoutChangingTheDownloadDestination()
    {
        using var source = new SourceHandler(CompiledScript) {Failure = new HttpRequestException("CDN unavailable")};

        Assert.IsNull(await Service(source).GetScript("https://bakabase.example:8443"));
        CollectionAssert.AreEqual(new[] {TampermonkeyService.ScriptCdnUrl}, source.Requests);
    }

    [TestMethod]
    public async Task SourceHttpFailureDoesNotReturnItsBodyAsJavaScript()
    {
        using var source = new SourceHandler(CompiledScript) {Status = HttpStatusCode.ServiceUnavailable};

        Assert.IsNull(await Service(source).GetScript("https://bakabase.example"));
    }

    [TestMethod]
    [DataRow("http://192.168.3.23:34567", "http://192.168.3.23:34567")]
    [DataRow("HTTPS://Bakabase.Example:443/", "https://bakabase.example")]
    [DataRow("http://localhost:34567/", "http://localhost:34567")]
    [DataRow("http://127.0.0.1:34568", "http://127.0.0.1:34568")]
    [DataRow("http://[::1]:34568/", "http://[::1]:34568")]
    [DataRow("https://[2001:db8::1234]:8443/", "https://[2001:db8::1234]:8443")]
    public void NativeRelayAndNetworkOriginsAreAccepted(string endpoint, string expected)
    {
        Assert.IsTrue(TampermonkeyService.TryNormalizeOrigin(endpoint, out var origin));
        Assert.AreEqual(expected, origin);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("bakabase.example:34567")]
    [DataRow("ftp://bakabase.example")]
    [DataRow("javascript:alert(1)")]
    [DataRow("https://user:password@bakabase.example")]
    [DataRow("https://bakabase.example/api")]
    [DataRow("https://bakabase.example/a/../")]
    [DataRow("https://bakabase.example?token=secret")]
    [DataRow("https://bakabase.example#fragment")]
    [DataRow("http://0.0.0.0:34567")]
    [DataRow("http://[::]:34567")]
    [DataRow("http://*:34567")]
    [DataRow("http://+:34567")]
    [DataRow("http://localhost:0")]
    [DataRow("http://localhost:65536")]
    [DataRow("https://bakabase.example\n")]
    [DataRow("https://bakabase.example\\")]
    public void UnsupportedOriginsCannotBecomeScriptConfiguration(string? endpoint)
    {
        Assert.IsFalse(TampermonkeyService.TryNormalizeOrigin(endpoint, out var origin));
        Assert.IsNull(origin);
    }

    [TestMethod]
    public async Task InvalidEndpointIsRejectedBeforeAnySourceRequest()
    {
        using var source = new SourceHandler(CompiledScript);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Service(source).GetScript("https://user:secret@other.example"));
        Assert.AreEqual(0, source.Requests.Count);
    }

    private static TampermonkeyService Service(SourceHandler source) => new(null!,
        new AppContext {ApiEndpoint = "http://172.20.0.2:34567"}, new Factory(source));

    private sealed class Factory(SourceHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SourceHandler(string template) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public Exception? Failure { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Failure is { } failure ? Task.FromException<HttpResponseMessage>(failure) :
                Task.FromResult(new HttpResponseMessage(Status) {Content = new StringContent(template)});
        }
    }
}
