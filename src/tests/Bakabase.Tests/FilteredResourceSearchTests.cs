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
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for filtered resource search — the <see cref="ResourceSearch.Group"/>
/// pipeline that earlier tests never exercised. Covers per-operation matching, And/Or
/// combinators, and (most importantly) index-vs-full-scan equivalence: the same filter
/// must yield identical results whether the inverted index is ready or the search falls
/// back to a full scan.
/// </summary>
[TestClass]
public sealed class FilteredResourceSearchTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;
    private Property _filenameProperty = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"FilteredResourceSearchTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    /// <summary>Creates layer-1 directory resources and captures the Filename internal property descriptor.</summary>
    private async Task Seed(params string[] names)
    {
        foreach (var n in names)
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

        var internalProps = await _sp.GetRequiredService<IPropertyService>().GetProperties(PropertyPool.Internal);
        _filenameProperty = internalProps.First(p => p.Id == (int)InternalProperty.Filename);
    }

    private ResourceSearchFilter FilenameFilter(SearchOperation op, object? value = null)
        => new()
        {
            PropertyPool = PropertyPool.Internal,
            PropertyId = (int)InternalProperty.Filename,
            Operation = op,
            DbValue = value,
            Property = _filenameProperty
        };

    private static ResourceSearch SearchWith(SearchCombinator combinator, params ResourceSearchFilter[] filters)
        => new()
        {
            PageSize = 100,
            Group = new ResourceSearchFilterGroup { Combinator = combinator, Filters = filters.ToList() }
        };

    private static ResourceSearch SearchWith(ResourceSearchFilter filter)
        => SearchWith(SearchCombinator.And, filter);

    private async Task<List<int>> SearchIds(ResourceSearch search)
    {
        var resp = await _sp.GetRequiredService<IResourceService>().Search(search);
        return resp.Data!.Select(r => r.Id).OrderBy(i => i).ToList();
    }

    private async Task<List<string>> SearchNames(ResourceSearch search)
    {
        var resp = await _sp.GetRequiredService<IResourceService>().Search(search);
        return resp.Data!.Select(r => r.FileName!).OrderBy(n => n).ToList();
    }

    private async Task RebuildIndex()
    {
        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));
    }

    #region Per-operation matching (full-scan path)

    [TestMethod]
    public async Task Equals_MatchesExactFilename()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var names = await SearchNames(SearchWith(FilenameFilter(SearchOperation.Equals, "banana")));
        CollectionAssert.AreEqual(new[] { "banana" }, names);
    }

    [TestMethod]
    public async Task Contains_MatchesSubstring()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var names = await SearchNames(SearchWith(FilenameFilter(SearchOperation.Contains, "err")));
        CollectionAssert.AreEqual(new[] { "berry", "cherry" }, names);
    }

    [TestMethod]
    public async Task StartsWith_MatchesPrefix()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var names = await SearchNames(SearchWith(FilenameFilter(SearchOperation.StartsWith, "ap")));
        CollectionAssert.AreEqual(new[] { "apple", "apricot" }, names);
    }

    [TestMethod]
    public async Task EndsWith_MatchesSuffix()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var names = await SearchNames(SearchWith(FilenameFilter(SearchOperation.EndsWith, "y")));
        CollectionAssert.AreEqual(new[] { "berry", "cherry" }, names);
    }

    [TestMethod]
    public async Task NotEquals_ExcludesMatch()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var resp = await _sp.GetRequiredService<IResourceService>()
            .Search(SearchWith(FilenameFilter(SearchOperation.NotEquals, "banana")));
        Assert.AreEqual(4, resp.Data!.Count);
        Assert.IsFalse(resp.Data!.Any(r => r.FileName == "banana"));
    }

    [TestMethod]
    public async Task IsNotNull_ReturnsEveryResource()
    {
        await Seed("apple", "banana", "cherry");
        var resp = await _sp.GetRequiredService<IResourceService>()
            .Search(SearchWith(FilenameFilter(SearchOperation.IsNotNull)));
        Assert.AreEqual(3, resp.Data!.Count);
    }

    [TestMethod]
    public async Task NoMatch_ReturnsEmpty()
    {
        await Seed("apple", "banana", "cherry");
        var resp = await _sp.GetRequiredService<IResourceService>()
            .Search(SearchWith(FilenameFilter(SearchOperation.Equals, "durian")));
        Assert.AreEqual(0, resp.Data!.Count);
        Assert.AreEqual(0, resp.TotalCount);
    }

    #endregion

    #region Combinators

    [TestMethod]
    public async Task And_Combinator_RequiresEveryFilter()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        // StartsWith "a" AND Contains "ric" -> only apricot.
        var names = await SearchNames(SearchWith(SearchCombinator.And,
            FilenameFilter(SearchOperation.StartsWith, "a"),
            FilenameFilter(SearchOperation.Contains, "ric")));
        CollectionAssert.AreEqual(new[] { "apricot" }, names);
    }

    [TestMethod]
    public async Task Or_Combinator_RequiresAnyFilter()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var names = await SearchNames(SearchWith(SearchCombinator.Or,
            FilenameFilter(SearchOperation.Equals, "apple"),
            FilenameFilter(SearchOperation.Equals, "cherry")));
        CollectionAssert.AreEqual(new[] { "apple", "cherry" }, names);
    }

    #endregion

    #region Index path and equivalence

    [TestMethod]
    public async Task Index_Equals_MatchesAfterRebuild()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        await RebuildIndex();
        var names = await SearchNames(SearchWith(FilenameFilter(SearchOperation.Equals, "banana")));
        CollectionAssert.AreEqual(new[] { "banana" }, names);
    }

    [TestMethod]
    public async Task IndexAndFullScan_ProduceIdenticalResults()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var cases = new[]
        {
            FilenameFilter(SearchOperation.Equals, "banana"),
            FilenameFilter(SearchOperation.Contains, "err"),
            FilenameFilter(SearchOperation.StartsWith, "ap"),
            FilenameFilter(SearchOperation.EndsWith, "y"),
            FilenameFilter(SearchOperation.NotEquals, "banana"),
            FilenameFilter(SearchOperation.IsNotNull),
        };

        // Index not built yet -> every search takes the full-scan fallback.
        var fullScan = new List<List<int>>();
        foreach (var c in cases)
        {
            fullScan.Add(await SearchIds(SearchWith(c)));
        }

        await RebuildIndex();

        for (var i = 0; i < cases.Length; i++)
        {
            var indexed = await SearchIds(SearchWith(cases[i]));
            CollectionAssert.AreEqual(fullScan[i], indexed,
                $"Index and full-scan disagree for operation {cases[i].Operation}");
        }
    }

    [TestMethod]
    public async Task GetAllIds_WithFilter_ReturnsMatchingIds()
    {
        await Seed("apple", "apricot", "banana", "berry", "cherry");
        var ids = await _sp.GetRequiredService<IResourceService>()
            .GetAllIds(SearchWith(FilenameFilter(SearchOperation.StartsWith, "ap")));
        Assert.AreEqual(2, ids.Length);
    }

    #endregion
}
