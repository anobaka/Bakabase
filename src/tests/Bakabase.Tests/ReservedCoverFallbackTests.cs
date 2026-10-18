using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Cover;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Providers.Cover;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Bakabase.Tests;

[TestClass]
public sealed class ReservedCoverFallbackTests
{
    private string _root = null!;
    private IServiceProvider? _services;
    private FixtureFileManager _files = null!;

    [TestInitialize]
    public void Initialize()
    {
        _root = TestServiceBuilder.NewTestDirectory();
        Directory.CreateDirectory(_root);
        _files = new FixtureFileManager(Path.Combine(_root, "data"));
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_services is IAsyncDisposable lifetime) await lifetime.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestMethod]
    public async Task MissingEarlierScope_UsesExistingLaterScopeWithoutChangingMetadata()
    {
        var missing = Path.Combine(_root, "missing-manual.jpg");
        var enhancer = await ImageFile("enhancer.jpg");
        var resource = WithCovers((PropertyValueScope.Av, [enhancer]), (PropertyValueScope.Manual, [missing]));

        var covers = await new ManualCoverProvider().GetCoversAsync(resource, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { enhancer }, covers);
        CollectionAssert.AreEqual(new[] { missing }, CoverValues(resource).Single(v => v.Scope == 0).Value as List<string>);
    }

    [TestMethod]
    public async Task PartiallyMissingScope_PreservesValidOrderAndDoesNotFallThrough()
    {
        var first = await ImageFile("first.jpg");
        var second = await ImageFile("second.jpg");
        var later = await ImageFile("later.jpg");
        List<string> original = [Path.Combine(_root, "missing.jpg"), first, second];
        var resource = WithCovers((PropertyValueScope.Av, [later]), (PropertyValueScope.Manual, original));

        var covers = await new ManualCoverProvider().GetCoversAsync(resource, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { first, second }, covers);
        Assert.AreEqual(3, original.Count, "Filtering a display result must not delete stored cover references.");
    }

    [TestMethod]
    public async Task ArchiveEntryCover_RemainsEligibleWhileItsContainerExists()
    {
        var archivePath = Path.Combine(_root, "images.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            archive.CreateEntry("images/cover.jpg");
        var cover = archivePath + "!images/cover.jpg";
        var resource = WithCovers((PropertyValueScope.Manual, [cover]));

        CollectionAssert.AreEqual(new[] { cover },
            await new ManualCoverProvider().GetCoversAsync(resource, CancellationToken.None));

        File.Delete(archivePath);
        Assert.IsNull(await new ManualCoverProvider().GetCoversAsync(resource, CancellationToken.None));
    }

    [TestMethod]
    public async Task HttpEntry_IsLeftToItsExistingProviderContractWithoutLocalFileValidation()
    {
        const string url = "https://example.invalid/cover.jpg";

        CollectionAssert.AreEqual(new[] { url }, await new ManualCoverProvider().GetCoversAsync(
            WithCovers((PropertyValueScope.Manual, [url])), CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingReservedCovers_FallBackToExistingFilesystemCacheOnInitialLoad()
    {
        var resource = await SeedResource();
        var missing = await AddMissingReservedCover(resource.Id);
        var cached = await ImageFile("cached.jpg");
        await SetCache(resource.Id, [cached]);

        var loaded = await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover);

        CollectionAssert.AreEqual(new[] { cached }, loaded!.Covers);
        Assert.AreEqual(DataStatus.Ready, FileSystemState(loaded));
        await AssertReservedCoverUnchanged(resource.Id, missing);
    }

    [TestMethod]
    public async Task MissingPositiveCache_AdvertisesDiscoveryAndRecoversFromFolderWithoutRefresh()
    {
        var resource = await SeedResource();
        await ImageFile(Path.Combine("media", "cover.jpg"), 32);
        var missing = await AddMissingReservedCover(resource.Id);
        await SetCache(resource.Id, [missing]);

        var loaded = await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover);

        Assert.IsNull(loaded!.Covers);
        Assert.AreEqual(DataStatus.NotStarted, FileSystemState(loaded),
            "The existing discovery stream must be requested for an unusable positive cache.");

        var covers = await LocalProvider.GetCoversAsync(loaded, CancellationToken.None);

        Assert.IsNotNull(covers);
        Assert.IsTrue(File.Exists(covers[0]));
        Assert.AreEqual(32, Image.Identify(covers[0]).Width);
        var reloaded = await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover);
        CollectionAssert.AreEqual(covers, reloaded!.Covers);
        Assert.AreEqual(DataStatus.Ready, FileSystemState(reloaded));
        await AssertReservedCoverUnchanged(resource.Id, missing);
    }

    [TestMethod]
    public async Task PartiallyMissingPositiveCache_ReturnsOnlyExistingFilesInOrder()
    {
        var resource = await SeedResource();
        var first = await ImageFile("cached-first.jpg");
        var second = await ImageFile("cached-second.jpg");
        await SetCache(resource.Id, [Path.Combine(_root, "missing.jpg"), first, second]);

        var loaded = await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover);

        CollectionAssert.AreEqual(new[] { first, second }, loaded!.Covers);
        Assert.AreEqual(DataStatus.Ready, FileSystemState(loaded));
    }

    [TestMethod]
    public async Task IntentionalEmptyCache_RemainsReadyEvenWhenFolderContainsAnImage()
    {
        var resource = await SeedResource();
        await ImageFile(Path.Combine("media", "cover.jpg"));
        await SetCache(resource.Id, null);

        var loaded = await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover);

        Assert.IsNull(loaded!.Covers);
        Assert.AreEqual(DataStatus.Ready, FileSystemState(loaded));
        Assert.IsNull(await LocalProvider.GetCoversAsync(loaded, CancellationToken.None));
    }

    [TestMethod]
    public async Task RefreshWithMissingReservedCover_RebuildsThumbnailInsteadOfCachingMissingPath()
    {
        var resource = await SeedResource();
        await ImageFile(Path.Combine("media", "cover.jpg"), 32);
        var missing = await AddMissingReservedCover(resource.Id);
        var oldThumbnail = _files.GetLocalCoverPathWithoutExtension(resource.Id) + ".jpg";
        await ImageFile(Path.GetRelativePath(_root, oldThumbnail), 4);
        await SetCache(resource.Id, [oldThumbnail]);

        var refreshed = await ResourceService.RefreshResourceCache(resource.Id, CancellationToken.None);

        Assert.IsNotNull(refreshed!.CoverPaths);
        Assert.IsTrue(refreshed.CoverPaths.All(File.Exists));
        Assert.AreEqual(32, Image.Identify(refreshed.CoverPaths[0]).Width,
            "The previous thumbnail is replaced with an image rediscovered from the folder.");
        CollectionAssert.AreEqual(refreshed.CoverPaths,
            (await ResourceService.Get(resource.Id, ResourceAdditionalItem.Cover))!.Covers);
        await AssertReservedCoverUnchanged(resource.Id, missing);
    }

    [TestMethod]
    public async Task RefreshWithMissingEarlierScope_KeepsExistingLaterScope()
    {
        var resource = await SeedResource();
        var missing = await AddMissingReservedCover(resource.Id, PropertyValueScope.Manual);
        var enhancer = await ImageFile("enhancer.jpg");
        await ReservedService.Add(new ReservedPropertyValue
        {
            ResourceId = resource.Id, Scope = (int)PropertyValueScope.Av, CoverPaths = [enhancer]
        });

        var refreshed = await ResourceService.RefreshResourceCache(resource.Id, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { enhancer }, refreshed!.CoverPaths);
        await AssertReservedCoverUnchanged(resource.Id, missing, PropertyValueScope.Manual);
    }

    private IResourceService ResourceService => _services!.GetRequiredService<IResourceService>();
    private IReservedPropertyValueService ReservedService => _services!.GetRequiredService<IReservedPropertyValueService>();
    private LocalFileCoverProvider LocalProvider => _services!.GetServices<ICoverProvider>()
        .OfType<LocalFileCoverProvider>().Single();

    private async Task<Resource> SeedResource()
    {
        _services = await TestServiceBuilder.BuildServiceProvider(_root, services =>
        {
            services.AddSingleton<IFileManager>(_files);
            services.AddSingleton<ICoverDiscoverer, CoverDiscoverer>();
        });
        var path = Path.Combine(_root, "media");
        Directory.CreateDirectory(path);
        var resource = new Resource { Path = path, IsFile = false };
        await ResourceService.AddOrPutRange([resource]);
        return resource;
    }

    private async Task SetCache(int resourceId, List<string>? paths) =>
        await _services!.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceCacheDbModel, int>>()
            .Add(new ResourceCacheDbModel
            {
                ResourceId = resourceId, CachedTypes = ResourceCacheType.Covers,
                CoverPaths = ResourceCacheExtensions.SerializeCoverPathsForDb(paths)
            });

    private async Task<string> AddMissingReservedCover(int resourceId, PropertyValueScope scope = PropertyValueScope.Av)
    {
        var missing = Path.Combine(_root, "old-enhancer", "missing.jpg");
        await ReservedService.Add(new ReservedPropertyValue
        {
            ResourceId = resourceId, Scope = (int)scope, CoverPaths = [missing]
        });
        return missing;
    }

    private async Task AssertReservedCoverUnchanged(int resourceId, string missing,
        PropertyValueScope scope = PropertyValueScope.Av) =>
        CollectionAssert.AreEqual(new[] { missing }, (await ReservedService.GetFirst(
            value => value.ResourceId == resourceId && value.Scope == (int)scope))!.CoverPaths);

    private static DataStatus FileSystemState(Resource resource) => resource.DataStates!
        .Single(state => state.DataType == ResourceDataType.Cover && state.Origin == DataOrigin.FileSystem).Status;

    private async Task<string> ImageFile(string relativePath, int width = 12)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgb24>(width, 8);
        await image.SaveAsJpegAsync(path);
        return path;
    }

    private static Resource WithCovers(params (PropertyValueScope Scope, List<string> Paths)[] values) => new()
    {
        Properties = new Dictionary<int, Dictionary<int, Resource.Property>>
        {
            [(int)PropertyPool.Reserved] = new()
            {
                [(int)ReservedProperty.Cover] = new Resource.Property("Cover", PropertyType.Attachment,
                    values.Select(value => new Resource.Property.PropertyValue(
                        (int)value.Scope, value.Paths, value.Paths, value.Paths)).ToList())
            }
        }
    };

    private static List<Resource.Property.PropertyValue> CoverValues(Resource resource) =>
        resource.Properties![(int)PropertyPool.Reserved][(int)ReservedProperty.Cover].Values!;

    private sealed class FixtureFileManager(string baseDir) : IFileManager
    {
        public string BaseDir => baseDir;
        public string BuildAbsolutePath(params object[] segmentsAfterBaseDir) =>
            Path.Combine([BaseDir, ..segmentsAfterBaseDir.Select(segment => segment.ToString()!)]);
        public async Task<string> Save(string path, byte[] data, CancellationToken ct)
        {
            var absolute = BuildAbsolutePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            await File.WriteAllBytesAsync(absolute, data, ct);
            return absolute;
        }
        public string GetLocalCoverPathWithoutExtension(int id) => BuildAbsolutePath("covers", "local", id);
        public string GetSourceCoverDir(string source, int id) => BuildAbsolutePath("covers", "source", source, id);
        public string GetManualCoverPath(int id, int index) => BuildAbsolutePath("covers", "manual", $"{id}-{index}.jpg");
        public string GetEnhancerFilePath(string id, string name) => BuildAbsolutePath("enhancers", id, name);
        public string GetEnhancerResourceFilePath(string id, string resource, string name) => BuildAbsolutePath("enhancers", id, resource, name);
        public string GetAttachmentPath(string name) => BuildAbsolutePath("attachments", name);
        public string GetAigcGeneratorDir(int id) => BuildAbsolutePath("aigc", $"g{id}");
        public string GetAigcRunDir(int id, int run) => BuildAbsolutePath("aigc", $"g{id}", $"r{run}");
        public string GetAigcArtifactRelativePath(int id, int run, string name) => Path.Combine("aigc", $"g{id}", $"r{run}", name);
    }
}
