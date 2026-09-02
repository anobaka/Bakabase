using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Workflow;
using Bakabase.Service.Components.Workflow.Fs;
using Bakabase.Service.Components.Workflow.Text;
using Bakabase.Service.Components.Workflow.Triggers;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Batch 3 of the file-cleaning vertical: the text family (E3). Contract-based acceptance
/// (ITextWorkpiece), the vocabulary-driven remove/trim transforms, and the B1 metadata the
/// descriptors ship for the editor's generic chain walk.
/// </summary>
[TestClass]
public sealed class WorkflowTextOpsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IServiceProvider _sp = null!;
    private string _root = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _root = Path.Combine(Path.GetTempPath(), $"BakabaseTextOps_{Guid.NewGuid():N}");
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

    private string ScanFilterJson() =>
        JsonSerializer.Serialize(new {roots = new[] {_root}, target = FsScanTarget.Files, depth = 1}, Json);

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

    private async Task<int> InsertRun(int defId, object payload)
    {
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

    private async Task<int> RunChain(params WorkflowActivityInputModel[] activities)
    {
        var defId = await CreateDefinition(FsWorkflowKinds.TriggerManualScan, ScanFilterJson(), activities);
        var trigger = _sp.GetRequiredService<IWorkflowTriggerRegistry>().Get(FsWorkflowKinds.TriggerManualScan)!;
        var runId = await InsertRun(defId, trigger.BuildManualPayload(ScanFilterJson(), null));
        await Execute(runId);
        return runId;
    }

    [TestMethod]
    public async Task TextChain_RemoveWrapped_RemoveTexts_Trim_PlansCleanName()
    {
        File.WriteAllText(Path.Combine(_root, "[SubGroup] Show ADWORD ep1.mkv"), "");

        int wrappersId, groupsId, adsId;
        await using (var scope = _sp.CreateAsyncScope())
        {
            var vocabulary = scope.ServiceProvider.GetRequiredService<ITextVocabularyService>();
            var wrappers = await vocabulary.AddType("Test wrappers", TextTypeShape.DelimiterPair);
            await vocabulary.AddEntry(wrappers.Id, "[", "]");
            var groups = await vocabulary.AddType("Test groups", TextTypeShape.Values);
            await vocabulary.AddEntry(groups.Id, "SubGroup");
            var ads = await vocabulary.AddType("Test ads", TextTypeShape.Values);
            await vocabulary.AddEntry(ads.Id, "ADWORD");
            (wrappersId, groupsId, adsId) = (wrappers.Id, groups.Id, ads.Id);
        }

        var runId = await RunChain(
            new WorkflowActivityInputModel
            {
                Kind = TextWorkflowKinds.TransformRemoveWrapped,
                ConfigJson = JsonSerializer.Serialize(
                    new {wrappersTypeId = wrappersId, setTypeId = groupsId, mode = TextMatchMode.EqualsAny}, Json)
            },
            new WorkflowActivityInputModel
            {
                Kind = TextWorkflowKinds.TransformRemoveTexts,
                ConfigJson = JsonSerializer.Serialize(
                    new {setTypeId = adsId, mode = TextMatchMode.ContainsAny}, Json)
            },
            new WorkflowActivityInputModel {Kind = TextWorkflowKinds.TransformTrim},
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Success, run.Status, run.ErrorMessage);

        await using var readScope = _sp.CreateAsyncScope();
        var plan = await readScope.ServiceProvider.GetRequiredService<IFileRenameEntryService>()
            .GetByRunId(runId);
        var entry = plan.Single();
        Assert.AreEqual("[SubGroup] Show ADWORD ep1.mkv", entry.From);
        Assert.AreEqual("Show ep1.mkv", entry.To);
        Assert.AreEqual(FileRenameStatus.Pending, entry.Status);
    }

    [TestMethod]
    public async Task TextActivity_OnNonTextItemType_IsRejectedAtSaveTime()
    {
        // The subscription trigger's item type does not implement ITextWorkpiece, so a text
        // node behind it must be refused by the same walk that checks tag acceptance.
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            CreateDefinition(SubscriptionWorkflowKinds.TriggerUpdated, null,
                new WorkflowActivityInputModel {Kind = TextWorkflowKinds.TransformTrim}));
        StringAssert.Contains(ex.Message, nameof(ITextWorkpiece));
    }

    [TestMethod]
    public async Task DeletedTextType_FailsTheRun_EvenUnderSkipPolicy()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "");

        var runId = await RunChain(
            new WorkflowActivityInputModel
            {
                Kind = TextWorkflowKinds.TransformRemoveTexts,
                ConfigJson = JsonSerializer.Serialize(new {setTypeId = 99999}, Json),
                OnItemError = WorkflowActivityErrorBehavior.Skip
            },
            new WorkflowActivityInputModel {Kind = FsWorkflowKinds.ActionSaveName});

        var run = await Run(runId);
        Assert.AreEqual(WorkflowRunStatus.Failed, run.Status,
            "a dangling text-type reference is config staleness and must fail the run");
        StringAssert.Contains(run.ErrorMessage, "99999");
    }

    [TestMethod]
    public async Task Descriptors_ShipContractMetadata_ForTheGenericChainWalk()
    {
        var activity = _sp.GetRequiredService<IWorkflowActivityRegistry>()
            .Get(TextWorkflowKinds.TransformRemoveWrapped)!;
        Assert.AreEqual(typeof(ITextWorkpiece), activity.AcceptedItemInterface);
        Assert.AreEqual(0, activity.AcceptedInputItemTypes.Count,
            "text nodes accept by contract only — a tag list would defeat the point");

        var fsEntry = _sp.GetRequiredService<IWorkflowItemTypeRegistry>().Get(WorkflowItemTypes.FsEntry)!;
        Assert.IsTrue(typeof(ITextWorkpiece).IsAssignableFrom(fsEntry.ClrType));
    }

    [TestMethod]
    public async Task Workpiece_RoundTrip_KeepsItemIdentity()
    {
        var item = new FsEntryItem
        {
            Path = "/tmp/x/a.txt", IsDirectory = false, OriginalName = "a.txt", WorkingName = "a.txt"
        };

        var replaced = (FsEntryItem) ((ITextWorkpiece) item).WithWorkingText("b.txt");
        Assert.AreEqual("b.txt", replaced.WorkingName);
        Assert.AreEqual(item.Path, replaced.Path);
        Assert.AreEqual(item.OriginalName, replaced.OriginalName);
        await Task.CompletedTask;
    }
}
