using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Alias.Models.Domain;
using Bakabase.Modules.Alias.Models.Domain.Constants;
using Bakabase.Modules.Alias.Models.Input;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Alias.Tests;

/// <summary>
/// Critical-path coverage for AliasService.MergeGroups — both overloads.
/// MergeGroups silently rewrites Preferred fields across many rows, and the
/// string[][] overload also auto-creates rows and throws on conflict; if
/// either contract drifts, the UI shows wrong "canonical" labels and the
/// alias-applied-values pipeline (used by enhancer + property display) feeds
/// users the wrong text.
/// </summary>
[TestClass]
public sealed class MergeGroupsTests
{
    private IServiceProvider _sp = null!;
    private IAliasService _aliasService = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _aliasService = _sp.GetRequiredService<IAliasService>();
    }

    private async Task SeedAsync(params (string Text, string? Preferred)[] rows)
    {
        foreach (var (text, preferred) in rows)
        {
            await _aliasService.Add(new AliasAddInputModel { Text = text, Preferred = preferred });
        }
    }

    #region string[] overload — flat merge

    [TestMethod]
    public async Task MergeGroups_Flat_FirstBecomesPreferredAndOthersPointAtIt()
    {
        // Start with three independent rows (each its own group).
        await SeedAsync(("apple", null), ("apple-fruit", null), ("malus", null));

        await _aliasService.MergeGroups(["apple", "apple-fruit", "malus"]);

        var all = await _aliasService.GetAll();
        var byText = all.ToDictionary(a => a.Text, a => a.Preferred);
        byText["apple"].Should().BeNull("first arg is the preferred head and must clear its own Preferred");
        byText["apple-fruit"].Should().Be("apple");
        byText["malus"].Should().Be("apple");
    }

    [TestMethod]
    public async Task MergeGroups_Flat_AbsorbsExistingGroupsViaPreferredLookup()
    {
        // "alpha" is the head of its own group; "a" already points at it.
        // Re-merging with "alpha" first must keep that and pull in "alpha2".
        await SeedAsync(("alpha", null), ("a", "alpha"), ("alpha2", null));

        await _aliasService.MergeGroups(["alpha", "alpha2"]);

        var all = await _aliasService.GetAll();
        all.Should().Contain(a => a.Text == "alpha" && a.Preferred == null);
        all.Should().Contain(a => a.Text == "alpha2" && a.Preferred == "alpha");
        // Pre-existing alias rows that pointed at the head must stay intact.
        all.Should().Contain(a => a.Text == "a" && a.Preferred == "alpha");
    }

    [TestMethod]
    public async Task MergeGroups_Flat_ResetsPreferredOnNewHead()
    {
        // "old-head" used to be preferred; "b" pointed at it. We now promote
        // "b" to be the preferred head. The old head must be downgraded.
        await SeedAsync(("old-head", null), ("b", "old-head"));

        await _aliasService.MergeGroups(["b", "old-head"]);

        var all = await _aliasService.GetAll();
        var byText = all.ToDictionary(a => a.Text, a => a.Preferred);
        byText["b"].Should().BeNull("promoted head must have null Preferred");
        byText["old-head"].Should().Be("b", "former head must now point at new head");
    }

    #endregion

    #region string[][] overload — bulk import with conflict detection

    [TestMethod]
    public async Task MergeGroups_Bulk_CreatesNewGroupsWhenNoneExist()
    {
        await _aliasService.MergeGroups(new[]
        {
            new[] { "cat", "feline", "kitty" },
            new[] { "dog", "canine", "puppy" }
        });

        var all = await _aliasService.GetAll();
        all.Should().Contain(a => a.Text == "cat" && a.Preferred == null);
        all.Should().Contain(a => a.Text == "feline" && a.Preferred == "cat");
        all.Should().Contain(a => a.Text == "kitty" && a.Preferred == "cat");
        all.Should().Contain(a => a.Text == "dog" && a.Preferred == null);
        all.Should().Contain(a => a.Text == "canine" && a.Preferred == "dog");
    }

    [TestMethod]
    public async Task MergeGroups_Bulk_MergesIntoExistingGroupAndAddsMissingMembers()
    {
        // "cat" group already exists; we re-merge with an extra new alias.
        await SeedAsync(("cat", null), ("feline", "cat"));

        await _aliasService.MergeGroups(new[] { new[] { "cat", "kitty" } });

        var all = await _aliasService.GetAll();
        all.Should().Contain(a => a.Text == "cat" && a.Preferred == null);
        all.Should().Contain(a => a.Text == "feline" && a.Preferred == "cat");
        all.Should().Contain(a => a.Text == "kitty" && a.Preferred == "cat",
            "the new member of an existing group must be inserted with Preferred set");
    }

    [TestMethod]
    public async Task MergeGroups_Bulk_PromotesNewHeadAndDowngradesOldHead()
    {
        // Existing group: head="old", member="member". Re-merge with "new" as the head,
        // including "old" and "member". The old head must become an alias of "new".
        await SeedAsync(("old", null), ("member", "old"));

        await _aliasService.MergeGroups(new[] { new[] { "new", "old", "member" } });

        var all = await _aliasService.GetAll();
        var byText = all.ToDictionary(a => a.Text, a => a.Preferred);
        byText["new"].Should().BeNull("the first element of the incoming group is the new head");
        byText["old"].Should().Be("new", "former head must be downgraded to an alias");
        byText["member"].Should().Be("new", "existing member must be reparented");
    }

    [TestMethod]
    public async Task MergeGroups_Bulk_ConflictAcrossGroupsThrowsAndOtherGroupsStillCommitted()
    {
        // Two pre-existing, disjoint groups.
        await SeedAsync(("g1-head", null), ("g1-alias", "g1-head"),
                        ("g2-head", null), ("g2-alias", "g2-head"));

        // The first incoming group is clean; the second straddles g1 and g2.
        var bulk = new[]
        {
            new[] { "clean-head", "clean-alias" },
            new[] { "merged-head", "g1-alias", "g2-alias" }
        };

        var act = async () => await _aliasService.MergeGroups(bulk);

        var ex = await act.Should().ThrowAsync<AliasException>();
        ex.Which.Type.Should().Be(AliasExceptionType.ConflictAliasGroup);

        // Even though one group conflicted, the loop processes all rows before
        // throwing — the clean group must still have been persisted.
        var all = await _aliasService.GetAll();
        all.Should().Contain(a => a.Text == "clean-head" && a.Preferred == null);
        all.Should().Contain(a => a.Text == "clean-alias" && a.Preferred == "clean-head");
    }

    [TestMethod]
    public async Task MergeGroups_Bulk_DedupesTailOnly_DistinctTailValuesCollapse()
    {
        // The pre-merge optimization is `x[0].Concat(x.Skip(1).Distinct())` —
        // it dedupes within the tail, but does not check the head against the
        // tail. Inputs with no head/tail overlap behave cleanly.
        await _aliasService.MergeGroups(new[] { new[] { "h", "x", "x", "y", "y" } });

        var all = await _aliasService.GetAll();
        all.Count(a => a.Text == "h").Should().Be(1);
        all.Count(a => a.Text == "x").Should().Be(1, "tail duplicates must be collapsed");
        all.Count(a => a.Text == "y").Should().Be(1);
        all.Single(a => a.Text == "h").Preferred.Should().BeNull();
        all.Single(a => a.Text == "x").Preferred.Should().Be("h");
        all.Single(a => a.Text == "y").Preferred.Should().Be("h");
    }

    [TestMethod]
    public async Task MergeGroups_Bulk_HeadAppearingInTail_IsDedupedToSingleHeadRow()
    {
        // Regression guard for a fix to the pre-merge optimizer: a caller
        // that passes the head again in the tail (e.g. user-pasted alias
        // list) used to write two rows with Text=head, Preferred=null,
        // leaving an orphan head that SearchGroups picked arbitrarily.
        // The optimizer now filters tail entries equal to the head before
        // Distinct().
        await _aliasService.MergeGroups(new[] { new[] { "head", "head", "tail" } });

        var all = await _aliasService.GetAll();
        all.Count(a => a.Text == "head").Should().Be(1, "head must appear exactly once in DB");
        all.Single(a => a.Text == "head").Preferred.Should().BeNull();
        all.Single(a => a.Text == "tail").Preferred.Should().Be("head");
    }

    #endregion
}
