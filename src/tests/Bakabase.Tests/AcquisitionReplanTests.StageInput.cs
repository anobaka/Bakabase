using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.Extensions.Logging.Abstractions;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private sealed class StageInputMaterializer : IFileArtifactMaterializer
    {
        internal readonly Dictionary<string, int> Copies = new();
        internal string? FailOnce;
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            Copies[Path.GetFileName(source)] = Copies.GetValueOrDefault(Path.GetFileName(source)) + 1;
            if (Path.GetFileName(source) == FailOnce)
            {
                FailOnce = null;
                await File.WriteAllBytesAsync(target, (await File.ReadAllBytesAsync(source, ct))[..3], ct);
                throw new IOException("Synthetic disk-full copy.");
            }
            File.Copy(source, target, false);
            return new(mode, new FileInfo(target).Length);
        }
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        {ct.ThrowIfCancellationRequested(); File.Move(source, target, false); return Task.FromResult(true);}
    }

    [DataTestMethod]
    [DataRow("auto")]
    [DataRow("copy")]
    public async Task Stage_input_copies_once_retains_download_and_reuses_canonical_result(string mode)
    {
        var (task, run, input) = await Failed();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "first-processing")).FullName;
        task = await BindCurrentStage(task, "processing", directory);
        var binding = task.Plan!.StageDirectories!.Parts["main"].Processing!;
        var row = await Db.AcquisitionTasks.FindAsync(task.Id);
        var unknown = JsonNode.Parse(row!.BuilderStateJson ?? "{}")!.AsObject(); unknown["futurePrivateOption"] = "preserved";
        row.BuilderStateJson = unknown.ToJsonString(); await Db.SaveChangesAsync();
        var materializer = new StageInputMaterializer();
        async Task<ArtifactRecord> Prepare() => await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts,
            new CompositeStorage(), _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, run.Id,
            "main", binding, input, materializer, _services.GetRequiredService<IServiceScopeFactory>(), default,
            new() {Materialization = mode, SourceCleanup = "keep"});
        var result = await Prepare();
        Assert.AreEqual(directory, result.RootDirectory); Assert.IsTrue(result.Managed);
        Assert.AreEqual(input.Fingerprint, result.Fingerprint);
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(result.Files.Single()));
        Assert.AreEqual(1, materializer.Copies.Values.Sum(), "Initialization makes one independent payload copy, then moves it into the visible stage.");
        Assert.AreEqual(result.Id, (await Prepare()).Id);
        Assert.AreEqual(1, materializer.Copies.Values.Sum());
        var saved = JsonNode.Parse((await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).BuilderStateJson!);
        Assert.AreEqual("preserved", saved!["futurePrivateOption"]!.GetValue<string>());
        Assert.IsNull(saved["processingStageInputId"]);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Original_processing_retry_rechecks_private_snapshot_and_foreign_references_before_dispatch()
    {
        var (task, run, input) = await Failed();
        var destination = Directory.CreateDirectory(Path.Combine(_root, "processing-retry")).FullName;
        task = await BindCurrentStage(task, "processing", destination);
        var plan = task.Plan!;
        var workset = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, plan, task.SourceRefs);
        var target = workset.Targets.Single() with {DownloadArtifactId = input.Id, DownloadOperationId = input.OperationId,
            DownloadDirectory = input.RootDirectory, Files = input.Files.ToList()};
        run.ErrorMessage = "STAGE_COMMIT_REFERENCED";
        var compiled = AcquisitionPlanCompiler.Compile(plan);
        run.PlanSnapshotJson = WorkflowRunPlans.Serialize(compiled);
        run.CurrentStepIndex = compiled.Nodes.ToList().FindIndex(n => n.Kind == AcquisitionPlanProcessingActivity.ActivityKind);
        run.CurrentItemJson = WorkflowItemSnapshot.Capture(workset with {Targets = [target]});
        var related = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running,
            CurrentItemJson = JsonSerializer.Serialize(new {input = destination})};
        Db.Add(related); await Db.SaveChangesAsync();
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var storage = new CompositeStorage();
        var fault = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, storage, workspaces, task.Id, run.Id, "main",
                target.StageDirectories!.Processing!, input, new StageInputMaterializer(),
                _services.GetRequiredService<IServiceScopeFactory>(), default));
        Assert.AreEqual("STAGE_COMMIT_REFERENCED", fault.Message);
        var diagnostics = new AcquisitionRecoveryDiagnostics(Db, storage, Artifacts, workspaces: workspaces);
        var row = await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        async Task<AcquisitionRecoveryDiagnosis> Check() => await diagnostics.DiagnoseAsync(row, run, default);
        Assert.AreEqual("RECOVERY_PROCESSING_FILES_REFERENCED", (await Check()).Row.ReasonCode);
        Db.Remove(related); await Db.SaveChangesAsync();
        var ready = await Check();
        Assert.AreEqual("RECOVERY_STAGE_INPUT_READY", ready.Row.ReasonCode);
        Assert.AreEqual("retry", ready.Row.Action);
        Assert.AreEqual(run.Id, ready.Row.WorkflowRunId);
        Assert.AreEqual("main", JsonSerializer.Deserialize<CloudFileProcessingSignal>(ready.SignalJson, WorkflowJson.Options)!.TargetId);
        File.Delete(input.Files.Single());
        Assert.AreEqual("RECOVERY_FILES_UNVERIFIED", (await Check()).Row.ReasonCode);
    }

    [TestMethod]
    public async Task Stage_input_uses_nonempty_current_directory_without_importing_old_download_or_claiming_ownership()
    {
        var (task, run, input) = await Failed();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "manual-processing")).FullName;
        var current = Path.Combine(directory, "current.key"); await File.WriteAllTextAsync(current, "manually supplied");
        task = await BindCurrentStage(task, "processing", directory);
        var materializer = new StageInputMaterializer();
        var result = await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, run.Id, "main",
            task.Plan!.StageDirectories!.Parts["main"].Processing!, input, materializer,
            _services.GetRequiredService<IServiceScopeFactory>(), default);
        CollectionAssert.AreEqual(new[] {current}, result.Files.ToArray());
        Assert.AreEqual("currentFiles", result.Kind); Assert.IsFalse(result.Managed);
        Assert.AreEqual(0, materializer.Copies.Count); Assert.IsTrue(File.Exists(input.Files.Single()));
        Assert.IsFalse(File.Exists(Path.Combine(directory, "original.bin")));
        Assert.IsTrue(await AcquisitionCurrentDirectoryFiles.IsOwnedAsync(Db, Artifacts, result, task.Id, run.Id, "main", default));
        var nextBoundary = await AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts, new CompositeStorage(),
            _services.GetRequiredService<FileProcessingWorkspaceResolver>(), task.Id, run.Id, "main",
            task.Plan!.StageDirectories!.Parts["main"].Processing!, input, materializer,
            _services.GetRequiredService<IServiceScopeFactory>(), default);
        Assert.AreEqual(result.Id, nextBoundary.Id, "A second boundary must reuse the captured input identity.");
        Assert.AreEqual(0, materializer.Copies.Count);
    }

    [TestMethod]
    public async Task Stage_input_keeps_explicit_download_selection_across_boundaries_instead_of_switching_to_processing()
    {
        var (task, old, original) = await Failed();
        var download = Directory.CreateDirectory(Path.Combine(_root, "explicit-download")).FullName;
        var archive = Path.Combine(download, "package.bin"); await File.WriteAllTextAsync(archive, "original bytes");
        var processing = Directory.CreateDirectory(Path.Combine(_root, "explicit-processing")).FullName;
        var unrelated = Path.Combine(processing, "manual.key"); await File.WriteAllTextAsync(unrelated, "other stage contents");
        task = await BindCurrentStage(task, "download", download);
        task = await BindCurrentStage(task, "processing", processing);
        var request = new AcquisitionPlanReplanInput(task.Task.PlanRevision, old.Id, "Two explicit remaining instructions.",
            [new("main", new() {Requirement = "required", Steps =
                [new() {Id = "open", Op = "extractArchive", Selector = "*.bin"},
                    new() {Id = "rename", Op = "renameFile", Input = "open", Selector = "final.txt", TargetName = "renamed.txt"}]},
                [], InputMode: "stageDirectory", InputStage: "download")], EnableAdaptiveAssessment: false);
        var revised = await Plans.ReplanAsync(task.Id, request, default);
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan!);
        var payload = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan!, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, StartedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(),
                Revision = revised.Task.PlanRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)}]};
        Db.Add(run); await Db.SaveChangesAsync();
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.WorkflowRunId, run.Id));
        WorkflowExecutionContext Context(int node) => new() {RunId = run.Id, WorkflowDefinitionId = 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = payload, ActivityConfigJson = compiled.Nodes[node].ConfigJson,
            Services = _services, Logger = NullLogger.Instance};
        _continuationArchives.ExpectedInput = "original bytes";
        var continued = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), payload, default);
        var first = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), continued.Replacement!, default);
        Assert.IsNull(first.Suspension, first.Suspension?.PromptJson);
        var selected = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        var retained = await AcquisitionProcessingStageInput.PrepareAsync(Context(1),
            ((DownloadWorkset)first.Replacement!).Targets.Single(), selected, default);
        Assert.AreEqual(selected.Id, retained.Id); Assert.AreEqual(download, retained.RootDirectory);
        var firstOutput = (await Artifacts.GetArtifactAsync(((DownloadWorkset)first.Replacement!).Targets.Single().ProcessedArtifactId!))!;
        Assert.AreEqual(selected.Id, JsonSerializer.Deserialize<FileArtifactProcessingMetadata>(firstOutput.MetadataJson!, WorkflowJson.Options)!.InputArtifactId);
        // Explicit stage selection keeps its identity across boundaries, including
        // recovery from the verified private input after the selected source disappears.
        File.Delete(archive);
        var second = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(2), first.Replacement!, default);
        Assert.IsNull(second.Suspension, second.Suspension?.PromptJson);
        var output = ((DownloadWorkset)second.Replacement!).Targets.Single();
        Assert.AreEqual(processing, output.ProcessedDirectory);
        Assert.AreEqual("renamed.txt", Path.GetFileName(output.ProcessedFiles!.Single()));
        Assert.AreEqual("remaining extraction", await File.ReadAllTextAsync(output.ProcessedFiles.Single()));
        Assert.AreEqual(1, _continuationArchives.Extractions); Assert.AreEqual(2, output.ProcessingStepArtifacts.Count);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync()); Assert.IsTrue(File.Exists(original.Files.Single()));
        Assert.AreEqual("other stage contents", await File.ReadAllTextAsync(unrelated));
    }

    [TestMethod]
    public async Task Stage_input_resumes_an_owned_partial_copy_without_recopying_complete_members()
    {
        var (task, run, _) = await Failed();
        var original = Directory.CreateDirectory(Path.Combine(_root, "two-downloads")).FullName;
        var first = Path.Combine(original, "one.bin"); var second = Path.Combine(original, "two.bin");
        await File.WriteAllTextAsync(first, "one original payload"); await File.WriteAllTextAsync(second, "two original payload");
        var input = await Artifacts.AdoptAsync(new("two-downloads", "two-downloads", "download", "input"),
            new(original, [first, second], "download", false), "test");
        await new ArtifactOperationBindingService(Db).BindAsync("workflow", run.Id.ToString(), "main:artifact.download-two", input.OperationId);
        var directory = Directory.CreateDirectory(Path.Combine(_root, "resumable-processing")).FullName;
        task = await BindCurrentStage(task, "processing", directory);
        var materializer = new StageInputMaterializer {FailOnce = "two.bin"};
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts,
            new CompositeStorage(), workspaces, task.Id, run.Id, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!,
            input, materializer, _services.GetRequiredService<IServiceScopeFactory>(), default));
        var pending = JsonNode.Parse((await Db.AcquisitionTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id)).BuilderStateJson!);
        Assert.IsNotNull(pending!["processingStageInputId"]);
        Assert.IsFalse(File.Exists(Path.Combine(directory, "one.bin")), "A partial initialization is not exposed as the current input.");
        await AcquisitionProcessingStageInput.RecoverAsync(Db, Artifacts, new CompositeStorage(), workspaces,
            task.Id, materializer, _services.GetRequiredService<IServiceScopeFactory>(), default);
        Assert.AreEqual("one original payload", await File.ReadAllTextAsync(Path.Combine(directory, "one.bin")));
        Assert.AreEqual("two original payload", await File.ReadAllTextAsync(Path.Combine(directory, "two.bin")));
        Assert.AreEqual(1, materializer.Copies["one.bin"]); Assert.AreEqual(2, materializer.Copies["two.bin"]);
        Assert.IsTrue(File.Exists(first)); Assert.IsTrue(File.Exists(second));
    }

    [TestMethod]
    public async Task Stage_input_does_not_overwrite_a_file_added_during_initialization()
    {
        var (task, run, input) = await Failed();
        var directory = Directory.CreateDirectory(Path.Combine(_root, "conflicting-processing")).FullName;
        task = await BindCurrentStage(task, "processing", directory);
        var materializer = new StageInputMaterializer {FailOnce = "original.bin"};
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionProcessingStageInput.PrepareCoreAsync(Db, Artifacts,
            new CompositeStorage(), workspaces, task.Id, run.Id, "main", task.Plan!.StageDirectories!.Parts["main"].Processing!,
            input, materializer, _services.GetRequiredService<IServiceScopeFactory>(), default));
        var manual = Path.Combine(directory, "original.bin"); await File.WriteAllTextAsync(manual, "new user contents");
        await Assert.ThrowsExactlyAsync<IOException>(() => AcquisitionProcessingStageInput.RecoverAsync(Db, Artifacts,
            new CompositeStorage(), workspaces, task.Id, materializer, _services.GetRequiredService<IServiceScopeFactory>(), default));
        Assert.AreEqual("new user contents", await File.ReadAllTextAsync(manual));
        Assert.AreEqual("original bytes", await File.ReadAllTextAsync(input.Files.Single()));
    }
}
