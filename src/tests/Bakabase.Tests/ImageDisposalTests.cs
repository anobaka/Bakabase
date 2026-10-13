using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using LocalFileCoverProvider = Bakabase.InsideWorld.Business.Components.Providers.Cover.LocalFileCoverProvider;

namespace Bakabase.Tests;

// These tests temporarily replace ImageSharp's default allocator. They observe actual
// buffer ownership, without requiring a GC or a platform-specific RSS threshold.
[TestClass]
[DoNotParallelize]
public sealed class ImageDisposalTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private TestFileManager _files = null!;
    private byte[] _png = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _root = TestServiceBuilder.NewTestDirectory();
        _files = new TestFileManager(Path.Combine(_root, "data"));
        using (var image = new Image<Rgba32>(16, 16))
        using (var output = new MemoryStream())
        {
            image[0, 0] = new Rgba32(200, 100, 50, 255);
            await image.SaveAsPngAsync(output);
            _png = output.ToArray();
        }
        _services = await TestServiceBuilder.BuildServiceProvider(_root, services =>
        {
            services.AddSingleton<IFileManager>(_files);
            services.AddSingleton<ICoverDiscoverer>(new FixtureDiscoverer(_png));
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task Automatic_cover_releases_decoded_pixels_after_saving_thumbnail()
    {
        var resource = await SeedResource();
        var allocator = new TrackingAllocator();
        var result = await WithAllocator(allocator, () => Provider().GetCoversAsync(resource, CancellationToken.None));

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(File.Exists(result[0]));
        Assert.AreEqual(16, Image.Identify(result[0]).Width);
        AssertReleased(allocator);
    }

    [TestMethod]
    public async Task Automatic_cover_releases_decoded_pixels_when_thumbnail_write_fails()
    {
        var resource = await SeedResource();
        var path = _files.GetLocalCoverPathWithoutExtension(resource.Id);
        Directory.CreateDirectory(path + ".png");
        Directory.CreateDirectory(path + ".jpg");
        var allocator = new TrackingAllocator();

        Assert.IsNull(await WithAllocator(allocator, () => Provider().GetCoversAsync(resource, CancellationToken.None)));
        AssertReleased(allocator);
    }

    [TestMethod]
    public async Task Manual_cover_releases_decoded_pixels_after_saving_jpeg()
    {
        var resource = await SeedResource();
        var allocator = new TrackingAllocator();
        await WithAllocator(allocator, () => _services.GetRequiredService<IResourceService>()
            .SaveCover(resource.Id, _png, CoverSaveMode.Prepend));

        var output = _files.GetManualCoverPath(resource.Id, 0);
        Assert.IsTrue(File.Exists(output));
        Assert.AreEqual(16, Image.Identify(output).Width);
        AssertReleased(allocator);
    }

    [TestMethod]
    public async Task Manual_cover_releases_decoded_pixels_when_jpeg_write_fails()
    {
        var resource = await SeedResource();
        Directory.CreateDirectory(_files.GetManualCoverPath(resource.Id, 0));
        var allocator = new TrackingAllocator();
        Exception? failure = null;
        try
        {
            await WithAllocator(allocator, () => _services.GetRequiredService<IResourceService>()
                .SaveCover(resource.Id, _png, CoverSaveMode.Prepend));
        }
        catch (Exception e)
        {
            failure = e;
        }

        Assert.IsTrue(failure is IOException or UnauthorizedAccessException,
            "the fixture must fail while writing the decoded image");
        AssertReleased(allocator);
        Assert.IsFalse((await _services.GetRequiredService<IReservedPropertyValueService>().GetAll())
            .Any(v => v.ResourceId == resource.Id && v.CoverPaths?.Count > 0));
    }

    [TestMethod]
    public async Task Virtual_cover_transfers_image_ownership_to_the_caller()
    {
        var allocator = new TrackingAllocator();
        using (var image = await WithAllocator(allocator, () =>
                   new CoverDiscoveryResult(true, "fixture.png", ".png", _png)
                       .LoadByImageSharp(CancellationToken.None)))
        {
            Assert.AreEqual(16, image.Width);
            Assert.IsTrue(allocator.ActiveOwners > 0, "the returned image must remain usable until caller disposal");
            Assert.AreEqual((byte)200, image[0, 0].R);
        }
        AssertReleased(allocator);
    }

    [TestMethod]
    public async Task Cancelled_virtual_cover_does_not_leave_owned_buffers()
    {
        var allocator = new TrackingAllocator();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = false;
        try
        {
            using var image = await WithAllocator(allocator, () =>
                new CoverDiscoveryResult(true, "fixture.png", ".png", _png).LoadByImageSharp(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Assert.IsTrue(cancelled);
        Assert.AreEqual(0, allocator.ActiveOwners);
    }

    private static void AssertReleased(TrackingAllocator allocator)
    {
        Assert.IsTrue(allocator.TotalAllocations > 0, "the production path must really decode an image");
        Assert.AreEqual(0, allocator.ActiveOwners, "all decoded image buffers must be disposed before returning");
    }

    private static async Task<T> WithAllocator<T>(TrackingAllocator allocator, Func<Task<T>> action)
    {
        var previous = Configuration.Default.MemoryAllocator;
        Configuration.Default.MemoryAllocator = allocator;
        try { return await action(); }
        finally { Configuration.Default.MemoryAllocator = previous; }
    }

    private static Task WithAllocator(TrackingAllocator allocator, Func<Task> action) =>
        WithAllocator(allocator, async () => { await action(); return true; });

    private LocalFileCoverProvider Provider() => _services.GetServices<ICoverProvider>()
        .OfType<LocalFileCoverProvider>().Single();

    private async Task<Resource> SeedResource()
    {
        var mediaRoot = Path.Combine(_root, "media");
        Directory.CreateDirectory(Path.Combine(mediaRoot, "album"));
        await _services.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = mediaRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer, Layer = 1, FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });
        await _services.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
        return (await _services.GetRequiredService<IResourceService>().GetAll()).Single();
    }

    private sealed class FixtureDiscoverer(byte[] data) : ICoverDiscoverer
    {
        public Task<CoverDiscoveryResult?> Discover(string path, CoverSelectOrder order, bool useIconAsFallback,
            CancellationToken ct) => Task.FromResult<CoverDiscoveryResult?>(
            new CoverDiscoveryResult(true, "fixture.png", ".png", data));
    }

    private sealed class TrackingAllocator : MemoryAllocator
    {
        private int _activeOwners;
        private int _totalAllocations;
        public int ActiveOwners => Volatile.Read(ref _activeOwners);
        public int TotalAllocations => Volatile.Read(ref _totalAllocations);
        protected override int GetBufferCapacityInBytes() => int.MaxValue;

        public override IMemoryOwner<T> Allocate<T>(int length, AllocationOptions options = AllocationOptions.None)
        {
            Interlocked.Increment(ref _activeOwners);
            Interlocked.Increment(ref _totalAllocations);
            return new Owner<T>(new T[length], () => Interlocked.Decrement(ref _activeOwners));
        }

        private sealed class Owner<T>(T[] data, Action release) : IMemoryOwner<T> where T : struct
        {
            private T[]? _data = data;
            public Memory<T> Memory => _data ?? throw new ObjectDisposedException(nameof(Owner<T>));
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _data, null) != null) release();
            }
        }
    }

    private sealed class TestFileManager(string baseDir) : IFileManager
    {
        public string BaseDir => baseDir;
        public string BuildAbsolutePath(params object[] segmentsAfterBaseDir) =>
            Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(s => s.ToString()!)]);
        public async Task<string> Save(string path, byte[] data, CancellationToken ct)
        {
            var output = BuildAbsolutePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllBytesAsync(output, data, ct);
            return output;
        }
        public string GetLocalCoverPathWithoutExtension(int resourceId) => BuildAbsolutePath("covers", "local", resourceId);
        public string GetSourceCoverDir(string source, int resourceId) => BuildAbsolutePath("covers", "source", source, resourceId);
        public string GetManualCoverPath(int resourceId, int index) => BuildAbsolutePath("covers", "manual", $"{resourceId}-{index}.jpg");
        public string GetEnhancerFilePath(string enhancerId, string fileName) => BuildAbsolutePath("enhancers", enhancerId, fileName);
        public string GetEnhancerResourceFilePath(string enhancerId, string resourceIdPart, string fileName) => BuildAbsolutePath("enhancers", enhancerId, resourceIdPart, fileName);
        public string GetAttachmentPath(string fileName) => BuildAbsolutePath("attachments", fileName);
        public string GetAigcGeneratorDir(int generatorId) => BuildAbsolutePath("aigc", $"g{generatorId}");
        public string GetAigcRunDir(int generatorId, int runId) => BuildAbsolutePath("aigc", $"g{generatorId}", $"r{runId}");
        public string GetAigcArtifactRelativePath(int generatorId, int runId, string fileName) => Path.Combine("aigc", $"g{generatorId}", $"r{runId}", fileName);
    }
}
