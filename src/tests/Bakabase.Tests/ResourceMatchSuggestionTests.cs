using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// The things that might already be here.
/// <para>
/// A list brought in from outside creates a resource for everything on it, and some of those are
/// already tracked under a slightly different name. What is only alike never merges by itself: a
/// wrong merge takes a resource's files, properties and history with it, and no similarity score is
/// worth that. It goes on a list instead, and a person decides.
/// </para>
/// </summary>
[TestClass]
public sealed class ResourceMatchSuggestionTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup() => _sp = await TestServiceBuilder.BuildServiceProvider();

    private IPlaceholderResourceService Placeholders => _sp.GetRequiredService<IPlaceholderResourceService>();
    private IResourceMatchSuggestionService Suggestions =>
        _sp.GetRequiredService<IResourceMatchSuggestionService>();
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();

    private async Task<int> NewResource(string title) =>
        (await Placeholders.CreateByTitle(title)).ResourceId;

    [TestMethod]
    public async Task ANameThatIsAlmostTheSameIsAskedAboutRatherThanReused()
    {
        var original = await NewResource("The Melancholy of Haruhi Suzumiya");
        var shared = await NewResource("Melancholy of Haruhi Suzumiya");

        Assert.AreNotEqual(original, shared,
            "close is not the same: reusing the resource on a guess is the thing this must never do");

        var pending = await Suggestions.GetPending();

        Assert.AreEqual(1, pending.Count);
        Assert.AreEqual(shared, pending[0].ResourceId, "the new one is what would go away");
        Assert.AreEqual(original, pending[0].CandidateResourceId);
        Assert.IsTrue(pending[0].Score > 0.8);
    }

    /// <summary>
    /// The pair that would discredit the whole list. Volumes of one series look more alike than most
    /// real matches do.
    /// </summary>
    [TestMethod]
    public async Task VolumesOfOneSeriesAreNeverPutForward()
    {
        await NewResource("Higurashi no Naku Koro ni Kai Chapter 1");
        await NewResource("Higurashi no Naku Koro ni Kai Chapter 2");

        Assert.AreEqual(0, (await Suggestions.GetPending()).Count);
    }

    [TestMethod]
    public async Task SomethingUnlikeAnythingHereRaisesNoQuestion()
    {
        await NewResource("Higurashi no Naku Koro ni");
        await NewResource("Little Busters Ecstasy");

        Assert.AreEqual(0, (await Suggestions.GetPending()).Count);
    }

    /// <summary>
    /// Confirming is the whole point: the newly found thing stops being a second resource, and
    /// everything it had arrived with — where it can be got, what collection wanted it — is now the
    /// existing resource's.
    /// </summary>
    [TestMethod]
    public async Task ConfirmingMovesWhatTheNewOneCarriedAndTakesItAway()
    {
        var original = await NewResource("The Melancholy of Haruhi Suzumiya");
        var shared = await NewResource("Melancholy of Haruhi Suzumiya");

        var collections = _sp.GetRequiredService<ICollectionService>();
        var collectionId = (await collections.Add(new CollectionInputModel {Name = "Wanted"})).Id;
        await collections.AddMembers(collectionId, [shared]);

        var leads = _sp.GetRequiredService<IAcquisitionLeadService>();
        await leads.Add(shared, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.SharedPage,
            Value = "https://example.invalid/post/1",
            Origin = AcquisitionLeadOrigin.User
        });

        var suggestion = (await Suggestions.GetPending()).Single();
        await Suggestions.Confirm(suggestion.Id);

        Assert.IsNull(await Resources.Get(shared), "two resources became one");

        var members = await collections.GetMembers(collectionId);
        CollectionAssert.AreEqual(new[] {original}, members.Select(m => m.ResourceId).ToArray(),
            "the collection wanted the work, not the row that stood for it");

        var moved = await leads.GetByResourceId(original);
        Assert.AreEqual("https://example.invalid/post/1", moved.Single().Value,
            "where to get it is the most valuable thing the newcomer brought");

        Assert.AreEqual(0, (await Suggestions.GetPending()).Count,
            "a question about a resource that no longer exists is not a question");
    }

    /// <summary>
    /// The other half of the contract: saying no has to stick. A source that lists the same thing
    /// every week must not ask the same question every week.
    /// </summary>
    [TestMethod]
    public async Task TellingTwoThingsApartIsRemembered()
    {
        var original = await NewResource("The Melancholy of Haruhi Suzumiya");
        var shared = await NewResource("Melancholy of Haruhi Suzumiya");

        await Suggestions.Dismiss((await Suggestions.GetPending()).Single().Id);

        Assert.AreEqual(0, (await Suggestions.GetPending()).Count);

        await Suggestions.Suggest(shared, [new ResourceMatchCandidate(original, 0.95, "asked again")]);

        Assert.AreEqual(0, (await Suggestions.GetPending()).Count,
            "the answer was given once, and it was no");
    }

    /// <summary>
    /// A resource with files has a path somebody's player, mark or media library points at. Nothing
    /// about a similar name is worth losing that, so only a placeholder can be merged away.
    /// </summary>
    [TestMethod]
    public async Task SomethingWithFilesIsNeverMergedAway()
    {
        await NewResource("The Melancholy of Haruhi Suzumiya");
        var shared = await NewResource("Melancholy of Haruhi Suzumiya");

        var resource = (await Resources.Get(shared))!;
        resource.Path = "/library/Melancholy of Haruhi Suzumiya";
        await Resources.AddOrPutRange([resource]);

        var suggestion = (await Suggestions.GetPending()).Single();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => Suggestions.Confirm(suggestion.Id));

        Assert.IsNotNull(await Resources.Get(shared));
    }

    /// <summary>
    /// The list is only useful while everything on it is plausible, so one new resource gets to
    /// raise a handful of questions rather than one per thing it vaguely resembles.
    /// </summary>
    [TestMethod]
    public async Task OneNewResourceDoesNotFillTheListOnItsOwn()
    {
        foreach (var suffix in new[] {"aa", "bb", "cc", "dd", "ee"})
        {
            await NewResource($"Kimi to Kanojo to Kanojo no Koi {suffix}");
        }

        var last = await NewResource("Kimi to Kanojo to Kanojo no Koi");

        var raisedByTheLastOne = (await Suggestions.GetPending()).Count(s => s.ResourceId == last);

        Assert.IsTrue(raisedByTheLastOne is > 0 and <= 3,
            $"it resembles five things and asked about {raisedByTheLastOne} of them");
    }
}
