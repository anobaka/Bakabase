using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.FileNameModifier.Models;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.Workflow.Fs;
using Bakabase.Service.Components.Workflow.Triggers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Batch 2 of the file-cleaning vertical: applying and undoing a plan, plus the hardening that
/// gates it — loud config failures, cancelled-run guards, and runtime item-type checks.
/// </summary>
[TestClass]
public sealed class WorkflowFsApplyUndoTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    /// <summary>A trigger that lies: declares item.fs.entry but emits strings — the shape the
    /// runner's runtime type check exists to catch.</summary>
    private sealed class LyingTrigger : IWorkflowTrigger
    {
        public const string TriggerKind = "test.lying";
        public string Kind => TriggerKind;
        public string DisplayName => "Lying trigger (test)";
        public Type PayloadType => typeof(FsManualScanPayload);
        public bool Matches(object payload, string? triggerFilterJson) => false;
        public bool RequiresManualPayload => false;
        public object BuildManualPayload(string? f, string? a) => new FsManualScanPayload();
        public IReadOnlyList<object> ExtractItems(object payload) => ["not-an-fs-entry"];
        public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.FsEntry;
    }

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider(services =>
            services.AddSingleton<IWorkflowTrigger, LyingTrigger>());
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseFsApply_{Guid.NewGuid():N}");
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

    private string ScanFilterJson(FsScanTarget target = FsScanTarget.Files, int depth = 1) =>
        JsonSerializer.Serialize(new {roots = new[] {_root}, target, depth}, Json);

    private static string ReplaceOpsConfig(params (string From, string To)[] replaces) =>
        JsonSerializer.Serialize(new
        {
            operations = replaces.Select(r => new FileNameModifierOperation
            {
                Target = FileNameModifierFileNameTarget.FileName,
                Operation = FileNameModifierOperationType.Replace,
                TargetText = r.From,
                Text = r.To
            }).ToList()
        }, Json);

    private async Task<int> CreateDefinition(string triggerKind, string? filterJson,
        params WorkflowActivityInputModel[] activities) =>
        (await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"t-{Guid.NewGuid():N}",
                TriggerKind = triggerKind,
                TriggerFilterJson = filterJson,
                Enabled = false,
                Activities = activities.ToList()
            })).Id;

    private async Task<int> InsertRun(int defId, object payload, WorkflowRunStatus status = WorkflowRunStatus.Pending)
    {
        await using var scope = _sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = defId,
            Status = status,
            StartedAt = DateTime.Now,
            PayloadJson = JsonSerializer.Serialize(payload, Json)
        };
        db.Set<WorkflowRunDbModel>().Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

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
            .Set<WorkflowRunDbModel>().AsNoTracking().FirstAsync(r => r.Id == runId);
    }

    private async Task<T> WithEntries<T>(Func<IFileRenameEntryService, Task<T>> use)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await use(scope.ServiceProvider.GetRequiredService<IFileRenameEntryService>());
    }

    /// <summary>Preview a cleaning chain over the temp root and return the run id.</summary>
    private async Task<int> Preview(string opsConfig, FsScanTarget target = FsScanTarget.Files, int depth = 1)
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerManualScan, ScanFilterJson(target, depth),
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.TransformFileNameOp, ConfigJson = opsConfig},
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var payload = trigger.BuildManualPayload(ScanFilterJson(target, depth), null);
        var runId = await InsertRun(defId, payload);
        await Execute(runId);
        return runId;
    }

    [TestMethod]
    public async Task Apply_RenamesDisk_AndSkipsExcluded()
    {
        File.WriteAllText(Path.Combine(_root, "a (x).txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b (x).txt"), "b");

        var runId = await Preview(ReplaceOpsConfig((" (x)", "")));
        var plan = await WithEntries(s => s.GetByRunId(runId));
        Assert.AreEqual(2, plan.Count);

        // The user unchecks the second row in the confirm panel.
        var excluded = plan.Single(e => e.From == "b (x).txt");
        await WithEntries(s => s.SetExcluded(excluded.Id, true));

        var after = await WithEntries(s => s.ApplyRun(runId));

        Assert.IsTrue(File.Exists(Path.Combine(_root, "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "a (x).txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "b (x).txt")), "excluded row must stay untouched");
        Assert.AreEqual(FileRenameStatus.Applied, after.Single(e => e.From == "a (x).txt").Status);
        Assert.AreEqual(FileRenameStatus.Excluded, after.Single(e => e.From == "b (x).txt").Status);
    }

    [TestMethod]
    public async Task Apply_DeepestFirst_ThenUndo_RestoresEverything()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_root, "Show (raw)"));
        File.WriteAllText(Path.Combine(dir.FullName, "ep1 (raw).mkv"), "");

        var runId = await Preview(ReplaceOpsConfig((" (raw)", "")), FsScanTarget.Both, depth: 2);
        var applied = await WithEntries(s => s.ApplyRun(runId));

        Assert.IsTrue(applied.All(e => e.Status == FileRenameStatus.Applied),
            string.Join("; ", applied.Select(e => $"{e.From}:{e.Status}:{e.Error}")));
        // Child renamed before its parent moved — final structure has both new names.
        Assert.IsTrue(File.Exists(Path.Combine(_root, "Show", "ep1.mkv")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Show (raw)")));

        var undone = await WithEntries(s => s.UndoRun(runId));
        Assert.IsTrue(undone.All(e => e.Status == FileRenameStatus.Undone),
            string.Join("; ", undone.Select(e => $"{e.From}:{e.Status}:{e.Error}")));
        Assert.IsTrue(File.Exists(Path.Combine(_root, "Show (raw)", "ep1 (raw).mkv")));
    }

    [TestMethod]
    public async Task Apply_DiskDriftedAfterPreview_RowFailsOthersProceed()
    {
        File.WriteAllText(Path.Combine(_root, "one (x).txt"), "");
        File.WriteAllText(Path.Combine(_root, "two (x).txt"), "");

        var runId = await Preview(ReplaceOpsConfig((" (x)", "")));
        // Drift: the first row's target appears between preview and apply.
        File.WriteAllText(Path.Combine(_root, "one.txt"), "squatter");

        var after = await WithEntries(s => s.ApplyRun(runId));

        var failed = after.Single(e => e.From == "one (x).txt");
        Assert.AreEqual(FileRenameStatus.Failed, failed.Status);
        Assert.IsNotNull(failed.Error);
        Assert.AreEqual(FileRenameStatus.Applied, after.Single(e => e.From == "two (x).txt").Status);
        Assert.AreEqual("squatter", File.ReadAllText(Path.Combine(_root, "one.txt")));
    }

    [TestMethod]
    public async Task MalformedConfig_FailsTheRun_EvenUnderSkipPolicy()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");

        var defId = await CreateDefinition(FsWorkflowKinds.TriggerManualScan, ScanFilterJson(),
            new WorkflowActivityInputModel
            {
                Kind = FsWorkflowKinds.TransformFileNameOp,
                ConfigJson = "{ operations: [ broken",
                OnItemError = WorkflowActivityErrorBehavior.Skip
            },
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var runId = await InsertRun(defId, trigger.BuildManualPayload(ScanFilterJson(), null));

        await Execute(runId);

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status,
            "a broken config must fail the run, not silently run the step on defaults");
        StringAssert.Contains(run.ErrorMessage, "configuration");
    }

    [TestMethod]
    public async Task DisablingADefinition_CancelsItsQueuedRuns_AndTheGuardSkipsThem()
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerManualScan, ScanFilterJson(),
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var runId = await InsertRun(defId, trigger.BuildManualPayload(ScanFilterJson(), null));

        await _sp.GetRequiredService<IWorkflowDefinitionService>()
            .UpdateAsync(defId, new WorkflowDefinitionUpdateInputModel {Enabled = false});

        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await Run(runId)).Status);

        // The stale BTask fires anyway; the runner's Pending-only guard must make it a no-op.
        await Execute(runId);
        Assert.AreEqual(WorkflowRunStatus.Cancelled, (await Run(runId)).Status);
    }

    [TestMethod]
    public async Task DeletingADefinition_RefusesWhileARunExecutes()
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerManualScan, ScanFilterJson(),
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var runId = await InsertRun(defId, trigger.BuildManualPayload(ScanFilterJson(), null),
            WorkflowRunStatus.Running);

        var definitions = _sp.GetRequiredService<IWorkflowDefinitionService>();
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => definitions.DeleteAsync(defId));

        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            await db.Set<WorkflowRunDbModel>().Where(r => r.Id == runId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Success));
        }

        await definitions.DeleteAsync(defId);
        Assert.IsNull(await definitions.GetAsync(defId));
    }

    [TestMethod]
    public async Task RuntimeItemTypeMismatch_FailsTheRun()
    {
        var defId = await CreateDefinition(LyingTrigger.TriggerKind, null,
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});
        var runId = await InsertRun(defId, new FsManualScanPayload());

        await Execute(runId);

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status);
        StringAssert.Contains(run.ErrorMessage, "typing");
    }
}
