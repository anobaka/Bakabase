using System.Net;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.PostSources;
using Bakabase.Service.Controllers;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.QrCode;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostSourceImageTests
{
    private const string Share = "https://pan.baidu.com/s/1fixture?pwd=Ab12";
    private const string Cdn = "https://cdn.example/download-code.png";

    [TestMethod]
    public async Task Lists_saved_body_and_reply_shares_for_review_without_parser_results_or_network()
    {
        await using var db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        const string signed = "https://pan.baidu.com/s/1fixture?pwd=Ab12&x=1";
        const string magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";
        var body = new PostContent
        {
            MainHtml = "<blockquote>下载地址：<a href='" + signed.Replace("&", "&amp;") +
                "'>" + Share + "</a></blockquote>" +
                "<p>https://example.org/image.jpg <a href='https://docs.google.com'>文档</a></p>",
            CommentHtmlList = [$"<div>备用：{magnet}。</div><p>{signed.Replace("&", "&amp;")}</p>"]
        };
        db.PostParserTasks.Add(new PostParserTaskDbModel {Id = 2, Revision = 9, Link = "https://forum.example/post",
            ContentSnapshotJson = JsonSerializer.Serialize(body, WorkflowJson.Options)});
        await db.SaveChangesAsync();
        var transport = new Factory([]);
        var list = (await new PostSourceImagesController(db, transport).Get(2, default)).Value!.Data!;
        CollectionAssert.AreEqual(new[] {new SavedPostLink(signed, "baidu.pcs", 0, "Ab12", "下载地址："),
                new SavedPostLink(magnet, "bt.native", 1, Context: "备用：")},
            list.Links.ToArray());
        Assert.AreEqual(0, transport.Uris.Count);
        Assert.IsFalse(await db.WorkflowRuns.AnyAsync());
    }

    [TestMethod]
    public void Keeps_the_author_label_with_each_share_in_the_same_floor()
    {
        var links = new SavedPostLinks();
        SavedPostImages.Read(new PostContent { MainHtml =
            $"<p>解压工具：{Share}</p><p>作品：<a href='https://gofile.io/d/fixture'>https://gofile.io/d/fixture</a></p>" }, links);
        CollectionAssert.AreEqual(new[] { "解压工具：", "作品：" }, links.Items.Select(link => link.Context).ToArray());
    }

    [TestMethod]
    public void Body_share_review_ignores_unrelated_attributes_ordinary_urls_and_incomplete_shares()
    {
        var links = new SavedPostLinks();
        SavedPostImages.Read(new PostContent {MainHtml =
            $"<script>{Share}</script><style>{Share}</style><img src='{Share}' alt='{Share}'>" +
            $"<a href='https://example.org/site'>{Share}</a>" +
            "<span>https://pan.baidu.com/s/ https://pan.baidu.com/s/not-a-share.jpg https://gofile.io/d/ https://drive.google.com/open " +
            "https://example.org/file.zip https://pan.baidu.com.fake.test/s/foo " +
            "https://private:secret@pan.baidu.com/s/foo magnet:?dn=missing-hash</span>"}, links);
        Assert.AreEqual(0, links.Items.Count);
    }

    [TestMethod]
    public void Lists_saved_images_including_lazy_and_linked_codes_without_using_a_cover_as_a_source()
    {
        var images = SavedPostImages.Read(new PostContent
        {
            SourceUrl = "https://forum.example/post/1",
            MainHtml = "<p>Download QR<br><img src='placeholder.gif' data-original-src='" + Cdn + "'></p>" +
                       "<img src='http://127.0.0.1/private'><img src='data:image/png;base64,a'>" +
                       "<a href='" + Cdn + "'>full size</a><a href='https://example.com/article'>article</a>",
            CommentHtmlList = ["<a href='/images/another.webp'>QR alternative</a>"]
        });
        CollectionAssert.AreEquivalent(new[] { Cdn, "https://forum.example/images/another.webp" },
            images.Select(i => i.Url).ToArray());
        var main = images.Single(i => i.Url == Cdn); Assert.AreEqual(0, main.Floor);
        Assert.AreEqual("Download QR", main.Context);
        Assert.AreEqual(1, images.Single(i => i.Url != Cdn).Floor);
    }

    [TestMethod]
    public async Task Decodes_only_the_selected_saved_image_and_returns_the_original_supported_share()
    {
        await using var db = new BakabaseDbContext(new DbContextOptionsBuilder<BakabaseDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        var body = new PostContent {MainHtml = $"<p><img src='{Cdn}'></p><img src='https://cdn.example/ordinary.jpg'>"};
        db.PostParserTasks.Add(new PostParserTaskDbModel {Id = 2, Revision = 17, Link = "https://forum.example/p",
            ContentSnapshotJson = JsonSerializer.Serialize(body, WorkflowJson.Options)});
        await db.SaveChangesAsync();
        var bytes = Qr(Share); var transport = new Factory(bytes);
        var controller = new PostSourceImagesController(db, transport);
        var list = (await controller.Get(2, default)).Value!.Data!;
        Assert.AreEqual(2, list.Images.Count); Assert.AreEqual(0, transport.Uris.Count);
        var image = list.Images.Single(i => i.Url == Cdn);
        foreach (var request in new[] {new DecodePostSourceImageInput(16, image.Id),
                     new DecodePostSourceImageInput(17, new string('b', 64))})
            Assert.AreEqual("sourceChanged", (await controller.Decode(2, request, default)).Value!.Data!.Status);
        Assert.AreEqual(0, transport.Uris.Count);
        var result = (await controller.Decode(2, new(17, image.Id), default)).Value!.Data!;
        Assert.AreEqual("decoded", result.Status);
        Assert.AreEqual(new DecodedPostImageSource(Share, "baidu.pcs"), result.Sources.Single());
        // A user-provided file also goes through the same decoder but never refetches its URL.
        result = (await controller.Decode(2, new(17, image.Id, Convert.ToBase64String(bytes)), default)).Value!.Data!;
        Assert.AreEqual(Share, result.Sources.Single().Url);
        CollectionAssert.AreEqual(new[] {Cdn}, transport.Uris.ToArray());
        Assert.IsFalse(await db.WorkflowRuns.AnyAsync());
    }

    [TestMethod]
    public void A_supported_image_can_reach_the_qr_decoder_as_jpeg_or_webp()
    {
        using var qr = Image.Load<L8>(Qr(Share));
        foreach (var format in new[] {"jpeg", "webp"})
        {
            using var stream = new MemoryStream();
            if (format == "jpeg") qr.SaveAsJpeg(stream); else qr.SaveAsWebp(stream);
            var result = PostSourceQr.Decode(stream.ToArray(), default);
            Assert.AreEqual("decoded", result.Status, format);
            Assert.AreEqual(Share, result.Sources.Single().Url, format);
        }
    }

    [DataTestMethod]
    [DataRow("not a link")]
    [DataRow("http://localhost/a.zip")]
    [DataRow("https://pan.baidu.com.evil.example/s/1secret")]
    [DataRow("https://images.example/ordinary.jpg")]
    [DataRow("https://source:secret@example.com/file.zip")]
    [DataRow("https://198.18.0.6/file.zip")]
    public void A_QR_payload_does_not_create_an_arbitrary_or_local_source(string payload)
    {
        var preview = PostSourceQr.Decode(Qr(payload), default);
        Assert.AreEqual("unsupportedSource", preview.Status); Assert.AreEqual(0, preview.Sources.Count);
    }

    [TestMethod]
    public async Task Missing_blank_oversized_and_cancelled_images_are_distinct()
    {
        using var plain = new Image<L8>(64,64);
        using var stream = new MemoryStream(); plain.SaveAsPng(stream);
        Assert.AreEqual("noQrCode", PostSourceQr.Decode(stream.ToArray(), default).Status);
        stream.SetLength(0); plain.SaveAsTiff(stream);
        Assert.AreEqual("unsupportedImage", PostSourceQr.Decode(stream.ToArray(), default).Status);
        Assert.AreEqual("unsupportedImage", PostSourceQr.Decode("not an image"u8.ToArray(), default).Status);
        Assert.AreEqual("imageTooLarge", PostSourceQr.Decode(new byte[PostSourceQr.MaxBytes + 1], default).Status);
        using var tooWide = new Image<L8>(8200, 10);
        stream.SetLength(0); tooWide.SaveAsPng(stream);
        Assert.AreEqual("imageTooLarge", PostSourceQr.Decode(stream.ToArray(), default).Status);
        var image = new SavedPostImage("selected", Cdn, null, 0);
        var missing = new Factory([], HttpStatusCode.NotFound);
        using var client = missing.CreateClient(PostSourceQr.Client);
        Assert.AreEqual("imageUnavailable", (await PostSourceQr.ReadAsync(client, image, null, default)).Status);
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() =>
            PostSourceQr.ReadAsync(client, image, null, new CancellationToken(true)));
        Assert.AreEqual(1, missing.Uris.Count);
    }

    private static byte[] Qr(string payload)
    {
        var code = new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 180, 180);
        using var image = new Image<L8>(code.Width, code.Height);
        for (var y = 0; y < code.Height; y++)
            for (var x = 0; x < code.Width; x++)
                image[x, y] = new L8(code[x, y] ? (byte)0 : (byte)255);
        using var stream = new MemoryStream(); image.SaveAsPng(stream); return stream.ToArray();
    }

    private sealed class Factory(byte[] bytes, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        public List<string> Uris { get; } = [];
        public HttpClient CreateClient(string name) => new(new Handler(this, bytes, status));
        private sealed class Handler(Factory owner, byte[] bytes, HttpStatusCode status) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                owner.Uris.Add(request.RequestUri!.AbsoluteUri);
                return Task.FromResult(new HttpResponseMessage(status) {Content = new ByteArrayContent(bytes)});
            }
        }
    }
}
