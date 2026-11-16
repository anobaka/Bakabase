using System.Net;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads.WebProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class PublicShareHttpHandlerTests
{
    [TestMethod]
    public async Task Explicit_application_proxy_owns_dns_and_keeps_its_credentials_out_of_origin_headers()
    {
        var directCalls = 0; var dnsCalls = 0; var proxyCalls = 0;
        Uri? selected = null; ICredentials? credentials = null;
        var proxy = new WebProxy("http://127.0.0.1:8080") {Credentials = new NetworkCredential("private-user", "private-password")};
        var direct = new Handler(_ => { directCalls++; return new(HttpStatusCode.OK); });
        var transport = new Handler(request =>
        {
            proxyCalls++;
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Proxy-Authorization"));
            Assert.IsFalse(request.RequestUri!.AbsoluteUri.Contains("private-password"));
            return new(HttpStatusCode.OK) {Content = new StringContent("original bytes")};
        });
        using var handler = new ProxyAwarePublicHttpHandler(proxy,
            uri => PublicShareDownloadClients.IsProviderHost("pikpak", uri),
            (_, _) => { dnsCalls++; throw new CloudSourceNetworkException(); }, direct,
            (endpoint, auth) => { selected = endpoint; credentials = auth; return transport; });
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://api-drive.mypikpak.net/drive/v1/share/detail");
        Assert.AreEqual("original bytes", await response.Content.ReadAsStringAsync());
        Assert.IsFalse(transport.Disposed, "The proxy transport must remain alive while a response is read.");
        Assert.AreEqual("http://127.0.0.1:8080/", selected!.AbsoluteUri);
        Assert.AreEqual("private-password", credentials!.GetCredential(selected, "Basic")!.Password);
        Assert.AreEqual(0, directCalls); Assert.AreEqual(0, dnsCalls); Assert.AreEqual(1, proxyCalls);
    }

    [TestMethod]
    public async Task Redirects_validate_every_host_and_drop_authorization_cookie_and_captcha_on_host_change()
    {
        var calls = 0;
        var transport = new Handler(request =>
        {
            calls++;
            if (calls == 1) return Redirect("https://dl.mypikpak.com/original?temporary=secret");
            Assert.IsNull(request.Headers.Authorization);
            Assert.IsFalse(request.Headers.Contains("Cookie"));
            Assert.IsFalse(request.Headers.Contains("X-Captcha-Token"));
            return new(HttpStatusCode.OK);
        });
        using var handler = new ProxyAwarePublicHttpHandler(new Direct(),
            uri => PublicShareDownloadClients.IsProviderHost("pikpak", uri), (_, _) => Task.CompletedTask,
            transport, (_, _) => throw new AssertFailedException("No proxy expected"));
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api-drive.mypikpak.net/file?secret=private");
        request.Headers.Authorization = new("Bearer", "private-token");
        request.Headers.TryAddWithoutValidation("Cookie", "private-cookie");
        request.Headers.TryAddWithoutValidation("X-Captcha-Token", "private-captcha");
        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode); Assert.AreEqual(2, calls);
    }

    [DataTestMethod]
    [DataRow("https://outside.invalid/collect?private-token=secret")]
    [DataRow("http://dl.mypikpak.com/collect?private-token=secret")]
    [DataRow("https://127.0.0.1/collect?private-token=secret")]
    [DataRow("https://user:secret@dl.mypikpak.com/file")]
    public async Task Unsafe_redirects_are_not_contacted_and_error_messages_do_not_expose_tokens(string next)
    {
        var calls = 0;
        var transport = new Handler(_ => { calls++; return Redirect(next); });
        using var handler = new ProxyAwarePublicHttpHandler(new Direct(),
            uri => PublicShareDownloadClients.IsProviderHost("pikpak", uri), (_, _) => Task.CompletedTask,
            transport, (_, _) => throw new AssertFailedException("No proxy expected"));
        using var client = new HttpClient(handler);
        try { await client.GetAsync("https://api-drive.mypikpak.net/drive/v1/share/detail"); Assert.Fail("Unsafe redirect should fail"); }
        catch (Exception error) when (error is HttpRequestException or ArgumentException)
        {
            Assert.IsFalse(error.Message.Contains("secret")); Assert.IsFalse(error.Message.Contains("private-token"));
        }
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task Direct_fake_ip_failure_keeps_the_typed_configuration_requirement_and_never_connects()
    {
        var transport = new Handler(_ => throw new AssertFailedException("Non-public DNS must not connect"));
        using var handler = new ProxyAwarePublicHttpHandler(new Direct(), _ => true,
            (_, _) => throw new CloudSourceNetworkException(), transport,
            (_, _) => throw new AssertFailedException("No proxy expected"));
        using var client = new HttpClient(handler);
        await Assert.ThrowsExceptionAsync<CloudSourceNetworkException>(() => client.GetAsync("https://api-drive.mypikpak.net/"));
    }

    [TestMethod]
    public async Task Network_error_category_survives_without_exposing_proxy_or_signed_origin_details()
    {
        using var handler = new ProxyAwarePublicHttpHandler(new Direct(), _ => true, (_, _) => Task.CompletedTask,
            new Handler(_ => throw new HttpRequestException(HttpRequestError.ProxyTunnelError, "https://private.invalid/?token=secret")),
            (_, _) => throw new AssertFailedException("No proxy expected"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAsync("https://api-drive.mypikpak.net/"));
        Assert.AreEqual(HttpRequestError.ProxyTunnelError, error.HttpRequestError);
        StringAssert.Contains(error.Message, "PUBLIC_SHARE_NETWORK_ERROR");
        Assert.IsFalse(error.Message.Contains("secret"));
        Assert.IsNull(error.InnerException);
    }

    [TestMethod]
    public async Task Short_link_transport_can_return_redirects_to_its_own_bounded_resolver()
    {
        var calls = 0;
        using var handler = new ProxyAwarePublicHttpHandler(new Direct(), _ => true, (_, _) => Task.CompletedTask,
            new Handler(_ => { calls++; return Redirect("https://next.invalid/path"); }),
            (_, _) => throw new AssertFailedException("No proxy expected"), followRedirects: false);
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("https://cm1.hk/s/fixture");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode); Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public async Task Proxied_image_fetch_validates_each_target_before_sending_to_the_proxy()
    {
        var checkedHosts = new List<string>();
        var calls = 0;
        using var handler = new ProxyAwarePublicHttpHandler(new WebProxy("http://127.0.0.1:8888"), _ => true,
            (_, _) => throw new AssertFailedException("direct transport used"), new Handler(_ => throw new AssertFailedException()),
            (_, _) => new Handler(_ => { calls++; return Redirect("https://second.example/file.png"); }),
            checkProxyTarget: (uri, _) =>
            {
                checkedHosts.Add(uri.IdnHost);
                if (uri.IdnHost == "second.example") throw new HttpRequestException("private address");
                return Task.CompletedTask;
            });
        using var client = new HttpClient(handler);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAsync("https://first.example/qr"));
        CollectionAssert.AreEqual(new[] {"first.example", "second.example"}, checkedHosts);
        Assert.AreEqual(1, calls);
    }

    private static HttpResponseMessage Redirect(string url) => new(HttpStatusCode.Found) {Headers = {Location = new Uri(url)}};
    private sealed class Direct : IWebProxy
    {
        public Uri GetProxy(Uri destination) => destination;
        public bool IsBypassed(Uri host) => true;
        public ICredentials? Credentials { get; set; }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
