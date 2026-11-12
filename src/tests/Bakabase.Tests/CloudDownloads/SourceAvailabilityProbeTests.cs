using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability.Probes;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class SourceAvailabilityProbeTests
{
    [DataTestMethod]
    [DataRow("BAIDU_SHARE_CANCELLED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_EXPIRED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_DELETED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_NOT_FOUND", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_BLOCKED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_VISIT_LIMIT_INVALIDATED", "confirmedUnavailable")]
    [DataRow("BAIDU_SHARE_TEMPORARILY_UNAVAILABLE", "temporarilyUnavailable")]
    [DataRow("BAIDU_SHARE_ACCESS_RESTRICTED", "accessRequired")]
    [DataRow("BAIDU_SHARE_VISITOR_LIMIT_REACHED", "accessRequired")]
    [DataRow("BAIDU_SHARE_FROZEN", "accessRequired")]
    [DataRow("BAIDU_SHARE_UNAVAILABLE", "unknown")]
    [DataRow("BAIDU_SHARE_VERIFICATION_FAILED", "unknown")]
    [DataRow("BAIDU_SHARE_EMPTY", "unknown")]
    [DataRow("BAIDU_SHARE_LIST_FAILED", "unknown")]
    [DataRow("BAIDU_SHARE_PAGE_INVALID", "unknown")]
    [DataRow("BAIDU_SHARE_ACCESS_CODE_REJECTED", "accessRequired")]
    [DataRow("BAIDU_SHARE_VERIFICATION_REQUIRED", "accessRequired")]
    [DataRow("BAIDU_SHARE_NETWORK_ERROR", "temporarilyUnavailable")]
    [DataRow("BAIDU_SHARE_NETWORK_REQUIRES_PROXY", "unknown")]
    [DataRow("BAIDU_UNKNOWN_COMMAND", "unsupported")]
    public void BaiduOnlyAuthoritativeCodesCanAuthorizeDisable(string code, string expected)
    {
        var result = BaiduShareAvailabilityProbe.Classify(code);
        Assert.AreEqual(expected, result.Status);
        Assert.IsTrue(result.ExpiresAt > result.CheckedAt);
    }

    [TestMethod]
    public void ProviderErrorTextCannotLeakIntoEvidence()
    {
        var result = BaiduShareAvailabilityProbe.Classify("BAIDU_error secret https://host/?token=secret");
        Assert.AreEqual("unknown", result.Status);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret"));
    }

    [DataTestMethod]
    [DataRow("http", false)]
    [DataRow("http", true)]
    [DataRow("pixeldrain", false)]
    [DataRow("pixeldrain", true)]
    [DataRow("oneDrive", false)]
    [DataRow("oneDrive", true)]
    public async Task PublicDnsPolicyFailuresRemainUnknownAndActionableEvenWhenHttpClientWrapsThem(string provider, bool wrapped)
    {
        var calls = 0;
        var factory = new Factory(new Handler(_ =>
        {
            calls++;
            throw wrapped ? new HttpRequestException("Transport failed with secret fixture detail", new CloudSourceNetworkException())
                : new CloudSourceNetworkException();
        }));
        var (probe, source) = Probe(provider, factory);
        var result = await probe.CheckAsync(source, default);
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, result.Status);
        Assert.AreEqual(CloudSourceNetworkException.RequirementCode, result.ReasonCode);
        Assert.AreEqual(1, calls, "Do not silently retry with a different proxy or resolver.");
        Assert.AreEqual(2, probe.CacheVersion);
        Assert.AreEqual(probe.CacheVersion, result.ProbeVersion);
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret"));
    }

    [DataTestMethod]
    [DataRow("http")]
    [DataRow("pixeldrain")]
    [DataRow("oneDrive")]
    public async Task OrdinaryTransportFailuresAndUntrustedErrorTextKeepTheGenericNetworkClassification(string provider)
    {
        var factory = new Factory(new Handler(_ => throw new HttpRequestException(CloudSourceNetworkException.RequirementCode + ": untrusted")));
        var (probe, source) = Probe(provider, factory);
        var result = await probe.CheckAsync(source, default);
        Assert.AreEqual(SourceAvailabilityStatuses.TemporarilyUnavailable, result.Status);
        Assert.AreEqual("SOURCE_NETWORK_ERROR", result.ReasonCode);
    }

    [TestMethod]
    public void BaiduDnsPolicyFailureUsesTheSharedSetupReasonAndVersion()
    {
        var result = BaiduShareAvailabilityProbe.Classify("BAIDU_SHARE_NETWORK_REQUIRES_PROXY");
        Assert.AreEqual(SourceAvailabilityStatuses.Unknown, result.Status);
        Assert.AreEqual(CloudSourceNetworkException.RequirementCode, result.ReasonCode);
        Assert.AreEqual(3, result.ProbeVersion);
    }

    private static (ISourceAvailabilityProbe Probe, CloudSource Source) Probe(string provider, IHttpClientFactory factory) => provider switch
    {
        "http" => (new HttpSourceAvailabilityProbe(factory), new() {Id = "s", Url = "https://example.test/file.zip"}),
        "pixeldrain" => (new PixeldrainAvailabilityProbe(factory), new() {Id = "s", Url = "https://pixeldrain.com/u/file"}),
        _ => (new SharePointAvailabilityProbe(factory), new() {Id = "s", Url = "https://tenant.sharepoint.com/:f:/g/shared"})
    };

    [DataTestMethod]
    [DataRow(404, "not_found", "confirmedUnavailable")]
    [DataRow(404, "invalid_endpoint", "unknown")]
    [DataRow(200, "not_found", "unknown")]
    [DataRow(403, "forbidden", "accessRequired")]
    [DataRow(403, "transfer_limit_exceeded", "temporarilyUnavailable")]
    [DataRow(429, "ip_rate_limit_reached", "temporarilyUnavailable")]
    [DataRow(503, "internal", "temporarilyUnavailable")]
    public void PixeldrainUsesStableErrorCodeAndHttpStatusTogether(int status, string code, string expected)
    {
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { success = false, value = code }));
        Assert.AreEqual(expected, PixeldrainAvailabilityProbe.Classify((HttpStatusCode)status, data.RootElement, "file", false).Status);
    }

    [TestMethod]
    public void PixeldrainListCannotPassWithMissingEntriesOrWrongSelectedItem()
    {
        using var partial = JsonDocument.Parse("""{"success":true,"id":"list","file_count":2,"files":[{"id":"file","size":12,"can_download":true}]}""");
        Assert.AreEqual("unknown", PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, partial.RootElement, "list", true).Status);
        using var full = JsonDocument.Parse("""{"success":true,"id":"list","file_count":2,"files":[{"id":"file1","size":12,"can_download":true},{"id":"file2","size":13,"can_download":false}]}""");
        Assert.AreEqual("available", PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, full.RootElement, "list", true, 0).Status);
        Assert.AreEqual("accessRequired", PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, full.RootElement, "list", true, 1).Status);
        Assert.AreEqual("unknown", PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, full.RootElement, "list", true, 2).Status);
        Assert.AreEqual("unknown", PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, full.RootElement, "wrong", true).Status);
    }

    [DataTestMethod]
    [DataRow("file_rate_limited_captcha_required", "accessRequired")]
    [DataRow("unavailable_for_legal_reasons", "accessRequired")]
    [DataRow("transfer_limit_exceeded", "temporarilyUnavailable")]
    public void PixeldrainSuccessfulMetadataDoesNotHideDownloadRestrictions(string restriction, string expected)
    {
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { success = true, id = "file", size = 12, availability = restriction }));
        Assert.AreEqual(expected, PixeldrainAvailabilityProbe.Classify(HttpStatusCode.OK, data.RootElement, "file", false).Status);
    }

    [DataTestMethod]
    [DataRow(404, "application/zip", "unknown")]
    [DataRow(410, "application/zip", "unknown")]
    [DataRow(405, "application/zip", "unknown")]
    [DataRow(200, "text/html", "unknown")]
    [DataRow(200, "application/json", "unknown")]
    [DataRow(200, "application/zip", "available")]
    [DataRow(403, "application/zip", "accessRequired")]
    public void HttpNeverTreatsGeneric404OrLoginHtmlAsAnInvalidOrAvailableShare(int status, string media, string expected) =>
        Assert.AreEqual(expected, HttpSourceAvailabilityProbe.Classify((HttpStatusCode)status, media, 100, null).Status);

    [TestMethod]
    public async Task PixeldrainProbeUsesMetadataGetOnlyAndPreservesSelectedFile()
    {
        var calls = new List<HttpRequestMessage>();
        var factory = new Factory(new Handler(request =>
        {
            calls.Add(request);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"success":true,"id":"list","file_count":1,"files":[{"id":"file","size":12,"can_download":true}]}""", Encoding.UTF8, "application/json") };
        }));
        var result = await new PixeldrainAvailabilityProbe(factory).CheckAsync(new() { Id = "s", Url = "https://pixeldra.in/l/list#item=0", AccessCode = "never-transmit" }, default);
        Assert.AreEqual("available", result.Status);
        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual(HttpMethod.Get, calls[0].Method);
        Assert.AreEqual("https://pixeldrain.com/api/list/list", calls[0].RequestUri!.AbsoluteUri);
        Assert.IsNull(calls[0].Content);
    }

    [TestMethod]
    public async Task PixeldrainRejectsOversizedResponseWithoutFalseInvalidity()
    {
        var factory = new Factory(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(new string('x', 2 * 1024 * 1024 + 1)) }));
        Assert.AreEqual("unknown", (await new PixeldrainAvailabilityProbe(factory).CheckAsync(new() { Id = "s", Url = "https://pixeldrain.com/u/file" }, default)).Status);
    }

    [TestMethod]
    public async Task UnsupportedProbeNeverCreatesRemoteAccounts()
    {
        var probe = new UnsupportedSourceAvailabilityProbe();
        var result = await probe.CheckAsync(new() { Id = "s", Url = "https://gofile.io/d/file" }, default);
        Assert.AreEqual("unsupported", result.Status);
        Assert.AreEqual("SOURCE_PROBE_READONLY_TOKEN_REQUIRED", result.ReasonCode);
    }

    [TestMethod]
    public async Task SharePointProbeOnlyReadsPublicPageAndFolderRoot()
    {
        var calls = new List<string>();
        var factory = new Factory(new Handler(request =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            calls.Add(request.RequestUri!.AbsolutePath);
            if (calls.Count == 1) return new(HttpStatusCode.OK)
            {
                RequestMessage = new(HttpMethod.Get, "https://tenant.sharepoint.com/personal/user/Documents/Forms/AllItems.aspx?id=%2Fpersonal%2Fuser%2FDocuments%2FShared"),
                Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    [".driveUrl"] = "https://tenant.sharepoint.com/personal/user/_api/v2.0/drives/drive",
                    [".driveAccessToken"] = "tempauth=secret-fixture",
                    ["listUrl"] = "/personal/user/Documents"
                }))
            };
            Assert.AreEqual("/personal/user/_api/v2.0/drives/drive/root:/Shared", request.RequestUri.AbsolutePath);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"root-id","folder":{"childCount":100000}}""") };
        }));
        var result = await new SharePointAvailabilityProbe(factory).CheckAsync(new() { Id = "s", Url = "https://tenant.sharepoint.com/:f:/g/shared" }, default);
        Assert.AreEqual("available", result.Status);
        Assert.AreEqual(2, calls.Count, "No recursive listing or payload requests.");
        Assert.IsFalse(JsonSerializer.Serialize(result).Contains("secret-fixture"));
    }

    [TestMethod]
    public async Task SharePointPublic404CannotProveDeletion()
    {
        var factory = new Factory(new Handler(_ => new(HttpStatusCode.NotFound)));
        var result = await new SharePointAvailabilityProbe(factory).CheckAsync(new() { Id = "s", Url = "https://tenant.sharepoint.com/:f:/g/shared" }, default);
        Assert.AreEqual("accessRequired", result.Status);
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }
}
