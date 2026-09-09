using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Service.Components.Subscription.Providers.Bangumi;
using Bakabase.Service.Components.Subscription.Providers.DLsite;

namespace Bakabase.Tests;

/// <summary>
/// Sources that say what exists rather than what you have.
/// <para>
/// A catalog is the only kind of source that can tell you about something you have never heard
/// of — which is also why its members show as missing and stay that way until someone says where
/// to get them. It names works, so its items carry identities.
/// </para>
/// </summary>
[TestClass]
public sealed class CatalogSourceTests
{
    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Subscription", name));

    [TestMethod]
    public void ACirclePageListsItsWorksOnce()
    {
        var works = DLsiteListParser.Parse(Fixture("dlsite-circle-page1.html"));

        CollectionAssert.AreEqual(new[] {"RJ01111111", "VJ00002222"},
            works.Select(w => w.WorkId).ToArray(),
            "each work is linked three times on the page and is one work");

        Assert.AreEqual("First Work", works[0].Title);
        Assert.AreEqual("https://img.dlsite.jp/RJ01111111.jpg", works[0].CoverUrl,
            "the real cover is in data-src; src is the lazy-load placeholder");
    }

    [TestMethod]
    public void TheListingPagerSaysWhereTheNextPageIs()
    {
        const string pageUrl = "https://www.dlsite.com/maniax/circle/profile/=/maker_id/RG11111.html";

        Assert.AreEqual(
            "https://www.dlsite.com/maniax/circle/profile/=/maker_id/RG11111.html/page/2",
            DLsiteListParser.FindNextPageUrl(Fixture("dlsite-circle-page1.html"), pageUrl));
    }

    /// <summary>
    /// A catalog names works, so an item carries an identity. That is what lets buying one later
    /// recognise it instead of making a second copy.
    /// </summary>
    [TestMethod]
    public void ACirclePageIsACatalogWithADLsiteIdentity()
    {
        var provider = new DLsiteCircleProvider(null!);

        Assert.AreEqual(SubscriptionSourceKind.Catalog, provider.SourceKind);
        Assert.AreEqual(ResourceSource.DLsite, provider.ResourceSource);
    }

    [TestMethod]
    public async Task ACirclePageMustBeADLsitePage()
    {
        var provider = new DLsiteCircleProvider(null!);

        Assert.IsFalse((await provider.ValidateTargetAsync("{}", CancellationToken.None)).IsValid);
        Assert.IsFalse((await provider.ValidateTargetAsync(
            """{"url":"https://example.com/circle"}""", CancellationToken.None)).IsValid,
            "a page that is not DLsite's cannot list DLsite works");
        Assert.IsTrue((await provider.ValidateTargetAsync(
            """{"url":"https://www.dlsite.com/maniax/circle/profile/=/maker_id/RG11111.html"}""",
            CancellationToken.None)).IsValid);
    }

    [TestMethod]
    public void ABangumiSeriesIsACatalogWithABangumiIdentity()
    {
        var provider = new BangumiSubjectRelationsProvider(null!);

        Assert.AreEqual(SubscriptionSourceKind.Catalog, provider.SourceKind);
        Assert.AreEqual(ResourceSource.Bangumi, provider.ResourceSource);
    }

    /// <summary>
    /// Bangumi's ids only ever appear inside URLs, so asking the user to extract one by hand
    /// would be a small cruelty for no reason.
    /// </summary>
    [TestMethod]
    public async Task ASubjectIsAcceptedAsAnIdOrAsThePageItWasCopiedFrom()
    {
        var provider = new BangumiSubjectRelationsProvider(null!);

        foreach (var subject in new[] {"12345", "https://bgm.tv/subject/12345", "bangumi.tv/subject/12345"})
        {
            var json = JsonSerializer.Serialize(
                new BangumiSubjectRelationsTarget {Subject = subject}, Camel);

            Assert.IsTrue((await provider.ValidateTargetAsync(json, CancellationToken.None)).IsValid,
                subject);
            StringAssert.Contains(provider.DescribeTarget(json), "12345", subject);
        }

        var nonsense = JsonSerializer.Serialize(
            new BangumiSubjectRelationsTarget {Subject = "an anime I like"}, Camel);

        Assert.IsFalse((await provider.ValidateTargetAsync(nonsense, CancellationToken.None)).IsValid);
    }

    /// <summary>
    /// The API answers with both names; the Chinese one is what the site itself leads with for a
    /// Chinese-language audience, and falling back rather than showing nothing is the point.
    /// </summary>
    [TestMethod]
    public void ARelatedSubjectPrefersItsChineseNameButDoesNotRequireOne()
    {
        var withBoth = new BangumiRelatedSubject {Id = 1, Name = "とある", NameCn = "某科学的"};
        var withoutCn = new BangumiRelatedSubject {Id = 2, Name = "とある"};

        Assert.AreEqual("某科学的", withBoth.DisplayName);
        Assert.AreEqual("とある", withoutCn.DisplayName);
        Assert.AreEqual("https://bgm.tv/subject/2", withoutCn.Url);
    }

    /// <summary>
    /// The one place a subject id can be recovered after an enhancement: the page it was read
    /// from. Without it the enhancer's hardest decision — that this folder is that work — is
    /// thrown away and re-derived by search every time.
    /// </summary>
    [TestMethod]
    public void ADetailRemembersWhichSubjectItCameFrom()
    {
        Assert.AreEqual("389156",
            new BangumiDetail {DetailUrl = "https://bgm.tv/subject/389156"}.SubjectId);
        Assert.IsNull(new BangumiDetail().SubjectId);
    }
}
