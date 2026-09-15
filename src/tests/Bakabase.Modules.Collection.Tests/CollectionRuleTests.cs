using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Models.Input;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Service.Components.Collections;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Bakabase.Modules.Collection.Tests;

/// <summary>
/// A rule collection: a name over whatever currently matches a filter. The one thing that must hold
/// is that it agrees with the resource page — otherwise "everything by this author" would mean two
/// different things depending on where you asked.
/// </summary>
[TestClass]
public sealed class CollectionRuleTests
{
    private IServiceProvider _sp = null!;

    private int _authorPropertyId;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();

        // Production resolves this at startup so the rule index hears about resource changes.
        // Without it these tests would be exercising an index nothing ever invalidates.
        _sp.GetRequiredService<CollectionRuleIndexInvalidator>();

        _authorPropertyId = (await _sp.GetRequiredService<ICustomPropertyService>()
            .Add(new CustomPropertyAddOrPutDto {Name = "Author", Type = PropertyType.SingleLineText})).Id;
    }

    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();

    private async Task<int> NewResource(string title, string? author = null)
    {
        var id = (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title))
            .ResourceId;

        if (author != null) await SetAuthor(id, author);

        return id;
    }

    private Task SetAuthor(int resourceId, string author) =>
        Resources.BulkPutPropertyValue([resourceId], new ResourcePropertyValuePutInputModel
        {
            PropertyId = _authorPropertyId,
            IsCustomProperty = true,
            Value = author.SerializeAsStandardValue(StandardValueType.String),
        });

    /// <summary>The rule a user would write in the editor: "author is X".</summary>
    private string AuthorIs(string author) => JsonConvert.SerializeObject(new ResourceSearchDbModel
    {
        Group = new ResourceSearchFilterGroupDbModel
        {
            Combinator = SearchCombinator.And,
            Filters =
            [
                new ResourceSearchFilterDbModel
                {
                    PropertyPool = PropertyPool.Custom,
                    PropertyId = _authorPropertyId,
                    Operation = SearchOperation.Equals,
                    Value = author.SerializeAsStandardValue(StandardValueType.String),
                }
            ]
        }
    });

    private async Task<int> NewRuleCollection(string rule, string name = "By that author") =>
        (await Collections.Add(new CollectionInputModel {Name = name, RuleSearchJson = rule})).Id;

    private async Task<int[]> MemberIds(int collectionId) =>
        (await Collections.GetMembers(collectionId)).Select(m => m.ResourceId).Order().ToArray();

    /// <summary>
    /// The whole contract of a rule collection. If these two ever disagreed, the collection page and
    /// the resource page would be answering the same question differently.
    /// </summary>
    [TestMethod]
    public async Task ARuleCollectionHoldsExactlyWhatTheSameFilterFinds()
    {
        var first = await NewResource("Volume 1", "Asagi");
        var second = await NewResource("Volume 2", "Asagi");
        await NewResource("Somebody else's book", "Kuro");

        var rule = AuthorIs("Asagi");
        var id = await NewRuleCollection(rule);

        var searched = (await _sp.GetRequiredService<IResourceProfileService>()
            .GetMatchingResourceIdsBySearchJson(rule)).Order().ToArray();

        CollectionAssert.AreEqual(new[] {first, second}, await MemberIds(id));
        CollectionAssert.AreEqual(searched, await MemberIds(id),
            "the collection is the filter, not a copy of what the filter once said");
    }

    /// <summary>
    /// The reason a rule collection is worth having over a written-down one: nobody has to maintain
    /// it. A book that becomes this author's is in the collection because it matches, not because
    /// somebody remembered to add it.
    /// </summary>
    [TestMethod]
    public async Task MembershipFollowsAPropertyChange()
    {
        var mine = await NewResource("Volume 1", "Asagi");
        var notYet = await NewResource("Volume 2", "Unknown");

        var id = await NewRuleCollection(AuthorIs("Asagi"));

        CollectionAssert.AreEqual(new[] {mine}, await MemberIds(id));

        await SetAuthor(notYet, "Asagi");

        CollectionAssert.AreEqual(new[] {mine, notYet}.Order().ToArray(), await MemberIds(id),
            "the cached answer has to go when the thing it was an answer about changes");

        await SetAuthor(mine, "Kuro");

        CollectionAssert.AreEqual(new[] {notYet}, await MemberIds(id),
            "and it leaves again when it stops matching");
    }

    /// <summary>
    /// Both kinds of membership at once: a rule is a floor, not a fence. Adding something by hand to
    /// a rule collection has to keep working, or "this one too" would mean rewriting the rule.
    /// </summary>
    [TestMethod]
    public async Task WrittenDownMembersJoinTheRuleMatchesRatherThanReplacingThem()
    {
        var matched = await NewResource("Volume 1", "Asagi");
        var byHand = await NewResource("An artbook nobody credited");

        var id = await NewRuleCollection(AuthorIs("Asagi"));

        await Collections.AddMembers(id, [byHand]);

        CollectionAssert.AreEqual(new[] {matched, byHand}.Order().ToArray(), await MemberIds(id));
    }

    /// <summary>
    /// A resource that is both written down and matched is one member. Otherwise its state — ignored,
    /// where it came from — would depend on which of the two the code happened to look at.
    /// </summary>
    [TestMethod]
    public async Task AMemberThatIsBothWrittenDownAndMatchedAppearsOnceAndKeepsItsState()
    {
        var resourceId = await NewResource("Volume 1", "Asagi");
        var id = await NewRuleCollection(AuthorIs("Asagi"));

        await Collections.AddMembers(id, [resourceId]);
        await Collections.SetMemberIgnored(id, resourceId, true);

        var members = await Collections.GetMembers(id);

        Assert.AreEqual(1, members.Count);
        Assert.IsTrue(members[0].IsIgnored, "the written-down row is the one that carries state");
    }

    /// <summary>
    /// Ratios work the same for rule members — they are members, not a second kind of thing.
    /// </summary>
    [TestMethod]
    public async Task ProgressCountsRuleMatchesLikeAnyOtherMember()
    {
        var owned = await NewResource("Have it", "Asagi");
        await NewResource("Do not have it", "Asagi");

        var resource = (await Resources.Get(owned))!;
        resource.Path = "/library/Have it";
        await Resources.AddOrPutRange([resource]);

        var progress = await Collections.GetProgress(await NewRuleCollection(AuthorIs("Asagi")));

        Assert.AreEqual(2, progress.Total);
        Assert.AreEqual(1, progress.Owned);
    }

    /// <summary>
    /// A rule is half-written for most of the time somebody is writing it. That state has to show
    /// the collection's written-down members, not an error.
    /// </summary>
    [TestMethod]
    public async Task AnUnreadableRuleLeavesTheWrittenDownMembersAlone()
    {
        var byHand = await NewResource("Volume 1");
        var id = await NewRuleCollection("{ not json at all");

        await Collections.AddMembers(id, [byHand]);

        CollectionAssert.AreEqual(new[] {byHand}, await MemberIds(id));
    }

    /// <summary>
    /// The editor's preview. Writing a rule blind — save it, look at the collection, come back — is
    /// how you end up with a collection that quietly holds the wrong hundred things.
    /// </summary>
    [TestMethod]
    public async Task ARuleCanBePreviewedBeforeItIsSaved()
    {
        await NewResource("Volume 1", "Asagi");
        await NewResource("Volume 2", "Asagi");
        await NewResource("Somebody else's book", "Kuro");

        var controller = new Bakabase.Service.Controllers.CollectionController(
            Collections, Resources, _sp.GetRequiredService<IPlaceholderResourceService>());
        var profiles = _sp.GetRequiredService<IResourceProfileService>();

        var preview = (await controller.PreviewRule(
            new Bakabase.Service.Controllers.CollectionRulePreviewInputModel
            {
                RuleSearchJson = AuthorIs("Asagi"),
                SampleSize = 1
            }, profiles)).Data!;

        Assert.AreEqual(2, preview.TotalCount, "how many it catches is the number that matters");
        Assert.AreEqual(1, preview.SampleResourceIds.Count, "and only a sample of them comes back");

        var empty = (await controller.PreviewRule(
            new Bakabase.Service.Controllers.CollectionRulePreviewInputModel {RuleSearchJson = null},
            profiles)).Data!;

        Assert.AreEqual(0, empty.TotalCount, "an empty rule catches nothing rather than everything");
    }
}
