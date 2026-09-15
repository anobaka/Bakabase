using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.Acquisition;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class UnknownResourceSourceTests
{
    private IServiceProvider _services = null!;
    private IResourceSourceLinkService Links => _services.GetRequiredService<IResourceSourceLinkService>();
    private FullMemoryCacheResourceService<BakabaseDbContext, ResourceSourceLinkDbModel, int> Rows =>
        _services.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceSourceLinkDbModel, int>>();

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    [TestMethod]
    [DataRow(6)]
    [DataRow(8)]
    [DataRow(999)]
    public void UndefinedSourceValuesNeverClaimAPlatformHolding(int value)
    {
        Assert.IsFalse(((ResourceSource)value).IsPlatformHolding());
        Assert.IsTrue(ResourceSource.Pixiv.IsPlatformHolding());
    }

    private async Task<int> Missing(string name) =>
        (await _services.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(name)).ResourceId;

    private static ResourceSourceLink Link(int resourceId, ResourceSource source, string key) => new()
    {
        ResourceId = resourceId,
        Source = source,
        SourceKey = key,
        CoverUrls = ["https://images.example/known-cover.jpg"]
    };

    [TestMethod]
    public async Task UnknownStoredRowsAreInvisibleToSourcesMatchingAndAcquisitionCandidates()
    {
        var first = await Missing("Known Pixiv source");
        var unknownOnly = await Missing("Old metadata sources");
        var samePixiv = await Missing("Another Pixiv copy");
        // Seed persisted values through the ORM to model an older database, bypassing current write validation.
        await Rows.AddRange(new[]
        {
            Link(first, (ResourceSource)6, "old-bangumi"),
            Link(first, (ResourceSource)999, "same-unknown"),
            Link(unknownOnly, (ResourceSource)8, "old-vndb"),
            Link(unknownOnly, (ResourceSource)999, "same-unknown"),
            Link(first, ResourceSource.Pixiv, "12345"),
            Link(samePixiv, ResourceSource.Pixiv, "12345")
        }.Select(link => link.ToDbModel()).ToList());

        Assert.AreEqual(2, (await Links.GetAll()).Count);
        Assert.AreEqual(0, (await Links.GetByResourceId(unknownOnly)).Count);
        Assert.AreEqual(2, (await Links.GetByResourceIds([first, unknownOnly, samePixiv])).Count);
        Assert.AreEqual(2, (await Links.GetByResourceIdsGrouped([first, unknownOnly, samePixiv])).Count);
        Assert.AreEqual(2, (await Links.GetPendingCoverDownloads()).Count);
        Assert.AreEqual(2, (await Links.GetPendingMetadataFetches()).Count);
        Assert.IsNull(await Links.FindResourceBySourceLinks([((ResourceSource)6, "old-bangumi")]));
        Assert.AreEqual(first, await Links.FindResourceBySourceLinks([(ResourceSource.Pixiv, "12345")]));
        CollectionAssert.AreEqual(new[] {samePixiv}, await Links.FindConflictingResourceIds(first));
        Assert.AreEqual(0, (await Links.FindConflictingResourceIds(unknownOnly)).Count);

        var resource = await _services.GetRequiredService<IResourceService>().Get(first);
        Assert.IsNotNull(resource);
        CollectionAssert.AreEqual(new[] {ResourceSource.Pixiv}, resource.SourceLinks!.Select(link => link.Source).ToArray());
        var page = await _services.GetRequiredService<AcquisitionCandidateService>().SearchAsync();
        Assert.AreEqual(0, page.Items.Single(item => item.ResourceId == unknownOnly).Leads.Count);
        Assert.AreEqual("Pixiv:12345", page.Items.Single(item => item.ResourceId == first).Leads.Single().Value);
        Assert.AreEqual(6, (await Rows.GetAll()).Count,
            "reading an older database must not silently rewrite or delete its rows");
    }

    [TestMethod]
    [DataRow("Add")]
    [DataRow("AddRange")]
    [DataRow("EnsureLinks")]
    [DataRow("Update")]
    public async Task WritesRejectUnknownSourcesBeforeChangingAnyLink(string operation)
    {
        var resourceId = await Missing("Validate source writes");
        var existing = await Links.Add(new ResourceSourceLink
        {
            ResourceId = resourceId, Source = ResourceSource.Pixiv, SourceKey = "12345"
        });
        var valid = Link(resourceId, ResourceSource.Pixiv, "12345");
        var unknown = Link(resourceId, (ResourceSource)999, "unsupported");
        unknown.Id = existing.Id;
        Func<Task> write = operation switch
        {
            "Add" => () => Links.Add(unknown),
            "AddRange" => () => Links.AddRange([valid, unknown]),
            "EnsureLinks" => () => Links.EnsureLinks(resourceId, [valid, unknown]),
            "Update" => () => Links.Update(unknown),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(write);
        var rows = await Rows.GetAll();
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual(ResourceSource.Pixiv, rows.Single().Source);
        Assert.IsTrue(string.IsNullOrEmpty(rows.Single().CoverUrls),
            "a valid earlier item in a rejected batch must not partially update the cached row");
    }
}
