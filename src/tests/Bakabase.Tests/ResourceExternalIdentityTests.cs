using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceExternalIdentityTests
{
    private IServiceProvider _sp = null!;
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();
    private IResourceExternalIdentityService Identities => _sp.GetRequiredService<IResourceExternalIdentityService>();

    [TestInitialize]
    public async Task Setup() => _sp = await TestServiceBuilder.BuildServiceProvider();

    private async Task<Resource> AddResource(string name, params ResourceExternalIdentity[] identities)
    {
        var resource = ResourceFactory.CreateWithoutIdentity(name);
        resource.ExternalIdentities = identities.ToList();
        await Resources.AddOrPutRange([resource]);
        return resource;
    }

    private static ResourceExternalIdentity Identity(ThirdPartyId site, string id) =>
        new() { ThirdPartyId = site, ExternalId = id };

    [TestMethod]
    public async Task ResourceReadLoadsWorkIdentitiesWithoutTurningThemIntoSources()
    {
        var resource = ResourceFactory.CreateWithoutIdentity("A locally owned work");
        resource.SourceLinks = [new ResourceSourceLink { Source = ResourceSource.PathMark, SourceKey = "mark-1" }];
        resource.ExternalIdentities = [Identity(ThirdPartyId.Bangumi, "123"), Identity(ThirdPartyId.Vndb, "v456")];
        await Resources.AddOrPutRange([resource]);

        var reloaded = (await Resources.Get(resource.Id))!;
        Assert.AreEqual(2, reloaded.ExternalIdentities!.Count);
        Assert.AreEqual(ResourceSource.PathMark, reloaded.SourceLinks!.Single().Source);
        Assert.AreEqual(resource.Id, await Identities.FindResource(ThirdPartyId.Bangumi, "123"),
            "An additional source or external identity must not prevent an exact match.");
        Assert.IsNull(await Identities.FindResource(ThirdPartyId.Vndb, "123"));
        Assert.IsNull(await Identities.FindResource(ThirdPartyId.Bangumi, "12"));
        Assert.IsTrue(reloaded.ExternalIdentities.All(x => x.Id > 0 && x.ResourceId == resource.Id));
        Assert.IsTrue(_sp.GetServices<ICoverProvider>().Any(x => x.Origin == DataOrigin.ExternalIdentity),
            "The normal provider registration must include covers attached to work identities.");
    }

    [TestMethod]
    public async Task EnsureIdentitiesDeduplicatesAndPreservesKnownMetadataAndCovers()
    {
        var resource = await AddResource("Known cover", Identity(ThirdPartyId.Bangumi, "123"));
        var identity = (await Identities.GetByResourceId(resource.Id)).Single();
        identity.MetadataJson = "{\"name\":\"Known work\"}";
        identity.CoverDownloadFailedAt = DateTime.Now;
        await Identities.Update(identity);

        await Identities.EnsureIdentities(resource.Id,
        [
            new() { ThirdPartyId = ThirdPartyId.Bangumi, ExternalId = " 123 ", CoverUrls = ["https://example.invalid/1.jpg"] },
            new() { ThirdPartyId = ThirdPartyId.Bangumi, ExternalId = "123", CoverUrls = ["https://example.invalid/1.jpg", "https://example.invalid/2.jpg"] },
            Identity(ThirdPartyId.Vndb, "v9"), Identity(ThirdPartyId.Vndb, "v9")
        ]);

        var identities = await Identities.GetByResourceId(resource.Id);
        Assert.AreEqual(2, identities.Count);
        var updated = identities.Single(x => x.ThirdPartyId == ThirdPartyId.Bangumi);
        Assert.AreEqual(identity.Id, updated.Id);
        Assert.AreEqual(identity.MetadataJson, updated.MetadataJson);
        CollectionAssert.AreEqual(new[] { "https://example.invalid/1.jpg", "https://example.invalid/2.jpg" }, updated.CoverUrls);
        Assert.IsNull(updated.CoverDownloadFailedAt, "New cover URLs can be attempted immediately.");

        updated.LocalCoverPaths = [Path.Combine(Path.GetTempPath(), "external-identity-cover.jpg")];
        updated.CoverDownloadFailedAt = DateTime.Now;
        await Identities.Update(updated);
        await Identities.ClearLocalCoverPaths(resource.Id);
        var invalidated = (await Identities.GetByResourceId(resource.Id)).Single(x => x.Id == updated.Id);
        Assert.IsNull(invalidated.LocalCoverPaths);
        Assert.IsNull(invalidated.CoverDownloadFailedAt);
        CollectionAssert.AreEqual(updated.CoverUrls, invalidated.CoverUrls);
    }

    [TestMethod]
    public async Task PlatformLinksAreNotMirroredIntoTheWorkIdentityTable()
    {
        var resource = ResourceFactory.CreateForExternalIdentity(ThirdPartyId.Pixiv, "123", "An illustration");
        await Resources.AddOrPutRange([resource]);

        Assert.AreEqual(0, (await Identities.GetByResourceId(resource.Id)).Count);
        Assert.IsNull(await Identities.FindResource(ThirdPartyId.Pixiv, "123"));
        Assert.AreEqual(ResourceSource.Pixiv, (await Resources.Get(resource.Id))!.SourceLinks!.Single().Source);
    }

    [TestMethod]
    public async Task SeparateLocalVariantsCanShareAnIdentityAndAreReportedAsConflicts()
    {
        var first = await AddResource("First edition", Identity(ThirdPartyId.Vndb, "v1"));
        var second = await AddResource("Second edition", Identity(ThirdPartyId.Vndb, "v1"));
        await AddResource("Another work", Identity(ThirdPartyId.Bangumi, "1"));

        Assert.AreEqual(1, (await Identities.GetByResourceId(second.Id)).Count);
        CollectionAssert.AreEquivalent(new[] { second.Id }, (await Resources.GetConflictingResourceIds(first.Id)).ToArray());
    }

    [TestMethod]
    public async Task ManualMergeKeepsAllWorkIdsAndDropsPathsBelongingToTheDeletedResource()
    {
        var target = await AddResource("Survivor", Identity(ThirdPartyId.Bangumi, "1"));
        var source = await AddResource("Incoming", Identity(ThirdPartyId.Bangumi, "2"), Identity(ThirdPartyId.Vndb, "v3"));
        var moving = (await Identities.GetByResourceId(source.Id)).Single(x => x.ThirdPartyId == ThirdPartyId.Bangumi);
        moving.CoverUrls = ["https://example.invalid/cover.jpg"];
        moving.LocalCoverPaths = [Path.Combine(Path.GetTempPath(), "deleted-resource-cache.jpg")];
        moving.MetadataJson = "{\"edition\":\"original\"}";
        await Identities.Update(moving);

        await Resources.MergeResources(new ResourceMergeInputModel
        {
            TargetResourceId = target.Id,
            SourceResourceIds = [source.Id]
        });

        Assert.IsNull(await Resources.Get(source.Id));
        Assert.AreEqual(0, (await Identities.GetByResourceId(source.Id)).Count);
        var survivorIdentities = (await Resources.Get(target.Id))!.ExternalIdentities!;
        Assert.AreEqual(3, survivorIdentities.Count, "Different works on one site are retained for explicit review.");
        var moved = survivorIdentities.Single(x => x.ThirdPartyId == ThirdPartyId.Bangumi && x.ExternalId == "2");
        CollectionAssert.AreEqual(moving.CoverUrls, moved.CoverUrls);
        Assert.AreEqual(moving.MetadataJson, moved.MetadataJson);
        Assert.IsNull(moved.LocalCoverPaths, "The survivor downloads its own cache copy from the preserved URLs.");
        Assert.AreEqual(target.Id, await Identities.FindResource(ThirdPartyId.Vndb, "v3"));
    }

    [TestMethod]
    public async Task ConfirmingAMatchMovesWorkIdentitiesBeforeDeletingThePlaceholder()
    {
        var target = await AddResource("Survivor", Identity(ThirdPartyId.Bangumi, "1"));
        var source = await AddResource("Incoming", Identity(ThirdPartyId.Bangumi, "2"), Identity(ThirdPartyId.Vndb, "v3"));
        var suggestions = _sp.GetRequiredService<IResourceMatchSuggestionService>();
        await suggestions.Suggest(source.Id, [new ResourceMatchCandidate(target.Id, 0.95, "User-confirmed match")]);

        await suggestions.Confirm((await suggestions.GetPending()).Single().Id);

        Assert.IsNull(await Resources.Get(source.Id));
        Assert.AreEqual(0, (await Identities.GetByResourceId(source.Id)).Count);
        Assert.AreEqual(3, (await Identities.GetByResourceId(target.Id)).Count);
        Assert.AreEqual(target.Id, await Identities.FindResource(ThirdPartyId.Vndb, "v3"));
    }

    [TestMethod]
    public async Task MaterializingOntoAnExistingResourceAndRemovingFilesKeepsBothIdentities()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"IdentityMaterialization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var occupant = await AddResource("Local files", Identity(ThirdPartyId.Bangumi, "10"));
            occupant.Path = directory;
            await Resources.AddOrPutRange([occupant]);
            var target = await AddResource("Tracked work", Identity(ThirdPartyId.Vndb, "v10"));
            var materialization = _sp.GetRequiredService<IResourceMaterializationService>();

            var result = await materialization.MaterializeAsync(target.Id, directory,
                new MaterializationOptions(EnqueuePathMarkSync: false));
            Assert.AreEqual(occupant.Id, result.MergedResourceId);
            Assert.IsNull(await Resources.Get(occupant.Id));
            Assert.AreEqual(2, (await Identities.GetByResourceId(target.Id)).Count);

            await materialization.DematerializeAsync(target.Id);
            Assert.IsFalse((await Resources.Get(target.Id))!.HasLocalPath);
            Assert.AreEqual(2, (await Identities.GetByResourceId(target.Id)).Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public async Task DeletingAResourceRemovesItsWorkIdentityAssociations()
    {
        var resource = await AddResource("Removed", Identity(ThirdPartyId.Vndb, "v99"));
        await Resources.DeleteByKeys([resource.Id]);

        Assert.AreEqual(0, (await Identities.GetByResourceId(resource.Id)).Count);
        Assert.IsNull(await Identities.FindResource(ThirdPartyId.Vndb, "v99"));
        Assert.AreEqual(0, (await Identities.GetByResourceIdsGrouped([resource.Id])).Count);
    }
}
