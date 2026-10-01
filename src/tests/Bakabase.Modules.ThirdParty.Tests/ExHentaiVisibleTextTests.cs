using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using CsQuery;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public sealed class ExHentaiVisibleTextTests
{
    private const string PageUrl = "https://exhentai.org/s/abcdef0123/4170203-46";
    private const string ImageUrl = "https://fixture.hath.network/image.png";
    private static readonly byte[] ImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6PoAAAAAASUVORK5CYII=");

    [TestMethod]
    public void RealCsQueryDoctypeAndCommentNodesHaveNamesButNoChildren()
    {
        var page = new CQ("<!DOCTYPE html><!-- outside --><html><body>visible<!-- inside --></body></html>");
        var nodes = Walk(page.Document).ToArray();
        var doctype = nodes.Single(node => node is IDomDocumentType);
        var comments = nodes.Where(node => node is IDomComment).ToArray();
        Assert.AreEqual(2, comments.Length);
        foreach (var leaf in comments.Append(doctype))
        {
            Assert.IsNotNull(leaf.NodeName, leaf.GetType().FullName);
            Assert.IsNull(leaf.ChildNodes,
                "CsQuery returns null, rather than an empty collection, for doctype/comment leaves.");
        }
        var text = nodes.First(node => node.NodeType == NodeType.TEXT_NODE);
        Assert.IsNotNull(text.NodeName);
        Assert.IsNull(text.ChildNodes);
    }

    [DataTestMethod]
    [DataRow("<!DOCTYPE html>")]
    [DataRow("<!-- outside comment -->")]
    [DataRow("<!DOCTYPE html><!-- outside comment -->")]
    public async Task ImageViewingDocumentsWithNonContainerNodesDoNotFailOrMistakeScriptAndCommentLiteralsForErrors(string prefix)
    {
        var html = Document(prefix,
            "<script>const error='Could not get dispatch for image; Insufficient GP';</script>" +
            "<style>.fake:after{content:'Please log in; Error 509'}</style>",
            $"before<img id='img' src='{ImageUrl}'>after");
        var handler = new Handler(request => request.RequestUri!.Host == "exhentai.org" ? Html(html) : Image());
        var result = await Client(handler).DownloadImage(PageUrl);
        CollectionAssert.AreEqual(ImageBytes, result.Data);
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [TestMethod]
    public async Task BalanceDocumentsPreserveNestedTextOrderAndIgnoreCommentScriptAndStyleCurrencies()
    {
        var html = Document("<!DOCTYPE html><!-- Available: 777 kGP -->",
            "<script>const fake='Available: 999 kGP';</script>" +
            "<style>.fake:after{content:'Available: 888 kGP'}</style>",
            "<table><tbody><tr><td>Available:</td><td><strong>1,234</strong> Credits</td></tr>" +
            "<!-- Available: 666 kGP --><tr><td>Available:</td><td><span><strong>&#50;.5</strong></span> kGP</td></tr>" +
            "</tbody></table>");
        var handler = new Handler(_ => Html(html));
        var balance = await Client(handler).GetAccountBalance(Account());
        Assert.AreEqual(2500L, balance.GpBalance);
        Assert.AreEqual(1234L, balance.CreditsBalance);
        Assert.AreEqual(1, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("<!DOCTYPE html>")]
    [DataRow("<!-- node comment -->")]
    public async Task ImageErrorDocumentsStillIdentifyVisibleRestrictionsWithoutReturningTheirBody(string prefix)
    {
        var html = Document(prefix, "", "Insufficient GP for the image<p>private-response-body-secret</p>");
        var handler = new Handler(request => request.RequestUri!.Host == "exhentai.org"
            ? Html($"<img id='img' src='{ImageUrl}'>") : Html(html));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).DownloadImage(PageUrl));
        StringAssert.Contains(error.Message, "GP or funds restriction");
        StringAssert.Contains(error.Message, "pageNumber=46");
        Assert.IsFalse(error.ToString().Contains("private-response-body-secret"));
        Assert.AreEqual(2, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("<p>Available: 10 Credits</p>")]
    [DataRow("<p>Available: 10 GP</p><p>Available: 20 GP</p>")]
    public async Task DocumentsWithMissingOrAmbiguousVisibleGpRemainExplicitErrors(string body)
    {
        var html = Document("<!DOCTYPE html><!-- Available: 777 kGP -->",
            "<script>const fake='Available: 999 kGP';</script>", body);
        var handler = new Handler(_ => Html(html));
        var error = await Assert.ThrowsExceptionAsync<InvalidDataException>(() => Client(handler).GetAccountBalance(Account()));
        StringAssert.Contains(error.Message, "could not be determined reliably");
        Assert.AreEqual(1, handler.Requests.Count);
    }

    private static string Document(string prefix, string head, string body) =>
        prefix + "<html><head><title>Gallery</title>" + head + "</head><body>" + body + "</body></html>";

    private static IEnumerable<IDomObject> Walk(IDomObject node)
    {
        yield return node;
        if (node.ChildNodes == null) yield break;
        foreach (var child in node.ChildNodes)
        foreach (var descendant in Walk(child))
            yield return descendant;
    }

    private static ExHentaiRequestContext Account() => new("ipb_member_id=123; ipb_pass_hash=fixture_hash; igneous=fixture_igneous");
    private static ExHentaiClient Client(Handler handler) => new(new Factory(new HttpClient(handler)), NullLoggerFactory.Instance);
    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK)
        {Content = new StringContent(html, Encoding.UTF8, "text/html")};
    private static HttpResponseMessage Image()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) {Content = new ByteArrayContent(ImageBytes)};
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }
    private sealed class Factory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
