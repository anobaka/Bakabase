using System.Text.Json.Nodes;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed partial class AcquisitionStorageOverrideTests
{
    private const long Floor = 1024L * 1024 * 1024;

    [TestMethod]
    public async Task GlobalReserveIsLiveAndOneRunCanOptBackIntoTheConfiguredThreshold()
    {
        using var fixture = await Fixture.CreateAsync();
        var normal = fixture.Options.Value.MinimumFreeSpaceBytes;
        Assert.AreEqual(normal, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        fixture.Options.Value.UseEmergencySpaceReserve = true;
        Assert.AreEqual(Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        var preview = await fixture.Service.PreviewAsync(1);
        Assert.IsTrue(preview.UseEmergencySpaceReserve);
        Assert.AreEqual("emergency", preview.EffectiveReserveMode);
        Assert.IsTrue((await fixture.Service.ApplyAsync(1, new(preview.Token, true, "configured"))).ContinuationAccepted);
        Assert.AreEqual(normal, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        Assert.AreEqual(Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "22")));
        Assert.AreEqual("configured", (await fixture.Service.PreviewAsync(1)).EffectiveReserveMode);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.ApplyAsync(1, new(preview.Token, true, "emergency")));
        fixture.Options.Value.UseEmergencySpaceReserve = false;
        Assert.AreEqual(normal, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "22")));
        fixture.Options.Value.MinimumFreeSpaceBytes = 0;
        fixture.Options.Value.UseEmergencySpaceReserve = true;
        Assert.AreEqual(0L, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "22")));
        Assert.AreEqual(0L, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "999")));
    }

    [TestMethod]
    public async Task GrantIsIsolatedHonorsExplicitMinimumAndInvalidatesNormalPolicyCache()
    {
        using var fixture = await Fixture.CreateAsync();
        var provider = fixture.Provider;
        Assert.AreEqual(100 * Floor, await provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        var preview = await fixture.Service.PreviewAsync(1);
        Assert.AreEqual(Floor, preview.OverrideMinimumFreeBytes);
        var result = await fixture.Service.ApplyAsync(1, new(preview.Token, true));
        Assert.IsTrue(result.ContinuationAccepted);
        Assert.AreEqual(Floor, await provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        Assert.AreEqual(100 * Floor, await provider.GetMinimumFreeBytesAsync(new("workflow", "22")));
        Assert.AreEqual(5 * Floor, await provider.GetMinimumFreeBytesAsync(new("workflow", "11", 5 * Floor)));
        var saved = await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        Assert.AreEqual("preserve", JsonNode.Parse(saved.BuilderStateJson!)!["unknownFutureKey"]!.GetValue<string>());
        Assert.AreEqual(1, fixture.Host.Calls);
        Assert.AreEqual(result, await fixture.Service.ApplyAsync(1, new(preview.Token, true)));
        Assert.AreEqual(1, fixture.Host.Calls);
    }

    [TestMethod]
    public async Task SharedOperationRequiresEveryOwnerAndUnknownOwnersKeepGlobalReserve()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.GrantAsync(1);
        fixture.Db.Set<ArtifactOperationBindingDbModel>().AddRange(
            new() {Id = "a", OperationId = "shared", OwnerKind = "workflow", OwnerId = "11"},
            new() {Id = "b", OperationId = "shared", OwnerKind = "workflow", OwnerId = "22"});
        await fixture.Db.SaveChangesAsync();
        var context = new StorageWriteContext("artifactOperation", "shared");
        Assert.AreEqual(100 * Floor, await fixture.Provider.GetMinimumFreeBytesAsync(context));
        await fixture.GrantAsync(2);
        Assert.AreEqual(Floor, await fixture.Provider.GetMinimumFreeBytesAsync(context));
        // A new binding is observed immediately after a granted read, with no cached permission.
        fixture.Db.Set<ArtifactConsumerDbModel>().Add(new() {Id = "unknown", OperationId = "shared", OwnerKind = "external", OwnerId = "x"});
        await fixture.Db.SaveChangesAsync();
        Assert.AreEqual(100 * Floor, await fixture.Provider.GetMinimumFreeBytesAsync(context));
    }

    [TestMethod]
    public async Task ExpiredChangedDeletedAndFinishedOwnersCannotUseGrant()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.GrantAsync(1);
        var task = await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var grant = AcquisitionStorageOverrideGrant.Read(task)!;
        var run = await fixture.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 11);
        var now = DateTimeOffset.UtcNow;
        Assert.IsTrue(grant.IsValid(task, run, now));
        Assert.IsFalse(grant.IsValid(task with {PlanRevision = 2}, run, now));
        Assert.IsFalse(grant.IsValid(task with {WorkflowRunId = 22}, run, now));
        Assert.IsFalse(grant.IsValid(task with {DeletedAt = DateTime.UtcNow}, run, now));
        Assert.IsFalse(grant.IsValid(task, run with {Status = WorkflowRunStatus.Success}, now));
        Assert.IsFalse(grant.IsValid(task, run with {Status = WorkflowRunStatus.Cancelled}, now));
        Assert.IsFalse(grant.IsValid(task, run, grant.ExpiresAt));
        fixture.Db.Set<WorkflowNodeExecutionDbModel>().Add(new() {WorkflowRunId = 11, Status = "failed", CompletedAt = DateTime.UtcNow});
        await fixture.Db.SaveChangesAsync();
        Assert.AreEqual(100 * Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")),
            "Requeueing a failed run must not revive a grant from its previous attempt.");
    }

    [TestMethod]
    public async Task PreviewIsReadOnlyBoundToSelectionAndStaleVersionIsRejected()
    {
        using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(1);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Service.ApplyAsync(2, new(preview.Token, true)));
        await fixture.Db.AcquisitionTasks.Where(t => t.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, 2));
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Service.ApplyAsync(1, new(preview.Token, true)));
        Assert.AreEqual("ACQUISITION_TASK_CHANGED", error.Message);
        Assert.AreEqual(0, fixture.Host.Calls);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
    }

    [TestMethod]
    public async Task BlockedRecoveryRevokesGrantAndIndeterminateContinuationNeverEnqueuesTwice()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Diagnostics.Ready = false;
        var preview = await fixture.Service.PreviewAsync(1);
        var result = await fixture.Service.ApplyAsync(1, new(preview.Token, true));
        Assert.AreEqual("revoked", result.Status);
        Assert.IsFalse(result.ContinuationAccepted);
        Assert.AreEqual(100 * Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
        fixture.Diagnostics.Ready = true;
        fixture.Host.ThrowAfterContinuation = true;
        preview = await fixture.Service.PreviewAsync(1);
        result = await fixture.Service.ApplyAsync(1, new(preview.Token, true));
        Assert.AreEqual("STORAGE_OVERRIDE_OUTCOME_UNKNOWN", result.ReasonCode);
        Assert.AreEqual(result, await fixture.Service.ApplyAsync(1, new(preview.Token, true)));
        Assert.AreEqual(1, fixture.Host.Calls);
        Assert.AreEqual(Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    [TestMethod]
    public async Task PreviewExpiringWhileWaitingForTaskGateCannotGrantPermission()
    {
        using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(1);
        var task = await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1);
        var run = await fixture.Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 11);
        fixture.Previews.Add(new(preview with {ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(100)},
            AcquisitionRecoveryService.Version(task, run)));
        var gate = Bakabase.Modules.Acquisition.Components.AcquisitionTaskGate.For(1);
        await gate.WaitAsync();
        Task<AcquisitionStorageOverrideResult> pending;
        try
        {
            pending = fixture.Service.ApplyAsync(1, new(preview.Token, true));
            await Task.Delay(250);
        }
        finally { gate.Release(); }
        var error = await Assert.ThrowsExceptionAsync<ArgumentException>(() => pending);
        Assert.AreEqual("STORAGE_OVERRIDE_PREVIEW_EXPIRED", error.Message);
        Assert.AreEqual(0, fixture.Host.Calls);
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
    }

    [TestMethod]
    public async Task OverrideRequiresTypedReserveFailureConfirmationAndUsefulReserveReduction()
    {
        using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(1);
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Service.ApplyAsync(1, new(preview.Token, false)));
        fixture.Options.Value.MinimumFreeSpaceBytes = Floor;
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => fixture.Service.PreviewAsync(1));
        Assert.IsFalse(AcquisitionStorageOverrideService.HasReserveLowReason(new() {WaitPromptJson = "{\"message\":\"text mentioning LOCAL_STORAGE_RESERVE_LOW\"}"}));
        Assert.IsTrue(AcquisitionStorageOverrideService.HasReserveLowReason(new() {WaitPromptJson = "{\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}"}));
        Assert.IsFalse(AcquisitionStorageOverrideService.HasReserveLowReason(new() {WaitPromptJson = "{\"message\":\"LOCAL_STORAGE_FULL\"}"}));
    }

    [TestMethod]
    public async Task DownloadCheckpointUsesCurrentTypedChildFailureAndRechecksItAtApply()
    {
        using var fixture = await Fixture.CreateAsync();
        var target = new CloudDownloadTarget {Id = "part", SelectedSourceId = "source", DownloadTaskId = 44,
            Sources = [new() {Id = "source", Url = "https://example.org/file", ProviderId = "http"}]};
        var checkpoint = JsonSerializer.Serialize(new {item = new DownloadWorkset {Targets = [target]}}, WorkflowJson.Options);
        var plan = WorkflowRunPlans.Serialize(new() {Name = "Download", TriggerKind = "manual",
            Nodes = [new() {NodeId = "download", Kind = "download.awaitCompletion"}]});
        await fixture.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.WaitPromptJson, (string?) null).SetProperty(r => r.CurrentStepIndex, 0)
            .SetProperty(r => r.CurrentItemJson, checkpoint).SetProperty(r => r.PlanSnapshotJson, plan));
        var reader = new RuntimeReader();
        var overview = new AcquisitionRuntimeOverviewService(fixture.Db, reader,
            new SourceAvailabilityService(new SourceAvailabilityStore(Path.Combine(Path.GetTempPath(), "override-readonly-" + Guid.NewGuid().ToString("N"))), []));
        var service = new AcquisitionStorageOverrideService(fixture.Db, fixture.Options, new(),
            new(fixture.Db, fixture.Diagnostics, new(), fixture.Host), runtime: overview);
        var preview = await service.PreviewAsync(1);
        reader.Code = "LOCAL_STORAGE_FULL";
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.ApplyAsync(1, new(preview.Token, true)));
        Assert.IsNull(AcquisitionStorageOverrideGrant.Read(await fixture.Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == 1)));
        reader.Code = StorageWriteGuard.ReserveLowCode;
        preview = await service.PreviewAsync(1);
        Assert.IsTrue((await service.ApplyAsync(1, new(preview.Token, true))).ContinuationAccepted);
        Assert.AreEqual(1, fixture.Host.Calls);
    }

    [TestMethod]
    public async Task SinglePublicationAllowsSlowFullVerificationAndConfirmsWithOneHashBeforeResume()
    {
        using var fixture = await Fixture.CreateAsync();
        const string kind = "fs.publishArtifacts";
        var plan = WorkflowRunPlans.Serialize(new() {Name = "Local publication", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "publish", Kind = kind, ConfigJson = "{}"}]});
        await fixture.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(update => update
            .SetProperty(r => r.CurrentStepIndex, 0).SetProperty(r => r.PlanSnapshotJson, plan));
        fixture.Diagnostics.LocalStep = kind;
        fixture.Diagnostics.HashDelay = TimeSpan.FromMilliseconds(4200);
        var recovery = new AcquisitionRecoveryService(fixture.Db, fixture.Diagnostics, new(), fixture.Host);
        var preview = await recovery.PreviewAsync(new([1], "workflow"));
        Assert.IsTrue(preview.Rows.Single().CanContinue, preview.Rows.Single().ReasonCode);
        Assert.AreEqual(1, fixture.Diagnostics.HashCount);
        Assert.AreEqual(0, fixture.Host.Calls);
        fixture.Diagnostics.HashDelay = TimeSpan.Zero;
        await fixture.GrantAsync(1);
        Assert.AreEqual(2, fixture.Diagnostics.HashCount);
        Assert.AreEqual(1, fixture.Host.Calls);
    }

    [TestMethod]
    public async Task CancelledPublicationHashRevokesOnlyItsGrantBeforeAnySignal()
    {
        using var fixture = await Fixture.CreateAsync();
        const string kind = "fs.publishArtifacts";
        var plan = WorkflowRunPlans.Serialize(new() {Name = "Local publication", TriggerKind = CloudWorkflow.Trigger,
            Nodes = [new() {NodeId = "publish", Kind = kind, ConfigJson = "{}"}]});
        await fixture.Db.WorkflowRuns.Where(r => r.Id == 11).ExecuteUpdateAsync(update => update
            .SetProperty(r => r.CurrentStepIndex, 0).SetProperty(r => r.PlanSnapshotJson, plan));
        fixture.Diagnostics.LocalStep = kind;
        fixture.Diagnostics.HashDelay = TimeSpan.FromMinutes(1);
        using var stop = new CancellationTokenSource();
        var preview = await fixture.Service.PreviewAsync(1);
        var result = fixture.Service.ApplyAsync(1, new(preview.Token, true), stop.Token);
        await fixture.Diagnostics.HashStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        Assert.AreEqual("revoked", (await result).Status);
        Assert.AreEqual(0, fixture.Host.Calls);
        Assert.AreEqual(100 * Floor, await fixture.Provider.GetMinimumFreeBytesAsync(new("workflow", "11")));
    }

    private sealed class RuntimeReader : IAcquisitionRuntimeDownloadReader
    {
        internal string Code = StorageWriteGuard.ReserveLowCode;
        public Task<IReadOnlyList<AcquisitionRuntimeDownload>> ReadAsync(IReadOnlyCollection<int> siteIds,
            IReadOnlyCollection<string> transportIds, IReadOnlyCollection<int> runIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AcquisitionRuntimeDownload>>([
                new("site:44", null, 11, "failed", null, null, null, DateTimeOffset.UtcNow, Code)]);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        internal BakabaseDbContext Db { get; }
        internal PolicyOptions Options { get; } = new();
        internal Diagnostics Diagnostics { get; } = new();
        internal Host Host { get; } = new();
        internal AcquisitionStorageWritePolicyProvider Provider { get; }
        internal AcquisitionStorageOverridePreviewStore Previews { get; } = new();
        internal AcquisitionStorageOverrideService Service { get; }
        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            var dbOptions = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite(connection).Options;
            Db = new(dbOptions);
            _services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(dbOptions)).BuildServiceProvider();
            Provider = new(_services.GetRequiredService<IServiceScopeFactory>(), Options);
            Service = new(Db, Options, Previews, new(Db, Diagnostics, new(), Host), resumer: Host,
                continuations: new(Db, new(Db, Diagnostics, new(), Host), new(), null!, null!, null!));
        }
        internal static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            for (var id = 1; id <= 2; id++)
            {
                fixture.Db.WorkflowRuns.Add(new() {Id = id * 11, Status = WorkflowRunStatus.Waiting,
                    WaitPromptJson = "{\"message\":\"LOCAL_STORAGE_RESERVE_LOW\"}",
                    Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = id.ToString(), Revision = "1"}]});
                fixture.Db.AcquisitionTasks.Add(new() {Id = id, WorkflowRunId = id * 11, PlanRevision = 1,
                    Status = AcquisitionStatus.Waiting, BuilderStateJson = "{\"unknownFutureKey\":\"preserve\"}"});
            }
            await fixture.Db.SaveChangesAsync();
            fixture.Db.ChangeTracker.Clear();
            return fixture;
        }
        internal async Task GrantAsync(int id)
        {
            var preview = await Service.PreviewAsync(id);
            Assert.IsTrue((await Service.ApplyAsync(id, new(preview.Token, true))).ContinuationAccepted);
        }
        public void Dispose() { Db.Dispose(); _services.Dispose(); _connection.Dispose(); }
    }
    private sealed class PolicyOptions : IBOptions<AcquisitionOptions>
    { public AcquisitionOptions Value { get; } = new() {MinimumFreeSpaceBytes = 100 * Floor}; }
    private sealed class Diagnostics : IAcquisitionRecoveryDiagnostics
    {
        internal bool Ready = true;
        internal string? LocalStep;
        internal TimeSpan HashDelay;
        internal int HashCount;
        internal TaskCompletionSource HashStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AcquisitionRecoveryDiagnosis> DiagnoseAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
            CancellationToken ct, bool verifyFiles = true, bool verifyContent = false, bool includeStoppedDownloads = false)
        {
            if (LocalStep != null)
            {
                if (!verifyFiles) return new(new(task.Id, task.Title, run?.Id, false, null, "RECOVERY_VERIFY_FILES", CurrentStepKind: LocalStep));
                HashCount++; HashStarted.TrySetResult();
                await Task.Delay(HashDelay, ct);
            }
            return new(new(task.Id, task.Title, run?.Id, Ready, Ready ? "resume" : null,
                Ready ? "RECOVERY_FILES_VERIFIED" : "RECOVERY_FILES_UNVERIFIED", CurrentStepKind: LocalStep));
        }
    }
    private sealed class Host : IWorkflowRunResumer
    {
        internal int Calls;
        internal string? LastSignal;
        internal Func<Task>? OnAccepted;
        internal bool ThrowAfterContinuation;
        public Task ResumeAsync(int id, string signal, CancellationToken ct = default) => throw new AssertFailedException();
        public Task RequeueAsync(int id, CancellationToken ct = default) => throw new AssertFailedException();
        public Task UpdateWaitAsync(int id, string reason, string? prompt, CancellationToken ct = default) => throw new AssertFailedException();
        public async Task ContinueConditionallyAsync(int id, bool waiting, string signal, Func<CancellationToken, Task> validate,
            CancellationToken ct = default)
        {
            var gate = WorkflowRunMutationGate.For(id); await gate.WaitAsync(ct);
            try { await validate(ct); LastSignal = signal; Calls++; if (OnAccepted != null) await OnAccepted(); if (ThrowAfterContinuation) throw new InvalidOperationException("Unknown enqueue result"); }
            finally { gate.Release(); }
        }
    }
}
