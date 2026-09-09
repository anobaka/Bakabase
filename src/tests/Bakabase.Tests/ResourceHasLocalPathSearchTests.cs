using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Coverage for the <c>HasLocalPath</c> internal property — the searchable form of
/// "this resource is known to Bakabase but has no local files yet" (an uninstalled Steam
/// game, a work the user intends to acquire). Two things must hold: the property tells the
/// two kinds of resource apart, and it says the same thing whether the inverted index is
/// ready or the search falls back to a full scan.
/// </summary>
[TestClass]
public sealed class ResourceHasLocalPathSearchTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private Property _hasLocalPathProperty = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceHasLocalPathSearchTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    /// <summary>
    /// Creates <paramref name="materializedNames"/> as on-disk directory resources, then adds one
    /// resource with no path at all — the shape <see cref="ResourceSyncService"/> already persists
    /// for an external source entry whose files are not downloaded yet.
    /// </summary>
    private async Task Seed(params string[] materializedNames)
    {
        foreach (var n in materializedNames)
        {
            Directory.CreateDirectory(Path.Combine(_testRoot, n));
        }

        await _sp.GetRequiredService<IPathMarkService>().Add(new PathMark
        {
            Path = _testRoot,
            Type = PathMarkType.Resource,
            ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
            {
                MatchMode = PathMatchMode.Layer,
                Layer = 1,
                FsTypeFilter = PathFilterFsType.Directory
            }),
            Priority = 100
        });
        await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);

        await _sp.GetRequiredService<IResourceService>().AddOrPutRange([
            new Resource
            {
                Path = null,
                IsFile = false,
                Status = ResourceStatus.Active,
                DisplayName = "unmaterialized-work",
                FileCreatedAt = DateTime.Now,
                FileModifiedAt = DateTime.Now,
                SourceLinks =
                [
                    new ResourceSourceLink
                    {
                        Source = ResourceSource.DLsite,
                        SourceKey = "RJ00000001"
                    }
                ]
            }
        ]);

        var internalProps = await _sp.GetRequiredService<IPropertyService>().GetProperties(PropertyPool.Internal);
        _hasLocalPathProperty = internalProps.First(p => p.Id == (int)InternalProperty.HasLocalPath);
    }

    private ResourceSearchFilter HasLocalPathFilter(SearchOperation op, object? value = null)
        => new()
        {
            PropertyPool = PropertyPool.Internal,
            PropertyId = (int)InternalProperty.HasLocalPath,
            Operation = op,
            DbValue = value,
            Property = _hasLocalPathProperty
        };

    private static ResourceSearch SearchWith(ResourceSearchFilter filter)
        => new()
        {
            PageSize = 100,
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters = [filter]
            }
        };

    private async Task<List<int>> SearchIds(ResourceSearchFilter filter)
    {
        var resp = await _sp.GetRequiredService<IResourceService>().Search(SearchWith(filter));
        return resp.Data!.Select(r => r.Id).OrderBy(i => i).ToList();
    }

    private async Task RebuildIndex()
    {
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public async Task PathLessResource_IsPersistedWithoutPath()
    {
        await Seed("alpha", "beta");

        var all = await _sp.GetRequiredService<IResourceService>().GetAll();
        var unmaterialized = all.Where(r => !r.HasLocalPath).ToList();

        Assert.AreEqual(1, unmaterialized.Count, "the path-less resource should survive a round trip");
        Assert.IsNull(unmaterialized[0].Path);
        Assert.AreEqual(2, all.Count(r => r.HasLocalPath));
    }

    [TestMethod]
    public async Task EqualsFalse_ReturnsOnlyUnmaterializedResources_OnIndex()
    {
        await Seed("alpha", "beta");
        await RebuildIndex();

        var resp = await _sp.GetRequiredService<IResourceService>()
            .Search(SearchWith(HasLocalPathFilter(SearchOperation.Equals, false)));

        Assert.AreEqual(1, resp.Data!.Count);
        Assert.IsFalse(resp.Data![0].HasLocalPath);
    }

    [TestMethod]
    public async Task EqualsTrue_ReturnsOnlyMaterializedResources_OnIndex()
    {
        await Seed("alpha", "beta");
        await RebuildIndex();

        var resp = await _sp.GetRequiredService<IResourceService>()
            .Search(SearchWith(HasLocalPathFilter(SearchOperation.Equals, true)));

        Assert.AreEqual(2, resp.Data!.Count);
        Assert.IsTrue(resp.Data!.All(r => r.HasLocalPath));
    }

    [TestMethod]
    public async Task IndexAndFullScan_ProduceIdenticalResults()
    {
        await Seed("alpha", "beta");

        var cases = new[]
        {
            HasLocalPathFilter(SearchOperation.Equals, false),
            HasLocalPathFilter(SearchOperation.Equals, true),
            HasLocalPathFilter(SearchOperation.NotEquals, false),
        };

        // The index has not been built yet, so every search takes the full-scan fallback.
        var fullScan = new List<List<int>>();
        foreach (var c in cases)
        {
            fullScan.Add(await SearchIds(c));
        }

        await RebuildIndex();

        for (var i = 0; i < cases.Length; i++)
        {
            var indexed = await SearchIds(cases[i]);
            CollectionAssert.AreEqual(fullScan[i], indexed,
                $"Index and full-scan disagree for {cases[i].Operation} {cases[i].DbValue}");
        }
    }
}
