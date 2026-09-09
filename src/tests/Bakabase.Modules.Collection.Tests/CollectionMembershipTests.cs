using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Collection.Tests;

/// <summary>
/// What a collection is: a name over a set of resources, some of which are not here. The number
/// that matters is how much of it you have, and most of these are about keeping that number honest.
/// </summary>
[TestClass]
public sealed class CollectionMembershipTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
    }

    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();

    private async Task<int> NewCollection(string name = "A Series") =>
        (await Collections.Add(new CollectionInputModel {Name = name})).Id;

    /// <summary>A resource nobody has yet — the whole reason a collection can be partly complete.</summary>
    private async Task<int> Missing(string title) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;

    private async Task<int> Owned(string title)
    {
        var id = await Missing(title);
        var resource = (await Resources.Get(id))!;

        resource.Path = $"/library/{title}";
        await Resources.AddOrPutRange([resource]);

        return id;
    }

    [TestMethod]
    public async Task TheCollectedRatioIsWhatYouHaveOverWhatCounts()
    {
        var id = await NewCollection();

        await Collections.AddMembers(id, [await Owned("One"), await Owned("Two"), await Missing("Three")]);

        var progress = await Collections.GetProgress(id);

        Assert.AreEqual(3, progress.Total);
        Assert.AreEqual(2, progress.Owned);
        Assert.AreEqual(2d / 3, progress.Ratio, 0.001);
    }

    /// <summary>
    /// An ignored member is neither had nor missing. Leaving it in the denominator would mean a
    /// series with three drama CDs nobody wants could never read as complete.
    /// </summary>
    [TestMethod]
    public async Task AnIgnoredMemberLeavesTheRatioEntirely()
    {
        var id = await NewCollection();
        var unwanted = await Missing("Drama CD");

        await Collections.AddMembers(id, [await Owned("One"), await Owned("Two"), unwanted]);
        Assert.AreEqual(2d / 3, (await Collections.GetProgress(id)).Ratio, 0.001);

        await Collections.SetMemberIgnored(id, unwanted, true);

        var progress = await Collections.GetProgress(id);

        Assert.AreEqual(2, progress.Total, "it is out of the count, not counted as had");
        Assert.AreEqual(1, progress.Ignored);
        Assert.AreEqual(1, progress.Ratio, 0.001);
    }

    [TestMethod]
    public async Task AnEmptyCollectionIsNotZeroPercent()
    {
        // It is not a question yet. Showing 0% for a collection someone just made would be a lie
        // about their library rather than a fact about the collection.
        Assert.AreEqual(1, (await Collections.GetProgress(await NewCollection())).Ratio, 0.001);
    }

    /// <summary>
    /// Adding twice must not disturb what is already recorded — a subscription that re-lists
    /// everything every hour would otherwise un-ignore what the user set aside.
    /// </summary>
    [TestMethod]
    public async Task AddingTheSameResourceAgainChangesNothing()
    {
        var id = await NewCollection();
        var resourceId = await Missing("One");

        await Collections.AddMembers(id, [resourceId]);
        await Collections.SetMemberIgnored(id, resourceId, true);
        await Collections.AddMembers(id, [resourceId, resourceId]);

        var members = await Collections.GetMembers(id);

        Assert.AreEqual(1, members.Count);
        Assert.IsTrue(members[0].IsIgnored, "and it is still ignored");
    }

    [TestMethod]
    public async Task DeletingACollectionTakesItsMembershipsWithIt()
    {
        var id = await NewCollection();
        var resourceId = await Missing("One");

        await Collections.AddMembers(id, [resourceId]);
        await Collections.Delete(id);

        Assert.IsNull(await Collections.Get(id));
        Assert.AreEqual(0,
            (await _sp.GetRequiredService<ICollectionResourceMappingService>()
                .GetByResourceId(resourceId)).Count,
            "and the resource is not left claiming to belong to something that is gone");
    }

    [TestMethod]
    public async Task MembersCanBeAskedForByWhatTheUserActuallyWantsToKnow()
    {
        var id = await NewCollection();
        var owned = await Owned("Have it");
        var missing = await Missing("Do not have it");
        var ignored = await Missing("Do not want it");

        await Collections.AddMembers(id, [owned, missing, ignored]);
        await Collections.SetMemberIgnored(id, ignored, true);

        var all = await Collections.SearchMembers(id, CollectionMemberFilter.All, 1, 100);
        var have = await Collections.SearchMembers(id, CollectionMemberFilter.Owned, 1, 100);
        var lack = await Collections.SearchMembers(id, CollectionMemberFilter.Missing, 1, 100);
        var setAside = await Collections.SearchMembers(id, CollectionMemberFilter.Ignored, 1, 100);

        Assert.AreEqual(3, all.TotalCount);
        CollectionAssert.AreEqual(new[] {owned}, have.ResourceIds.ToArray());
        CollectionAssert.AreEqual(new[] {missing}, lack.ResourceIds.ToArray(),
            "an ignored member is not missing — it was set aside on purpose");
        CollectionAssert.AreEqual(new[] {ignored}, setAside.ResourceIds.ToArray());
    }

    [TestMethod]
    public async Task AResourceKnowsWhichCollectionsItIsIn()
    {
        var first = await NewCollection("First");
        var second = await NewCollection("Second");
        var resourceId = await Missing("In both");

        await Collections.AddMembers(first, [resourceId]);
        await Collections.AddMembers(second, [resourceId]);

        var byResource = await _sp.GetRequiredService<ICollectionResourceMappingService>()
            .GetCollectionIdsByResourceIds([resourceId]);

        CollectionAssert.AreEquivalent(new[] {first, second}, byResource[resourceId].ToArray());
    }

    /// <summary>
    /// The design document's acceptance case for collections, kept as a test because it is the
    /// number a user looks at: five things you have plus three you do not is 62.5%, and getting
    /// one of them is 75%.
    /// </summary>
    [TestMethod]
    public async Task FiveHadAndThreeMissingReadsAsSixtyTwoAndAHalfPercent()
    {
        var id = await NewCollection();
        var members = new List<int>();

        for (var i = 1; i <= 5; i++) members.Add(await Owned($"Volume {i}"));

        var next = await Missing("Volume 6");

        members.Add(next);
        members.Add(await Missing("Volume 7"));
        members.Add(await Missing("Volume 8"));

        await Collections.AddMembers(id, members);

        Assert.AreEqual(0.625, (await Collections.GetProgress(id)).Ratio, 0.0001);

        // One of them lands — which is all "acquired" means to the ratio.
        var resource = (await Resources.Get(next))!;

        resource.Path = "/library/Volume 6";
        await Resources.AddOrPutRange([resource]);

        Assert.AreEqual(0.75, (await Collections.GetProgress(id)).Ratio, 0.0001);
    }

    [TestMethod]
    public async Task MembersKeepTheOrderTheyWereGiven()
    {
        var id = await NewCollection();
        var a = await Missing("Volume 1");
        var b = await Missing("Volume 2");
        var c = await Missing("Volume 3");

        await Collections.AddMembers(id, [a, b, c]);
        await Collections.ReorderMembers(id, [c, a, b]);

        CollectionAssert.AreEqual(new[] {c, a, b},
            (await Collections.GetMembers(id)).Select(m => m.ResourceId).ToArray());
    }
}
