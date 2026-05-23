using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for CoverDiscoverer: locating a resource's cover image
/// from the file system — the named-"cover" priority, first-image fallback,
/// modify-time ordering, and the empty/missing-path cases. The video and
/// archive sources need FfMpeg/real archives and are out of scope here.
/// </summary>
[TestClass]
public sealed class CoverDiscoveryTests
{
    private static CoverDiscoverer _discoverer = null!;
    private string _dir = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        var sp = await TestServiceBuilder.BuildServiceProvider();
        _discoverer = new CoverDiscoverer(
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<FfMpegService>());
    }

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"CoverDiscoveryTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            try { Directory.Delete(_dir, true); } catch { }
        }
    }

    private void Touch(string name) => File.WriteAllBytes(Path.Combine(_dir, name), Array.Empty<byte>());

    private Task<Abstractions.Components.Cover.CoverDiscoveryResult?> Discover(
        string path, CoverSelectOrder order = CoverSelectOrder.FilenameAscending) =>
        _discoverer.Discover(path, order, useIconAsFallback: false, CancellationToken.None);

    [TestMethod]
    public async Task Discover_NonExistentPath_ReturnsNull()
    {
        Assert.IsNull(await Discover(Path.Combine(_dir, "does-not-exist")));
    }

    [TestMethod]
    public async Task Discover_DirectoryWithNamedCover_ReturnsIt()
    {
        Touch("cover.jpg");
        var result = await Discover(_dir);
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Path.EndsWith("cover.jpg", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.IsVirtualPath);
    }

    [TestMethod]
    public async Task Discover_NamedCoverWins_OverOtherImages()
    {
        Touch("aaa.png");
        Touch("cover.jpg");
        var result = await Discover(_dir);
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Path.EndsWith("cover.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Discover_NoNamedCover_FallsBackToTheSingleImage()
    {
        Touch("photo.jpg");
        var result = await Discover(_dir);
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Path.EndsWith("photo.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Discover_SingleImageFilePath_ReturnsThatFile()
    {
        Touch("picture.png");
        var result = await Discover(Path.Combine(_dir, "picture.png"));
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Path.EndsWith("picture.png", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Discover_NoImages_ReturnsNull()
    {
        Touch("readme.txt");
        Touch("notes.md");
        Assert.IsNull(await Discover(_dir));
    }

    [TestMethod]
    public async Task Discover_ModifyTimeDescendingOrder_PicksNewestImage()
    {
        Touch("older.jpg");
        Touch("newer.jpg");
        File.SetLastWriteTime(Path.Combine(_dir, "older.jpg"), new DateTime(2020, 1, 1));
        File.SetLastWriteTime(Path.Combine(_dir, "newer.jpg"), new DateTime(2024, 1, 1));

        var result = await Discover(_dir, CoverSelectOrder.FileModifyDtDescending);
        Assert.IsNotNull(result);
        Assert.IsTrue(result!.Path.EndsWith("newer.jpg", StringComparison.OrdinalIgnoreCase));
    }
}
