using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Controllers;
using Bakabase.Service.Components.Workflow.Fs;
using Bakabase.Service.Components.Workflow.Text;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// A chain that stops halfway to wait for something only the outside world can supply — the file
/// a person still has to fetch, the password, the choice between two links. The wait has to
/// survive a process restart, so what is under test is mostly what ends up on the run row.
/// </summary>
[TestClass]
public sealed class WorkflowSuspensionTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    /// <summary>
    /// Suspends the first time it sees an item and renames it to whatever the signal says. Records
    /// every call so a test can tell "ran again from the top" from "picked up at the cursor".
    /// </summary>
    private sealed class WaitForWordActivity : IWorkflowActivity, IResumableWorkflowActivity
    {
        public const string ActivityKind = "action.test.waitForWord";
        public static readonly List<string> Calls = [];
        public static readonly List<string?> ResumePrompts = [];

        public string Kind => ActivityKind;
        public string DisplayName => "Wait for a word (test)";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";

        public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
            CancellationToken ct)
        {
            Calls.Add($"process:{((FsEntryItem) item).WorkingName}");
            await ctx.ReportOutputMetadata([new() {Kind = "scalar", Key = "state", Value = "awaiting"}]);
            return WorkflowItemOutcome.Suspend(
                new WorkflowSuspension("test.waitingForWord", """{"ask":"word"}""", item));
        }

        public async Task<WorkflowItemOutcome> ResumeAsync(WorkflowExecutionContext ctx, object item, string signalJson,
            CancellationToken ct)
        {
            var entry = (FsEntryItem) item;
            ResumePrompts.Add(ctx.ResumePromptJson);
            Calls.Add($"resume:{entry.WorkingName}");
            var word = JsonDocument.Parse(signalJson).RootElement.GetProperty("word").GetString()!;
            await ctx.ReportOutputMetadata([new() {Kind = "scalar", Key = "state", Value = "answered"}]);
            return WorkflowItemOutcome.ReplaceWith(entry with {WorkingName = word});
        }
    }

    private sealed class MutableMetadataItem : IWorkflowOutputMetadata
    {
        public string Value { get; set; } = "before";
        public IReadOnlyList<WorkflowOutputBlock> GetWorkflowOutputMetadata(string activityKind) =>
            [new() {Kind = "scalar", Key = "state", Value = Value}];
    }

    private sealed class MetadataMutationActivity : IWorkflowActivity
    {
        public string Kind => "action.test.metadataMutation";
        public string DisplayName => "Metadata mutation";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";
        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
        {
            Assert.IsNull(ctx.ResumePromptJson, "A downstream activity cannot use the resumed step's authority.");
            if (item is not MutableMetadataItem projected)
                return Task.FromResult(WorkflowItemOutcome.ReplaceWith(new MutableMetadataItem()));
            projected.Value = "after";
            return Task.FromResult(WorkflowItemOutcome.ReplaceWith(projected));
        }
    }

    /// <summary>Suspends like the one above, but has no way of being answered.</summary>
    private sealed class NonResumableWaitActivity : IWorkflowActivity
    {
        public const string ActivityKind = "action.test.waitForever";

        public string Kind => ActivityKind;
        public string DisplayName => "Wait forever (test)";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";

        public Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
            CancellationToken ct) =>
            Task.FromResult(WorkflowItemOutcome.Suspend(
                new WorkflowSuspension("test.waitingForever", null, item)));
    }

    private sealed class ReportingFailureActivity : IWorkflowActivity
    {
        public string Kind => "action.test.reportingFailure";
        public string DisplayName => "Reporting failure";
        public WorkflowActivityCategory Category => WorkflowActivityCategory.Action;
        public string Group => "test";
        public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item, CancellationToken ct)
        {
            await ctx.ReportOutputMetadata([new() {Kind = "task", Key = "partial", TaskKind = "download", TaskId = "site:42"}]);
            if (ctx.GetConfig<JsonElement>().TryGetProperty("cancel", out var cancel) && cancel.GetBoolean())
                throw new OperationCanceledException();
            if (ctx.GetConfig<JsonElement>().TryGetProperty("storage", out var storage))
                throw storage.GetString() switch
                {
                    "typed" => new LocalStorageException("LOCAL_STORAGE_RESERVE_LOW"),
                    "os" => new IOException("SECRET_PATH", unchecked((int)0x80070070)),
                    _ => new IOException("Network failed; the text LOCAL_STORAGE_FULL is not an OS capacity code.")
                };
            throw new InvalidOperationException("password=secret-canary");
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        WaitForWordActivity.Calls.Clear();
        WaitForWordActivity.ResumePrompts.Clear();
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
        {
            services.AddSingleton<IWorkflowActivity, WaitForWordActivity>();
            services.AddSingleton<IWorkflowActivity, NonResumableWaitActivity>();
            services.AddSingleton<IWorkflowActivity, ReportingFailureActivity>();
            services.AddSingleton<IWorkflowActivity, MetadataMutationActivity>();
        });
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseSuspend_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }

    private static BTaskArgs BuildArgs(IServiceProvider sp) => new(
        new PauseToken(), CancellationToken.None, new BTask("test", () => "test"),
        _ => Task.CompletedTask, sp);

    private string ScanFilterJson(FsScanTarget target = FsScanTarget.Files) =>
        JsonSerializer.Serialize(new {roots = new[] {_root}, target, depth = 1}, Json);

    private static WorkflowActivityInputModel Activity(string kind, object? config = null) => new()
    {
        Kind = kind,
        ConfigJson = config == null ? "{}" : JsonSerializer.Serialize(config, Json),
        OnItemError = WorkflowActivityErrorBehavior.Fail,
    };

    private async Task<int> StartRun(string filterJson, params WorkflowActivityInputModel[] activities)
    {
        var defId = (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"t-{Guid.NewGuid():N}",
                TriggerKind = FsWorkflowKinds.TriggerManualScan,
                TriggerFilterJson = filterJson,
                Enabled = false,
                Activities = activities.ToList()
            })).Id;

        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var payload = trigger.BuildManualPayload(filterJson, null);

        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = defId,
            Status = WorkflowRunStatus.Pending,
            StartedAt = DateTime.Now,
            PayloadJson = JsonSerializer.Serialize(payload, Json)
        };
        db.Set<WorkflowRunDbModel>().Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    /// <summary>Drives the runner the way the background task would.</summary>
    private async Task Execute(int runId)
    {
        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        }

        await runner.ExecuteAsync(runId, BuildArgs(_sp));
    }

    private async Task<WorkflowRunDbModel> Run(int runId)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().AsNoTracking().Include(r => r.NodeExecutions).FirstAsync(r => r.Id == runId);
    }

    private async Task Resume(int runId, string signalJson)
    {
        await using var scope = _sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IWorkflowRunResumer>().ResumeAsync(runId, signalJson);
    }

    /// <summary>
    /// The whole cycle on a single-item run: suspend at the second step, park with a cursor, and
    /// come back into that same step's <c>ResumeAsync</c> rather than starting over.
    /// </summary>
    [TestMethod]
    public async Task ResumeAuthorityIsConsumedBeforeTheDownstreamActivity()
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity(WaitForWordActivity.ActivityKind),
            Activity("action.test.metadataMutation"));
        await Execute(id);
        await Resume(id, """{"word":"continued.txt"}""");
        await Execute(id);
        var run = await Run(id);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        Assert.AreEqual("""{"ask":"word"}""", WaitForWordActivity.ResumePrompts.Single());
    }

    [TestMethod]
    public async Task Expired_archive_group_answer_cannot_consume_another_nodes_wait()
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity(WaitForWordActivity.ActivityKind));
        await Execute(id);
        var before = await Run(id);
        var signal = JsonSerializer.Serialize(new {targetId = "part", action = "retainIncompleteArchiveGroup",
            stepId = "unpack", artifactId = "input", fingerprint = "old", archiveGroupToken = new string('b', 64)});
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Resume(id, signal));
        Assert.AreEqual("FILE_PROCESSING_RECOVERY_WAIT_CHANGED", error.Message);
        var after = await Run(id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, after.Status);
        Assert.AreEqual(before.WaitPromptJson, after.WaitPromptJson);
        Assert.AreEqual(before.CurrentItemJson, after.CurrentItemJson);
        Assert.IsNull(after.PendingSignalJson);
        Assert.AreEqual(before.NodeExecutions.Count, after.NodeExecutions.Count);
    }

    [TestMethod]
    public async Task NodeInputProjectionIsPersistedBeforeInPlaceMutation()
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity("action.test.metadataMutation"),
            Activity("action.test.metadataMutation"));
        await Execute(id);
        var run = await Run(id);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        var nodes = run.NodeExecutions.OrderBy(n => n.StepIndex).ToArray();
        Assert.IsNull(nodes[0].InputsJson, "Inputs without an explicit projection remain unknown.");
        Assert.AreEqual("before", nodes[1].ToDomainModel().Inputs.Single().Value);
        Assert.AreEqual("after", nodes[1].ToDomainModel().Outputs.Single().Value);
    }

    [TestMethod]
    public async Task SingleItemRun_SuspendsAtStepTwo_ThenResumesIntoThatStepAndFinishes()
    {
        File.WriteAllText(Path.Combine(_root, "raw.mkv"), "");

        var runId = await StartRun(ScanFilterJson(),
            Activity(TextWorkflowKinds.TransformCapture, new {pattern = @"^(?<stem>[^.]+)"}),
            Activity(WaitForWordActivity.ActivityKind),
            Activity(FsWorkflowKinds.ActionSaveName));

        await Execute(runId);

        var waiting = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, waiting.Status, waiting.ErrorMessage);
        Assert.AreEqual(1, waiting.CurrentStepIndex, "the cursor names the step that is waiting");
        Assert.AreEqual("test.waitingForWord", waiting.WaitReason);
        Assert.AreEqual("""{"ask":"word"}""", waiting.WaitPromptJson);
        Assert.IsNotNull(waiting.WaitingSince);
        Assert.IsNotNull(waiting.CurrentItemJson);
        Assert.IsNull(waiting.CompletedAt, "waiting is not an ending");

        await Resume(runId, """{"word":"clean.mkv"}""");

        var pending = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Pending, pending.Status);
        Assert.AreEqual(1, pending.CurrentStepIndex, "resuming does not move the cursor — the runner does");

        await Execute(runId);

        var done = await Run(runId);
        Assert.AreEqual("""{"ask":"word"}""", WaitForWordActivity.ResumePrompts.Single());
        Assert.AreEqual(WorkflowRunStatus.Success, done.Status, done.ErrorMessage);
        Assert.IsNull(done.CurrentStepIndex, "a finished run keeps no cursor");
        Assert.IsNull(done.CurrentItemJson);
        Assert.IsNull(done.WaitReason);
        var attempts = done.NodeExecutions.OrderBy(n => n.Id).ToList();
        CollectionAssert.AreEqual(new[] {"succeeded", "waiting", "succeeded", "succeeded"}, attempts.Select(n => n.Status).ToArray());
        Assert.AreEqual(attempts[1].NodeId, attempts[2].NodeId);
        Assert.AreEqual(1, attempts[1].Attempt);
        Assert.AreEqual(2, attempts[2].Attempt);
        Assert.AreEqual("signal", attempts[2].ResumeKind);
        Assert.AreEqual("awaiting", attempts[1].ToDomainModel().Outputs.Single().Value);
        Assert.AreEqual("answered", attempts[2].ToDomainModel().Outputs.Single().Value);
        CollectionAssert.AreEqual(new[] {0, 1, 2}, done.ToDomainModel().StepStats.Select(s => s.StepIndex).ToArray());

        CollectionAssert.AreEqual(
            new[] {"process:raw.mkv", "resume:raw.mkv"},
            WaitForWordActivity.Calls,
            "the waiting step is entered exactly twice, the second time through ResumeAsync");

        await using var scope = _sp.CreateAsyncScope();
        var plan = await scope.ServiceProvider.GetRequiredService<IFileRenameEntryService>().GetByRunId(runId);
        Assert.AreEqual("clean.mkv", plan.Single().To,
            "the rename step downstream of the wait saw the item the signal produced");
    }

    [TestMethod]
    public async Task WaitingRunDetailSearchAndSourceHistoryUseItsRecordedKindWithoutLiveTaskOrCurrentDefinition()
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity(WaitForWordActivity.ActivityKind));
        await Execute(id);
        var waiting = await Run(id);
        Assert.AreEqual(WorkflowRunStatus.Waiting, waiting.Status);

        await using var scope = _sp.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<BakabaseDbContext>();
        db.Set<WorkflowRunSourceDbModel>().Add(new()
            {WorkflowRunId = id, Module = "fixture", EntityKind = "input", EntityId = "waiting"});
        await db.SaveChangesAsync();
        // Deliberately change only this fixture's live definition after execution. All read
        // surfaces must still display the actual persisted node, not this replacement kind.
        await db.Set<WorkflowActivityDbModel>().Where(a => a.WorkflowDefinitionId == waiting.WorkflowDefinitionId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Kind, "different.current.definition"));
        var controller = ActivatorUtilities.CreateInstance<WorkflowController>(services);
        var detail = await controller.GetRun(id, db, services.GetRequiredService<BTaskManager>(), default);
        Assert.AreEqual(WaitForWordActivity.ActivityKind, detail.Data!.ActivityKind);
        var search = await controller.SearchRuns(waiting.WorkflowDefinitionId, new());
        Assert.AreEqual(WaitForWordActivity.ActivityKind, search.Data!.Single().ActivityKind);
        // This filesystem fixture has no downloader projection; the source-history query
        // therefore needs no site download service or external provider dependencies.
        var sources = new WorkflowSourcesController(db,
            services.GetRequiredService<WorkflowSourceIndex<BakabaseDbContext>>(), [],
            services.GetRequiredService<BTaskManager>(), null!);
        var history = await sources.Query(new() {Sources = [new("fixture", "input", "waiting")]}, default);
        Assert.AreEqual(WaitForWordActivity.ActivityKind, history.Data!.Single().Runs.Single().Run.ActivityKind);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedAndCancelledAttemptsRetainExplicitSafePartialOutputs(bool cancel)
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity("action.test.reportingFailure", new {cancel}));
        if (cancel) await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => Execute(id));
        else await Execute(id);
        var run = await Run(id);
        var node = run.NodeExecutions.Single();
        Assert.AreEqual(cancel ? "cancelled" : "failed", node.Status);
        Assert.IsNotNull(node.CompletedAt);
        Assert.IsTrue(node.ToDomainModel().Outputs.Any(b => b.TaskId == "site:42"));
        Assert.IsFalse(node.OutputsJson.Contains("secret-canary"));
    }

    [DataTestMethod]
    [DataRow("typed", "LOCAL_STORAGE_RESERVE_LOW")]
    [DataRow("os", "LOCAL_STORAGE_FULL")]
    [DataRow("network", "WORKFLOW_ACTIVITY_FAILED")]
    public async Task StorageFailureMetadataUsesOnlyTypedOrOsEvidence(string storage, string expected)
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(), Activity("action.test.reportingFailure", new {storage}));
        await Execute(id);
        var run = await Run(id);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        var node = run.NodeExecutions.Single();
        Assert.AreEqual(expected, node.ToDomainModel().Outputs.Single(b => b.Key == "activity-failed").Code);
        Assert.IsFalse(node.OutputsJson.Contains("SECRET_PATH"));
    }

    [TestMethod]
    public async Task ProcessRestartEndsOldAttemptAndCheckpointStartsAnotherWithoutReplayingUpstream()
    {
        File.WriteAllText(Path.Combine(_root, "input.txt"), "");
        var id = await StartRun(ScanFilterJson(),
            Activity(TextWorkflowKinds.TransformCapture, new {pattern = @"^(?<stem>[^.]+)"}),
            Activity(WaitForWordActivity.ActivityKind));
        await Execute(id);
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var run = await db.WorkflowRuns.Include(r => r.NodeExecutions).SingleAsync(r => r.Id == id);
            run.Status = WorkflowRunStatus.Running;
            var unfinished = run.NodeExecutions.Single(n => n.StepIndex == 1);
            unfinished.Status = "running";
            unfinished.CompletedAt = null;
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>().MarkInterruptedRunsAsync();
        }
        await Execute(id);
        var result = await Run(id);
        var nodes = result.NodeExecutions.OrderBy(n => n.Id).ToList();
        CollectionAssert.AreEqual(new[] {"succeeded", "interrupted", "waiting"}, nodes.Select(n => n.Status).ToArray());
        Assert.AreEqual(2, nodes[2].Attempt);
        Assert.AreEqual("checkpoint", nodes[2].ResumeKind);
        Assert.AreEqual(1, result.ToDomainModel().StepStats.Count);
    }

    /// <summary>
    /// The step before the wait must not run a second time — that is the whole point of persisting
    /// a cursor, and the only reason activities need not be idempotent across a chain.
    /// </summary>
    [TestMethod]
    public async Task Resume_DoesNotReplayTheStepsBeforeTheCursor()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");

        var runId = await StartRun(ScanFilterJson(),
            Activity(FsWorkflowKinds.TransformFileNameOp, new
            {
                // Insert "x-" at the start of the whole file name.
                operations = new[] {new {target = 1, operation = 1, position = 1, text = "x-"}}
            }),
            Activity(WaitForWordActivity.ActivityKind));

        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await Run(runId)).Status);

        await Resume(runId, """{"word":"final.txt"}""");
        await Execute(runId);

        var done = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, done.Status, done.ErrorMessage);

        // Both entries into the waiting step saw the same name: the prefixing step ran once, on
        // the first attempt, and the resume picked the item up from the cursor instead of the top.
        CollectionAssert.AreEqual(new[] {"process:x-a.txt", "resume:x-a.txt"}, WaitForWordActivity.Calls);
    }

    /// <summary>
    /// A batch has no single item a question could be about, so a step that suspends there is a
    /// mistake in how the workflow was put together, not a runtime hiccup — and it fails loudly
    /// rather than parking a run nobody can answer.
    /// </summary>
    [TestMethod]
    public async Task SuspensionDuringABatchRun_FailsTheRun()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "");

        var runId = await StartRun(ScanFilterJson(), Activity(WaitForWordActivity.ActivityKind));

        await Execute(runId);

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        StringAssert.Contains(run.ErrorMessage, "single-item");
        Assert.IsNull(run.WaitReason, "nothing is waiting — the run is over");
    }

    /// <summary>
    /// An activity that suspends without being able to take an answer would park forever. The
    /// runner refuses the signal rather than dropping it silently.
    /// </summary>
    [TestMethod]
    public async Task ResumingAStepThatCannotBeResumed_FailsTheRun()
    {
        File.WriteAllText(Path.Combine(_root, "only.txt"), "");

        var runId = await StartRun(ScanFilterJson(), Activity(NonResumableWaitActivity.ActivityKind));

        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Waiting, (await Run(runId)).Status);

        await Resume(runId, "{}");
        await Execute(runId);

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        StringAssert.Contains(run.ErrorMessage, "IResumableWorkflowActivity");
    }

    /// <summary>Answering a run that is not asking would restart a finished chain from its cursor.</summary>
    [TestMethod]
    public async Task ResumingARunThatIsNotWaiting_Throws()
    {
        File.WriteAllText(Path.Combine(_root, "only.txt"), "");
        var runId = await StartRun(ScanFilterJson(), Activity(FsWorkflowKinds.ActionSaveName));
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, (await Run(runId)).Status);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Resume(runId, "{}"));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => Resume(runId + 10_000, "{}"));
    }

    /// <summary>
    /// What a restart does to each kind of half-finished row: a run that knows where it got to
    /// goes back in the queue, one that does not is written off, and a run waiting on a person is
    /// none of the restart's business.
    /// </summary>
    [TestMethod]
    public async Task Restart_RequeuesRunsWithACursor_WritesOffTheRest_AndLeavesWaitingRunsAlone()
    {
        var defId = (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"t-{Guid.NewGuid():N}",
                TriggerKind = FsWorkflowKinds.TriggerManualScan,
                TriggerFilterJson = ScanFilterJson(),
                Enabled = false,
            })).Id;

        int withCursor, withoutCursor, waiting;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var rows = new[]
            {
                new WorkflowRunDbModel
                {
                    WorkflowDefinitionId = defId, Status = WorkflowRunStatus.Running,
                    StartedAt = DateTime.Now, CurrentStepIndex = 2, CurrentItemJson = "{}"
                },
                new WorkflowRunDbModel
                {
                    WorkflowDefinitionId = defId, Status = WorkflowRunStatus.Running, StartedAt = DateTime.Now
                },
                new WorkflowRunDbModel
                {
                    WorkflowDefinitionId = defId, Status = WorkflowRunStatus.Waiting,
                    StartedAt = DateTime.Now, CurrentStepIndex = 1, WaitReason = "test.waitingForWord",
                    WaitingSince = DateTime.Now
                },
            };
            db.Set<WorkflowRunDbModel>().AddRange(rows);
            await db.SaveChangesAsync();
            (withCursor, withoutCursor, waiting) = (rows[0].Id, rows[1].Id, rows[2].Id);

            await scope.ServiceProvider.GetRequiredService<WorkflowRunRehydrator<BakabaseDbContext>>()
                .MarkInterruptedRunsAsync();
        }

        Assert.AreEqual(WorkflowRunStatus.Pending, (await Run(withCursor)).Status);
        Assert.AreEqual(2, (await Run(withCursor)).CurrentStepIndex, "it restarts at the step it reached");

        var lost = await Run(withoutCursor);
        Assert.AreEqual(WorkflowRunStatus.Interrupted, lost.Status);
        StringAssert.Contains(lost.ErrorMessage, "restart");

        var stillWaiting = await Run(waiting);
        Assert.AreEqual(WorkflowRunStatus.Waiting, stillWaiting.Status);
        Assert.AreEqual("test.waitingForWord", stillWaiting.WaitReason);
    }

    /// <summary>An item survives the round trip through the run row with its CLR type intact.</summary>
    [TestMethod]
    public void ItemSnapshot_RoundTripsThroughTheRunRow()
    {
        var item = new FsEntryItem
        {
            Path = Path.Combine(_root, "x.mkv"),
            IsDirectory = false,
            OriginalName = "x.mkv",
            WorkingName = "y.mkv",
        };

        var restored = WorkflowItemSnapshot.Restore(WorkflowItemSnapshot.Capture(item));

        Assert.IsInstanceOfType<FsEntryItem>(restored);
        Assert.AreEqual(item, restored);

        Assert.ThrowsException<InvalidOperationException>(
            () => WorkflowItemSnapshot.Restore("""{"type":"No.Such.Type, Nowhere","item":{}}"""),
            "a type this build no longer has fails the restore rather than the whole process");
    }
}
