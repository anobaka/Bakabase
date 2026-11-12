using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private readonly ContinuationArchives _continuationArchives = new();

    private async Task<ArtifactRecord> ProcessedInput(int runId, ArtifactRecord download)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "completed-first-step")).FullName;
        var file = Path.Combine(directory, "inner.r");
        await File.WriteAllTextAsync(file, "already extracted first layer");
        var artifact = await Artifacts.AdoptAsync(new("completed-step", "completed-step-intent", "files.process", download.Fingerprint),
            new(directory, [file], "processedFiles", true, [download.Id]), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", runId.ToString(), "main:process:s1", artifact.OperationId);
        return artifact;
    }

    private static AcquisitionPlanReplanInput ContinueInput(AcquisitionPlanTaskDetail task, int runId, string artifactId) =>
        new(task.Task.PlanRevision, runId, "The committed first-layer artifact is the verified input to the remaining extraction.",
            [new("main", new() {Requirement = "required", Steps =
                [new() {Id = "s2", Op = "extractArchive", Selector = "*.r"}]}, [artifactId], artifactId)]);

    [TestMethod]
    public async Task Continuation_runs_only_remaining_instruction_without_original_download_or_network_readiness()
    {
        var (task, old, download) = await Failed();
        var input = await ProcessedInput(old.Id, download);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, old.Id, input.Id), default);
        Assert.AreEqual(input.Id, revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId);
        Assert.AreEqual(old.Id, revised.PlanHistory.Last().PreviousRunId);
        Assert.AreEqual(input.Fingerprint, revised.PlanHistory.Last().ArtifactEvidence!.Single().Fingerprint);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new AcquisitionWorkflowRunMutationGuard(Db).EnsureCanMutateAsync(old.Id));
        File.Delete(download.Files.Single());
        // There are no download methods registered in this fixture. The local continuation must still be ready.
        var checkedPlan = await Plans.PreflightAsync(task.Id, default);
        Assert.IsFalse(checkedPlan.Issues.Any(i => i.Blocking));
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan);
        CollectionAssert.AreEqual(new[] {AcquisitionPlanContinueFilesActivity.ActivityKind,
            AcquisitionPlanProcessingActivity.ActivityKind, AssessFileArtifactDeliveryActivity.ActivityKind, "fs.publishArtifacts"}, compiled.Nodes.Select(n => n.Kind).ToArray());
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        var restored = (DownloadWorkset)(await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default)).Replacement!;
        Assert.IsNull(restored.Targets.Single().DownloadArtifactId, "A processed input must not impersonate a downloaded artifact.");
        Assert.IsTrue(await Artifacts.HasActiveConsumersAsync(input.OperationId));
        var outcome = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), restored, default);
        Assert.IsNull(outcome.Suspension, outcome.Suspension?.PromptJson);
        var processed = (DownloadWorkset)outcome.Replacement!;
        Assert.AreEqual(1, _continuationArchives.Extractions);
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(processed.Targets.Single().ProcessedFiles!.Single()));
        Assert.AreEqual("already extracted first layer", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId));
        Assert.AreEqual(WorkflowRunStatus.Failed, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).Status);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Continuation_rejects_unreviewed_or_unproven_download_artifacts_and_stale_files()
    {
        var (task, run, download) = await Failed();
        var input = await ProcessedInput(run.Id, download);
        var request = ContinueInput(task, run.Id, input.Id);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id,
            request with {Items = [request.Items[0] with {ContinueFromArtifactId = "unreviewed"}]}, default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Plans.ReplanAsync(task.Id, ContinueInput(task, run.Id, download.Id), default));
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        await File.WriteAllTextAsync(input.Files.Single(), "changed after review");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Plans.RunAsync(task.Id, revised.Task.PlanRevision, default));
        Assert.AreEqual(1, await Db.WorkflowRuns.CountAsync());
        Assert.AreEqual(0, _continuationArchives.Extractions);
    }

    [TestMethod]
    public async Task Continuation_node_rejects_a_run_owned_by_another_task()
    {
        var (task, old, download) = await Failed(); var input = await ProcessedInput(old.Id, download);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, old.Id, input.Id), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = (task.Id + 1).ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var payload = AcquisitionPlanCompiler.Workset(task.Id, 2, revised.Plan!, revised.SourceRefs);
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = payload, ActivityConfigJson = compiled.Nodes[0].ConfigJson, Services = _services, Logger = NullLogger.Instance};
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(context, payload, default));
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId));
    }

    [TestMethod]
    public async Task Continuation_releases_its_pin_when_runtime_verification_fails()
    {
        var (task, old, download) = await Failed(); var input = await ProcessedInput(old.Id, download);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, old.Id, input.Id), default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var run = new WorkflowRunDbModel {WorkflowDefinitionId = 0, Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        await File.WriteAllTextAsync(input.Files.Single(), "modified between scheduling and execution");
        var payload = AcquisitionPlanCompiler.Workset(task.Id, 2, revised.Plan!, revised.SourceRefs);
        var context = new WorkflowExecutionContext {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = payload, ActivityConfigJson = compiled.Nodes[0].ConfigJson, Services = _services, Logger = NullLogger.Instance};
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(context, payload, default));
        Assert.IsFalse(await Artifacts.HasActiveConsumersAsync(input.OperationId), "Even before the runner records failure, this node must release the pin it acquired.");
    }

    private sealed class ContinuationArchives : IArchiveExtractionService
    {
        public int Extractions { get; private set; }
        public string OutputFileName { get; set; } = "final.txt";
        public string ExpectedInput { get; set; } = "already extracted first layer";
        public Task<string?> ProbeFormatAsync(string file, CancellationToken ct) => Task.FromResult<string?>(".rar");
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords, Action<string>? output,
            CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Extractions++;
            Assert.AreEqual(ExpectedInput, await File.ReadAllTextAsync(request.Files.Single(), ct));
            await File.WriteAllTextAsync(Path.Combine(request.Directory, OutputFileName), "remaining extraction", ct);
            return new(true, request.Directory, "");
        }
    }
}
