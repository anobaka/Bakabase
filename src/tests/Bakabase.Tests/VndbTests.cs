using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Identity;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Vndb;
using Bakabase.Modules.ThirdParty.ThirdParties.Vndb.Models;
using Bakabase.Service.Components.Subscription.Providers.Vndb;

namespace Bakabase.Tests;

/// <summary>
/// VNDB as a catalog source.
/// <para>
/// A filter written wrongly does not fail — it returns a different, plausible list, and the
/// subscription quietly collects the wrong works. So the request bodies are pinned down here
/// alongside the reading of the replies; neither is something the network would tell you about.
/// </para>
/// </summary>
[TestClass]
public sealed class VndbTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void EverythingByADeveloperIsAskedForByProducerId()
    {
        var body = JsonNode.Parse(VndbRequests.ByDeveloper("p17", 1))!.AsObject();
        var filters = body["filters"]!.AsArray();

        Assert.AreEqual("developer", filters[0]!.GetValue<string>());
        Assert.AreEqual("=", filters[1]!.GetValue<string>());

        var nested = filters[2]!.AsArray();

        Assert.AreEqual("id", nested[0]!.GetValue<string>());
        Assert.AreEqual("p17", nested[2]!.GetValue<string>(),
            "a producer filter takes a nested id predicate, not the id on its own");
        Assert.AreEqual(1, body["page"]!.GetValue<int>());
    }

    [TestMethod]
    public void AskingWhatBelongsWithOneWorkReadsItsRelations()
    {
        var body = JsonNode.Parse(VndbRequests.Relations("v17"))!.AsObject();

        Assert.AreEqual("v17", body["filters"]!.AsArray()[2]!.GetValue<string>());
        StringAssert.Contains(body["fields"]!.GetValue<string>(), "relations.relation_official",
            "without this field there is no way to tell a fan disc from a sequel");
    }

    /// <summary>
    /// VNDB says whether another page exists. A full last page is otherwise indistinguishable from
    /// a full middle one, so guessing from the count would drop everything after the first hundred.
    /// </summary>
    [TestMethod]
    public void APageSaysWhetherThereIsAnother()
    {
        var page = JsonSerializer.Deserialize<VndbQueryResponse>(
            """
            {"results":[{"id":"v17","title":"Ever17","released":"2002-08-29","image":{"url":"https://t.vndb.org/cv/17.jpg"}}],"more":true}
            """, Json)!;

        Assert.IsTrue(page.More);
        Assert.AreEqual("v17", page.Results[0].Id);
        Assert.AreEqual("https://vndb.org/v17", page.Results[0].Url);
    }

    /// <summary>
    /// The romanised title is the one people search for; the original is the fallback when VNDB has
    /// no romanisation, which happens for works that were never released in English.
    /// </summary>
    [TestMethod]
    public void AWorkIsCalledWhateverVndbCallsIt()
    {
        Assert.AreEqual("Ever17",
            JsonSerializer.Deserialize<VndbVisualNovel>(
                """{"id":"v17","title":"Ever17","alttitle":"Ever17 -the out of infinity-"}""", Json)!.DisplayName);

        Assert.AreEqual("加奈～いもうと～",
            JsonSerializer.Deserialize<VndbVisualNovel>(
                """{"id":"v99","alttitle":"加奈～いもうと～"}""", Json)!.DisplayName);

        Assert.AreEqual("v1",
            JsonSerializer.Deserialize<VndbVisualNovel>("""{"id":"v1"}""", Json)!.DisplayName,
            "a work with no title at all is still something you can point at");
    }

    [TestMethod]
    public void RelationsCarryWhatKindTheyAreAndWhetherTheyCount()
    {
        var vn = JsonSerializer.Deserialize<VndbVisualNovel>(
            """
            {"id":"v17","title":"Ever17","relations":[
              {"id":"v18","title":"Never7","relation":"ser","relation_official":true},
              {"id":"v19","title":"A fan disc","relation":"fan","relation_official":false}
            ]}
            """, Json)!;

        Assert.AreEqual(2, vn.Relations!.Count);
        Assert.IsTrue(vn.Relations[0].RelationOfficial);
        Assert.IsFalse(vn.Relations[1].RelationOfficial,
            "relation_official is snake_cased in VNDB's replies and would silently read as false if unmapped");
    }

    /// <summary>
    /// A bare v-number reads as "volume 1" in half the file names in a library, so it only counts
    /// as a VNDB identity once somebody has said that is what they mean.
    /// </summary>
    [TestMethod]
    public void ABareVNumberIsNotAnIdentityOnItsOwn()
    {
        Assert.IsFalse(ExternalIdentityParser.TryExtract("Some Work v17", out _, out _));
        Assert.IsFalse(ExternalIdentityParser.TryExtract("v17", out _, out _));

        Assert.IsTrue(ExternalIdentityParser.TryExtractFor(ResourceSource.Vndb, "v17", out var key));
        Assert.AreEqual("v17", key);
    }

    [TestMethod]
    public void AVndbLinkIsAnIdentity()
    {
        Assert.IsTrue(ExternalIdentityParser.TryExtract("https://vndb.org/v17", out var source, out var key));
        Assert.AreEqual(ResourceSource.Vndb, source);
        Assert.AreEqual("v17", key);
    }

    /// <summary>
    /// Asking for "just the id" of a site whose ids mostly appear inside URLs is a small cruelty,
    /// so both are accepted — but only when the whole of what was typed is one.
    /// </summary>
    [TestMethod]
    public void ATargetIsEitherTheIdOrThePageItCameFrom()
    {
        Assert.AreEqual("v17", VndbSeriesProvider.VisualNovelIdOf("https://vndb.org/v17"));
        Assert.AreEqual("v17", VndbSeriesProvider.VisualNovelIdOf(" V17 "));
        Assert.IsNull(VndbSeriesProvider.VisualNovelIdOf("Some Work v17"));
        Assert.IsNull(VndbSeriesProvider.VisualNovelIdOf("p17"), "a producer is not a visual novel");

        Assert.AreEqual("p17", VndbDeveloperProvider.ProducerIdOf("https://vndb.org/p17"));
        Assert.IsNull(VndbDeveloperProvider.ProducerIdOf("v17"));
    }

    [TestMethod]
    public void VndbIsACatalogRatherThanSomewhereThingsAreHeld()
    {
        Assert.IsFalse(ResourceSource.Vndb.IsPlatformHolding(),
            "VNDB knows what a work is and nothing about where to get it");
    }
}
