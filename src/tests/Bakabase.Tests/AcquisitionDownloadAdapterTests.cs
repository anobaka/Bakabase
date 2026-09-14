using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionDownloadAdapterTests
{
    private const string Magnet = "magnet:?xt=urn:btih:0123456789012345678901234567890123456789";
    private readonly Downloads _downloads = new();
    private readonly MetadataStore _store = new();
    private readonly List<int> _progress = [];
    private ServiceProvider _services = null!;
    private string _working = null!;

    [TestInitialize]
    public void Setup()
    {
        _working = Path.Combine(Path.GetTempPath(), "BakabaseDownloadAdapter_" + Guid.NewGuid().ToString("N"));
        _services = new ServiceCollection()
            .AddSingleton<IHttpDownloader>(_downloads)
            .AddSingleton<ITorrentDownloader>(_downloads)
            .AddSingleton<IAria2Downloader>(_downloads)
            .AddSingleton<IAcquisitionTorrentMetadataStore>(_store)
            .BuildServiceProvider();
        _downloads.Result = new TorrentDownloadResult(Path.Combine(_working, "torrent-data"),
            [Path.Combine(_working, "torrent-data", "nested", "payload.bin")]);
    }

    [TestCleanup]
    public void Cleanup() => _services.Dispose();

    private AcquisitionStepContext Context(string? config = null) => new(_services, NullLogger.Instance,
        (percent, _) => { _progress.Add(percent); return Task.CompletedTask; }, _working, config);

    private AcquisitionWorkItem Item(AcquisitionDriveKind kind = AcquisitionDriveKind.DirectUrl) => new()
    {
        ResourceId = 9,
        LeadKind = AcquisitionLeadKind.SharedPage,
        LeadValue = "https://example.invalid/sharing-page",
        Links = [new("https://example.invalid/ignored", DriveKind: kind),
            new(kind == AcquisitionDriveKind.Magnet ? Magnet : "https://example.invalid/chosen", DriveKind: kind)],
        SelectedLinkIndex = 1,
        WorkingDirectory = _working,
        Files = ["already-fetched.bin"]
    };

    [TestMethod]
    public async Task Http_DelegatesTheSelectedUrlAndTimeout_WithoutCreatingItsOwnClientOrDirectory()
    {
        var item = Item() with {Files = ["already-fetched.bin", _downloads.HttpResult]};
        using var cancellation = new CancellationTokenSource();
        var outcome = await new FetchHttpStep().ExecuteAsync(Context(
            """{"timeoutMinutes":7,"parallelConnections":8,"maxRetries":5,"speedLimitKiB":1048576}"""),
            item, cancellation.Token);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual("https://example.invalid/chosen", _downloads.HttpRequest!.Url);
        Assert.AreEqual(_working, _downloads.HttpRequest.Directory);
        Assert.AreEqual(TimeSpan.FromMinutes(7), _downloads.HttpRequest.Timeout);
        Assert.AreEqual(8, _downloads.HttpRequest.ParallelConnections);
        Assert.AreEqual(5, _downloads.HttpRequest.MaxRetries);
        Assert.AreEqual(1073741824L, _downloads.HttpRequest.MaximumBytesPerSecond);
        Assert.IsNull(_downloads.HttpRequest.HttpClientName, "acquisition must use the neutral module's default client");
        Assert.IsNull(_downloads.HttpRequest.FileName);
        Assert.AreEqual(cancellation.Token, _downloads.Token);
        CollectionAssert.AreEqual(new[] {25}, _progress);
        CollectionAssert.AreEqual(item.Files.ToArray(), ((AcquisitionStepOutcome.Continue) outcome).Item.Files.ToArray());
        Assert.IsFalse(Directory.Exists(_working), "the adapter must leave filesystem work to the downloader");
    }

    [TestMethod]
    public async Task UnsupportedAndMissingLinks_DoNotStartAnyDownloader()
    {
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(await new FetchHttpStep()
            .ExecuteAsync(Context(), Item(AcquisitionDriveKind.Baidu), CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Skip>(await new FetchMagnetStep()
            .ExecuteAsync(Context(), Item(), CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await new FetchHttpStep()
            .ExecuteAsync(Context(), Item() with {Links = []}, CancellationToken.None));
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await new FetchTorrentStep()
            .ExecuteAsync(Context(), Item() with {Links = [], LeadKind = AcquisitionLeadKind.Torrent, LeadValue = "/private/data.torrent"}, CancellationToken.None));
        Assert.IsNull(_downloads.Method);
        Assert.IsFalse(Directory.Exists(_working));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Magnet_MapsExistingHandlerConfigurationAndPreservesTheDownloadedTree(bool aria2)
    {
        var config = aria2
            ? """{"handler":1,"rpcUrl":"https://aria2.example.invalid/jsonrpc","secret":"saved-token","timeoutMinutes":7,"pollSeconds":11}"""
            : """{"handler":0,"timeoutMinutes":7}""";
        var item = Item(AcquisitionDriveKind.Magnet) with {Files = ["existing", _downloads.Result.Files[0]]};
        var outcome = await new FetchMagnetStep().ExecuteAsync(Context(config), item, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(aria2 ? "aria2" : "magnet", _downloads.Method);
        Assert.AreEqual(Magnet, _downloads.Url);
        Assert.AreEqual(_working, _downloads.Directory);
        Assert.AreEqual(TimeSpan.FromMinutes(7), _downloads.Timeout);
        if (aria2)
        {
            Assert.AreEqual("https://aria2.example.invalid/jsonrpc", _downloads.Aria2Options!.RpcUrl);
            Assert.AreEqual("saved-token", _downloads.Aria2Options.Secret);
            Assert.AreEqual(TimeSpan.FromSeconds(11), _downloads.Aria2Options.PollInterval);
        }
        var result = ((AcquisitionStepOutcome.Continue) outcome).Item;
        Assert.IsTrue(result.PreserveDirectoryStructure);
        Assert.AreEqual(_downloads.Result.Directory, result.ExtractedDirectory);
        CollectionAssert.AreEqual(item.Files.ToArray(), result.Files.ToArray());
        CollectionAssert.AreEqual(new[] {25}, _progress);
    }

    [TestMethod]
    public async Task TorrentUrl_FromASelectedSharingLink_IsReadByTheNeutralDownloader()
    {
        var outcome = await new FetchTorrentStep().ExecuteAsync(Context("""{"timeoutMinutes":7}"""),
            Item(), CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual("torrent-url", _downloads.Method);
        Assert.AreEqual("https://example.invalid/chosen", _downloads.Url);
        Assert.AreEqual(_working, _downloads.Directory);
        Assert.AreEqual(TimeSpan.FromMinutes(7), _downloads.Timeout);
        Assert.IsNull(_store.ReadReference);
        var result = ((AcquisitionStepOutcome.Continue) outcome).Item;
        Assert.IsTrue(result.PreserveDirectoryStructure);
        Assert.AreEqual(_downloads.Result.Directory, result.ExtractedDirectory);
        CollectionAssert.AreEqual(new[] {"already-fetched.bin"}.Concat(_downloads.Result.Files).ToArray(), result.Files.ToArray());
    }

    [TestMethod]
    public async Task UploadedTorrent_ResolvesOnlyTheManagedReference_ThenDelegatesItsBytes()
    {
        var reference = AcquisitionTorrentMetadataStore.ReferencePrefix + new string('a', 64);
        var item = Item() with {Links = [], LeadKind = AcquisitionLeadKind.Torrent, LeadValue = reference};
        var outcome = await new FetchTorrentStep().ExecuteAsync(Context(), item, CancellationToken.None);

        Assert.IsInstanceOfType<AcquisitionStepOutcome.Continue>(outcome);
        Assert.AreEqual(reference, _store.ReadReference);
        Assert.AreEqual("torrent-bytes", _downloads.Method);
        CollectionAssert.AreEqual(_store.Bytes, _downloads.Metadata);
        Assert.AreEqual(TimeSpan.FromHours(4), _downloads.Timeout);
    }

    [DataTestMethod]
    [DataRow("http")]
    [DataRow("magnet")]
    [DataRow("aria2")]
    [DataRow("torrent")]
    public async Task TimeoutIsAFailedStep_ButCallerCancellationStillPropagates(string kind)
    {
        var step = Step(kind);
        var item = Item(kind is "magnet" or "aria2" ? AcquisitionDriveKind.Magnet : AcquisitionDriveKind.DirectUrl);
        var context = Context(kind == "aria2"
            ? """{"handler":1,"timeoutMinutes":7}"""
            : """{"timeoutMinutes":7}""");
        _downloads.Failure = new TimeoutException("The download deadline elapsed.");
        Assert.IsInstanceOfType<AcquisitionStepOutcome.Fail>(await step.ExecuteAsync(context, item, CancellationToken.None));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _downloads.Failure = new OperationCanceledException(cancellation.Token);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => step.ExecuteAsync(context, item, cancellation.Token));
    }

    private static IAcquisitionStep Step(string kind) => kind switch
    {
        "http" => new FetchHttpStep(),
        "magnet" or "aria2" => new FetchMagnetStep(),
        _ => new FetchTorrentStep()
    };

    private sealed class MetadataStore : IAcquisitionTorrentMetadataStore
    {
        public readonly byte[] Bytes = [1, 2, 3];
        public string? ReadReference;
        public Task<string> SaveAsync(byte[] metadata, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string reference, CancellationToken ct = default)
        {
            ReadReference = reference;
            return Task.FromResult(Bytes);
        }
    }

    private sealed class Downloads : IHttpDownloader, ITorrentDownloader, IAria2Downloader
    {
        public string? Method, Url, Directory;
        public TimeSpan Timeout;
        public CancellationToken Token;
        public HttpDownloadRequest? HttpRequest;
        public Aria2DownloadOptions? Aria2Options;
        public byte[]? Metadata;
        public Exception? Failure;
        public string HttpResult = "downloaded.bin";
        public TorrentDownloadResult Result = null!;

        public async Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            HttpRequest = request;
            Method = "http";
            await Report(progress, ct);
            return HttpResult;
        }

        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnet, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) =>
            Download("magnet", magnet, workingDirectory, timeout, progress, ct);

        public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Metadata = metadata;
            return Download("torrent-bytes", null, workingDirectory, timeout, progress, ct);
        }

        public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) =>
            Download("torrent-url", url, workingDirectory, timeout, progress, ct);

        public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnet, string workingDirectory,
            Aria2DownloadOptions options, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Aria2Options = options;
            return Download("aria2", magnet, workingDirectory, options.Timeout, progress, ct);
        }

        private async Task<TorrentDownloadResult> Download(string method, string? url, string directory,
            TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Method = method;
            Url = url;
            Directory = directory;
            Timeout = timeout;
            await Report(progress, ct);
            return Result;
        }

        private async Task Report(Func<int, string?, Task>? progress, CancellationToken ct)
        {
            Token = ct;
            if (Failure != null) throw Failure;
            if (progress != null) await progress(25, "Downloading");
        }
    }
}
