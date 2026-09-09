using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Identity;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

/// <summary>
/// "I am missing X" is a complete thought on its own — no collection, no subscription, no download
/// pipeline. These tests cover the three ways a user can say it (a name, an identity on a platform,
/// a link someone shared) and the matching that keeps saying it twice from producing two resources.
/// </summary>
[TestClass]
public sealed class PlaceholderResourceServiceTests
{
    private IServiceProvider _sp = null!;
    private readonly FakeDLsiteLookup _dlsite = new();
    private readonly FakeSharedUrlTitleResolver _titles = new();

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            // The real lookups talk to DLsite and the open web; these answer for them.
            services.RemoveAll<IExternalIdentityLookup>();
            services.AddSingleton<IExternalIdentityLookup>(_dlsite);
            services.RemoveAll<ISharedUrlTitleResolver>();
            services.AddSingleton<ISharedUrlTitleResolver>(_titles);
        });
    }

    private IPlaceholderResourceService Service => _sp.GetRequiredService<IPlaceholderResourceService>();
    private IResourceService ResourceService => _sp.GetRequiredService<IResourceService>();

    private async Task<string?> ReadName(int resourceId) =>
        (await _sp.GetRequiredService<IReservedPropertyValueService>()
            .GetAll(v => v.ResourceId == resourceId))
        .Select(v => v.Name)
        .FirstOrDefault(n => !string.IsNullOrEmpty(n));

    [TestMethod]
    public async Task CreateByTitle_MakesAResourceWithNoLocalFiles()
    {
        var result = await Service.CreateByTitle("Some Game That Is Not Here");

        Assert.IsTrue(result.Created);
        var resource = (await ResourceService.Get(result.ResourceId))!;
        Assert.IsFalse(resource.HasLocalPath, "nothing has been downloaded yet");
        Assert.AreEqual(ResourceStatus.Active, resource.Status);
        Assert.AreEqual("Some Game That Is Not Here", await ReadName(result.ResourceId));
    }

    [TestMethod]
    public async Task CreateByTitle_SaidTwice_MatchesInsteadOfDuplicating()
    {
        var first = await Service.CreateByTitle("A Work");
        var second = await Service.CreateByTitle("  a work  ");

        Assert.IsTrue(first.Created);
        Assert.IsFalse(second.Created, "the same name is the same thing");
        Assert.AreEqual(first.ResourceId, second.ResourceId);
        Assert.AreEqual(1, (await ResourceService.GetAll()).Count);
    }

    [TestMethod]
    public async Task CreateOrMatchByExternalIdentity_CarriesTheIdentityAndWhatThePlatformCallsIt()
    {
        _dlsite.Details["RJ00000123"] = new ExternalIdentityDetail("RJ00000123", "とある作品",
            ["https://img.dlsite.jp/RJ00000123.jpg"]);

        var result = await Service.CreateOrMatchByExternalIdentity(ResourceSource.DLsite, "RJ00000123");

        Assert.IsTrue(result.Created);
        Assert.AreEqual("とある作品", result.Name);
        Assert.AreEqual("とある作品", await ReadName(result.ResourceId));

        var links = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([result.ResourceId]);
        Assert.AreEqual(1, links.Count);
        Assert.AreEqual(ResourceSource.DLsite, links[0].Source);
        Assert.AreEqual("RJ00000123", links[0].SourceKey);
        CollectionAssert.AreEqual(new[] { "https://img.dlsite.jp/RJ00000123.jpg" },
            links[0].CoverUrls?.ToArray(), "the cover is worth showing before anything is downloaded");
    }

    [TestMethod]
    public async Task CreateOrMatchByExternalIdentity_SecondTimeMatchesTheSameResource()
    {
        _dlsite.Details["RJ00000123"] = new ExternalIdentityDetail("RJ00000123", "とある作品", null);

        var first = await Service.CreateOrMatchByExternalIdentity(ResourceSource.DLsite, "RJ00000123");
        var second = await Service.CreateOrMatchByExternalIdentity(ResourceSource.DLsite, "RJ00000123");

        Assert.IsFalse(second.Created, "the identity is the same, so the resource is the same");
        Assert.AreEqual(first.ResourceId, second.ResourceId);
        Assert.AreEqual(1, _dlsite.LookupCount, "a resource that is already known needs no lookup");
    }

    [TestMethod]
    public async Task CreateOrMatchByExternalIdentity_StillCreatesWhenThePlatformIsUnreachable()
    {
        _dlsite.Throw = true;

        var result = await Service.CreateOrMatchByExternalIdentity(ResourceSource.DLsite, "RJ00000999");

        Assert.IsTrue(result.Created, "knowing what you are missing does not depend on the site being up");
        Assert.AreEqual("RJ00000999", await ReadName(result.ResourceId),
            "with no title to be had, the id is what the resource is known by");
    }

    [TestMethod]
    public async Task CreateOrMatchBySharedUrl_AttachesTheLinkAsALeadNotAnIdentity()
    {
        _titles.Titles["https://forum.example.com/read.php?tid=1"] = "[汉化] Some Work";

        var result = await Service.CreateOrMatchBySharedUrl("https://forum.example.com/read.php?tid=1");

        Assert.IsTrue(result.Created);
        Assert.AreEqual("[汉化] Some Work", result.Name);

        var links = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([result.ResourceId]);
        Assert.AreEqual(0, links.Count, "a forum shares a link; it does not own the work");

        var leads = await _sp.GetRequiredService<IAcquisitionLeadService>()
            .GetByResourceId(result.ResourceId);
        Assert.AreEqual(1, leads.Count);
        Assert.AreEqual(AcquisitionLeadKind.SharedPage, leads[0].Kind);
        Assert.AreEqual("https://forum.example.com/read.php?tid=1", leads[0].Value);
    }

    [TestMethod]
    public async Task CreateOrMatchBySharedUrl_MatchesAnExistingResourceByItsTitle()
    {
        var existing = await Service.CreateByTitle("Some Work");
        _titles.Titles["https://forum.example.com/read.php?tid=2"] = "Some Work";

        var result = await Service.CreateOrMatchBySharedUrl("https://forum.example.com/read.php?tid=2");

        Assert.IsFalse(result.Created, "the page is about a work already being tracked");
        Assert.AreEqual(existing.ResourceId, result.ResourceId);

        var leads = await _sp.GetRequiredService<IAcquisitionLeadService>()
            .GetByResourceId(existing.ResourceId);
        Assert.AreEqual(1, leads.Count, "the link is worth keeping — it is where the files can be had");
    }

    [TestMethod]
    public async Task CreateOrMatchBySharedUrl_AStorePageIsAnIdentityAfterAll()
    {
        _dlsite.Details["RJ00000456"] = new ExternalIdentityDetail("RJ00000456", "A Bought Work", null);

        var result = await Service.CreateOrMatchBySharedUrl(
            "https://www.dlsite.com/maniax/work/=/product_id/RJ00000456.html");

        var links = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([result.ResourceId]);
        Assert.AreEqual(1, links.Count, "a store page identifies the work rather than merely sharing it");
        Assert.AreEqual(ResourceSource.DLsite, links[0].Source);
        Assert.AreEqual("RJ00000456", links[0].SourceKey);
        Assert.AreEqual(0, (await _sp.GetRequiredService<IAcquisitionLeadService>()
            .GetByResourceId(result.ResourceId)).Count);
    }

    [TestMethod]
    public async Task SyncingASourceWithNoResolver_DoesNothingAtAll()
    {
        var result = await Service.CreateOrMatchByExternalIdentity(ResourceSource.Bangumi, "12345");
        var before = (await ResourceService.Get(result.ResourceId))!;

        var syncResult = await _sp.GetRequiredService<ResourceSyncService>().SyncResources(
            ResourceSource.Bangumi, null, null, new PauseToken(), CancellationToken.None);

        Assert.AreEqual(0, syncResult.ResourcesCreated);
        Assert.AreEqual(0, syncResult.ResourcesDeleted);

        var after = (await ResourceService.Get(result.ResourceId))!;
        Assert.AreEqual(before.Status, after.Status,
            "a source nothing can discover must not conclude its resources have gone missing");
    }

    [TestMethod]
    public void ExternalIdentityParser_RecognisesIdsAndPageLinks()
    {
        AssertExtracts("RJ01234567", ResourceSource.DLsite, "RJ01234567");
        AssertExtracts("rj01234567", ResourceSource.DLsite, "RJ01234567");
        AssertExtracts("https://www.dlsite.com/maniax/work/=/product_id/RJ01234567.html",
            ResourceSource.DLsite, "RJ01234567");
        AssertExtracts("https://store.steampowered.com/app/570/Dota_2/", ResourceSource.Steam, "570");
        AssertExtracts("https://exhentai.org/g/123456/abcdef0123/", ResourceSource.ExHentai,
            "123456/abcdef0123");
        AssertExtracts("https://bgm.tv/subject/8", ResourceSource.Bangumi, "8");
        AssertExtracts("https://www.pixiv.net/en/artworks/98765", ResourceSource.Pixiv, "98765");

        // A bare number could be a Steam app, a Bangumi subject or a Pixiv artwork; guessing would
        // attach the resource to the wrong platform.
        Assert.IsFalse(ExternalIdentityParser.TryExtract("570", out _, out _));
        Assert.IsFalse(ExternalIdentityParser.TryExtract("Some Game Title", out _, out _));

        // Once the platform is known it is no longer ambiguous.
        Assert.IsTrue(ExternalIdentityParser.TryExtractFor(ResourceSource.Steam, "570", out var steamKey));
        Assert.AreEqual("570", steamKey);
    }

    private static void AssertExtracts(string input, ResourceSource expectedSource, string expectedKey)
    {
        Assert.IsTrue(ExternalIdentityParser.TryExtract(input, out var source, out var key),
            $"'{input}' should be recognised");
        Assert.AreEqual(expectedSource, source, input);
        Assert.AreEqual(expectedKey, key, input);
    }

    private sealed class FakeDLsiteLookup : IExternalIdentityLookup
    {
        public Dictionary<string, ExternalIdentityDetail> Details { get; } = new();
        public bool Throw { get; set; }
        public int LookupCount { get; private set; }

        public ResourceSource Source => ResourceSource.DLsite;

        public Task<ExternalIdentityDetail?> Lookup(string sourceKey, CancellationToken ct)
        {
            LookupCount++;
            if (Throw)
            {
                throw new InvalidOperationException("dlsite is down");
            }

            return Task.FromResult(Details.GetValueOrDefault(sourceKey));
        }
    }

    private sealed class FakeSharedUrlTitleResolver : ISharedUrlTitleResolver
    {
        public Dictionary<string, string> Titles { get; } = new();

        public Task<string?> ResolveTitle(string url, CancellationToken ct) =>
            Task.FromResult(Titles.GetValueOrDefault(url));
    }
}
