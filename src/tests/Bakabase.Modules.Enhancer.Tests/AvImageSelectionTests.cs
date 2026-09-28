using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Components.Enhancers.Av;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Bakabase.Modules.ThirdParty.ThirdParties.Javdb.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Modules.Enhancer.Tests;

[TestClass]
public sealed class AvImageSelectionTests
{
    [TestMethod]
    public async Task ImageDownload_UsesOnlyTheSelectedJavdbSource()
    {
        using var localizers = BuildLocalizers();
        var handler = new ImageHandler();
        using var httpClient = new HttpClient(handler);

        var sut = CreateEnhancer(localizers, httpClient, [
            Client(AvSourceIds.Avsex),
            Client(AvSourceIds.Javdb)
        ], [AvSourceIds.Javdb]);

        var context = await sut.BuildImageContext();

        context.Should().NotBeNull();
        context!.CoverPaths.Keys.Should().BeEquivalentTo([AvSourceIds.Javdb]);
        context.PosterPaths.Keys.Should().BeEquivalentTo([AvSourceIds.Javdb]);
        handler.Requests.Should().BeEquivalentTo([
            ImageUrl(AvSourceIds.Javdb, "cover"),
            ImageUrl(AvSourceIds.Javdb, "poster")
        ]);
        AssertSaved(sut.Logs, "Cover", AvSourceIds.Javdb);
        AssertSaved(sut.Logs, "Poster", AvSourceIds.Javdb);
    }

    [TestMethod]
    public async Task CoverFailure_DoesNotPreventTheSameSourcePosterDownload()
    {
        using var localizers = BuildLocalizers();
        var handler = new ImageHandler([ImageUrl(AvSourceIds.Javdb, "cover")]);
        using var httpClient = new HttpClient(handler);
        var sut = CreateEnhancer(localizers, httpClient,
            [Client(AvSourceIds.Javdb)], [AvSourceIds.Javdb]);

        var context = await sut.BuildImageContext();

        context.Should().NotBeNull();
        context!.CoverPaths.Should().BeEmpty();
        context.PosterPaths.Keys.Should().BeEquivalentTo([AvSourceIds.Javdb]);
        handler.Requests.Should().BeEquivalentTo([
            ImageUrl(AvSourceIds.Javdb, "cover"),
            ImageUrl(AvSourceIds.Javdb, "poster")
        ]);
        AssertHttpFailure(sut.Logs, "Cover", AvSourceIds.Javdb, 503);
        AssertSaved(sut.Logs, "Poster", AvSourceIds.Javdb);
    }

    [TestMethod]
    public async Task ImageDownload_FallsBackToTheNextAllowedSource()
    {
        using var localizers = BuildLocalizers();
        var handler = new ImageHandler([
            ImageUrl(AvSourceIds.Javdb, "cover"),
            ImageUrl(AvSourceIds.Javdb, "poster")
        ]);
        using var httpClient = new HttpClient(handler);
        var sut = CreateEnhancer(localizers, httpClient, [
            Client(AvSourceIds.Avsex),
            Client(AvSourceIds.Javdb)
        ], [AvSourceIds.Javdb, AvSourceIds.Avsex]);

        var context = await sut.BuildImageContext();

        context.Should().NotBeNull();
        context!.CoverPaths.Keys.Should().BeEquivalentTo([AvSourceIds.Avsex]);
        context.PosterPaths.Keys.Should().BeEquivalentTo([AvSourceIds.Avsex]);
        handler.Requests.Should().BeEquivalentTo([
            ImageUrl(AvSourceIds.Javdb, "cover"),
            ImageUrl(AvSourceIds.Javdb, "poster"),
            ImageUrl(AvSourceIds.Avsex, "cover"),
            ImageUrl(AvSourceIds.Avsex, "poster")
        ]);
        handler.Requests.IndexOf(ImageUrl(AvSourceIds.Javdb, "cover"))
            .Should().BeLessThan(handler.Requests.IndexOf(ImageUrl(AvSourceIds.Avsex, "cover")));
        handler.Requests.IndexOf(ImageUrl(AvSourceIds.Javdb, "poster"))
            .Should().BeLessThan(handler.Requests.IndexOf(ImageUrl(AvSourceIds.Avsex, "poster")));
        AssertHttpFailure(sut.Logs, "Cover", AvSourceIds.Javdb, 503);
        AssertHttpFailure(sut.Logs, "Poster", AvSourceIds.Javdb, 503);
        AssertSaved(sut.Logs, "Cover", AvSourceIds.Avsex);
        AssertSaved(sut.Logs, "Poster", AvSourceIds.Avsex);
    }

    private static void AssertSaved(IEnumerable<EnhancementLog> logs, string target, string source)
    {
        logs.Should().ContainSingle(log =>
            log.Level == "Information" &&
            log.Event == EnhancementLogEvent.FileSaved &&
            log.Message.Contains($"AV {target} saved from {source}", StringComparison.Ordinal));
    }

    private static void AssertHttpFailure(IEnumerable<EnhancementLog> logs, string target, string source,
        int statusCode)
    {
        var log = logs.Single(log =>
            log.Level == "Warning" &&
            log.Event == EnhancementLogEvent.Error &&
            log.Message.Contains($"AV {target} from {source}", StringComparison.Ordinal));
        using var data = JsonDocument.Parse(JsonSerializer.Serialize(log.Data));
        data.RootElement.GetProperty("StatusCode").GetInt32().Should().Be(statusCode);
    }

    private static ServiceProvider BuildLocalizers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddProperty<DbContext>();
        services.AddTransient<IEnhancerLocalizer, EnhancerLocalizer>();
        return services.BuildServiceProvider();
    }

    private static TestableAvEnhancer CreateEnhancer(
        IServiceProvider serviceProvider,
        HttpClient httpClient,
        IAvClient[] clients,
        string[] preferredSources)
    {
        var preferences = new Dictionary<int, IReadOnlyList<string>>
        {
            [(int)AvEnhancerTarget.Cover] = preferredSources,
            [(int)AvEnhancerTarget.Poster] = preferredSources
        };
        return new TestableAvEnhancer(
            NullLoggerFactory.Instance,
            new RecordingFileManager(),
            clients,
            new SingleHttpClientFactory(httpClient),
            new FixedAvSourceOptionsProvider(preferences),
            serviceProvider);
    }

    private static IAvClient Client(string source) => new FixedAvClient(source, new JavdbVideoDetail
    {
        Source = source,
        Number = "SSIS-001",
        Title = "Test video",
        CoverUrl = ImageUrl(source, "cover"),
        PosterUrl = ImageUrl(source, "poster")
    });

    private static string ImageUrl(string source, string image) =>
        $"https://images.example/{source}/{image}.jpg";

    private sealed class TestableAvEnhancer(
        ILoggerFactory loggerFactory,
        IFileManager fileManager,
        IEnumerable<IAvClient> clients,
        IHttpClientFactory httpClientFactory,
        IAvSourceOptionsProvider optionsProvider,
        IServiceProvider serviceProvider)
        : AvEnhancer(loggerFactory, fileManager, clients, httpClientFactory, optionsProvider,
            null!, null!, serviceProvider)
    {
        public IReadOnlyList<EnhancementLog> Logs { get; private set; } = [];

        public async Task<AvEnhancerContext?> BuildImageContext()
        {
            var logCollector = new EnhancementLogCollector();
            var context = await BuildContextInternal($"SSIS-001-{Guid.NewGuid():N}", new Resource
            {
                Id = 1,
                Path = "/test/SSIS-001.mp4",
                IsFile = true
            }, null!, logCollector, CancellationToken.None);
            Logs = logCollector.GetLogs();
            return context;
        }
    }

    private sealed class FixedAvClient(string sourceId, IAvDetail detail) : IAvClient
    {
        public string SourceId => sourceId;

        public Task<IAvDetail?> SearchAndParseVideo(string number, string? appointUrl = null,
            string? language = null) => Task.FromResult<IAvDetail?>(detail);
    }

    private sealed class FixedAvSourceOptionsProvider(
        IReadOnlyDictionary<int, IReadOnlyList<string>> preferences) : IAvSourceOptionsProvider
    {
        public AvSourceResolvedConfig Resolve(string sourceId) => new(true, null, null, null);

        public IReadOnlyDictionary<int, IReadOnlyList<string>>? GetPreferredSourcesByTarget() => preferences;
    }

    private sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ImageHandler(IEnumerable<string>? failingUrls = null) : HttpMessageHandler
    {
        private readonly HashSet<string> _failingUrls = failingUrls?.ToHashSet() ?? [];
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requests.Add(url);
            var status = _failingUrls.Contains(url) ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent([1, 2, 3])
            });
        }
    }

    private sealed class RecordingFileManager : IFileManager
    {
        public string BaseDir => "/test";
        public string BuildAbsolutePath(params object[] segmentsAfterBaseDir) =>
            Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(s => s.ToString()!)]);
        public Task<string> Save(string path, byte[] data, CancellationToken ct) => Task.FromResult(path);
        public string GetLocalCoverPathWithoutExtension(int resourceId) => BuildAbsolutePath("covers", resourceId);
        public string GetSourceCoverDir(string source, int resourceId) => BuildAbsolutePath("covers", source, resourceId);
        public string GetManualCoverPath(int resourceId, int index) => BuildAbsolutePath("covers", resourceId, index);
        public string GetEnhancerFilePath(string enhancerId, string fileName) => BuildAbsolutePath("enhancers", enhancerId, fileName);
        public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) =>
            BuildAbsolutePath("enhancers", enhancerId, resourceIdPart, fileName);
        public string GetAttachmentPath(string fileName) => BuildAbsolutePath("attachments", fileName);
        public string GetAigcGeneratorDir(int generatorId) => BuildAbsolutePath("aigc", generatorId);
        public string GetAigcRunDir(int generatorId, int runId) => BuildAbsolutePath("aigc", generatorId, runId);
        public string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName) =>
            Path.Combine("aigc", generatorId.ToString(), runId.ToString(), fileName);
    }
}
