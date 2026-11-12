using Bakabase.Abstractions.Components.FileSystem;
using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingStorageGuardTests
{
    private string root = null!;
    private string input = null!;
    private string state = null!;
    private static StorageWriteContext Owner => new("workflow", "81");
    [TestInitialize] public void Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "processing-storage-guard-" + Guid.NewGuid().ToString("N"));
        input = Path.Combine(root, "input"); state = Path.Combine(root, "state"); Directory.CreateDirectory(input);
    }
    [TestCleanup] public void Cleanup() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<string> Input(string name, string text)
    { var path = Path.Combine(input, name); await File.WriteAllTextAsync(path, text); return path; }
    private static FileProcessingPlan Extract => new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};
    private static StorageWriteGuard Guard(Func<long> free) => new(new Policy(), _ => ("test-volume", free()), TimeSpan.FromMilliseconds(10));

    [TestMethod]
    public async Task Complete_input_snapshot_budget_is_checked_before_hashing_or_copying_any_member()
    {
        var first = await Input("a.zip", "1234"); var second = await Input("b.zip", "5678");
        var archives = new Archives();
        // Each four-byte file would fit on its own; the atomic snapshot needs all eight.
        var executor = new FileProcessingPlanExecutor(archives, writeGuard: Guard(() => 107));
        using var scope = StorageWriteScope.Enter(Owner);
        await using var locked = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.None);
        var waiting = await executor.ExecuteAsync(Extract, input, [first, second], state);
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("StorageUnavailable", waiting.FailureCode);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, waiting.Message);
        Assert.AreEqual(0, archives.Count); Assert.IsFalse(Directory.Exists(state));
        Assert.AreEqual("5678", await File.ReadAllTextAsync(second));
    }

    [TestMethod]
    public async Task Reusing_a_complete_input_snapshot_does_not_charge_its_payload_again()
    {
        var original = await Input("private.zip", new string('x', 64)); long free = 200;
        var archives = new PasswordOnlyArchives(); var guard = Guard(() => Volatile.Read(ref free));
        var executor = new FileProcessingPlanExecutor(archives, new FileArtifactMaterializer((_, _) => false, guard), guard);
        using var scope = StorageWriteScope.Enter(Owner);
        var first = await executor.ExecuteAsync(Extract, input, [original], state);
        Assert.IsTrue(first.NeedsPassword);
        var snapshot = Directory.GetFiles(state, "private.zip", SearchOption.AllDirectories).Single();
        var written = File.GetLastWriteTimeUtc(snapshot);
        Interlocked.Exchange(ref free, 101);
        var replay = await executor.ExecuteAsync(Extract, input, [original], state);
        Assert.IsTrue(replay.NeedsPassword, replay.Message);
        Assert.AreEqual(2, archives.Probes);
        Assert.AreEqual(written, File.GetLastWriteTimeUtc(snapshot));
        Assert.AreEqual(64, new FileInfo(snapshot).Length);
    }

    [TestMethod]
    public async Task Unknown_plan_waits_before_capacity_checks_or_input_snapshot_work()
    {
        var original = Path.Combine(input, "not-read.bin");
        var executor = new FileProcessingPlanExecutor(new Archives(),
            writeGuard: Guard(() => throw new AssertFailedException("Unknown instructions must not start storage work.")));
        using var scope = StorageWriteScope.Enter(Owner);
        var result = await executor.ExecuteAsync(new() {Requirement = "unknown"}, input, [original], state);
        Assert.IsFalse(result.Completed); Assert.IsFalse(Directory.Exists(state));
        Assert.IsNull(result.FailureCode);
    }

    [TestMethod]
    public async Task A_failed_clone_checks_the_whole_copy_before_writing_payload()
    {
        var original = await Input("payload.bin", new string('x', 16)); var target = Path.Combine(input, "copy.bin");
        var materializer = new FileArtifactMaterializer((_, _) => false, Guard(() => 110));
        using var scope = StorageWriteScope.Enter(Owner);
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => materializer.MaterializeAsync(original, target, "auto", default));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        Assert.IsTrue(!File.Exists(target) || new FileInfo(target).Length == 0);
        Assert.AreEqual(16, new FileInfo(original).Length);
    }

    [TestMethod]
    public async Task Low_reserve_blocks_new_processing_copies_and_independent_calls_keep_their_policy()
    {
        var original = await Input("game.zip", "plain"); var archives = new Archives();
        var executor = new FileProcessingPlanExecutor(archives, writeGuard: Guard(() => 99));
        using (StorageWriteScope.Enter(Owner))
        {
            var waiting = await executor.ExecuteAsync(Extract, input, [original], state);
            Assert.IsFalse(waiting.Completed); Assert.AreEqual("StorageUnavailable", waiting.FailureCode);
            Assert.AreEqual(StorageWriteGuard.ReserveLowCode, waiting.Message); Assert.AreEqual(0, archives.Count);
            Assert.AreEqual("plain", await File.ReadAllTextAsync(original));
        }
        var completed = await executor.ExecuteAsync(Extract, input, [original], state);
        Assert.IsTrue(completed.Completed); Assert.AreEqual(1, archives.Count);
    }

    [TestMethod]
    public async Task Running_extraction_stops_on_reserve_and_resumes_without_repeating_completed_archive_groups()
    {
        var first = await Input("a.zip", "plain"); var second = await Input("b.zip", "wait");
        long free = 1000; var archives = new Archives {Wait = true};
        var executor = new FileProcessingPlanExecutor(archives, writeGuard: Guard(() => Volatile.Read(ref free)));
        using var scope = StorageWriteScope.Enter(Owner);
        var running = executor.ExecuteAsync(Extract, input, [first, second], state);
        await archives.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Interlocked.Exchange(ref free, 99);
        var waiting = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("StorageUnavailable", waiting.FailureCode);
        Assert.IsTrue(archives.Stopped, "The writer must stop before the workflow is reported as waiting.");
        Assert.AreEqual(1, archives.PlainCalls); Assert.IsTrue(File.Exists(first)); Assert.IsTrue(File.Exists(second));
        Interlocked.Exchange(ref free, 1000); archives.Wait = false;
        var completed = await executor.ExecuteAsync(Extract, input, [first, second], state);
        Assert.IsTrue(completed.Completed); Assert.AreEqual(1, archives.PlainCalls);
        Assert.AreEqual(3, archives.Count, "Only the interrupted archive may be retried.");
    }

    [TestMethod]
    public async Task Nested_extraction_inherits_reserve_and_resumes_its_existing_parent()
    {
        var original = await Input("outer.zip", "outer"); long free = 1000;
        var archives = new Archives {Wait = true};
        var executor = new RecursiveArchiveExecutor(archives, writeGuard: Guard(() => Volatile.Read(ref free)));
        using var scope = StorageWriteScope.Enter(Owner);
        var running = executor.ExecuteAsync(input, [original], state, new() {MinimumFreeBytes = 0});
        await archives.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Interlocked.Exchange(ref free, 99);
        var waiting = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(waiting.Completed); Assert.AreEqual("StorageUnavailable", waiting.WaitReason);
        Assert.IsTrue(archives.Stopped); Assert.AreEqual(1, archives.OuterCalls);
        Interlocked.Exchange(ref free, 1000); archives.Wait = false;
        var completed = await executor.ExecuteAsync(input, [original], state, new() {MinimumFreeBytes = 0});
        Assert.IsTrue(completed.Completed); Assert.AreEqual(1, archives.OuterCalls); Assert.IsTrue(File.Exists(original));
    }

    [TestMethod]
    public async Task Real_copy_observes_reserve_between_chunks_but_same_volume_move_adds_no_payload()
    {
        var original = Path.Combine(input, "large.bin"); await File.WriteAllBytesAsync(original, new byte[3 * 1024 * 1024]);
        var target = Path.Combine(input, "copy.bin");
        var guarded = new FileArtifactMaterializer((_, _) => false,
            Guard(() => File.Exists(target) && new FileInfo(target).Length >= 1024 * 1024 ? 99 : 4 * 1024 * 1024));
        using var scope = StorageWriteScope.Enter(Owner);
        var error = await Assert.ThrowsExactlyAsync<LocalStorageException>(() => guarded.MaterializeAsync(original, target, "copy", default));
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, error.Code);
        Assert.AreEqual(3 * 1024 * 1024, new FileInfo(original).Length);
        Assert.IsTrue(File.Exists(target) && new FileInfo(target).Length > 0 && new FileInfo(target).Length < new FileInfo(original).Length);
        var moved = Path.Combine(input, "renamed.bin");
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Assert.IsTrue(await guarded.TryMoveAsync(original, moved, default));
            Assert.IsFalse(File.Exists(original)); Assert.AreEqual(3 * 1024 * 1024, new FileInfo(moved).Length);
        }
    }

    [TestMethod]
    public async Task Delivery_stops_at_reserve_keeps_partial_files_and_reuses_the_same_generation()
    {
        var appData = Path.Combine(root, "appdata"); var delivery = Path.Combine(root, "delivery"); Directory.CreateDirectory(appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "test.db")).Options;
        await using var db = new BakabaseDbContext(options); await db.Database.EnsureCreatedAsync();
        db.Add(new WorkflowRunDbModel {Id = 81, WorkflowDefinitionId = 1, Status = WorkflowRunStatus.Running});
        await db.SaveChangesAsync();
        var store = new ArtifactOperationStore(db, () => appData);
        await using var services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => appData)).BuildServiceProvider();
        var original = await Input("game.bin", "payload");
        var artifact = await store.AdoptAsync(new("source", "source", "files.local", "input"), new(input, [original]), "fixture");
        long free = 99; var guard = Guard(() => Volatile.Read(ref free));
        var publisher = new FileArtifactPublisher(store, new AllowStorage(), () => appData,
            services.GetRequiredService<IServiceScopeFactory>(), writeGuard: guard);
        await using var activityServices = new ServiceCollection().AddSingleton<IArtifactOperationStore>(store)
            .AddSingleton(publisher).BuildServiceProvider();
        var workset = new DownloadWorkset {OutputDirectory = input, Targets = [new()
            {Id = "part", ProcessedArtifactId = artifact.Id, ProcessingIntentKey = "intent"}]};
        var activity = new PublishFileArtifactsActivity();
        var context = new WorkflowExecutionContext {RunId = 81, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = JsonSerializer.Serialize(new FileArtifactPublicationConfig
                {TargetDirectory = delivery}, WorkflowJson.Options), Services = activityServices, Logger = NullLogger.Instance};
        var waiting = await activity.ProcessItemAsync(context, workset, default);
        Assert.IsNotNull(waiting.Suspension);
        Assert.AreEqual("artifacts.publication", waiting.Suspension.Reason);
        using var prompt = JsonDocument.Parse(waiting.Suspension.PromptJson!);
        Assert.AreEqual("StorageUnavailable", prompt.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, prompt.RootElement.GetProperty("message").GetString());
        var retained = (DownloadWorkset)waiting.Suspension.Item;
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, retained.Targets.Single().Problem);
        Assert.AreEqual(StorageWriteGuard.ReserveLowCode, retained.GetWorkflowOutputMetadata(activity.Kind)
            .Single(b => b.Key == "part:problem").Code);
        Assert.IsTrue(File.Exists(original)); Assert.IsFalse(File.Exists(Path.Combine(delivery, "game.bin")));
        var operation = await db.Set<ArtifactOperationDbModel>().AsNoTracking().SingleAsync(o => o.Kind == "files.publish");
        Assert.AreEqual(ArtifactOperationState.Running, operation.State); Assert.AreEqual(1, operation.Attempt);
        Interlocked.Exchange(ref free, 1000);
        var done = await activity.ResumeAsync(context, retained, "{\"targetId\":\"part\"}", default);
        Assert.IsNull(done.Suspension);
        var delivered = ((DownloadWorkset)done.Replacement!).Targets.Single();
        Assert.AreEqual(operation.Id, delivered.PublicationOperationId);
        Interlocked.Exchange(ref free, 99);
        var reused = await publisher.PublishAsync("intent", artifact, delivery, "another", workflowRunId: 81);
        Assert.AreEqual(delivered.PublishedArtifactId, reused.Artifact?.Id, "Readonly reuse remains available below the reserve.");
        Assert.IsTrue(File.Exists(original));
    }

    private sealed class Policy : IStorageWritePolicyProvider
    { public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) => Task.FromResult(context == null ? 0L : 100L); }
    private sealed class PasswordOnlyArchives : IArchiveExtractionService
    {
        public int Probes;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct)
        { Probes++; return Task.FromResult(new ArchivePasswordProbe(false, null, [], "", "Password required")); }
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct) =>
            throw new AssertFailedException("No password was supplied.");
    }
    private sealed class AllowStorage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
    private sealed class Archives : IArchiveExtractionService
    {
        public int Count; public int PlainCalls; public int OuterCalls; public bool Wait; public bool Stopped;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates, Action<string>? output, CancellationToken ct)
            => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Count++; var kind = await File.ReadAllTextAsync(request.Files[0], ct);
            if (kind == "plain") PlainCalls++; if (kind == "outer") OuterCalls++;
            Directory.CreateDirectory(request.Directory);
            if (kind == "outer") await File.WriteAllTextAsync(Path.Combine(request.Directory, "inner.zip"), "wait", ct);
            else await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.bin"), "payload", ct);
            if (kind == "wait" && Wait)
            {
                Started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                finally { Stopped = true; }
            }
            return new(true, request.Directory, "");
        }
    }
}
