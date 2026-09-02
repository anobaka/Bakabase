using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
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
using Microsoft.Extensions.Logging;

namespace Bakabase.Tests;

/// <summary>
/// Batch 5 of the file-cleaning vertical (E6): the scheduler sweep over
/// <see cref="IWorkflowScheduledTrigger"/> definitions, and the directory-watch service's
/// settle-then-publish pipeline. Both are driven directly (no timers) so the tests are
/// deterministic.
/// </summary>
[TestClass]
public sealed class WorkflowAutomationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseAuto_{Guid.NewGuid():N}");
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

    private async Task<int> CreateDefinition(string triggerKind, object filter, bool enabled = true)
    {
        var def = await _sp.GetRequiredService<IWorkflowDefinitionService>().CreateAsync(
            new WorkflowDefinitionCreationInputModel
            {
                Name = $"t-{Guid.NewGuid():N}",
                TriggerKind = triggerKind,
                TriggerFilterJson = JsonSerializer.Serialize(filter, Json),
                Enabled = enabled,
                Activities = [new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName}]
            });
        return def.Id;
    }

    private async Task<int> RunCount(int defId)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().CountAsync(r => r.WorkflowDefinitionId == defId);
    }

    private WorkflowSchedulerTask Scheduler() =>
        new(_sp, _sp.GetRequiredService<IBakabaseLocalizer>());

    [TestMethod]
    public async Task Scheduler_StartsADueDefinition_Once()
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerScheduledScan,
            new {roots = new[] {_root}, target = 3, depth = 1, intervalMinutes = 60});

        await Scheduler().RunAsync(BuildArgs(_sp));
        Assert.AreEqual(1, await RunCount(defId), "never-ran + scheduled = due immediately");

        // The second sweep must not stack another run: either the first is still queued, or it
        // completed and stamped LastRunAt inside the 60-minute interval.
        await Scheduler().RunAsync(BuildArgs(_sp));
        Assert.AreEqual(1, await RunCount(defId));
    }

    [TestMethod]
    public async Task Scheduler_SkipsDisabled_ManualOnly_AndNotDueDefinitions()
    {
        var disabled = await CreateDefinition(FsWorkflowKinds.TriggerScheduledScan,
            new {roots = new[] {_root}, intervalMinutes = 60}, enabled: false);
        var manualOnly = await CreateDefinition(FsWorkflowKinds.TriggerScheduledScan,
            new {roots = new[] {_root}}); // no interval = manual-only, not broken
        var manualScan = await CreateDefinition(FsWorkflowKinds.TriggerManualScan,
            new {roots = new[] {_root}}); // not a scheduled trigger at all

        var notDue = await CreateDefinition(FsWorkflowKinds.TriggerScheduledScan,
            new {roots = new[] {_root}, intervalMinutes = 60});
        await using (var scope = _sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .Set<WorkflowDefinitionDbModel>().Where(d => d.Id == notDue)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.LastRunAt, _ => DateTime.Now));
        }

        await Scheduler().RunAsync(BuildArgs(_sp));

        Assert.AreEqual(0, await RunCount(disabled));
        Assert.AreEqual(0, await RunCount(manualOnly));
        Assert.AreEqual(0, await RunCount(manualScan));
        Assert.AreEqual(0, await RunCount(notDue));
    }

    [TestMethod]
    public void ScheduledScanTrigger_IntervalParsing()
    {
        var trigger = new FsScheduledScanTrigger();
        Assert.AreEqual(TimeSpan.FromMinutes(15),
            trigger.GetInterval("""{"roots":["/x"],"intervalMinutes":15}"""));
        Assert.IsNull(trigger.GetInterval("""{"roots":["/x"]}"""), "no interval = manual-only");
        Assert.IsNull(trigger.GetInterval("""{"intervalMinutes":0}"""));
        Assert.IsNull(trigger.GetInterval(null));
        Assert.IsNull(trigger.GetInterval("not json"));
    }

    [TestMethod]
    public async Task Watch_SettledEntry_IsPublishedOnce_AndCreatesARun()
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerWatch,
            new {roots = new[] {_root}, target = 1, settleSeconds = 5});

        var service = new WorkflowFsWatchService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<ILogger<WorkflowFsWatchService>>());
        await service.RefreshDefinitionsAsync();

        var file = Path.Combine(_root, "dropped (x).mkv");
        File.WriteAllText(file, "");
        service.NoteFsEvent(file);

        // Not settled yet — the file might still be mid-copy.
        await service.TickAsync(DateTime.UtcNow);
        Assert.AreEqual(0, await RunCount(defId));

        // Quiet long enough → one publish → one run for the definition.
        await service.TickAsync(DateTime.UtcNow.AddSeconds(6));
        Assert.AreEqual(1, await RunCount(defId));

        // Still quiet, already published — no duplicate run.
        await service.TickAsync(DateTime.UtcNow.AddSeconds(10));
        Assert.AreEqual(1, await RunCount(defId));

        // New activity on the same entry re-arms it: it fires again once quiet again.
        service.NoteFsEvent(file);
        await service.TickAsync(DateTime.UtcNow.AddSeconds(30));
        Assert.AreEqual(2, await RunCount(defId));
    }

    [TestMethod]
    public async Task Watch_FiltersByExtension_PerDefinition()
    {
        var mkvOnly = await CreateDefinition(FsWorkflowKinds.TriggerWatch,
            new {roots = new[] {_root}, target = 1, extensionFilter = new[] {"mkv"}, settleSeconds = 1});

        var service = new WorkflowFsWatchService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<ILogger<WorkflowFsWatchService>>());
        await service.RefreshDefinitionsAsync();

        var txt = Path.Combine(_root, "notes.txt");
        File.WriteAllText(txt, "");
        service.NoteFsEvent(txt);
        await service.TickAsync(DateTime.UtcNow.AddSeconds(5));
        Assert.AreEqual(0, await RunCount(mkvOnly), "a .txt must not fire an mkv-only watch");

        var mkv = Path.Combine(_root, "ep1.mkv");
        File.WriteAllText(mkv, "");
        service.NoteFsEvent(mkv);
        await service.TickAsync(DateTime.UtcNow.AddSeconds(10));
        Assert.AreEqual(1, await RunCount(mkvOnly));
    }

    [TestMethod]
    public async Task WatchRun_ExtractsTheSettledEntries_AndPlansRenames()
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerWatch,
            new {roots = new[] {_root}, target = 1, settleSeconds = 1});
        // The saveName-only chain plans nothing (no name change), so add a rename op upstream.
        await _sp.GetRequiredService<IWorkflowDefinitionService>().UpdateAsync(defId,
            new WorkflowDefinitionUpdateInputModel
            {
                Activities =
                [
                    new WorkflowActivityInputModel
                    {
                        Kind = FsWorkflowKinds.TransformFileNameOp,
                        ConfigJson = JsonSerializer.Serialize(new
                        {
                            operations = new[]
                                {new {target = 1, operation = 4, targetText = " (x)", text = ""}}
                        }, Json)
                    },
                    new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName}
                ]
            });
        // UpdateAsync leaves Enabled untouched; re-read to be sure the watch is still armed.
        Assert.IsTrue((await _sp.GetRequiredService<IWorkflowDefinitionService>().GetAsync(defId))!.Enabled);

        var service = new WorkflowFsWatchService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<ILogger<WorkflowFsWatchService>>());
        await service.RefreshDefinitionsAsync();

        var file = Path.Combine(_root, "movie (x).mkv");
        File.WriteAllText(file, "");
        service.NoteFsEvent(file);
        await service.TickAsync(DateTime.UtcNow.AddSeconds(5));

        // The bus recorded a Pending run; execute it directly (the tests' BTask daemon is
        // never started) — same as every other workflow test.
        int runId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var run = await db.Set<WorkflowRunDbModel>().AsNoTracking()
                .SingleAsync(r => r.WorkflowDefinitionId == defId);
            runId = run.Id;
        }

        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        }

        await runner.ExecuteAsync(runId, BuildArgs(_sp));

        await using (var scope = _sp.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
                .Set<WorkflowRunDbModel>().AsNoTracking().SingleAsync(r => r.Id == runId);
            Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        }

        await using var readScope = _sp.CreateAsyncScope();
        var plan = await readScope.ServiceProvider
            .GetRequiredService<Bakabase.Abstractions.Services.IFileRenameEntryService>()
            .GetByRunId(runId);
        var entry = plan.Single();
        Assert.AreEqual("movie (x).mkv", entry.From);
        Assert.AreEqual("movie.mkv", entry.To);
        Assert.AreEqual(FileRenameStatus.Pending, entry.Status);
    }
}
