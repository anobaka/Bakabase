using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for resource-type path mark sync: the FileSystem resolver's
/// Layer / Regex matching, the file-system-type and extension filters, and the
/// sync lifecycle (re-sync idempotency, picking up new directories, resource
/// removal when the owning mark is soft-deleted).
/// </summary>
[TestClass]
public sealed class PathMarkResourceSyncTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkResourceSyncTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private string Sub(string relative) => Path.Combine(_testRoot, relative);

    private void MakeDirs(params string[] relatives)
    {
        foreach (var r in relatives)
        {
            Directory.CreateDirectory(Sub(r));
        }
    }

    private void MakeFiles(params string[] relatives)
    {
        foreach (var r in relatives)
        {
            var full = Sub(r);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, Array.Empty<byte>());
        }
    }

    private static ResourceMarkConfig LayerConfig(
        int layer,
        PathFilterFsType? fsType = PathFilterFsType.Directory,
        List<string>? extensions = null)
        => new()
        {
            MatchMode = PathMatchMode.Layer,
            Layer = layer,
            FsTypeFilter = fsType,
            Extensions = extensions
        };

    private async Task<PathMark> AddMark(string path, ResourceMarkConfig config)
        => await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = path,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(config),
            Priority = 100
        });

    private Task Sync() => _sp.GetRequiredService<ResourceSyncService>()
        .SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);

    private async Task<int> ResourceCount()
        => (await _sp.GetRequiredService<IResourceService>().GetAll()).Count;

    #region Layer mode

    [TestMethod]
    public async Task Layer1_Directory_DirectChildDirectoriesBecomeResources()
    {
        MakeDirs("A", "B", "C");
        await AddMark(_testRoot, LayerConfig(1));
        await Sync();
        Assert.AreEqual(3, await ResourceCount());
    }

    [TestMethod]
    public async Task Layer2_Directory_GrandchildDirectoriesBecomeResources()
    {
        MakeDirs("p/a", "p/b", "q/c");
        await AddMark(_testRoot, LayerConfig(2));
        await Sync();
        // Only the three layer-2 directories; the intermediate p / q are not resources.
        Assert.AreEqual(3, await ResourceCount());
    }

    [TestMethod]
    public async Task Layer0_Directory_MarkedPathItselfBecomesResource()
    {
        MakeDirs("Solo");
        await AddMark(Sub("Solo"), LayerConfig(0));
        await Sync();
        Assert.AreEqual(1, await ResourceCount());
    }

    [TestMethod]
    public async Task Layer1_Directory_LooseFilesAreIgnored()
    {
        MakeDirs("d1", "d2");
        MakeFiles("loose1.txt", "loose2.txt");
        await AddMark(_testRoot, LayerConfig(1, PathFilterFsType.Directory));
        await Sync();
        Assert.AreEqual(2, await ResourceCount());
    }

    [TestMethod]
    public async Task Layer1_File_OnlyFilesBecomeResources()
    {
        MakeDirs("sub");
        MakeFiles("a.mp4", "b.mp4");
        await AddMark(_testRoot, LayerConfig(1, PathFilterFsType.File));
        await Sync();
        Assert.AreEqual(2, await ResourceCount());
    }

    [TestMethod]
    public async Task Layer1_File_ExtensionFilter_LimitsToMatchingExtensions()
    {
        MakeFiles("a.txt", "b.jpg", "c.txt");
        await AddMark(_testRoot, LayerConfig(1, PathFilterFsType.File, extensions: [".txt"]));
        await Sync();
        Assert.AreEqual(2, await ResourceCount());
    }

    #endregion

    #region Regex mode

    [TestMethod]
    public async Task Regex_MatchesDirectoriesByPattern()
    {
        MakeDirs("Movie01", "Movie02", "Other");
        await AddMark(_testRoot, new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Regex,
            Regex = "Movie",
            FsTypeFilter = PathFilterFsType.Directory
        });
        await Sync();
        Assert.AreEqual(2, await ResourceCount());
    }

    #endregion

    #region Lifecycle

    [TestMethod]
    public async Task EmptyDirectory_SyncProducesNoResources()
    {
        MakeDirs("Empty");
        await AddMark(Sub("Empty"), LayerConfig(1));
        await Sync();
        Assert.AreEqual(0, await ResourceCount());
    }

    [TestMethod]
    public async Task ReSync_MarkPendingAgain_DoesNotDuplicateResources()
    {
        MakeDirs("A", "B", "C");
        var mark = await AddMark(_testRoot, LayerConfig(1));
        await Sync();
        Assert.AreEqual(3, await ResourceCount());

        await _sp.GetRequiredService<IPathMarkService>().MarkAsPending(mark.Id);
        await Sync();
        Assert.AreEqual(3, await ResourceCount());
    }

    [TestMethod]
    public async Task ReSync_AfterAddingDirectory_PicksUpTheNewResource()
    {
        MakeDirs("A", "B");
        var mark = await AddMark(_testRoot, LayerConfig(1));
        await Sync();
        Assert.AreEqual(2, await ResourceCount());

        MakeDirs("C");
        await _sp.GetRequiredService<IPathMarkService>().MarkAsPending(mark.Id);
        await Sync();
        Assert.AreEqual(3, await ResourceCount());
    }

    [TestMethod]
    public async Task SoftDeleteMark_ThenSync_DeletesOwnedResources()
    {
        MakeDirs("A", "B");
        var mark = await AddMark(_testRoot, LayerConfig(1));
        await Sync();
        Assert.AreEqual(2, await ResourceCount());

        await _sp.GetRequiredService<IPathMarkService>().SoftDelete(mark.Id);
        await Sync();
        Assert.AreEqual(0, await ResourceCount());
    }

    #endregion
}
