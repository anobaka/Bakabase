using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiImageDownloadTests
{
    private const string PageUrl = "https://exhentai.org/s/abc/12345-1";
    private const string ImageUrl = "https://images.example/1.jpg";

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task ImageHttpErrorsAreRejectedInsteadOfSavedAsImages(HttpStatusCode status)
    {
        var content = new TrackingContent("upstream error", "text/html");
        using var http = new HttpClient(new ImageHandler(status, content));
        var client = new ExHentaiClient(new Factory(http), NullLoggerFactory.Instance);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.DownloadImage(PageUrl));

        Assert.AreEqual(status, error.StatusCode);
        Assert.IsTrue(content.Disposed, "The rejected image response must be disposed.");
    }

    [TestMethod]
    public async Task DownloadedImageBytesRemainUsableAfterTheResponseIsDisposed()
    {
        var content = new TrackingContent("image bytes", "image/jpeg");
        using var http = new HttpClient(new ImageHandler(HttpStatusCode.OK, content));
        var client = new ExHentaiClient(new Factory(http), NullLoggerFactory.Instance);

        var image = await client.DownloadImage(PageUrl);

        Assert.AreEqual("image bytes", Encoding.UTF8.GetString(image.Data));
        Assert.AreEqual("image/jpeg", image.ContentType);
        Assert.IsTrue(content.Disposed);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.OK)]
    [DataRow(HttpStatusCode.ServiceUnavailable)]
    public async Task DirectImageDownloadsAlsoDisposeTheResponse(HttpStatusCode status)
    {
        var content = new TrackingContent("image bytes", "image/jpeg");
        using var http = new HttpClient(new ImageHandler(status, content));
        var client = new ExHentaiClient(new Factory(http), NullLoggerFactory.Instance);

        if (status == HttpStatusCode.OK)
        {
            var image = await client.DownloadImageByUrl(ImageUrl);
            Assert.AreEqual("image bytes", Encoding.UTF8.GetString(image.Data));
        }
        else
        {
            var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(
                () => client.DownloadImageByUrl(ImageUrl));
            Assert.AreEqual(status, error.StatusCode);
        }

        Assert.IsTrue(content.Disposed);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ImageHandler(HttpStatusCode status, HttpContent imageContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"<img id='img' src='{ImageUrl}' />")
                }
                : new HttpResponseMessage(status) { Content = imageContent });
    }

    private sealed class TrackingContent : ByteArrayContent
    {
        public bool Disposed { get; private set; }

        public TrackingContent(string value, string mediaType) : base(Encoding.UTF8.GetBytes(value))
        {
            Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
