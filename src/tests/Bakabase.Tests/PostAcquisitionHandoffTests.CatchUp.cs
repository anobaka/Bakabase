using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.DownloadIntents;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class PostAcquisitionHandoffTests
{
    private async Task<PostAcquisitionCatchUpSelection> CatchUpSelection(int id) =>
        (await Worker.PrepareSavedResultsAsync(new([id]), default)).Items.Single().Selection!;

    [TestMethod]
    public async Task ExplicitSavedResultCatchUpAcceptsCleanedHistoryWithoutRecreatingProducerOrChangingResult()
    {
        var saved = await Saved();
        Db.WorkflowRuns.Remove(saved.Run);
        saved.Post.Error = PostParserSavedResults.MissingWorkflowRunError;
        await Db.SaveChangesAsync();
        var original = JsonSerializer.Serialize(saved.Post);
        var preview = await Worker.PrepareSavedResultsAsync(new([saved.Post.Id]), default);
        Assert.AreEqual(1, preview.EligibleCount);
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
        var selection = preview.Items.Single().Selection!;
        Assert.AreEqual(saved.Run.Id, selection.WorkflowRunId);
        var confirmed = await Worker.ConfirmSavedResultsAsync(new([selection]), default);
        Assert.AreEqual(1, confirmed.AcceptedCount);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync(), "Confirmation never creates a producer or starts acquisition inline.");
        var handoff = await Db.AcquisitionHandoffs.SingleAsync();
        StringAssert.StartsWith(handoff.Id, "saved-v1:");
        Assert.AreEqual(saved.Run.Id, handoff.ProducerRunId, "Preserve the historical pointer; do not manufacture a successful run.");
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("created", handoff.Status);
        var acquisition = await Db.AcquisitionTasks.SingleAsync();
        Assert.IsNotNull(acquisition.WorkflowRunId);
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync(), "Only the new acquisition execution is created.");
        Assert.AreEqual(original, JsonSerializer.Serialize(await Db.PostParserTasks.AsNoTracking().SingleAsync()));
    }

    [TestMethod]
    public async Task CatchUpWithNoProducerPointerIsExplicitAndRepeatedConfirmationIsIdempotent()
    {
        var saved = await Saved();
        saved.Post.WorkflowRunId = null;
        Db.WorkflowRuns.Remove(saved.Run);
        await Db.SaveChangesAsync();
        var selection = await CatchUpSelection(saved.Post.Id);
        Assert.IsNull(selection.WorkflowRunId);
        var first = await Worker.ConfirmSavedResultsAsync(new([selection]), default);
        var second = await Worker.ConfirmSavedResultsAsync(new([selection]), default);
        Assert.AreEqual(1, first.AcceptedCount);
        Assert.AreEqual(1, second.ExistingCount);
        Assert.AreEqual(first.Items.Single().HandoffId, second.Items.Single().HandoffId);
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync());
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual("created", (await Db.AcquisitionHandoffs.SingleAsync()).Status);
        Assert.AreEqual(1, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task CatchUpFixedSelectionExcludesNewMatchesAndReportsPartialAcceptance()
    {
        var valid = await Saved(); var changed = await Saved();
        var preview = await Worker.PrepareSavedResultsAsync(new([valid.Post.Id, changed.Post.Id]), default);
        changed.Post.Revision++; await Db.SaveChangesAsync();
        var outside = await Saved();
        var result = await Worker.ConfirmSavedResultsAsync(new(preview.Items.Select(i => i.Selection!).ToList()), default);
        Assert.AreEqual(2, result.RequestedCount); Assert.AreEqual(1, result.AcceptedCount); Assert.AreEqual(1, result.RejectedCount);
        Assert.AreEqual("HANDOFF_SOURCE_CHANGED", result.Items.Single(i => i.TaskId == changed.Post.Id).Code);
        Assert.AreEqual(valid.Post.Id.ToString(), (await Db.AcquisitionHandoffs.SingleAsync()).SourceId);
        Assert.IsFalse(await Db.AcquisitionHandoffs.AnyAsync(h => h.SourceId == outside.Post.Id.ToString()));
    }

    [TestMethod]
    [DataRow("revision")]
    [DataRow("producer")]
    [DataRow("results")]
    [DataRow("deleted")]
    [DataRow("waiting")]
    [DataRow("staleReadiness")]
    [DataRow("disabled")]
    public async Task CatchUpRejectsChangesBetweenPreparationAndConfirmation(string change)
    {
        var saved = await Saved(); var selection = await CatchUpSelection(saved.Post.Id);
        switch (change)
        {
            case "revision": saved.Post.Revision++; break;
            case "producer": saved.Post.WorkflowRunId = null; break;
            case "results": saved.Post.Results += " "; break;
            case "deleted": saved.Post.IsDeleted = true; break;
            case "waiting": saved.Run.Status = WorkflowRunStatus.Waiting; break;
            case "staleReadiness": saved.Post.ContentReadinessJson = JsonSerializer.Serialize(
                new PostParserContentReadinessState {ParsedResultMayBeStale = true}, JsonSerializerOptions.Web); break;
            case "disabled": Options.PostParserAcquisition.Enabled = false; break;
        }
        await Db.SaveChangesAsync();
        var result = await Worker.ConfirmSavedResultsAsync(new([selection]), default);
        Assert.AreEqual(0, result.AcceptedCount); Assert.AreEqual(1, result.RejectedCount);
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    [DataRow("results")]
    [DataRow("producer")]
    [DataRow("waiting")]
    [DataRow("staleReadiness")]
    public async Task CatchUpReceiptRevalidatesSavedEvidenceWhenWorkerConsumesIt(string change)
    {
        var saved = await Saved();
        await Worker.ConfirmSavedResultsAsync(new([await CatchUpSelection(saved.Post.Id)]), default);
        switch (change)
        {
            case "results": saved.Post.Results += " "; break;
            case "producer": saved.Post.WorkflowRunId = null; break;
            case "waiting": saved.Run.Status = WorkflowRunStatus.Waiting; break;
            case "staleReadiness": saved.Post.ContentReadinessJson = JsonSerializer.Serialize(
                new PostParserContentReadinessState {ParsedResultMayBeStale = true}, JsonSerializerOptions.Web); break;
        }
        await Db.SaveChangesAsync();
        await Worker.ProcessNextAsync(default);
        var handoff = await Db.AcquisitionHandoffs.SingleAsync();
        Assert.AreEqual("attention", handoff.Status); Assert.AreEqual("HANDOFF_SOURCE_CHANGED", handoff.Code);
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task CatchUpDeduplicatesAutomaticAndManualAssociationsWithoutRetryingOldErrors()
    {
        var automatic = await Saved();
        var autoHandoff = await Stage(automatic);
        autoHandoff.Status = "error"; autoHandoff.Code = "HANDOFF_FAILED";
        await Db.SaveChangesAsync();
        var manual = await Saved();
        var prepared = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(new()
            {TaskId = manual.Post.Id, Revision = manual.Post.Revision, ResourceIndices = [0]}, Path.GetTempPath(), default);
        var preview = await Worker.PrepareSavedResultsAsync(new([automatic.Post.Id, manual.Post.Id]), default);
        Assert.AreEqual(2, preview.ExistingCount); Assert.AreEqual(0, preview.EligibleCount);
        Assert.IsTrue(preview.Items.All(i => i.Selection != null), "Existing associations still expose an exact saved-result selection for separate explicit review.");
        Assert.AreEqual("HANDOFF_FAILED", preview.Items.Single(i => i.TaskId == automatic.Post.Id).Code);
        CollectionAssert.AreEqual(prepared.AcquisitionTaskIds.ToArray(), preview.Items.Single(i => i.TaskId == manual.Post.Id).AcquisitionTaskIds.ToArray());
        Assert.AreEqual("error", autoHandoff.Status);
        Assert.IsNull((await Db.AcquisitionTasks.SingleAsync()).WorkflowRunId);
    }

    [TestMethod]
    public async Task ConcurrentCatchUpConfirmationsCreateOneDurableHandoff()
    {
        var saved = await Saved();
        var input = new PostAcquisitionCatchUpConfirmInput([await CatchUpSelection(saved.Post.Id)]);
        var responses = await Task.WhenAll(Worker.ConfirmSavedResultsAsync(input, default), Worker.ConfirmSavedResultsAsync(input, default));
        Assert.AreEqual(1, responses.Sum(r => r.AcceptedCount)); Assert.AreEqual(1, responses.Sum(r => r.ExistingCount));
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync());
        Assert.AreEqual(0, await Db.AcquisitionTasks.CountAsync());
    }

    [TestMethod]
    public async Task LegacyDuplicateHandoffRecordsRemainExistingAndDoNotBreakTheWholePreview()
    {
        var saved = await Saved();
        // Only this disposable test database simulates a legacy store without the unique index.
        await Db.Database.ExecuteSqlRawAsync("DROP INDEX IX_AcquisitionHandoffs_SourceModule_SourceKind_SourceId_SourceRevision");
        var now = DateTime.UtcNow;
        foreach (var index in new[] {0, 1}) Db.AcquisitionHandoffs.Add(new AcquisitionHandoffDbModel
        {
            Id = "legacy-" + index, SourceModule = "postParser", SourceKind = "task",
            SourceId = saved.Post.Id.ToString(), SourceRevision = saved.Post.Revision.ToString(),
            ProducerRunId = saved.Run.Id, Status = "error", CreatedAt = now, UpdatedAt = now.AddSeconds(index)
        });
        await Db.SaveChangesAsync();
        var preview = await Worker.PrepareSavedResultsAsync(new([saved.Post.Id]), default);
        Assert.AreEqual(1, preview.ExistingCount);
        Assert.AreEqual("HANDOFF_MULTIPLE_RECORDS", preview.Items.Single().Code);
        Assert.AreEqual("legacy-1", preview.Items.Single().HandoffId);
        var confirmed = await Worker.ConfirmSavedResultsAsync(new([preview.Items.Single().Selection!]), default);
        Assert.AreEqual(1, confirmed.ExistingCount); Assert.AreEqual(0, confirmed.AcceptedCount);
        Assert.AreEqual(2, await Db.AcquisitionHandoffs.CountAsync());
    }

    [TestMethod]
    public async Task CompletionObserverRecognizesSavedResultReceiptBySourceInsteadOfLegacyId()
    {
        var saved = await Saved();
        await Worker.ConfirmSavedResultsAsync(new([await CatchUpSelection(saved.Post.Id)]), default);
        await Observer.StageAsync(saved.Run, saved.Input, default); await Db.SaveChangesAsync();
        Assert.AreEqual(1, await Db.AcquisitionHandoffs.CountAsync());
    }

    [TestMethod]
    public async Task CatchUpPreservesExplicitSupplementAsDraftRatherThanPromotingItToMain()
    {
        var saved = await Saved(two: false);
        var info = PostParserSavedResults.ReadDownloadInfo(saved.Post.Results!);
        info.Groups[0] = info.Groups[0] with {Kind = "tool"};
        saved.Post.Results = JsonSerializer.Serialize(new Dictionary<string, PostDownloadInfo> {{"DownloadInfo", info}}, JsonSerializerOptions.Web);
        await Db.SaveChangesAsync();
        await Worker.ConfirmSavedResultsAsync(new([await CatchUpSelection(saved.Post.Id)]), default);
        await Worker.ProcessNextAsync(default);
        var handoff = await Db.AcquisitionHandoffs.SingleAsync();
        Assert.AreEqual("attention", handoff.Status); Assert.AreEqual("HANDOFF_NO_MAIN_SOURCE", handoff.Code);
        var target = await Db.AcquisitionTasks.SingleAsync();
        Assert.IsNull(target.WorkflowRunId); Assert.IsNull(target.PlanJson);
        var detail = await _services.GetRequiredService<AcquisitionPlanService>().GetAsync(target.Id, default);
        Assert.AreEqual("https://example.test/first.zip", detail!.DraftSources.Single().Url);
    }

    [TestMethod]
    public async Task CatchUpFreezesCurrentResolvedDirectoryAndRejectsInvalidSelectionsWithoutWrites()
    {
        var saved = await Saved();
        var selection = await CatchUpSelection(saved.Post.Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Worker.ConfirmSavedResultsAsync(new([selection, selection]), default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Worker.ConfirmSavedResultsAsync(new([selection with {ResultsHash = "bad"}]), default));
        Assert.AreEqual(0, await Db.AcquisitionHandoffs.CountAsync());
        await Worker.ConfirmSavedResultsAsync(new([selection]), default);
        _services.GetRequiredService<IBOptions<DownloaderGlobalOptions>>().Value.DefaultDownloadPath = Path.Combine(Path.GetTempPath(), "later");
        await Worker.ProcessNextAsync(default);
        Assert.AreEqual(Path.GetTempPath(), (await Db.AcquisitionTasks.SingleAsync()).TargetDirectory);
    }
}
