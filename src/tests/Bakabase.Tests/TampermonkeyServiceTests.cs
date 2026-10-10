using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tampermonkey;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.TestKit.Implementations;
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
    public async Task CompiledScriptIsSeededBeforeTheBundleWithoutChangingDistributionOrStoredChoices(
        string requestedEndpoint, string expectedEndpoint)
    {
        using var source = new SourceHandler(CompiledScript);
        var service = Service(source);

        var script = await service.GetScript(requestedEndpoint, scriptFile: null);

        Assert.IsNotNull(script);
        Assert.IsFalse(CompiledScript.Contains("DEFAULT_API_URL", StringComparison.Ordinal));
        var metadataEnd = script.IndexOf("// ==/UserScript==", StringComparison.Ordinal) + "// ==/UserScript==".Length;
        var connectLine = $"// @connect      {new Uri(expectedEndpoint).Host}\n";
        Assert.AreEqual(Metadata, script[..metadataEnd].Replace(connectLine, string.Empty),
            "The original metadata must stay first, with only the installed server added to @connect.");
        StringAssert.Contains(script[..metadataEnd], connectLine);
        Assert.IsTrue(script.EndsWith("\n" + Bundle, StringComparison.Ordinal), "The compiled bundle must remain intact.");
        var beforeBundle = script[metadataEnd..^Bundle.Length];
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
    [DataRow("zh-CN", "zh")]
    [DataRow("cn", "zh")]
    [DataRow("en-US", "en")]
    [DataRow("en", "en")]
    public async Task InstallSeedsTheAppLanguageBeforeTheBundleWithoutOverwritingAnExplicitChoice(string language, string locale)
    {
        using var source = new SourceHandler(CompiledScript);

        var script = await Service(source, language).GetScript("http://192.168.3.23:34567/", scriptFile: null);

        Assert.IsNotNull(script);
        var bootstrap = script[..^Bundle.Length];
        StringAssert.Contains(bootstrap, "if (!GM_getValue('locale', ''))");
        StringAssert.Contains(bootstrap, $"GM_setValue('locale', {JsonSerializer.Serialize(locale)});");
    }

    [TestMethod]
    [DataRow("http://192.168.3.23:34567", "192.168.3.23")]
    [DataRow("https://bakabase.example:8443", "bakabase.example")]
    [DataRow("http://[::1]:34568", "[::1]")]
    [DataRow("https://\u4f8b\u5b50.example:8443", "xn--fsqu00a.example")]
    public async Task InstallDeclaresTheServerHostWithoutRemovingWildcardOrDistributionMetadata(string endpoint, string host)
    {
        var template = CompiledScript.Replace("// ==/UserScript==", "// @connect *\n// ==/UserScript==");
        using var source = new SourceHandler(template);

        var script = await Service(source).GetScript(endpoint, scriptFile: null);

        Assert.IsNotNull(script);
        var metadata = script[..script.IndexOf("// ==/UserScript==", StringComparison.Ordinal)];
        StringAssert.Contains(metadata, $"// @connect      {host}\n");
        StringAssert.Contains(metadata, "// @connect *\n");
        StringAssert.Contains(metadata, $"// @updateURL    {TampermonkeyService.ScriptCdnUrl}");
        StringAssert.Contains(metadata, $"// @downloadURL  {TampermonkeyService.ScriptCdnUrl}");
    }

    [TestMethod]
    public async Task InstallDoesNotDuplicateAnAlreadyDeclaredHost()
    {
        var template = CompiledScript.Replace("// ==/UserScript==", "// @connect 192.168.3.23\n// ==/UserScript==");
        using var source = new SourceHandler(template);

        var script = await Service(source).GetScript("http://192.168.3.23:34567", scriptFile: null);

        Assert.IsNotNull(script);
        Assert.IsTrue(script.StartsWith(template[..template.IndexOf(Bundle, StringComparison.Ordinal)], StringComparison.Ordinal));
        Assert.AreEqual(1, Regex.Matches(script, @"@connect\s+192\.168\.3\.23").Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("<html>Gateway error</html>")]
    [DataRow("// ==UserScript==\n// @name incomplete")]
    [DataRow("alert('not a userscript');\n// ==/UserScript==")]
    public async Task InvalidSourceTemplateIsNotReturnedAsAnInstallableScript(string template)
    {
        using var source = new SourceHandler(template);

        Assert.IsNull(await Service(source).GetScript("http://localhost:34567", scriptFile: null));
    }

    [TestMethod]
    public async Task SourceFetchExceptionReturnsNullWithoutChangingTheDownloadDestination()
    {
        using var source = new SourceHandler(CompiledScript) {Failure = new HttpRequestException("CDN unavailable")};

        Assert.IsNull(await Service(source).GetScript("https://bakabase.example:8443", scriptFile: null));
        CollectionAssert.AreEqual(new[] {TampermonkeyService.ScriptCdnUrl}, source.Requests);
    }

    [TestMethod]
    public async Task SourceHttpFailureDoesNotReturnItsBodyAsJavaScript()
    {
        using var source = new SourceHandler(CompiledScript) {Status = HttpStatusCode.ServiceUnavailable};

        Assert.IsNull(await Service(source).GetScript("https://bakabase.example", scriptFile: null));
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

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Service(source).GetScript("https://user:secret@other.example", scriptFile: null));
        Assert.AreEqual(0, source.Requests.Count);
    }

    [TestMethod]
    public async Task ScriptFileOverrideServesTheLocalBuildWithInstallConfigurationWithoutChangingTheAsset()
    {
        var localBuild = CompiledScript.Replace("// @version      2.1.0", "// @version      2.1.1");
        var path = Path.GetTempFileName();
        using var source = new SourceHandler(CompiledScript) {Failure = new HttpRequestException("CDN unavailable")};
        try
        {
            await File.WriteAllTextAsync(path, localBuild);

            var script = await Service(source).GetScript("http://192.168.3.23:34567/", path);

            Assert.IsNotNull(script);
            StringAssert.Contains(script, "// @version      2.1.1");
            StringAssert.Contains(script, "// @connect      192.168.3.23\n");
            StringAssert.Contains(script, "GM_setValue('api_base_url', \"http://192.168.3.23:34567\");");
            StringAssert.Contains(script, "if (!GM_getValue('api_base_url', ''))");
            StringAssert.Contains(script, "if (!GM_getValue('locale', ''))");
            StringAssert.Contains(script, "GM_setValue('locale', \"zh\");");
            StringAssert.Contains(script, $"// @updateURL    {TampermonkeyService.ScriptCdnUrl}");
            StringAssert.Contains(script, $"// @downloadURL  {TampermonkeyService.ScriptCdnUrl}");
            Assert.IsTrue(script.EndsWith("\n" + Bundle, StringComparison.Ordinal));
            Assert.AreEqual(localBuild, await File.ReadAllTextAsync(path), "Configuration is injected in memory; the asset stays read-only.");
            Assert.AreEqual(0, source.Requests.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public async Task NoScriptFileOverrideKeepsTheCdnSource(string? scriptFile)
    {
        using var source = new SourceHandler(CompiledScript);

        Assert.IsNotNull(await Service(source).GetScript("http://localhost:34567", scriptFile));

        CollectionAssert.AreEqual(new[] {TampermonkeyService.ScriptCdnUrl}, source.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnreadableScriptFileOverrideReturnsNullWithoutFallingBackToTheCdn(bool directoryInsteadOfFile)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BakabaseUserscript_{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "bakabase.user.js");
        Directory.CreateDirectory(directory);
        if (directoryInsteadOfFile) Directory.CreateDirectory(path);
        using var source = new SourceHandler(CompiledScript);
        try
        {
            Assert.IsNull(await Service(source).GetScript("http://localhost:34567", path));
            Assert.AreEqual(0, source.Requests.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("<html>Invalid local asset</html>")]
    [DataRow("// ==UserScript==\n// @name incomplete")]
    public async Task InvalidLocalScriptMetadataIsRejectedWithoutCdnFallback(string template)
    {
        var path = Path.GetTempFileName();
        using var source = new SourceHandler(CompiledScript);
        try
        {
            await File.WriteAllTextAsync(path, template);

            Assert.IsNull(await Service(source).GetScript("http://localhost:34567", path));
            Assert.AreEqual(0, source.Requests.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static TampermonkeyService Service(SourceHandler source, string language = "zh-CN") => new(null!,
        new AppContext {ApiEndpoint = "http://172.20.0.2:34567"}, new Factory(source),
        new TestBOptionsManager<AppOptions>(new AppOptions {Language = language}));

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
