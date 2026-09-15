using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Components.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public class IdentityCoverProviderTests
{
    private string _directory = null!;
    private IResourceExternalIdentityService _service = null!;
    private IdentityServiceRecorder _recorder = null!;
    private RecordingHttpFactory _http = null!;
    private IdentityCoverProvider _provider = null!;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "IdentityCoverTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _recorder = new IdentityServiceRecorder();
        _service = _recorder;
        _http = new RecordingHttpFactory();
        _provider = new IdentityCoverProvider(_service, _http, new TestFileManager(_directory),
            NullLogger<IdentityCoverProvider>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, true);

    private static ResourceExternalIdentity Identity(string externalId = "123") => new()
    {
        Id = 1,
        ResourceId = 12,
        ThirdPartyId = ThirdPartyId.Bangumi,
        ExternalId = externalId,
        CoverUrls = ["https://covers.example.test/cover?id=123"]
    };

    private static Resource Resource(params ResourceExternalIdentity[] identities) => new()
    {
        Id = 12,
        ExternalIdentities = [..identities]
    };

    [TestMethod]
    public async Task IdentitiesWithoutKnownCoverUrlsDoNotStartMetadataOrImageRequests()
    {
        var identity = Identity();
        identity.CoverUrls = null;
        var resource = Resource(identity);

        Assert.IsTrue(_provider.AppliesTo(resource));
        Assert.AreEqual(DataStatus.Ready, _provider.GetStatus(resource));
        Assert.IsNull(await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.AreEqual(0, _http.Urls.Count);
        Assert.AreEqual(0, _recorder.Updates.Count);
        Assert.IsFalse(_provider.AppliesTo(Resource()));
    }

    [TestMethod]
    public async Task KnownImageIsSavedAndReusedWithoutAnotherRequest()
    {
        var identity = Identity();
        var resource = Resource(identity);
        Assert.AreEqual(DataStatus.NotStarted, _provider.GetStatus(resource));

        var result = await _provider.GetCoversAsync(resource, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        StringAssert.EndsWith(result[0], ".png");
        CollectionAssert.AreEqual(RecordingHttpFactory.Image, await File.ReadAllBytesAsync(result[0]));
        Assert.AreEqual(1, _recorder.Updates.Count);
        Assert.AreSame(identity, _recorder.Updates[0]);
        Assert.IsNull(identity.CoverDownloadFailedAt);
        Assert.AreEqual(DataStatus.Ready, _provider.GetStatus(resource));
        CollectionAssert.AreEqual(result, await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.AreEqual(1, _http.Urls.Count);
        Assert.IsTrue(_http.ClientNames.All(n => n == InternalOptions.HttpClientNames.Default));
    }

    [TestMethod]
    public async Task CachePathsDoNotCollideAcrossWorkIdsOrSites()
    {
        var first = Identity("../same-work");
        var second = Identity("../different-work");
        var third = Identity("../same-work");
        third.ThirdPartyId = ThirdPartyId.Vndb;
        var paths = new List<string>();

        foreach (var identity in new[] {first, second, third})
        {
            var result = await _provider.GetCoversAsync(Resource(identity), CancellationToken.None);
            Assert.IsNotNull(result);
            paths.AddRange(result);
        }

        Assert.AreEqual(3, paths.Distinct().Count());
        Assert.IsTrue(paths.All(path => Path.GetDirectoryName(path) ==
            Path.Combine(_directory, "covers", "source", "external-identity", "12")));
    }

    [TestMethod]
    public async Task FailedDownloadBacksOffAndRetainsTheUrlForALaterRetry()
    {
        _http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var identity = Identity();
        var resource = Resource(identity);

        Assert.IsNull(await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.IsNotNull(identity.CoverDownloadFailedAt);
        Assert.AreEqual(DataStatus.Failed, _provider.GetStatus(resource));
        Assert.IsNull(await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.AreEqual(1, _http.Urls.Count);
        Assert.AreEqual(1, identity.CoverUrls!.Count);

        identity.CoverDownloadFailedAt = DateTime.Now.AddHours(-25);
        _http.Respond = (_, _) => Task.FromResult(RecordingHttpFactory.ImageResponse());
        Assert.AreEqual(DataStatus.NotStarted, _provider.GetStatus(resource));
        Assert.IsNotNull(await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.IsNull(identity.CoverDownloadFailedAt);
        Assert.AreEqual(2, _http.Urls.Count);
    }

    [TestMethod]
    public async Task OneFailingIdentityDoesNotPreventAnotherIdentityProvidingACover()
    {
        var first = Identity();
        first.CoverUrls = ["https://covers.example.test/missing"];
        var second = Identity("124");
        _http.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/missing"
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : RecordingHttpFactory.ImageResponse());

        var resource = Resource(first, second);
        Assert.IsNotNull(await _provider.GetCoversAsync(resource, CancellationToken.None));
        Assert.IsNotNull(first.CoverDownloadFailedAt);
        Assert.IsNotNull(second.LocalCoverPaths);
        Assert.AreEqual(DataStatus.Ready, _provider.GetStatus(resource));
    }

    [TestMethod]
    public async Task CancellationPropagatesWithoutRecordingAFailedDownload()
    {
        using var cancellation = new CancellationTokenSource();
        _http.Respond = (_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        };
        var identity = Identity();

        try
        {
            await _provider.GetCoversAsync(Resource(identity), cancellation.Token);
            Assert.Fail("Cancellation must propagate.");
        }
        catch (OperationCanceledException)
        {
            Assert.IsNull(identity.CoverDownloadFailedAt);
            Assert.IsNull(identity.LocalCoverPaths);
            Assert.AreEqual(0, _recorder.Updates.Count);
        }
    }

    [TestMethod]
    public async Task HtmlResponseIsNotSavedAsACover()
    {
        _http.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Unavailable</html>", System.Text.Encoding.UTF8, "text/html")
        });
        var identity = Identity();

        Assert.IsNull(await _provider.GetCoversAsync(Resource(identity), CancellationToken.None));
        Assert.IsNotNull(identity.CoverDownloadFailedAt);
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    public async Task InvalidationClearsOnlyThisResourcesIdentityCoverCache()
    {
        await _provider.InvalidateAsync(12);

        CollectionAssert.AreEqual(new[] {12}, _recorder.InvalidatedResourceIds);
        Assert.AreEqual(0, _recorder.Updates.Count);
        Assert.AreEqual(0, _http.Urls.Count);
    }

    private sealed class IdentityServiceRecorder : IResourceExternalIdentityService
    {
        public List<ResourceExternalIdentity> Updates { get; } = [];
        public List<int> InvalidatedResourceIds { get; } = [];

        public Task Update(ResourceExternalIdentity identity)
        {
            Updates.Add(identity);
            return Task.CompletedTask;
        }

        public Task ClearLocalCoverPaths(int resourceId)
        {
            InvalidatedResourceIds.Add(resourceId);
            return Task.CompletedTask;
        }

        public Task<List<ResourceExternalIdentity>> GetByResourceId(int resourceId) =>
            throw new NotSupportedException();
        public Task<Dictionary<int, List<ResourceExternalIdentity>>> GetByResourceIdsGrouped(int[] resourceIds) =>
            throw new NotSupportedException();
        public Task<int?> FindResource(ThirdPartyId thirdPartyId, string externalId) =>
            throw new NotSupportedException();
        public Task<List<int>> FindConflictingResourceIds(int resourceId) => throw new NotSupportedException();
        public Task EnsureIdentities(int resourceId, IEnumerable<ResourceExternalIdentity> identities) =>
            throw new NotSupportedException();
        public Task DeleteByResourceIds(IEnumerable<int> resourceIds) => throw new NotSupportedException();
    }

    private sealed class RecordingHttpFactory : IHttpClientFactory
    {
        public static readonly byte[] Image = [137, 80, 78, 71, 13, 10, 26, 10];
        public List<Uri> Urls { get; } = [];
        public List<string> ClientNames { get; } = [];
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
            (_, _) => Task.FromResult(ImageResponse());

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new HttpClient(new Handler(this));
        }

        public static HttpResponseMessage ImageResponse()
        {
            var content = new ByteArrayContent(Image);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) {Content = content};
        }

        private sealed class Handler(RecordingHttpFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.Urls.Add(request.RequestUri!);
                return owner.Respond(request, cancellationToken);
            }
        }
    }

    private sealed class TestFileManager(string baseDir) : IFileManager
    {
        public string BaseDir => baseDir;
        public string BuildAbsolutePath(params object[] segmentsAfterBaseDir) =>
            Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(segment => segment.ToString()!)]);
        public async Task<string> Save(string path, byte[] data, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data, ct);
            return path;
        }
        public string GetSourceCoverDir(string source, int resourceId) =>
            BuildAbsolutePath("covers", "source", source, resourceId);
        public string GetLocalCoverPathWithoutExtension(int resourceId) => throw new NotSupportedException();
        public string GetManualCoverPath(int resourceId, int index) => throw new NotSupportedException();
        public string GetEnhancerFilePath(string enhancerId, string fileName) => throw new NotSupportedException();
        public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) =>
            throw new NotSupportedException();
        public string GetAttachmentPath(string fileName) => throw new NotSupportedException();
        public string GetAigcGeneratorDir(int generatorId) => throw new NotSupportedException();
        public string GetAigcRunDir(int generatorId, int runId) => throw new NotSupportedException();
        public string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName) =>
            throw new NotSupportedException();
    }
}
