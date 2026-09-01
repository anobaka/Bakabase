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
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Batch 1 of the file-cleaning vertical: a manual scan feeds fs entries through a name
/// transform into a preview-only saveName, and what comes out is a plan — rows in
/// FileRenameEntries — with the disk untouched.
/// </summary>
[TestClass]
public sealed class WorkflowFsCleaningTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseFsCleaning_{Guid.NewGuid():N}");
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

    private static string FileNameOpConfig(params (string From, string To)[] replaces) =>
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

    /// <summary>Create the definition through the service (exercising the typed-chain save
    /// validation), then execute its manual payload through the runner directly — tests must not
    /// depend on the BTask daemon's timing.</summary>
    private async Task<(int RunId, WorkflowRunDbModel Run)> RunCleaning(string fileNameOpConfig)
    {
        var definitions = _sp.GetRequiredService<IWorkflowDefinitionService>();
        var def = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = "cleaning",
            TriggerKind = FsWorkflowKinds.TriggerManualScan,
            TriggerFilterJson = ScanFilterJson(),
            Enabled = false,
            Activities =
            [
                new WorkflowActivityInputModel {Kind = FsWorkflowKinds.TransformFileNameOp, ConfigJson = fileNameOpConfig},
                new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName}
            ]
        });

        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var payload = trigger.BuildManualPayload(ScanFilterJson(), null);

        int runId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = def.Id,
                Status = WorkflowRunStatus.Pending,
                StartedAt = DateTime.Now,
                PayloadJson = JsonSerializer.Serialize(payload, Json)
            };
            db.Set<WorkflowRunDbModel>().Add(run);
            await db.SaveChangesAsync();
            runId = run.Id;
        }

        WorkflowRunner<BakabaseDbContext> runner;
        await using (var scope = _sp.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<BakabaseDbContext>>();
        }

        await runner.ExecuteAsync(runId, BuildArgs(_sp));

        await using var assertScope = _sp.CreateAsyncScope();
        var reloaded = await assertScope.ServiceProvider.GetRequiredService<BakabaseDbContext>()
            .Set<WorkflowRunDbModel>().AsNoTracking().FirstAsync(r => r.Id == runId);
        return (runId, reloaded);
    }

    private async Task<List<Bakabase.Abstractions.Models.Db.FileRenameEntry>> Entries(int runId)
    {
        await using var scope = _sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IFileRenameEntryService>().GetByRunId(runId);
    }

    [TestMethod]
    public async Task EndToEnd_PlansRenames_WithoutTouchingDisk()
    {
        File.WriteAllText(Path.Combine(_root, "[Grp] Alpha [1080p].mkv"), "");
        File.WriteAllText(Path.Combine(_root, "Keep.txt"), "");
        var sub = Directory.CreateDirectory(Path.Combine(_root, "Sub"));
        File.WriteAllText(Path.Combine(sub.FullName, "[Grp] Nested.mkv"), "");

        var (runId, run) = await RunCleaning(FileNameOpConfig(("[Grp] ", ""), (" [1080p]", "")));

        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        // Target=Files at depth 1: the two root files; neither the sub-directory nor its content.
        Assert.AreEqual(2, run.InputCount);

        var entries = await Entries(runId);
        var planned = entries.Single();
        Assert.AreEqual("[Grp] Alpha [1080p].mkv", planned.From);
        Assert.AreEqual("Alpha.mkv", planned.To);
        Assert.AreEqual(FileRenameStatus.Pending, planned.Status);

        // Preview only — the disk still holds the original names.
        Assert.IsTrue(File.Exists(Path.Combine(_root, "[Grp] Alpha [1080p].mkv")));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "Alpha.mkv")));
    }

    [TestMethod]
    public async Task DuplicateTargetsInOnePlan_SecondBecomesConflict()
    {
        File.WriteAllText(Path.Combine(_root, "dupA (1).txt"), "");
        File.WriteAllText(Path.Combine(_root, "dupA (2).txt"), "");

        var (runId, run) = await RunCleaning(FileNameOpConfig((" (1)", ""), (" (2)", "")));

        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);
        var entries = await Entries(runId);
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(FileRenameStatus.Pending, entries[0].Status);
        Assert.AreEqual(FileRenameStatus.Conflict, entries[1].Status);
        Assert.IsTrue(entries.All(e => e.To == "dupA.txt"));
    }

    [TestMethod]
    public async Task TargetAlreadyOnDisk_BecomesConflict()
    {
        File.WriteAllText(Path.Combine(_root, "Movie (old).mkv"), "");
        File.WriteAllText(Path.Combine(_root, "Movie.mkv"), "");

        var (runId, _) = await RunCleaning(FileNameOpConfig((" (old)", "")));

        var entries = await Entries(runId);
        var conflict = entries.Single(e => e.From == "Movie (old).mkv");
        Assert.AreEqual(FileRenameStatus.Conflict, conflict.Status);
        Assert.IsNotNull(conflict.Error);
    }

    [TestMethod]
    public async Task InvalidCharacters_AreSanitizedIntoThePlan()
    {
        File.WriteAllText(Path.Combine(_root, "Colon.txt"), "");

        var (runId, _) = await RunCleaning(FileNameOpConfig(("Colon", "a:b")));

        var planned = (await Entries(runId)).Single();
        Assert.AreEqual("a_b.txt", planned.To);
        Assert.AreEqual(FileRenameStatus.Pending, planned.Status);
    }

    [TestMethod]
    public void Sanitizer_HandlesReservedNamesAndTrailingDots()
    {
        Assert.AreEqual("a_b", FileNameSanitizer.Sanitize("a<b..."));
        Assert.AreEqual("_CON.txt", FileNameSanitizer.Sanitize("CON.txt"));
        Assert.AreEqual("", FileNameSanitizer.Sanitize("  ..  "));
    }

    [TestMethod]
    public void BuildManualPayload_RejectsMissingConfigAndBadRoots()
    {
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;

        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(null, null));
        Assert.ThrowsException<InvalidOperationException>(() => trigger.BuildManualPayload(
            JsonSerializer.Serialize(new {roots = new[] {Path.Combine(_root, "definitely-missing")}}, Json), null));
    }

    [TestMethod]
    public void ManualScan_NeedsNoManualPayload()
    {
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        Assert.IsFalse(trigger.RequiresManualPayload);
        Assert.AreEqual(WorkflowItemTypes.FsEntry, trigger.ResolveOutputItemType(null));
    }
}
