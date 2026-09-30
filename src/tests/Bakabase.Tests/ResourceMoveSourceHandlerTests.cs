using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Orm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceMoveSourceHandlerTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private readonly string _source = Path.Combine(Path.GetTempPath(), "move-source-handler", "old").Replace('\\', '/');
    private readonly string _target = Path.Combine(Path.GetTempPath(), "move-source-handler", "new").Replace('\\', '/');

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    private IResourceSourceMoveHandler Handler(ResourceSource source) =>
        _services.GetServices<IResourceSourceMoveHandler>().Single(h => h.Source == source);

    private ResourceSourceMoveContext Context(ResourceSource source) => new(10, 10, _source, _target,
        new ResourceSourceLink { Id = 20, ResourceId = 10, Source = source,
            SourceKey = source == ResourceSource.DLsite ? "RJ123456" : "123/abc" });

    private static ResourceSourceMoveStep Step(ResourceSourceMoveContext context, ResourceSourceMoveEvaluation result) => new()
    {
        RootResourceId = context.RootResourceId, ResourceId = context.ResourceId, LinkId = context.Link.Id,
        Source = context.Link.Source, SourceKey = context.Link.SourceKey, HandlerVersion = 1,
        SourcePath = context.SourcePath, DestPath = context.DestPath, PreviousLocation = result.PreviousLocation,
        NewLocation = result.NewLocation, StateJson = result.StateJson
    };

    private async Task SeedPlatform(ResourceSource source, string path)
    {
        if (source == ResourceSource.DLsite)
            Db.DLsiteWorks.Add(new DLsiteWorkDbModel { WorkId = "RJ123456", ResourceId = 10,
                IsDownloaded = true, IsPurchased = true, LocalPath = path, Title = "Keep title", DrmKey = "Keep key" });
        else
            Db.ExHentaiGalleries.Add(new ExHentaiGalleryDbModel { GalleryId = 123, GalleryToken = "abc", ResourceId = 10,
                IsDownloaded = true, LocalPath = path, Title = "Keep title", Account = "Keep account" });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
    }

    [DataTestMethod]
    [DataRow(ResourceSource.DLsite)]
    [DataRow(ResourceSource.ExHentai)]
    public async Task PlatformLocation_NestedMappingIsIdempotentAndRefreshesCachedRows(ResourceSource source)
    {
        await SeedPlatform(source, _source + "/content");
        // Load the same caches consumed by platform pages before changing the database.
        var dlCache = _services.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, DLsiteWorkDbModel, int>>();
        var ehCache = _services.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ExHentaiGalleryDbModel, int>>();
        await dlCache.GetAll();
        await ehCache.GetAll();
        var handler = Handler(source);
        var context = Context(source);
        var evaluation = await handler.EvaluateAsync(context);
        Assert.IsNull(evaluation.ReasonCode);
        Assert.AreEqual(_target + "/content", evaluation.NewLocation);
        var step = Step(context, evaluation);
        await handler.ValidateRecordedStateAsync(step);
        await handler.ApplyAsync(step);
        await handler.ValidateRecordedStateAsync(step);
        // Simulates a crash after the mutation but before saving the source-step checkpoint.
        await handler.ApplyAsync(step);
        if (source == ResourceSource.DLsite)
        {
            var row = (await dlCache.GetAll()).Single();
            Assert.AreEqual(_target + "/content", row.LocalPath);
            Assert.AreEqual("Keep title", row.Title);
            Assert.AreEqual("Keep key", row.DrmKey);
            Assert.IsTrue(row.IsDownloaded && row.IsPurchased);
        }
        else
        {
            var row = (await ehCache.GetAll()).Single();
            Assert.AreEqual(_target + "/content", row.LocalPath);
            Assert.AreEqual("Keep title", row.Title);
            Assert.AreEqual("Keep account", row.Account);
            Assert.IsTrue(row.IsDownloaded);
        }
    }

    [DataTestMethod]
    [DataRow(ResourceSource.DLsite)]
    [DataRow(ResourceSource.ExHentai)]
    public async Task PlatformLocation_ThirdLocationIsNeverOverwritten(ResourceSource source)
    {
        await SeedPlatform(source, _source);
        var context = Context(source);
        var handler = Handler(source);
        var step = Step(context, await handler.EvaluateAsync(context));
        var third = _source + "-other";
        if (source == ResourceSource.DLsite)
            await Db.DLsiteWorks.ExecuteUpdateAsync(s => s.SetProperty(w => w.LocalPath, third));
        else
            await Db.ExHentaiGalleries.ExecuteUpdateAsync(s => s.SetProperty(g => g.LocalPath, third));
        var validationError = await Assert.ThrowsExceptionAsync<ResourceSourceMoveException>(() => handler.ValidateRecordedStateAsync(step));
        Assert.AreEqual("sourceLocationChanged", validationError.ReasonCode);
        var error = await Assert.ThrowsExceptionAsync<ResourceSourceMoveException>(() => handler.ApplyAsync(step));
        Assert.AreEqual("sourceLocationChanged", error.ReasonCode);
        var current = source == ResourceSource.DLsite
            ? (await Db.DLsiteWorks.AsNoTracking().SingleAsync()).LocalPath
            : (await Db.ExHentaiGalleries.AsNoTracking().SingleAsync()).LocalPath;
        Assert.AreEqual(third, current);
        Assert.AreEqual("sourceLocationChanged", (await handler.EvaluateAsync(context)).ReasonCode);
    }

    [TestMethod]
    public async Task PathMark_RekeysOnlyItsPathAndKeepsConcurrentMetadata()
    {
        var row = new ResourceSourceLinkDbModel { ResourceId = 10, Source = ResourceSource.PathMark, SourceKey = _source };
        Db.ResourceSourceLinks.Add(row);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var context = new ResourceSourceMoveContext(10, 10, _source, _target,
            new ResourceSourceLink { Id = row.Id, ResourceId = 10, Source = ResourceSource.PathMark, SourceKey = _source });
        var handler = Handler(ResourceSource.PathMark);
        var step = Step(context, await handler.EvaluateAsync(context));
        await Db.ResourceSourceLinks.ExecuteUpdateAsync(s => s.SetProperty(l => l.MetadataJson, "updated independently"));
        await handler.ValidateRecordedStateAsync(step);
        await handler.ApplyAsync(step);
        await handler.ValidateRecordedStateAsync(step);
        await handler.ApplyAsync(step);
        var result = await Db.ResourceSourceLinks.AsNoTracking().SingleAsync();
        Assert.AreEqual(_target, result.SourceKey);
        Assert.AreEqual("updated independently", result.MetadataJson);
    }

    [TestMethod]
    public async Task SourcesExplicitlyOptIn_AndSteamRejects()
    {
        var handlers = _services.GetServices<IResourceSourceMoveHandler>().ToList();
        CollectionAssert.AreEquivalent(Enum.GetValues<ResourceSource>(), handlers.Select(h => h.Source).ToArray());
        Assert.AreEqual("steamManaged", (await Handler(ResourceSource.Steam).EvaluateAsync(Context(ResourceSource.Steam))).ReasonCode);
        Assert.AreEqual("sourceRecordMissing", (await Handler(ResourceSource.DLsite).EvaluateAsync(Context(ResourceSource.DLsite))).ReasonCode);
    }

    [TestMethod]
    public async Task Steam_OnlyAllowsAcknowledgingAnAlreadyRestoredLegacyInstallation()
    {
        Db.SteamApps.Add(new SteamAppDbModel { AppId = 123, ResourceId = 10, IsInstalled = true, InstallPath = _source });
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();
        var context = new ResourceSourceMoveContext(10, 10, _source, _source,
            new ResourceSourceLink { Id = 20, ResourceId = 10, Source = ResourceSource.Steam, SourceKey = "123" },
            IsSourceRestoration: true);
        var handler = Handler(ResourceSource.Steam);
        Assert.IsNull((await handler.EvaluateAsync(context)).ReasonCode);
        Assert.AreEqual("steamManaged", (await handler.EvaluateAsync(context with { IsSourceRestoration = false })).ReasonCode);
        await Db.SteamApps.ExecuteUpdateAsync(s => s.SetProperty(a => a.InstallPath, _target));
        Assert.AreEqual("sourceLocationChanged", (await handler.EvaluateAsync(context)).ReasonCode);
    }

    [TestMethod]
    public async Task ChangePath_CanDeferNotificationUntilTheCoordinatorFinishes()
    {
        var resources = _services.GetRequiredService<IResourceService>();
        await resources.AddOrPutRange([new Resource { Path = _source }]);
        var resource = (await resources.GetAll()).Single();
        var notifications = 0;
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += _ => notifications++;
        await resources.ChangePath([resource.Id], new() { [resource.Id] = _target }, publishChange: false);
        Assert.AreEqual(0, notifications);
        await resources.ChangePath([resource.Id], new() { [resource.Id] = _source });
        Assert.AreEqual(1, notifications);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangePath_ExpectedPathsRejectsTheWholeBatchOnDriftOrDeletion(bool deleted)
    {
        var resources = _services.GetRequiredService<IResourceService>();
        await resources.AddOrPutRange([new Resource { Path = _source }, new Resource { Path = _source + "/child" }]);
        var rows = (await resources.GetAll()).OrderBy(r => r.Path!.Length).ToArray();
        var expected = rows.ToDictionary(r => r.Id, r => r.Path!);
        var destinations = rows.ToDictionary(r => r.Id, r => _target + r.Path![_source.Length..]);
        var childId = rows[1].Id;
        // Keep the ORM cache primed with the old location to prove this is a database check.
        if (deleted)
            await Db.ResourcesV2.Where(r => r.Id == childId).ExecuteDeleteAsync();
        else
            await Db.ResourcesV2.Where(r => r.Id == childId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Path, _source + "-third"));
        var notifications = 0;
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += _ => notifications++;
        var result = await resources.ChangePath(rows.Select(r => r.Id).ToArray(), destinations,
            expectedPaths: expected);
        Assert.AreNotEqual(0, result.Code);
        Assert.AreEqual(0, notifications);
        var current = await Db.ResourcesV2.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
        Assert.AreEqual(_source, current.Single(r => r.Id == rows[0].Id).Path);
        if (!deleted) Assert.AreEqual(_source + "-third", current.Single(r => r.Id == childId).Path);
    }

    [TestMethod]
    public async Task ChangePath_ExpectedPathsAcceptsRecoveryAndRefreshesCacheWithoutChangingMetadata()
    {
        var resources = _services.GetRequiredService<IResourceService>();
        await resources.AddOrPutRange([new Resource { Path = _source }]);
        var row = (await resources.GetAll()).Single();
        await Db.ResourcesV2.Where(r => r.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Tags, ResourceTag.Pinned));
        var expected = new Dictionary<int, string> { [row.Id] = _source };
        var destinations = new Dictionary<int, string> { [row.Id] = _target };
        Assert.AreEqual(0, (await resources.ChangePath([row.Id], destinations, false, expected)).Code);
        Assert.AreEqual(0, (await resources.ChangePath([row.Id], destinations, false, expected)).Code);
        var updated = (await resources.GetAll()).Single();
        Assert.AreEqual(_target, updated.Path);
        Assert.AreEqual(ResourceTag.Pinned, (await Db.ResourcesV2.AsNoTracking().SingleAsync()).Tags);
    }
}
