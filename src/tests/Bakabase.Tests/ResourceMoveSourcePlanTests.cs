using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.ResourceMove;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceMoveSourcePlanTests
{
    private IServiceProvider _sp = null!;
    private string _root = null!;
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private IResourceMoveService Moves => _sp.GetRequiredService<IResourceMoveService>();
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();
    private IResourceSourceLinkService Links => _sp.GetRequiredService<IResourceSourceLinkService>();
    private ResourceMoveGuard Guard => _sp.GetRequiredService<ResourceMoveGuard>();
    private BTaskArgs Args => new(new PauseToken(), CancellationToken.None,
        new BTask("source-plan-test", () => "test"), _ => Task.CompletedTask, _sp);

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), "ResourceMoveSourceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private string Dir(string relative)
    {
        var path = Path.Combine(_root, relative); Directory.CreateDirectory(path); return path;
    }

    private async Task<Resource> Resource(string relative)
    {
        var path = Dir(relative);
        await Resources.AddOrPutRange([new Resource { Path = path }]);
        return (await Resources.GetAll()).Single(r => r.Path == path);
    }

    private Task<ResourceSourceLink> Link(Resource resource, ResourceSource source, string key) =>
        Links.Add(new() { ResourceId = resource.Id, Source = source, SourceKey = key });

    private async Task AddPlatforms(Resource resource)
    {
        Db.DLsiteWorks.Add(new() { WorkId = "RJ123456", ResourceId = resource.Id, LocalPath = resource.Path,
            IsDownloaded = true, IsPurchased = true, DrmKey = "keep-drm", WorkType = "game", Title = "Keep title" });
        Db.ExHentaiGalleries.Add(new() { GalleryId = 123, GalleryToken = "abcd", ResourceId = resource.Id,
            LocalPath = resource.Path, IsDownloaded = true, Account = "keep-account", Title = "Keep gallery" });
        await Db.SaveChangesAsync();
        await Link(resource, ResourceSource.DLsite, "RJ123456");
        await Link(resource, ResourceSource.ExHentai, "123/abcd");
    }

    [TestMethod]
    public async Task SteamRootAndNestedSteam_BlockTheWholePhysicalRoot()
    {
        var parent = await Resource("Library"); var game = await Resource("Library/Game");
        await Link(game, ResourceSource.Steam, "1234"); var dest = Dir("Dest");
        var rootPreview = (await Moves.Preview([game.Id], dest)).Data!;
        rootPreview.ExcludedResources.Single().ReasonCode.Should().Be("steamManaged");
        var parentPreview = (await Moves.Preview([parent.Id, game.Id], dest)).Data!;
        parentPreview.Items.Should().ContainSingle();
        parentPreview.ExcludedResources.Single().ResourceId.Should().Be(parent.Id);
        parentPreview.ExcludedResources.Single().ReasonCode.Should().Be("containsSteamManagedResource");
        parentPreview.ExcludedResources.Single().BlockingResourceIds.Should().Equal(game.Id);
        (await Moves.CreateBatch([game.Id], dest)).Code.Should().NotBe(0);
        (await Moves.CreateBatch([parent.Id], dest)).Code.Should().NotBe(0);
        (await Db.ResourceMoveRecords.AnyAsync()).Should().BeFalse();
        Guard.IsResourceLocked(parent.Id).Should().BeFalse();
        Directory.Exists(game.Path).Should().BeTrue();
    }

    [TestMethod]
    public async Task MovingChildInsideSteamManagedRoot_IsAlsoRejected()
    {
        var game = await Resource("Game"); var child = await Resource("Game/Mods");
        await Link(game, ResourceSource.Steam, "4567"); var dest = Dir("Dest");
        var preview = (await Moves.Preview([child.Id], dest)).Data!;
        preview.ExcludedResources.Single().ReasonCode.Should().Be("steamManaged");
        preview.ExcludedResources.Single().BlockingResourceIds.Should().Equal(game.Id);
        (await Moves.CreateBatch([child.Id], dest)).Code.Should().NotBe(0);
    }

    [TestMethod]
    public async Task UnknownSource_IsNotFilteredIntoAnUnrestrictedLocalMove()
    {
        var resource = await Resource("Unknown");
        Db.ResourceSourceLinks.Add(new() { ResourceId = resource.Id, Source = (ResourceSource)999, SourceKey = "foreign" });
        await Db.SaveChangesAsync();
        var preview = (await Moves.Preview([resource.Id], Dir("Dest"))).Data!;
        preview.ExcludedResources.Single().ReasonCode.Should().Be("sourceMoveUnsupported");
        (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Code.Should().NotBe(0);
    }

    [TestMethod]
    public async Task MovingIntoSteamManagedRoot_IsRejectedEvenWithOverwrite()
    {
        var game = await Resource("Game"); var local = await Resource("Local");
        await Link(game, ResourceSource.Steam, "9876");
        var preview = (await Moves.Preview([local.Id], game.Path!)).Data!;
        preview.ExcludedResources.Single().ReasonCode.Should().Be("steamManaged");
        (await Moves.CreateBatch([local.Id], game.Path!, new() { ConflictPolicy = "overwrite" })).Code.Should().NotBe(0);
        Directory.Exists(local.Path).Should().BeTrue();
        (await Db.ResourceMoveRecords.AnyAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task ParentMove_UpdatesAllSourceLocationsAndKeepsPlatformManagementWorking()
    {
        var parent = await Resource("Parent"); var child = await Resource("Parent/Content");
        await File.WriteAllTextAsync(Path.Combine(child.Path!, "game.exe"), "test data");
        await AddPlatforms(child);
        await Link(parent, ResourceSource.PathMark, parent.Path!);
        await Link(child, ResourceSource.PathMark, child.Path!);
        var originalKeys = (await Links.GetByResourceId(child.Id)).Where(l => l.Source != ResourceSource.PathMark)
            .Select(l => (l.Source, l.SourceKey)).ToArray();
        var dest = Dir("Dest");
        var batch = (await Moves.CreateBatch([parent.Id], dest)).Data!;
        var notifications = new List<(string? WorkPath, string? GalleryPath, ResourceMoveRecordStatus Status)>();
        _sp.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += args =>
        {
            notifications.Add((Db.DLsiteWorks.AsNoTracking().Single().LocalPath,
                Db.ExHentaiGalleries.AsNoTracking().Single().LocalPath,
                Db.ResourceMoveRecords.AsNoTracking().Single().Status));
        };
        await Moves.ExecuteBatch(batch.BatchId, Args);
        // Rebuilding parent-child relationships can publish another event. Every observation
        // above must see committed platform locations and a completed move record.
        notifications.Should().NotBeEmpty();
        notifications.Should().OnlyContain(n => n.WorkPath == dest + "/Parent/Content" &&
            n.GalleryPath == dest + "/Parent/Content" && n.Status == ResourceMoveRecordStatus.Succeeded);
        var work = await _sp.GetRequiredService<IDLsiteWorkService>().GetByWorkId("RJ123456");
        work!.DrmKey.Should().Be("keep-drm"); work.IsDownloaded.Should().BeTrue();
        work.LocalPath.Should().Be(dest + "/Parent/Content");
        var gallery = await _sp.GetRequiredService<IExHentaiGalleryService>().GetByGalleryId(123, "abcd");
        gallery!.Account.Should().Be("keep-account"); gallery.LocalPath.Should().Be(work.LocalPath);
        (await Links.GetByResourceId(child.Id)).Where(l => l.Source != ResourceSource.PathMark)
            .Select(l => (l.Source, l.SourceKey)).Should().BeEquivalentTo(originalKeys);
        var launch = await _sp.GetRequiredService<IDLsiteWorkService>().ResolveLaunchTarget("RJ123456");
        File.Exists(launch.File).Should().BeTrue();
        var sync = _sp.GetRequiredService<ResourceSyncService>();
        await sync.SyncResources(ResourceSource.DLsite, null, null, new PauseToken(), CancellationToken.None);
        await sync.SyncResources(ResourceSource.ExHentai, null, null, new PauseToken(), CancellationToken.None);
        (await Resources.Get(child.Id))!.Path.Should().Be(work.LocalPath);
        Guard.IsResourceLocked(child.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task SourceLinkAddedAfterEnqueue_StopsBeforeTouchingFiles()
    {
        var resource = await Resource("Local"); var dest = Dir("Dest");
        var batch = (await Moves.CreateBatch([resource.Id], dest)).Data!;
        await Link(resource, ResourceSource.Steam, "9999");
        await FluentActions.Awaiting(() => Moves.ExecuteBatch(batch.BatchId, Args)).Should().ThrowAsync<BTaskException>();
        var row = (await Moves.GetRecords()).Single();
        row.Status.Should().Be(ResourceMoveRecordStatus.Failed); row.ErrorCode.Should().Be("steamManaged");
        row.PhysicalMoveStarted.Should().BeFalse(); Directory.Exists(resource.Path).Should().BeTrue();
        Directory.Exists(dest + "/Local").Should().BeFalse();
    }

    [TestMethod]
    public async Task PlatformLocationChangedAfterPreview_InvalidatesConfirmationAndQueuedExecution()
    {
        var resource = await Resource("Local"); await AddPlatforms(resource); var dest = Dir("Dest");
        var preview = (await Moves.Preview([resource.Id], dest)).Data!;
        var batch = (await Moves.CreateBatch([resource.Id], dest)).Data!;
        await Moves.CancelBatch(batch.BatchId);
        await Db.DLsiteWorks.ExecuteUpdateAsync(s => s.SetProperty(w => w.LocalPath, Dir("Different")));
        (await Moves.CreateBatch([resource.Id], dest, new() { ExpectedPreviewFingerprint = preview.PreviewFingerprint })).Code.Should().NotBe(0);
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        Directory.Exists(resource.Path).Should().BeTrue();
        Guard.IsResourceLocked(resource.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task ChangedSourceKeyAfterCancellation_CannotReuseTheOldPlan()
    {
        var resource = await Resource("Local"); var link = await Link(resource, ResourceSource.Pixiv, "123");
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        await Moves.CancelBatch(batch.BatchId);
        link.SourceKey = "456"; await Links.Update(link);
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        Directory.Exists(resource.Path).Should().BeTrue();
    }

    private async Task PhysicallyMove(ResourceMoveRecordDbModel row)
    {
        var state = new ResourceMoveExecutionState { Id = row.Id, SourcePath = row.SourcePath, DestPath = row.DestPath };
        await new LocalFilesResourceMoveExecutor().ExecuteOrResumeAsync(state, async () =>
        {
            row.MoveJournalJson = state.MoveJournalJson; row.PhysicalMoveStarted = state.PhysicalMoveStarted;
            await Db.SaveChangesAsync();
        }, _ => Task.CompletedTask, new PauseToken(), CancellationToken.None, (_, _) => false);
        row.Status = ResourceMoveRecordStatus.NeedsRecovery; await Db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task PostMovePlatformConflict_RetainsReservationAndResumesTheFrozenPlan()
    {
        var resource = await Resource("Local"); await AddPlatforms(resource); var dest = Dir("Dest");
        await File.WriteAllTextAsync(Path.Combine(resource.Path!, "data.txt"), "unchanged");
        var batch = (await Moves.CreateBatch([resource.Id], dest)).Data!;
        var row = await Db.ResourceMoveRecords.SingleAsync();
        var originalPlan = row.ExecutionPlanJson;
        await PhysicallyMove(row);
        var third = Dir("Third");
        await Db.ExHentaiGalleries.ExecuteUpdateAsync(s => s.SetProperty(g => g.LocalPath, third));
        var notifications = 0;
        _sp.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += _ => notifications++;
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        row = await Db.ResourceMoveRecords.SingleAsync();
        row.Status.Should().Be(ResourceMoveRecordStatus.NeedsRecovery);
        Guard.IsResourceLocked(resource.Id).Should().BeTrue(); notifications.Should().Be(0);
        (await Db.ExHentaiGalleries.AsNoTracking().SingleAsync()).LocalPath.Should().Be(third);
        (await Db.DLsiteWorks.AsNoTracking().SingleAsync()).LocalPath.Should().Be(resource.Path);
        await Db.ExHentaiGalleries.ExecuteUpdateAsync(s => s.SetProperty(g => g.LocalPath, resource.Path));
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().Be(0);
        await Moves.ExecuteBatch(batch.BatchId, Args);
        (await Moves.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        notifications.Should().Be(1); Guard.IsResourceLocked(resource.Id).Should().BeFalse();
        File.ReadAllText(Path.Combine(row.DestPath, "data.txt")).Should().Be("unchanged");
        var planned = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(originalPlan!)!;
        var finished = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(row.ExecutionPlanJson!)!;
        finished.ExecutorId.Should().Be(planned.ExecutorId);
        finished.Sources.Select(s => (s.SourceKey, s.PreviousLocation, s.NewLocation))
            .Should().BeEquivalentTo(planned.Sources.Select(s => (s.SourceKey, s.PreviousLocation, s.NewLocation)));
    }

    [TestMethod]
    public async Task MultipleSourceExecutorRequirements_AgreeAndRunOnceForThePhysicalRoot()
    {
        var executor = new CountingExecutor("source-files");
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<IResourceSourceMoveHandler>();
            services.AddSingleton<IResourceSourceMoveHandler>(new SelectingHandler(ResourceSource.Aigc, "source-files"));
            services.AddSingleton<IResourceSourceMoveHandler>(new SelectingHandler(ResourceSource.Pixiv, "source-files"));
            services.AddSingleton<IResourceMoveExecutor>(executor);
        });
        var parent = await Resource("Parent"); var child = await Resource("Parent/Child");
        await Link(parent, ResourceSource.Aigc, "a"); await Link(child, ResourceSource.Pixiv, "b");
        var batch = (await Moves.CreateBatch([parent.Id], Dir("Dest"))).Data!;
        var plan = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>((await Db.ResourceMoveRecords.SingleAsync()).ExecutionPlanJson!)!;
        plan.ExecutorId.Should().Be("source-files"); plan.Sources.Should().HaveCount(2);
        await Moves.ExecuteBatch(batch.BatchId, Args);
        executor.Calls.Should().Be(1);
        (await Resources.Get(child.Id))!.Path.Should().EndWith("Dest/Parent/Child");
    }

    [TestMethod]
    public async Task PartialSourceRepair_CrashAfterApplyBeforeCheckpointIsIdempotent()
    {
        var fault = new FaultSwitch();
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            var registration = services.Single(d => d.ServiceType == typeof(IResourceSourceMoveHandler) &&
                d.ImplementationType == typeof(ExHentaiResourceSourceMoveHandler));
            services.Remove(registration);
            services.AddScoped<ExHentaiResourceSourceMoveHandler>();
            services.AddScoped<IResourceSourceMoveHandler>(sp =>
                new FailOnceAfterApplyHandler(sp.GetRequiredService<ExHentaiResourceSourceMoveHandler>(), fault));
        });
        var resource = await Resource("Local"); await AddPlatforms(resource);
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        var notified = 0;
        _sp.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += _ => notified++;
        await FluentActions.Awaiting(() => Moves.ExecuteBatch(batch.BatchId, Args)).Should().ThrowAsync<BTaskSuspendedException>();
        var row = await Db.ResourceMoveRecords.SingleAsync();
        row.Status.Should().Be(ResourceMoveRecordStatus.NeedsRecovery); notified.Should().Be(0);
        Guard.IsResourceLocked(resource.Id).Should().BeTrue();
        var interrupted = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(row.ExecutionPlanJson!)!;
        interrupted.Sources.Single(s => s.Source == ResourceSource.DLsite).Applied.Should().BeTrue();
        interrupted.Sources.Single(s => s.Source == ResourceSource.ExHentai).Applied.Should().BeFalse();
        (await Db.ExHentaiGalleries.AsNoTracking().SingleAsync()).LocalPath.Should().Be(row.DestPath);
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().Be(0);
        await Moves.ExecuteBatch(batch.BatchId, Args);
        (await Moves.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        notified.Should().Be(1); Guard.IsResourceLocked(resource.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task RecoveryCannotOverwriteAThirdCoreResourcePath()
    {
        var resource = await Resource("Local");
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        var row = await Db.ResourceMoveRecords.SingleAsync();
        await PhysicallyMove(row);
        var third = Dir("Third");
        await Resources.ChangePath([resource.Id], new() { [resource.Id] = third });
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        (await Resources.Get(resource.Id))!.Path.Should().Be(third);
        (await Moves.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.NeedsRecovery);
        Guard.IsResourceLocked(resource.Id).Should().BeTrue();
        Directory.Exists(row.DestPath).Should().BeTrue();
    }

    [TestMethod]
    public async Task RecoveryWithUnavailableExecutorVersionCannotFallBackToDefault()
    {
        var resource = await Resource("Local");
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        var row = await Db.ResourceMoveRecords.SingleAsync();
        await PhysicallyMove(row);
        var plan = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(row.ExecutionPlanJson!)!;
        plan.ExecutorVersion = 999; row.ExecutionPlanJson = JsonConvert.SerializeObject(plan);
        await Db.SaveChangesAsync();
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        (await Moves.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.NeedsRecovery);
        Guard.IsResourceLocked(resource.Id).Should().BeTrue();
        Directory.Exists(row.DestPath).Should().BeTrue();
    }

    [TestMethod]
    public async Task RecoveryRejectsNewSourceOwnershipBeforeContinuingAStartedJournal()
    {
        var resource = await Resource("Local");
        await File.WriteAllTextAsync(Path.Combine(resource.Path!, "original.txt"), "original");
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        var row = await Db.ResourceMoveRecords.SingleAsync();
        var state = new ResourceMoveExecutionState { Id = row.Id, SourcePath = row.SourcePath, DestPath = row.DestPath };
        await FluentActions.Awaiting(() => ResourceMoveSafeFileSystem.Move(state, async () =>
        {
            row.PhysicalMoveStarted = state.PhysicalMoveStarted; row.MoveJournalJson = state.MoveJournalJson;
            row.Status = ResourceMoveRecordStatus.NeedsRecovery; await Db.SaveChangesAsync();
            throw new IOException("shutdown immediately after ownership checkpoint");
        }, _ => Task.CompletedTask, new PauseToken(), CancellationToken.None, (_, _) => false)).Should().ThrowAsync<IOException>();
        await Link(resource, ResourceSource.Steam, "7777");
        (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
        File.ReadAllText(Path.Combine(resource.Path!, "original.txt")).Should().Be("original");
        Directory.Exists(row.DestPath).Should().BeFalse();
        Guard.IsResourceLocked(resource.Id).Should().BeTrue();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LegacyExternalMove_RequiresConfirmedSourceRestorationBeforeUnlocking(bool hasJournal)
    {
        var resource = await Resource("Local"); await AddPlatforms(resource);
        await File.WriteAllTextAsync(Path.Combine(resource.Path!, "data.txt"), "original");
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Data!;
        var row = await Db.ResourceMoveRecords.SingleAsync();
        await PhysicallyMove(row);
        row.ExecutionPlanJson = null;
        if (!hasJournal) row.MoveJournalJson = null;
        await Db.SaveChangesAsync();
        if (hasJournal)
        {
            (await Moves.RetryBatch(batch.BatchId)).Code.Should().NotBe(0);
            row.ConflictKind.Should().Be("legacySourcePlanMissing"); row.ErrorCode.Should().Be("legacySourcePlanMissing");
        }
        else
        {
            (await Moves.RetryBatch(batch.BatchId)).Code.Should().Be(0);
            await FluentActions.Awaiting(() => Moves.ExecuteBatch(batch.BatchId, Args)).Should().ThrowAsync<BTaskSuspendedException>();
            row.ConflictKind.Should().Be("legacyRecovery");
        }
        (await Moves.GetBatch(batch.BatchId))!.CanRetry.Should().BeFalse();
        var decision = new ResourceMoveConflictResolution { Action = "restoreSource", Scope = "once", ConflictVersion = row.ConflictVersion };
        (await Moves.ResolveConflict(row.Id, decision)).Code.Should().NotBe(0);
        // The user explicitly restores their complete source. Existing destination/stage data
        // is never silently removed or claimed by this acknowledgement.
        Directory.CreateDirectory(resource.Path!);
        File.Copy(Path.Combine(row.DestPath, "data.txt"), Path.Combine(resource.Path!, "data.txt"));
        await Db.DLsiteWorks.ExecuteUpdateAsync(s => s.SetProperty(w => w.LocalPath, row.DestPath));
        (await Moves.ResolveConflict(row.Id, decision)).Code.Should().NotBe(0);
        await Db.DLsiteWorks.ExecuteUpdateAsync(s => s.SetProperty(w => w.LocalPath, resource.Path));
        (await Moves.ResolveConflict(row.Id, decision)).Code.Should().Be(0);
        row.Status.Should().Be(ResourceMoveRecordStatus.Cancelled);
        row.PhysicalMoveStarted.Should().BeFalse(); row.MoveJournalJson.Should().BeNull(); row.ExecutionPlanJson.Should().BeNull();
        row.PolicyAuditJson.Should().Contain("restoreSource");
        if (hasJournal) row.PolicyAuditJson.Should().Contain("previousJournal");
        Guard.IsResourceLocked(resource.Id).Should().BeFalse();
        File.ReadAllText(Path.Combine(row.DestPath, "data.txt")).Should().Be("original");
        File.ReadAllText(Path.Combine(resource.Path!, "data.txt")).Should().Be("original");
    }

    [TestMethod]
    public async Task ConflictingSourceExecutorRequirements_AreRejectedBeforeReservation()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.RemoveAll<IResourceSourceMoveHandler>();
            services.AddSingleton<IResourceSourceMoveHandler>(new SelectingHandler(ResourceSource.Aigc, "a-files"));
            services.AddSingleton<IResourceSourceMoveHandler>(new SelectingHandler(ResourceSource.Pixiv, "b-files"));
            services.AddSingleton<IResourceMoveExecutor>(new CountingExecutor("a-files"));
            services.AddSingleton<IResourceMoveExecutor>(new CountingExecutor("b-files"));
        });
        var resource = await Resource("Local");
        await Link(resource, ResourceSource.Aigc, "a"); await Link(resource, ResourceSource.Pixiv, "b");
        var preview = (await Moves.Preview([resource.Id], Dir("Dest"))).Data!;
        preview.ExcludedResources.Single().ReasonCode.Should().Be("sourceMoveUnsupported");
        (await Moves.CreateBatch([resource.Id], Dir("Dest"))).Code.Should().NotBe(0);
        Guard.IsResourceLocked(resource.Id).Should().BeFalse();
        (await Db.ResourceMoveRecords.AnyAsync()).Should().BeFalse();
    }

    private sealed class SelectingHandler(ResourceSource source, string executor) : IResourceSourceMoveHandler
    {
        public ResourceSource Source => source;
        public Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceSourceMoveEvaluation { Executor = new(executor) });
        public Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FaultSwitch { public bool Triggered { get; set; } }

    private sealed class FailOnceAfterApplyHandler(IResourceSourceMoveHandler inner, FaultSwitch fault) : IResourceSourceMoveHandler
    {
        public ResourceSource Source => inner.Source;
        public Task<ResourceSourceMoveEvaluation> EvaluateAsync(ResourceSourceMoveContext context, CancellationToken cancellationToken = default) =>
            inner.EvaluateAsync(context, cancellationToken);
        public Task ValidateRecordedStateAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default) =>
            inner.ValidateRecordedStateAsync(step, cancellationToken);
        public async Task ApplyAsync(ResourceSourceMoveStep step, CancellationToken cancellationToken = default)
        {
            await inner.ApplyAsync(step, cancellationToken);
            if (!fault.Triggered) { fault.Triggered = true; throw new IOException("crash after platform update before checkpoint"); }
        }
    }

    private sealed class CountingExecutor(string id) : IResourceMoveExecutor
    {
        public string Id => id;
        public int Version => 1;
        public int Calls { get; private set; }
        public Task ExecuteOrResumeAsync(ResourceMoveExecutionState state, Func<Task> checkpoint, Func<int, Task> progress,
            PauseToken pause, CancellationToken cancellation, Func<string, string, bool> authorized)
        {
            Calls++;
            return ResourceMoveSafeFileSystem.Move(state, checkpoint, progress, pause, cancellation, authorized);
        }
        public void Cleanup(ResourceMoveExecutionState state) => ResourceMoveSafeFileSystem.Cleanup(state);
    }
}
