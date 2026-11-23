using System.Net;
using System.Text;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability.Probes;

namespace Bakabase.Tests;

[TestClass]
public sealed class GoogleDriveFolderAvailabilityProbeTests
{
    private const string Folder = "https://drive.google.com/drive/folders/Folder_01";
    private const string Listing = """
        <html><body class="flip-embedded"><div id="flip-contents"><div class="flip-entries">
        <div class="flip-entry" id="entry-Child_01" tabindex="0" role="link"><div class="flip-entry-info">
        <a href="https://docs.google.com/presentation/d/Child_01/edit?usp=drive_web">Visible entry</a></div></div>
        </div></div></body></html>
        """;

    [TestMethod]
    public async Task Only_requests_the_official_root_listing_and_preserves_the_link_resource_key()
    {
        var clients = new Clients(r =>
        {
            Assert.AreEqual(HttpMethod.Get, r.Method);
            Assert.AreEqual("https://drive.google.com/embeddedfolderview?id=Folder_01&resourcekey=0-key", r.RequestUri!.AbsoluteUri);
            Assert.IsTrue(GoogleDriveFolderAvailabilityProbe.AllowedEndpoint(r.RequestUri));
            return Html(HttpStatusCode.OK, Listing);
        });
        var result = await Check(clients, Folder + "?usp=sharing&resourcekey=0-key");
        Assert.AreEqual("available", result.Status);
        Assert.AreEqual(1, clients.Calls);
    }

    [TestMethod]
    [DataRow(404, "unknown")]
    [DataRow(410, "unknown")]
    [DataRow(403, "unknown")]
    [DataRow(401, "accessRequired")]
    [DataRow(429, "temporarilyUnavailable")]
    [DataRow(503, "temporarilyUnavailable")]
    public async Task Provider_errors_do_not_confirm_deletion(int code, string status) =>
        Assert.AreEqual(status, (await Check(new Clients(_ => Html((HttpStatusCode)code, Listing)))).Status);

    [TestMethod]
    [DataRow("https://accounts.google.com/ServiceLogin", "accessRequired")]
    [DataRow("https://accounts.google.com.example/ServiceLogin", "unknown")]
    [DataRow("//accounts.google.com/ServiceLogin", "unknown")]
    public async Task Classifies_the_redirect_without_following_it(string redirect, string status)
    {
        var clients = new Clients(_ => new(HttpStatusCode.Redirect) {Headers = {Location = new Uri(redirect, UriKind.RelativeOrAbsolute)}});
        Assert.AreEqual(status, (await Check(clients)).Status);
        Assert.AreEqual(1, clients.Calls);
    }

    [TestMethod]
    public async Task Empty_unrecognized_and_oversized_pages_do_not_confirm_availability()
    {
        foreach (var page in new[] {"<html>Sign in</html>", """<body class="flip-embedded"><div class="flip-entries"></div></body>""",
                     Listing.Replace("presentation/d/Child_01", "presentation/d/Other"), new string(' ', 256 * 1024 + 1)})
            Assert.AreEqual("unknown", (await Check(new Clients(_ => Html(HttpStatusCode.OK, page)))).Status);
    }

    [TestMethod]
    public async Task Other_locators_and_unrecognized_selectors_are_not_sent_to_the_probe()
    {
        var clients = new Clients(_ => throw new AssertFailedException("Unexpected request"));
        foreach (var url in new[] {"https://drive.google.com/file/d/File_01/view", "https://drive.google.com.example/drive/folders/id",
                     "http://drive.google.com/drive/folders/id", Folder + "?resourcekey=a&resourcekey=b", Folder + "#item",
                     Folder + "?action=download"})
            Assert.AreEqual("unsupported", (await Check(clients, url)).Status);
        Assert.AreEqual(0, clients.Calls);
    }

    private static HttpResponseMessage Html(HttpStatusCode status, string body) =>
        new(status) {Content = new StringContent(body, Encoding.UTF8, "text/html")};

    private static Task<SourceAvailabilityResult> Check(Clients client, string url = Folder) =>
        new GoogleDriveFolderAvailabilityProbe(client).CheckAsync(new() {Id = "test", Url = url}, default);

    private sealed class Clients(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
    {
        public int Calls {get; private set;}
        public HttpClient CreateClient(string name)
        {
            Assert.AreEqual(GoogleDriveFolderAvailabilityProbe.ClientName, name);
            return new(new Handler(r => {Calls++; return send(r);}));
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(send(request));
    }
}
