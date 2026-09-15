using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Handlers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using LegacyPostContent = Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.PostContent;
using CorePostContent = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public class PostParserCapabilityAdapterTests
{
    [TestMethod]
    public async Task PlatformReaderWinsAndReturnsRestrictedContentWithoutPurchasing()
    {
        var platform = new FakePlatformReader();
        var service = new LegacyPostContentService([new PlainTextReader(), platform]);
        var content = await service.ReadAsync(" https://soulplus.example/thread/1 ");
        Assert.AreEqual("SoulPlus", content.SourceHint);
        Assert.AreEqual("Thread", content.Title);
        Assert.AreEqual("Main content", content.MainHtml);
        CollectionAssert.AreEqual(new[] {"Comment content"}, content.CommentHtmlList);
        Assert.AreEqual(20m, content.Locks.Single().Price);
        Assert.IsFalse(content.Locks.Single().IsBought);
        Assert.AreEqual("https://soulplus.example/thread/1", platform.LastReference);
        Assert.AreEqual(1, platform.Reads);
    }

    [TestMethod]
    public async Task ExplicitSourceHintKeepsLegacyPlatformSpecificInputWorking()
    {
        var platform = new FakePlatformReader();
        var service = new LegacyPostContentService([platform]);
        Assert.IsFalse(service.CanRead("12345"));
        Assert.IsTrue(service.CanRead("12345", "soulplus"));
        await service.ReadAsync("12345", "soulplus");
        Assert.AreEqual("12345", platform.LastReference);
    }

    [TestMethod]
    public async Task PastedTextUsesAutomaticFallbackWithoutAPostParserTask()
    {
        var service = new LegacyPostContentService([new PlainTextReader()]);
        var content = await service.ReadAsync("Pasted title\nhttps://example.com/archive.zip");
        Assert.AreEqual("Pasted title", content.Title);
        StringAssert.Contains(content.MainHtml, "https://example.com/archive.zip");
        Assert.IsNull(content.SourceHint);
    }

    [TestMethod]
    public async Task MissingOrUnknownSourcesDoNotSilentlyChooseAnotherPlatform()
    {
        var platform = new FakePlatformReader();
        var service = new LegacyPostContentService([platform, new PlainTextReader()]);
        Assert.IsFalse(service.CanRead(" ", "SoulPlus"));
        Assert.IsFalse(service.CanRead("A pasted text", "MissingReader"));
        await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.ReadAsync(" "));
        Assert.AreEqual(0, platform.Reads);
    }

    [TestMethod]
    public async Task LegacyHandlerKeepsResultFormatAndDriveClassificationOutsideCore()
    {
        var handler = new DownloadInfoHandler(new FakeExtractor());
        var result = await handler.HandleAsync(new LegacyPostContent {Title = "Old", MainHtml = "Content"},
            CancellationToken.None);
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(result.Data,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var resource = data.RootElement.GetProperty("resources")[0];
        Assert.AreEqual("Parsed", result.OptimizedTitle);
        Assert.IsFalse(data.RootElement.TryGetProperty("title", out _));
        Assert.AreEqual("https://pan.baidu.com/s/example", resource.GetProperty("link").GetString());
        Assert.AreEqual("abcd", resource.GetProperty("code").GetString());
        Assert.AreEqual("archive-password", resource.GetProperty("password").GetString());
        Assert.AreEqual((int) AcquisitionDriveKind.Baidu, resource.GetProperty("driveKind").GetInt32());
    }

    private sealed class FakePlatformReader : ISharedContentReader
    {
        public PostParserSource? Source => PostParserSource.SoulPlus;
        public int Priority => 100;
        public int Reads;
        public string? LastReference;

        public bool CanRead(string reference) => reference.Contains("soulplus.example", StringComparison.Ordinal);

        public Task<LegacyPostContent> ReadAsync(string reference, CancellationToken ct)
        {
            Reads++;
            LastReference = reference;
            return Task.FromResult(new LegacyPostContent
            {
                Title = "Thread", MainHtml = "Main content", CommentHtmlList = ["Comment content"],
                Locks = [new SharedContentLock("https://soulplus.example/buy/1", 20m, false)]
            });
        }
    }

    private sealed class FakeExtractor : IPostDownloadInfoExtractor
    {
        public Task<PostDownloadInfo> ExtractAsync(CorePostContent content, CancellationToken ct = default) =>
            Task.FromResult(new PostDownloadInfo
            {
                Title = "Parsed",
                Resources =
                [
                    new PostDownloadResource
                    {
                        Link = "https://pan.baidu.com/s/example", Code = "abcd", Password = "archive-password"
                    }
                ]
            });
    }
}
