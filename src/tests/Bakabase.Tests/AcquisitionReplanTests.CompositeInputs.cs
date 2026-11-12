using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    private static AcquisitionPlanReplanInput CompositeRequest(AcquisitionPlanTaskDetail task, int runId, ArtifactRecord[] inputs) =>
        new(task.Task.PlanRevision, runId, "The source identifies multiple existing parts as one processing input.",
            [new("main", new() {Requirement = "required", Steps = [new() {Id = "rename", Op = "renameExtension", Selector = "*.bin", Extension = ".dat"}]}, inputs.Select(a => a.Id).ToArray())],
            UseCurrentStorageDefaults: true, CompositeInputs: task.Plan!.Parts.Select((p, i) => new AcquisitionPlanCompositeSource(p.Id, inputs[i].Id)).ToArray());

    [TestMethod]
    public async Task Composite_continuation_preserves_exact_parent_ownership_and_shared_root_siblings_without_redownload()
    {
        var (task, old, inputs) = await StoppedDownloads(2);
        var sibling = Path.Combine(_root, "unrelated-license.key"); await File.WriteAllTextAsync(sibling, "must remain");
        var plans = CurrentStoragePlans(Directory.CreateDirectory(Path.Combine(_root, "new-work")).FullName,
            Directory.CreateDirectory(Path.Combine(_root, "new-delivery")).FullName);
        var frozen = old.CurrentItemJson;
        var revised = await plans.ReplanAsync(task.Id, CompositeRequest(task, old.Id, inputs), default);
        Assert.AreEqual(1, revised.Plan!.Parts.Count);
        var reference = revised.Plan.Parts.Single().ContinueFrom!;
        var composite = (await Artifacts.GetArtifactAsync(reference.ArtifactId))!;
        Assert.AreEqual("compositeFiles", composite.Kind); Assert.IsFalse(composite.Managed);
        Assert.AreEqual(_root, composite.RootDirectory);
        CollectionAssert.AreEquivalent(inputs.SelectMany(a => a.Files).ToArray(), composite.Files.ToArray());
        Assert.IsFalse(composite.Files.Contains(sibling));
        Assert.IsNull(revised.Plan.Parts.Single().StoragePolicy?.SourceCleanup);
        Assert.AreEqual(JsonSerializer.Serialize(task.PlanHistory[0]), JsonSerializer.Serialize(revised.PlanHistory[0]));
        Assert.AreEqual(frozen, (await Db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == old.Id)).CurrentItemJson);
        var proof = JsonSerializer.Deserialize<FileCompositionProof>(composite.MetadataJson!, WorkflowJson.Options)!;
        foreach (var input in inputs)
            Assert.IsTrue(FileDispositionProofs.ValidFor(proof.Parents.Single(p => p.InputArtifactId == input.Id), input, composite));
        var compiled = AcquisitionPlanCompiler.Compile(revised.Plan);
        Assert.IsTrue(compiled.Nodes.All(n => n.Kind is "acquisition.plan.continueFiles" or "acquisition.plan.process" or "fs.publishArtifacts"));
        var workset = AcquisitionPlanCompiler.Workset(task.Id, revised.Task.PlanRevision, revised.Plan, revised.SourceRefs);
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Running, Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        WorkflowExecutionContext Context(int n) => new() {RunId = run.Id, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = compiled.Nodes[n].ConfigJson, Services = _services, Logger = NullLogger.Instance};
        var restored = await new AcquisitionPlanContinueFilesActivity().ProcessItemAsync(Context(0), workset, default);
        var processed = await new AcquisitionPlanProcessingActivity().ProcessItemAsync(Context(1), restored.Replacement!, default);
        Assert.IsNull(processed.Suspension, processed.Suspension?.PromptJson);
        var delivered = await new PublishFileArtifactsActivity().ProcessItemAsync(Context(2), processed.Replacement!, default);
        Assert.IsNull(delivered.Suspension, delivered.Suspension?.PromptJson);
        var output = ((DownloadWorkset)delivered.Replacement!).Targets.Single();
        Assert.AreEqual(2, output.PublishedFiles!.Count);
        foreach (var input in inputs)
            Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(Artifacts, input, [output.PublishedArtifactId!])).Complete);
        Assert.IsTrue(inputs.SelectMany(a => a.Files).All(File.Exists));
        Assert.AreEqual("must remain", await File.ReadAllTextAsync(sibling));
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
        var cleanup = new ArchiveQuarantineService(Artifacts, Path.Combine(_root, "appdata"), new CompositeStorage());
        Assert.IsFalse((await cleanup.QuarantineAsync(composite.Id, [output.PublishedArtifactId!])).Completed);
        Assert.IsTrue(Directory.Exists(_root)); Assert.IsTrue(File.Exists(sibling));
    }

    [DataTestMethod]
    [DataRow("swapped-owner")]
    [DataRow("missing-part")]
    [DataRow("duplicate-artifact")]
    [DataRow("changed-file")]
    [DataRow("foreign-task")]
    public async Task Composite_continuation_rejects_inexact_provenance_without_changing_task_or_files(string fault)
    {
        var (task, run, inputs) = await StoppedDownloads(2); var request = CompositeRequest(task, run.Id, inputs);
        if (fault == "swapped-owner") request = request with {CompositeInputs = [new("main", inputs[1].Id), new("second", inputs[0].Id)]};
        if (fault == "missing-part") request = request with {CompositeInputs = [request.CompositeInputs![0]]};
        if (fault == "duplicate-artifact") request = request with {CompositeInputs = [new("main", inputs[0].Id), new("second", inputs[0].Id)]};
        if (fault == "changed-file") await File.WriteAllTextAsync(inputs[1].Files[0], "different");
        if (fault == "foreign-task") { (await Db.Set<WorkflowRunSourceDbModel>().SingleAsync()).EntityId = "987654"; await Db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<Exception>(() => Plans.ReplanAsync(task.Id, request, default));
        Assert.AreEqual(1, (await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanRevision);
        Assert.AreEqual(2, JsonSerializer.Deserialize<AcquisitionPlan>((await Db.AcquisitionTasks.AsNoTracking().SingleAsync()).PlanJson!, WorkflowJson.Options)!.Parts.Count);
        Assert.AreEqual(0, await Db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Composite_projection_rejects_overlapping_registered_files_even_with_identical_bytes()
    {
        var (task, run, inputs) = await StoppedDownloads(2);
        var second = inputs[0] with {Id = "another-registration"};
        var provenance = new AcquisitionCompositeProvenance(1, task.Id, run.Id, "main",
            [new("main", new(task.Id, run.Id, inputs[0].Id, inputs[0].OperationId, inputs[0].Fingerprint)),
             new("second", new(task.Id, run.Id, second.Id, second.OperationId, second.Fingerprint))]);
        Assert.ThrowsExactly<ArgumentException>(() => AcquisitionCompositeFiles.Project([inputs[0], second], provenance));
    }

    [TestMethod]
    public async Task Composite_coverage_requires_each_parent_proof_and_rejects_missing_donor_receipts()
    {
        var (task, run, inputs) = await StoppedDownloads(2);
        var revised = await Plans.ReplanAsync(task.Id, CompositeRequest(task, run.Id, inputs), default);
        var composite = (await Artifacts.GetArtifactAsync(revised.Plan!.Parts.Single().ContinueFrom!.ArtifactId))!;
        Assert.IsTrue((await ArtifactFileCoverage.EvaluateAsync(Artifacts, inputs[1], [composite.Id])).Complete);
        var metadata = JsonSerializer.Deserialize<FileCompositionProof>(composite.MetadataJson!, WorkflowJson.Options)!;
        var row = await Db.Set<ArtifactDbModel>().SingleAsync(a => a.Id == composite.Id);
        row.MetadataJson = JsonSerializer.Serialize(metadata with {Parents = [metadata.Parents.Single(p => p.InputArtifactId == inputs[0].Id)]}, WorkflowJson.Options);
        await Db.SaveChangesAsync();
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(Artifacts, inputs[1], [composite.Id])).Complete);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => AcquisitionPlanArtifactInputGuard.RequireAsync(Db, Artifacts,
            task.Id, "main", revised.Plan.Parts.Single().ContinueFrom!, default));
        Assert.IsTrue(inputs.SelectMany(a => a.Files).All(File.Exists));
    }

    [TestMethod]
    public async Task Composite_coverage_never_substitutes_identity_paths_for_a_missing_parent_proof()
    {
        var (task, run, inputs) = await StoppedDownloads(2);
        // One parent legitimately uses the shared ancestor as its manifest base. Its
        // relative paths already equal the composite's paths, but ownership still
        // requires a committed per-parent receipt rather than the legacy fallback.
        var sharedBase = await Artifacts.AdoptAsync(new("shared-base-input", "shared-base-input", "download", "source"),
            new(_root, inputs[0].Files, "download", false), "test");
        var origin = new AcquisitionCompositeProvenance(1, task.Id, run.Id, "main",
            [new("main", new(task.Id, run.Id, sharedBase.Id, sharedBase.OperationId, sharedBase.Fingerprint)),
             new("second", new(task.Id, run.Id, inputs[1].Id, inputs[1].OperationId, inputs[1].Fingerprint))]);
        var projected = AcquisitionCompositeFiles.Project([sharedBase, inputs[1]], origin);
        var damagedProof = projected.Proof with {Parents = [projected.Proof.Parents.Single(p => p.InputArtifactId == inputs[1].Id)]};
        var composite = await Artifacts.AdoptAsync(new("composite-missing-proof", "composite-missing-proof", "files.compose", projected.Proof.InputFingerprint),
            new(projected.Root, ArtifactManifestFiles.ResolveFiles(projected.Root, projected.Manifest), "compositeFiles", false,
                [sharedBase.Id, inputs[1].Id], JsonSerializer.Serialize(damagedProof, WorkflowJson.Options)), "test");
        Assert.IsFalse((await ArtifactFileCoverage.EvaluateAsync(Artifacts, sharedBase, [composite.Id])).Complete);
        Assert.IsTrue(sharedBase.Files.All(File.Exists));
    }

    private sealed class CompositeStorage : Bakabase.Abstractions.Components.FileSystem.IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<Bakabase.Abstractions.Components.FileSystem.UserStorageRoot> GetRoots(Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose purpose = Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose purpose = Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose purpose = Bakabase.Abstractions.Components.FileSystem.UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }
}
