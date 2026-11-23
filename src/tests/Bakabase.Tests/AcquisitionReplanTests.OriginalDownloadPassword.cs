using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private sealed class SelectedArchivePasswordTool(string required) : IArchiveExtractionService
    {
        public List<string?[]> Candidates { get; } = [];
        public int Extractions { get; private set; }

        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            Candidates.Add(candidates.ToArray());
            var accepted = candidates.FirstOrDefault(p => p == required);
            return Task.FromResult(new ArchivePasswordProbe(accepted != null, accepted, [], "", "An archive password is required."));
        }

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Assert.IsTrue(request.Password == required);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "sample.txt"), "archive payload", ct);
            Extractions++;
            return new(true, request.Directory, "");
        }
    }

    [DataTestMethod]
    [DataRow("source-credential")]
    [DataRow("step-credential")]
    [DataRow("manual-credential")]
    public async Task Normal_acquisition_extracts_original_bytes_with_only_its_bound_password_candidates(string required)
    {
        var (task, old, raw) = await Failed(passwordProtectedDownload: true);
        var previous = AcquisitionPlanCompiler.Workset(task.Id, 1, task.Plan!, task.SourceRefs);
        var target = previous.Targets.Single() with {DownloadArtifactId = raw.Id, DownloadOperationId = raw.OperationId,
            DownloadDirectory = raw.RootDirectory, Files = raw.Files.ToList()};
        old.CurrentItemJson = WorkflowItemSnapshot.Capture(previous with {Targets = [target]});
        (await Db.Set<ArtifactOperationDbModel>().SingleAsync(o => o.Id == raw.OperationId)).InputFingerprint =
            DownloadArtifactCoordinator.Source(target);
        await Db.SaveChangesAsync();

        task = await BindCurrentStage(task, "processing", Path.Combine(_root, "private-stage"), taskAllocation: true);
        var revised = await Plans.ReplanAsync(task.Id, new(task.Task.PlanRevision, old.Id, "Process the original registered archive.",
            [new("main", new() {Requirement = "required", Steps = [new() {Id = "outer", Op = "extractArchive",
                Input = "download", Password = "step-credential"}]}, [raw.Id], ContinueFromArtifactId: raw.Id, InputMode: "currentDirectory")],
            EnableAdaptiveAssessment: false), default);
        var frozen = revised.Plan!;
        var compiled = AcquisitionPlanCompiler.Compile(frozen);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, frozen, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            PlanSnapshotJson = WorkflowRunPlans.Serialize(compiled),
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = revised.Task.PlanRevision.ToString()}]};
        Db.Add(run);
        await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));

        var tool = new SelectedArchivePasswordTool(required);
        var processor = new FileArtifactProcessor(Artifacts, new(tool), new(tool), () => Path.Combine(_root, "appdata"),
            _services.GetRequiredService<IServiceScopeFactory>());
        var services = new VolumeProcessingServices(_services, processor);
        WorkflowExecutionContext Context(int index, string? prompt = null) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = workset, ActivityConfigJson = compiled.Nodes[index].ConfigJson,
            Services = services, ResumePromptJson = prompt, Logger = NullLogger.Instance};
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var selected = (await Artifacts.GetArtifactAsync(frozen.Parts.Single().ContinueFrom!.ArtifactId))!;
        var bound = await AcquisitionFileProcessingEvidence.ReadAsync(Context(1), target.Id, target.SelectedSource!.Url, default);
        Assert.IsNotNull(bound);
        Task<string?> Candidate(ArtifactRecord candidate, CloudDownloadTarget part) =>
            AcquisitionFileProcessingEvidence.OriginalDownloadPasswordAsync(bound, part, candidate, Artifacts, default, Db, run.Id);
        Assert.IsTrue(await Candidate(selected, target) == "source-credential");
        Assert.IsNull(await Candidate(selected, target with {Id = "another-part"}));
        Assert.IsNull(await Candidate(selected with {ParentArtifactIds = [raw.Id, "another-parent"]}, target));
        Assert.IsNull(await Candidate(selected with {Fingerprint = "changed-fingerprint"}, target));
        Assert.IsNull(await Candidate(selected, target with {Sources = [target.SelectedSource with {Url = "https://example.test/another"}]}));
        var activity = new AcquisitionPlanProcessingActivity();
        var result = await activity.ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsTrue(tool.Candidates[0].SequenceEqual(["step-credential", "source-credential"]));
        if (required == "manual-credential")
        {
            Assert.IsNotNull(result.Suspension);
            Assert.AreEqual("PasswordUnknown", JsonDocument.Parse(result.Suspension.PromptJson!).RootElement.GetProperty("reason").GetString());
            var response = JsonSerializer.Serialize(new CloudFileProcessingSignal {TargetId = "main", Password = required}, WorkflowJson.Options);
            result = await activity.ResumeAsync(Context(1, result.Suspension.PromptJson), result.Replacement ?? continued.Replacement!, response, default);
            Assert.IsTrue(tool.Candidates.Last().SequenceEqual(["manual-credential", "step-credential", "source-credential"]));
        }
        Assert.IsNull(result.Suspension);
        Assert.AreEqual(1, tool.Extractions);
        var output = ((DownloadWorkset)result.Replacement!).Targets.Single();
        Assert.IsNotNull(output.ProcessedArtifactId);
        Assert.AreEqual("archive payload", await File.ReadAllTextAsync(output.ProcessedFiles.Single()));
        Assert.IsTrue(File.Exists(raw.Files.Single()));
    }
}
