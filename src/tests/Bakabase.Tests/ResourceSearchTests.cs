using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
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
/// Integration coverage for resource search: the unfiltered Search path
/// (paging / total count), GetAllIds, and the inverted-index service lifecycle
/// (rebuild -> ready, status, no-filter null contract).
/// </summary>
[TestClass]
public sealed class ResourceSearchTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceSearchTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    /// <summary>Creates <paramref name="count"/> directory resources via a layer-1 resource mark + sync.</summary>
    private async Task SeedResources(int count)
    {
        for (var i = 0; i < count; i++)
        {
            Directory.CreateDirectory(Path.Combine(_testRoot, $"Res{i:D2}"));
        }

        var pathMarkService = _sp.GetRequiredService<IPathMarkService>();
        await pathMarkService.Add(new PathMark
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

        var resourceSyncService = _sp.GetRequiredService<ResourceSyncService>();
        await resourceSyncService.SyncResources(
            ResourceSource.PathMark, null, null, new PauseToken(), CancellationToken.None);
    }

    [TestMethod]
    public async Task Search_NoFilter_ReturnsAllResources()
    {
        await SeedResources(5);
        var response = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch());
        Assert.AreEqual(5, response.TotalCount);
        Assert.AreEqual(5, response.Data!.Count);
    }

    [TestMethod]
    public async Task Search_PageSize_CapsResultsButReportsFullTotal()
    {
        await SeedResources(5);
        var response = await _sp.GetRequiredService<IResourceService>()
            .Search(new ResourceSearch { PageIndex = 1, PageSize = 2 });
        Assert.AreEqual(2, response.Data!.Count);
        Assert.AreEqual(5, response.TotalCount);
    }

    [TestMethod]
    public async Task Search_LastPage_ReturnsRemainder()
    {
        await SeedResources(5);
        // page 3 with size 2 -> items 5..5 -> 1 result
        var response = await _sp.GetRequiredService<IResourceService>()
            .Search(new ResourceSearch { PageIndex = 3, PageSize = 2 });
        Assert.AreEqual(1, response.Data!.Count);
        Assert.AreEqual(5, response.TotalCount);
    }

    [TestMethod]
    public async Task GetAllIds_NoFilter_ReturnsEveryResourceId()
    {
        await SeedResources(4);
        var ids = await _sp.GetRequiredService<IResourceService>().GetAllIds(new ResourceSearch());
        Assert.AreEqual(4, ids.Length);
        Assert.AreEqual(4, ids.Distinct().Count());
    }

    [TestMethod]
    public async Task SearchIndex_RebuildAll_BecomesReady()
    {
        await SeedResources(3);
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));
        Assert.IsTrue(index.IsReady);
    }

    [TestMethod]
    public async Task SearchIndex_Status_ReflectsResourceCount()
    {
        await SeedResources(3);
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));

        var all = await resourceService.GetAll();
        var status = index.GetStatus();
        Assert.IsTrue(status.IsReady);
        Assert.AreEqual(all.Count, status.TotalResourceCount);
    }

    [TestMethod]
    public async Task SearchIndex_NullFilterGroup_ReturnsNull()
    {
        // null group => "no filter" => callers fall back to a full scan.
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        Assert.IsNull(await index.SearchResourceIdsAsync(null));
    }

    #region Sorting

    [TestMethod]
    public void BuildForSearch_NoOrders_FallsBackToDefaultOrdering()
    {
        Assert.AreEqual(2, ((ResourceSearchOrderInputModel[]?)null).BuildForSearch().Length);
        Assert.AreEqual(2, Array.Empty<ResourceSearchOrderInputModel>().BuildForSearch().Length);
    }

    [TestMethod]
    public void BuildForSearch_Filename_HasCaseInsensitiveComparer()
    {
        var built = new[]
        {
            new ResourceSearchOrderInputModel { Property = ResourceSearchSortableProperty.Filename, Asc = true }
        }.BuildForSearch();
        Assert.AreEqual(1, built.Length);
        Assert.IsTrue(built[0].Asc);
        Assert.IsNotNull(built[0].Comparer);
    }

    [TestMethod]
    public void BuildForSearch_DateProperty_HasNoCustomComparer()
    {
        var built = new[]
        {
            new ResourceSearchOrderInputModel { Property = ResourceSearchSortableProperty.AddDt, Asc = false }
        }.BuildForSearch();
        Assert.AreEqual(1, built.Length);
        Assert.IsFalse(built[0].Asc);
        Assert.IsNull(built[0].Comparer);
    }

    [TestMethod]
    public async Task Search_OrderByFilename_Ascending()
    {
        await SeedResources(5);
        var response = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch
        {
            Orders = [new ResourceSearchOrderInputModel { Property = ResourceSearchSortableProperty.Filename, Asc = true }]
        });
        var names = response.Data!.Select(r => r.FileName).ToList();
        CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [TestMethod]
    public async Task Search_OrderByFilename_Descending()
    {
        await SeedResources(5);
        var response = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch
        {
            Orders = [new ResourceSearchOrderInputModel { Property = ResourceSearchSortableProperty.Filename, Asc = false }]
        });
        var names = response.Data!.Select(r => r.FileName).ToList();
        CollectionAssert.AreEqual(names.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    [TestMethod]
    public async Task Search_DefaultOrder_IsByIdDescending()
    {
        await SeedResources(5);
        var response = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch());
        var ids = response.Data!.Select(r => r.Id).ToList();
        CollectionAssert.AreEqual(ids.OrderByDescending(i => i).ToList(), ids);
    }

    #endregion

    #region Scale

    private const int ScaleCount = 50;

    [TestMethod]
    public async Task Search_AtScale_ReturnsEveryResource()
    {
        await SeedResources(ScaleCount);
        var response = await _sp.GetRequiredService<IResourceService>()
            .Search(new ResourceSearch { PageSize = 100 });
        Assert.AreEqual(ScaleCount, response.TotalCount);
        Assert.AreEqual(ScaleCount, response.Data!.Count);
    }

    [TestMethod]
    public async Task Search_AtScale_PaginationCoversEveryResourceExactlyOnce()
    {
        await SeedResources(ScaleCount);
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var seen = new HashSet<int>();
        for (var page = 1; page <= 3; page++)
        {
            var resp = await resourceService.Search(new ResourceSearch { PageIndex = page, PageSize = 20 });
            Assert.AreEqual(ScaleCount, resp.TotalCount);
            foreach (var r in resp.Data!)
            {
                Assert.IsTrue(seen.Add(r.Id), $"Resource {r.Id} appeared on more than one page.");
            }
        }
        Assert.AreEqual(ScaleCount, seen.Count);
    }

    [TestMethod]
    public async Task SearchIndex_AtScale_RebuildIndexesEveryResource()
    {
        await SeedResources(ScaleCount);
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(30));
        Assert.IsTrue(index.IsReady);
        Assert.AreEqual(ScaleCount, index.GetStatus().TotalResourceCount);
    }

    [TestMethod]
    public async Task Search_AtScale_FilenameOrderingStaysCorrect()
    {
        await SeedResources(ScaleCount);
        var response = await _sp.GetRequiredService<IResourceService>().Search(new ResourceSearch
        {
            PageSize = 100,
            Orders = [new ResourceSearchOrderInputModel { Property = ResourceSearchSortableProperty.Filename, Asc = true }]
        });
        var names = response.Data!.Select(r => r.FileName).ToList();
        Assert.AreEqual(ScaleCount, names.Count);
        CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
    }

    #endregion
}
