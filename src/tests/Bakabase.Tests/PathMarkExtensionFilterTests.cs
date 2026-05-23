using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for resource-mark file-extension filtering: an explicit Extensions list, an
/// ExtensionGroupIds reference, and the two merged together.
/// </summary>
[TestClass]
public sealed class PathMarkExtensionFilterTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkExtTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private void MakeFiles(params string[] names)
    {
        foreach (var n in names)
        {
            File.WriteAllBytes(Path.Combine(_testRoot, n), Array.Empty<byte>());
        }
    }

    private async Task SyncWithFileMark(List<string>? extensions, List<int>? extensionGroupIds)
    {
        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.File,
                Extensions = extensions,
                ExtensionGroupIds = extensionGroupIds
            }),
            Priority = 100
        });
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
    }

    private async Task<int> ResourceCount()
        => (await _sp.GetRequiredService<IResourceService>().GetAll()).Count;

    private async Task<int> AddExtensionGroup(params string[] extensions)
    {
        var group = await _sp.GetRequiredService<IExtensionGroupService>()
            .Add(new ExtensionGroupAddInputModel("grp", [.. extensions]));
        return group.Id;
    }

    [TestMethod]
    public async Task Extensions_KeepsOnlyMatchingFiles()
    {
        MakeFiles("a.txt", "b.jpg", "c.txt");
        await SyncWithFileMark([".txt"], null);
        Assert.AreEqual(2, await ResourceCount());
    }

    [TestMethod]
    public async Task ExtensionGroupIds_FiltersUsingTheGroupExtensions()
    {
        MakeFiles("a.txt", "b.jpg", "pic.png");
        var groupId = await AddExtensionGroup(".jpg", ".png");
        await SyncWithFileMark(null, [groupId]);
        Assert.AreEqual(2, await ResourceCount());
    }

    [TestMethod]
    public async Task Extensions_AndExtensionGroup_AreMerged()
    {
        MakeFiles("a.txt", "b.jpg", "c.png");
        var groupId = await AddExtensionGroup(".jpg");
        await SyncWithFileMark([".txt"], [groupId]);
        // .txt (explicit) + .jpg (group) -> a.txt and b.jpg; c.png excluded.
        Assert.AreEqual(2, await ResourceCount());
    }
}
