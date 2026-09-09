using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Modules.BulkModification.Models.Db;
using Bakabase.Modules.BulkModification.Models.Input;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components;
using Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// A resource without a path is a normal resource that happens to have no local files yet — an
/// uninstalled game, a work waiting to be downloaded. Plenty of code was written when every
/// resource had files; these tests pin the places where that assumption used to produce a crash,
/// a wrong answer, or a permanently blocked enhancer.
/// </summary>
[TestClass]
public sealed class UnmaterializedResourceTouchpointsTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"UnmaterializedResourceTouchpointsTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
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

    private IResourceService ResourceService => _sp.GetRequiredService<IResourceService>();

    private async Task<(Resource Materialized, Resource Unmaterialized)> SeedOneOfEach()
    {
        var dir = Path.Combine(_testRoot, "on-disk");
        Directory.CreateDirectory(dir);

        await ResourceService.AddOrPutRange([
            new Resource
            {
                Path = dir,
                IsFile = false,
                Status = ResourceStatus.Active,
                FileCreatedAt = DateTime.Now.AddDays(-10),
                FileModifiedAt = DateTime.Now.AddDays(-10)
            },
            new Resource
            {
                Path = null,
                IsFile = false,
                Status = ResourceStatus.Active,
                DisplayName = "not downloaded yet",
                // Deliberately "newer" than the materialized one: these columns hold the moment the
                // row was written, and a naive sort would float this to the top of "most recently
                // modified files" despite there being no file at all.
                FileCreatedAt = DateTime.Now,
                FileModifiedAt = DateTime.Now,
                SourceLinks =
                [
                    new ResourceSourceLink { Source = ResourceSource.DLsite, SourceKey = "RJ00000123" }
                ]
            }
        ]);

        var all = await ResourceService.GetAll();
        return (all.First(r => r.HasLocalPath), all.First(r => !r.HasLocalPath));
    }

    private static ResourceSearch OrderBy(ResourceSearchSortableProperty property, bool asc) => new()
    {
        PageSize = 100,
        Orders = [new ResourceSearchOrderInputModel { Property = property, Asc = asc }]
    };

    [TestMethod]
    public async Task OrderByFileTime_PutsResourcesWithoutLocalFilesLast()
    {
        var (materialized, unmaterialized) = await SeedOneOfEach();

        foreach (var property in new[]
                 {
                     ResourceSearchSortableProperty.FileCreateDt,
                     ResourceSearchSortableProperty.FileModifyDt
                 })
        {
            foreach (var asc in new[] { true, false })
            {
                var page = await ResourceService.Search(OrderBy(property, asc));
                var ids = page.Data!.Select(r => r.Id).ToList();

                Assert.AreEqual(materialized.Id, ids[0],
                    $"{property} {(asc ? "asc" : "desc")}: a resource with files comes first");
                Assert.AreEqual(unmaterialized.Id, ids[^1],
                    $"{property} {(asc ? "asc" : "desc")}: a resource without files sinks to the end");
            }
        }
    }

    [TestMethod]
    public async Task FileTimeSearch_IgnoresResourcesWithoutLocalFiles_OnIndexAndFullScan()
    {
        var (materialized, unmaterialized) = await SeedOneOfEach();

        var internalProps = await _sp.GetRequiredService<IPropertyService>().GetProperties(PropertyPool.Internal);
        var fileModifiedAt = internalProps.First(p => p.Id == (int)InternalProperty.FileModifiedAt);

        ResourceSearch Search(SearchOperation operation, DateTime value) => new()
        {
            PageSize = 100,
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilter
                    {
                        PropertyPool = PropertyPool.Internal,
                        PropertyId = (int)InternalProperty.FileModifiedAt,
                        Operation = operation,
                        DbValue = value,
                        Property = fileModifiedAt
                    }
                ]
            }
        };

        var cases = new[]
        {
            Search(SearchOperation.GreaterThan, DateTime.Now.AddDays(-1)),
            Search(SearchOperation.LessThan, DateTime.Now.AddDays(-5))
        };

        // Nothing has been indexed yet, so these take the full-scan fallback.
        var fullScan = new List<List<int>>();
        foreach (var c in cases)
        {
            fullScan.Add((await ResourceService.Search(c)).Data!.Select(r => r.Id).OrderBy(i => i).ToList());
        }

        var index = _sp.GetRequiredService<IResourceSearchIndexService>();
        await index.RebuildAllAsync(CancellationToken.None);
        await index.WaitForReadyAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < cases.Length; i++)
        {
            var indexed = (await ResourceService.Search(cases[i])).Data!.Select(r => r.Id).OrderBy(x => x).ToList();
            CollectionAssert.AreEqual(fullScan[i], indexed,
                "the index and the full-scan fallback must agree about resources that have no file times");
            CollectionAssert.DoesNotContain(indexed, unmaterialized.Id,
                "a resource with no files has no file times to compare against");
        }

        Assert.IsTrue(fullScan[0].Contains(materialized.Id) || fullScan[1].Contains(materialized.Id),
            "the resource that does have files should be matched by one of the two bounds");
    }

    [TestMethod]
    public async Task BulkModificationDiff_KeepsResourcesWithoutLocalFiles_AndPathFilterSkipsThem()
    {
        var (materialized, unmaterialized) = await SeedOneOfEach();

        var bmService = _sp.GetRequiredService<IBulkModificationService>();
        await bmService.Add(new BulkModification { Name = "test" });
        var bm = (await bmService.GetAll()).Single();

        // The shape Preview persists. Storing it used to fail outright: the column was NOT NULL,
        // so a single resource without local files in the selection killed the whole preview.
        var db = _sp.GetRequiredService<BakabaseDbContext>();
        db.Set<BulkModificationDiffDbModel>().AddRange(
            new BulkModificationDiffDbModel
            {
                BulkModificationId = bm.Id,
                ResourceId = materialized.Id,
                ResourcePath = materialized.Path,
                Diffs = "[]"
            },
            new BulkModificationDiffDbModel
            {
                BulkModificationId = bm.Id,
                ResourceId = unmaterialized.Id,
                ResourcePath = null,
                Diffs = "[]"
            });
        await db.SaveChangesAsync();

        var all = await bmService.SearchDiffs(bm.Id, new BulkModificationResourceDiffsSearchInputModel
        {
            PageIndex = 1,
            PageSize = 100
        });
        Assert.AreEqual(2, all.Data!.Count, "both diffs are stored, path or no path");

        var filtered = await bmService.SearchDiffs(bm.Id, new BulkModificationResourceDiffsSearchInputModel
        {
            PageIndex = 1,
            PageSize = 100,
            Path = "on-disk"
        });
        Assert.AreEqual(1, filtered.Data!.Count, "filtering by path must not throw on a null path");
        Assert.AreEqual(materialized.Id, filtered.Data![0].ResourceId);
    }

    [TestMethod]
    public void EnhancersThatReadTheFiles_DeclareThatTheyNeedThem()
    {
        // These three read the resource's path, its folder contents or its filename, so on a
        // resource with no files they would record an empty result that is never retried.
        foreach (var id in new[] { EnhancerId.Bakabase, EnhancerId.Kodi, EnhancerId.Regex })
        {
            Assert.IsTrue(EnhancerRequirements.RequiresLocalFiles((int)id), $"{id} reads local files");
        }

        // These work from a keyword, which a resource can have without any files.
        foreach (var id in new[] { EnhancerId.DLsite, EnhancerId.Bangumi, EnhancerId.ExHentai })
        {
            Assert.IsFalse(EnhancerRequirements.RequiresLocalFiles((int)id),
                $"{id} only needs something to search for");
        }
    }

    [TestMethod]
    public async Task KeywordEnhancer_WithoutLocalFiles_FallsBackToTheReservedName()
    {
        var (_, unmaterialized) = await SeedOneOfEach();

        await _sp.GetRequiredService<IReservedPropertyValueService>().Add(new ReservedPropertyValue
        {
            ResourceId = unmaterialized.Id,
            Scope = (int)PropertyValueScope.Synchronization,
            Name = "RJ00000123 Some Work"
        });

        var withProperties = (await ResourceService.Get(unmaterialized.Id, ResourceAdditionalItem.All))!;
        var enhancer = ActivatorUtilities.CreateInstance<KeywordCapturingEnhancer>(_sp);

        await enhancer.ResolveKeyword(withProperties, new EnhancerFullOptions());

        Assert.AreEqual("RJ00000123 Some Work", enhancer.CapturedKeyword,
            "with no filename to use, the name is the only thing an enhancer can search on");
    }

    /// <summary>
    /// A real keyword enhancer with its network-facing half replaced, so the keyword resolution
    /// that happens before it can be observed.
    /// </summary>
    private sealed class KeywordCapturingEnhancer : DLsiteEnhancer
    {
        public KeywordCapturingEnhancer(
            Microsoft.Extensions.Logging.ILoggerFactory loggerFactory,
            Bakabase.Abstractions.Components.FileSystem.IFileManager fileManager,
            Bakabase.Modules.ThirdParty.ThirdParties.DLsite.DLsiteClient client,
            Bakabase.Modules.StandardValue.Abstractions.Services.IStandardValueService standardValueService,
            Bakabase.Abstractions.Components.Text.ITextOps textOps,
            IServiceProvider serviceProvider)
            : base(loggerFactory, fileManager, client, standardValueService, textOps, serviceProvider)
        {
        }

        public string? CapturedKeyword { get; private set; }

        public Task<DLsiteEnhancerContext?> ResolveKeyword(Resource resource, IKeywordEnhancerOptions options) =>
            BuildContextInternal(resource, options, new EnhancementLogCollector(), CancellationToken.None);

        protected override Task<DLsiteEnhancerContext?> BuildContextInternal(string keyword, Resource resource,
            IKeywordEnhancerOptions options, EnhancementLogCollector logCollector, CancellationToken ct)
        {
            CapturedKeyword = keyword;
            return Task.FromResult<DLsiteEnhancerContext?>(null);
        }
    }
}
