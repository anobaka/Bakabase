using System;
using System.IO;
using System.Linq;
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
/// Coverage for two resource-mark discovery features the existing path-mark tests miss:
/// negative Layer values (which resolve to an ancestor directory) and the
/// IsResourceBoundary flag (which stops an outer mark from claiming resources inside a
/// nested subtree).
/// </summary>
[TestClass]
public sealed class PathMarkResourceBoundaryTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"PathMarkBoundaryTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private void MakeDir(string relative) => Directory.CreateDirectory(Path.Combine(_testRoot, relative));

    private Task AddMark(string path, ResourceMarkConfig config)
        => _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = path,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(config),
            Priority = 100
        });

    private Task Sync() => _sp.GetRequiredService<ResourceSyncService>()
        .SyncResources(ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);

    private Task<System.Collections.Generic.List<Resource>> Resources()
        => _sp.GetRequiredService<IResourceService>().GetAll();

    private string Sub(string relative) => Path.Combine(_testRoot, relative);

    [TestMethod]
    public async Task NegativeLayer_Minus1_ResolvesToParentDirectory()
    {
        MakeDir("deep/sub");
        await AddMark(Sub("deep/sub"), new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = -1,
            FsTypeFilter = PathFilterFsType.Directory
        });
        await Sync();

        var resources = await Resources();
        Assert.AreEqual(1, resources.Count);
        Assert.AreEqual(Sub("deep").Replace('\\', '/'), resources[0].Path!.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task NegativeLayer_Minus2_ResolvesToGrandparentDirectory()
    {
        MakeDir("deep/sub");
        await AddMark(Sub("deep/sub"), new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = -2,
            FsTypeFilter = PathFilterFsType.Directory
        });
        await Sync();

        var resources = await Resources();
        Assert.AreEqual(1, resources.Count);
        Assert.AreEqual(_testRoot.Replace('\\', '/'), resources[0].Path!.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task WithoutBoundary_OuterMarkCoversEverySubtree()
    {
        MakeDir("A/x");
        MakeDir("B/y");
        MakeDir("special/z");
        await AddMark(_testRoot, new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 2,
            FsTypeFilter = PathFilterFsType.Directory
        });
        await Sync();

        Assert.AreEqual(3, (await Resources()).Count);
    }

    [TestMethod]
    public async Task ResourceBoundary_StopsOuterMarkWithinTheNestedSubtree()
    {
        MakeDir("A/x");
        MakeDir("B/y");
        MakeDir("special/z");

        // Outer mark would claim all three layer-2 directories...
        await AddMark(_testRoot, new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 2,
            FsTypeFilter = PathFilterFsType.Directory
        });
        // ...but a boundary mark on "special" blocks the outer mark inside that subtree.
        await AddMark(Sub("special"), new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer,
            Layer = 99,
            FsTypeFilter = PathFilterFsType.Directory,
            IsResourceBoundary = true
        });
        await Sync();

        var resources = await Resources();
        Assert.AreEqual(2, resources.Count);
        Assert.IsFalse(resources.Any(r => r.Path!.Replace('\\', '/').Contains("/special/")));
    }
}
