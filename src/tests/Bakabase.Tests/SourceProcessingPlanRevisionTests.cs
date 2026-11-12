using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class SourceProcessingPlanRevisionTests
{
    private IServiceProvider _services = null!;
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private SourceProcessingPlanService Revisions => _services.GetRequiredService<SourceProcessingPlanService>();
    private AcquisitionPlanService Plans => _services.GetRequiredService<AcquisitionPlanService>();
    private const string Quote = "Extract the archive with the saved password.";
    private static FileProcessingPlan Approved => new() {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]};

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider(services =>
    {
        services.RemoveAll<DownloadMethodRegistry>();
        services.AddScoped(_ => new DownloadMethodRegistry([], [], null!, null!));
    });
    [TestCleanup] public void Cleanup() => (_services as IDisposable)?.Dispose();

    private async Task<PostParserTaskDbModel> Saved(string? body = null)
    {
        var task = new PostParserTaskDbModel
        {
            Link = "https://example.test/post", Title = "Original", Revision = 2, ParsingState = "complete",
            ContentSnapshotJson = JsonSerializer.Serialize(new {mainHtml = body ?? "<p>" + Quote + "</p>",
                comments = new[] {new {html = "<p>Purchased comment: A&amp;B may be unpacked.</p>"}}}),
            Results = JsonSerializer.Serialize(new {DownloadInfo = new {isComplete = true, resources = new[]
            {
                new {link = "https://example.test/a.zip", password = "saved-a", extraction = new {requirement = "required", steps = Array.Empty<object>(), evidence = new[] {"AI-only statement"}}},
                new {link = "https://example.test/b.zip", password = "saved-b", extraction = new {requirement = "required", steps = Array.Empty<object>(), evidence = new[] {"AI-only statement"}}}
            }}})
        };
        Db.PostParserTasks.Add(task); await Db.SaveChangesAsync(); return task;
    }
    private static WorkflowSourceRef Source(PostParserTaskDbModel task, string index = "0") => new("postParser", "task", task.Id.ToString(), "2", index);
    private async Task<SourceProcessingPlanView> Approve(WorkflowSourceRef source, string quote = Quote)
    {
        var current = await Revisions.GetAsync(source, default);
        return await Revisions.ReviseAsync(new(source, current.SourceHash, current.Revision, Approved,
            [new("extract", quote)], "Reviewed saved source instructions"), default);
    }
    private static AcquisitionPlan Initial(WorkflowSourceRef source, bool legacy = false) => new()
    {
        Title = "Prepared", OutputDirectory = Path.GetTempPath(), Parts = [new() {Id = "main", SelectedSourceId = "0",
            Sources = [new() {Id = "0", Url = "https://example.test/a.zip", Kind = "http"}],
            ProcessingSource = legacy ? null : source, Processing = new() {Requirement = "required"}}]
    };

    [TestMethod]
    public async Task Append_preserves_original_result_and_rejects_stale_head()
    {
        var task = await Saved(); var original = task.Results; var source = Source(task);
        var before = await Revisions.GetAsync(source, default);
        var after = await Approve(source);
        Assert.AreEqual(0, after.OriginalPlan!.Steps.Count); Assert.AreEqual(1, after.EffectivePlan!.Steps.Count);
        Assert.AreEqual(1, after.EffectiveRevision); Assert.AreEqual(1, after.History.Count);
        Assert.AreEqual(original, (await Db.PostParserTasks.AsNoTracking().SingleAsync()).Results);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Revisions.ReviseAsync(new(source,
            before.SourceHash, 0, Approved, [new("extract", Quote)], "Competing edit"), default));
        Assert.AreEqual(1, await Db.ProcessingPlanRevisions.CountAsync());
    }

    [DataTestMethod]
    [DataRow("AI-only statement")]
    [DataRow("A quote from a different post")]
    public async Task Evidence_must_exist_in_saved_body_not_AI_evidence(string quote)
    {
        var task = await Saved();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Approve(Source(task), quote));
        Assert.AreEqual(0, await Db.ProcessingPlanRevisions.CountAsync());
    }

    [TestMethod]
    public async Task Saved_purchased_comments_and_HTML_entities_are_eligible_evidence()
    {
        var task = await Saved();
        var result = await Approve(Source(task), "Purchased comment: A&B may be unpacked.");
        Assert.AreEqual(1, result.Revision);
    }

    [TestMethod]
    public async Task Credential_or_body_changes_invalidate_effective_overlay_without_deleting_history()
    {
        var task = await Saved(); var source = Source(task); var approved = await Approve(source);
        task.Results = task.Results!.Replace("saved-a", "different-password"); await Db.SaveChangesAsync();
        var changed = await Revisions.GetAsync(source, default);
        Assert.AreNotEqual(approved.SourceHash, changed.SourceHash);
        Assert.AreEqual(0, changed.EffectiveRevision); Assert.AreEqual(0, changed.EffectivePlan!.Steps.Count);
        Assert.AreEqual(1, changed.Revision); Assert.IsTrue(changed.HasStaleRevisions);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Revisions.ReviseAsync(new(source,
            approved.SourceHash, 1, Approved, [new("extract", Quote)], "Stale source"), default));
    }

    [TestMethod]
    public async Task Preflight_and_new_acquisition_share_overlay_and_explicit_override_wins()
    {
        var task = await Saved(); var source = Source(task); var approved = await Approve(source);
        var input = new PostDownloadIntentRequest {TaskId = task.Id, Revision = 2, ResourceIndices = [0]};
        var preflight = await _services.GetRequiredService<PostDownloadIntentAdapter>().PreflightAsync(input, Path.GetTempPath(), default);
        Assert.AreEqual(1, JsonSerializer.Deserialize<FileProcessingPlan>(preflight.Candidates[0].Candidate.ExtractionPlanJson!, WorkflowJson.Options)!.Steps.Count);
        Assert.AreEqual(0, JsonSerializer.Deserialize<FileProcessingPlan>(preflight.Candidates[1].Candidate.ExtractionPlanJson!, WorkflowJson.Options)!.Steps.Count);
        var created = await _services.GetRequiredService<PostAcquisitionPlanAdapter>().CreateAsync(input, Path.GetTempPath(), default);
        var detail = (await Plans.GetAsync(created.AcquisitionTaskIds.Single(), default))!;
        Assert.AreEqual(source, detail.Plan!.Parts[0].ProcessingSource);
        Assert.AreEqual(approved.History[0].Id, detail.Plan.Parts[0].AppliedProcessingRevisionId);
        var overridden = await _services.GetRequiredService<PostDownloadIntentAdapter>().PreflightAsync(input with
        {CandidateProcessingPlans = new() {{"0", "{\"requirement\":\"notRequired\",\"steps\":[]}"}}}, Path.GetTempPath(), default);
        Assert.AreEqual("notRequired", JsonSerializer.Deserialize<FileProcessingPlan>(overridden.Candidates[0].Candidate.ExtractionPlanJson!, WorkflowJson.Options)!.Requirement);
        Assert.AreEqual(0, await Db.WorkflowRuns.CountAsync());
    }

    [TestMethod]
    public async Task Existing_task_changes_only_on_explicit_CAS_apply_and_retains_plan_history()
    {
        var task = await Saved(); var source = Source(task);
        var prepared = await Plans.CreatePreparedPlanAsync(Initial(source, legacy: true), [source]);
        var approved = await Approve(source);
        Assert.AreEqual(0, (await Plans.GetAsync(prepared.Id, default))!.Plan!.Parts[0].Processing!.Steps.Count);
        var input = new ApplyProcessingPlanRevisionsInput(prepared.Task.PlanRevision, [new("main", source, approved.History[0].Id)]);
        var applied = await Plans.ApplyProcessingRevisionsAsync(prepared.Id, input, default);
        Assert.AreEqual(2, applied.Task.PlanRevision); Assert.AreEqual(2, applied.PlanHistory.Count);
        Assert.AreEqual(0, applied.PlanHistory[0].Plan.Parts[0].Processing!.Steps.Count);
        Assert.AreEqual(1, applied.Plan!.Parts[0].Processing!.Steps.Count);
        Assert.AreEqual(approved.History[0].Id, applied.Plan.Parts[0].AppliedProcessingRevisionId);
        Assert.IsNull(applied.Task.WorkflowRunId);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ApplyProcessingRevisionsAsync(prepared.Id, input, default));
    }

    [TestMethod]
    public async Task Apply_rejects_unrelated_source_and_started_task_and_runtime_does_not_show_stale_plan_warning()
    {
        var task = await Saved(); var source = Source(task); var approved = await Approve(source);
        var prepared = await Plans.CreatePreparedPlanAsync(Initial(source), [source]);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ApplyProcessingRevisionsAsync(prepared.Id,
            new(1, [new("main", Source(task, "1"), approved.History[0].Id)]), default));
        var run = new WorkflowRunDbModel {StartedAt = DateTime.Now, Status = WorkflowRunStatus.Success, WorkflowDefinitionId = 0};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var row = await Db.AcquisitionTasks.SingleAsync(t => t.Id == prepared.Id); row.WorkflowRunId = run.Id; await Db.SaveChangesAsync();
        var detail = (await Plans.GetAsync(row.Id, default))!;
        Assert.IsFalse(detail.Issues.Any(i => i.Code == "PROCESSING_STEPS_MISSING"));
        Assert.AreEqual(0, detail.Plan!.Parts[0].Processing!.Steps.Count, "Runtime correction does not rewrite the frozen plan.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.ApplyProcessingRevisionsAsync(prepared.Id,
            new(1, [new("main", source, approved.History[0].Id)]), default));
    }

    [TestMethod]
    public async Task Active_producer_or_incomplete_source_identity_cannot_be_revised()
    {
        var task = await Saved();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Revisions.GetAsync(Source(task) with {ResultKey = null}, default));
        var run = new WorkflowRunDbModel {StartedAt = DateTime.Now, Status = WorkflowRunStatus.Running, WorkflowDefinitionId = 0};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync(); task.WorkflowRunId = run.Id; await Db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Revisions.GetAsync(Source(task), default));
    }

    [TestMethod]
    public async Task Plain_pasted_text_preserves_literal_angle_brackets_and_entity_looking_text()
    {
        var task = await Saved();
        const string quote = "Extract <archive>.zip using the documented &amp; value.";
        task.ContentSnapshotJson = JsonSerializer.Serialize(new {scope = "pastedText", mainHtml = quote});
        await Db.SaveChangesAsync();
        Assert.AreEqual(1, (await Approve(Source(task), quote)).Revision);
    }

    [TestMethod]
    public async Task Reparsed_source_retains_readable_audit_but_cannot_apply_old_revision()
    {
        var task = await Saved(); var source = Source(task); var approved = await Approve(source);
        task.Revision++; await Db.SaveChangesAsync();
        var history = await Revisions.GetAsync(source, default);
        Assert.IsFalse(history.SourceAvailable); Assert.AreEqual("PROCESSING_PLAN_SOURCE_CHANGED", history.SourceProblem);
        Assert.AreEqual(1, history.History.Count); Assert.AreEqual(0, history.EffectiveRevision);
        Assert.IsNull(history.EffectivePlan); Assert.AreEqual("", history.SourceHash);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Revisions.RequireCurrentRevisionAsync(source, approved.History[0].Id, default));
    }

    [TestMethod]
    public async Task Local_processing_uses_effective_plan_without_rewriting_saved_results()
    {
        var task = await Saved(); var original = task.Results; await Approve(Source(task));
        await _services.GetRequiredService<Bakabase.Service.Components.FileProcessing.FileProcessingWorkflowSeeder>().SeedAsync();
        var definition = await Db.WorkflowDefinitions.SingleAsync(d => d.IsBuiltin && d.TriggerKind == "fs.processingPlan");
        var directory = Path.Combine(Path.GetTempPath(), "revision-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "archive.zip"); await File.WriteAllTextAsync(file, "fixture");
            var result = await _services.GetRequiredService<PostLocalProcessingService>().StartAsync(new()
            {TaskId = task.Id, Revision = task.Revision, ResourceIndex = 0, WorkflowDefinitionId = definition.Id, Files = [file]}, directory, default);
            var row = await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == result.Id);
            var payload = JsonSerializer.Deserialize<Bakabase.Service.Components.FileProcessing.FileProcessingPlanPayload>(row.PayloadJson!, WorkflowJson.Options)!;
            var plan = JsonSerializer.Deserialize<FileProcessingPlan>(payload.ExtractionPlanJson, WorkflowJson.Options)!;
            Assert.AreEqual("extractArchive", plan.Steps.Single().Op);
            Assert.AreEqual("saved-a", plan.Steps.Single().Password, "Source archive password remains the explicit fallback.");
            Assert.AreEqual(original, (await Db.PostParserTasks.AsNoTracking().SingleAsync()).Results);
        }
        finally {Directory.Delete(directory, true);}
    }

    [TestMethod]
    public async Task Concurrent_writers_with_same_expected_head_commit_one_revision()
    {
        var task = await Saved(); var source = Source(task); var before = await Revisions.GetAsync(source, default);
        async Task<bool> Write()
        {
            using var scope = _services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<SourceProcessingPlanService>().ReviseAsync(new(source,
                    before.SourceHash, 0, Approved, [new("extract", Quote)], "Reviewed concurrently"), default);
                return true;
            }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(Task.Run(Write), Task.Run(Write));
        Assert.AreEqual(1, results.Count(r => r));
        Assert.AreEqual(1, await Db.ProcessingPlanRevisions.CountAsync());
    }
}
