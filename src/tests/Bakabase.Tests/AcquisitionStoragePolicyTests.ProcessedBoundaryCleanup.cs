using System.Text.Json;
using System.Runtime.InteropServices;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AcquisitionStoragePolicyTests
{
    private sealed class SyntheticArchiveLayers : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            var next = Path.GetFileName(request.Files[0]) switch
                {"outer.zip" => "inner.zip", "inner.zip" => "review.zip", "review.zip" => "final.txt", _ => throw new InvalidOperationException("Unexpected fixture input")};
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, next), next + await File.ReadAllTextAsync(request.Files[0], ct), ct);
            return new(true, request.Directory, "");
        }
    }

    [DataTestMethod]
    [DataRow("publish-after-cleanup")]
    [DataRow("publication-still-low")]
    [DataRow("earlier-step")]
    [DataRow("another-processing-step-follows")]
    [DataRow("not-all-other-parts-approved")]
    [DataRow("foreign-binding")]
    [DataRow("active-stage-reader")]
    [DataRow("foreign-raw-consumer")]
    [DataRow("missing-result")]
    [DataRow("wrong-review-token")]
    [DataRow("unrelated-prompt")]
    [DataRow("pending-signal")]
    [DataRow("changed-media-after-preview")]
    [DataRow("new-reader-after-preview")]
    [DataRow("version-after-preview")]
    [DataRow("other-task")]
    public async Task Continue_to_publication_can_reclaim_only_accepted_obsolete_processing_boundaries(string scenario)
    {
        using var fixture = new BoundaryCleanupTestDirectory();
        var sp = await TestServiceBuilder.BuildServiceProvider(fixture.Directory, s => s.AddScoped(_ =>
            new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!)));
        await using var lifetime = (IAsyncDisposable)sp;
        var service = sp.GetRequiredService<AcquisitionPlanService>(); var db = sp.GetRequiredService<BakabaseDbContext>();
        var store = sp.GetRequiredService<IArtifactOperationStore>(); var maintenance = sp.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var resolver = sp.GetRequiredService<FileProcessingWorkspaceResolver>(); var storage = sp.GetRequiredService<IUserStoragePolicy>();
        var app = sp.GetRequiredService<AppService>(); var archives = new SyntheticArchiveLayers();
        var processor = new FileArtifactProcessor(store, new(archives), new(archives), () => app.AppDataDirectory, sp.GetRequiredService<IServiceScopeFactory>(), storage: storage);
        var ancestors = new PublishedArtifactAncestorCleanup(db, store, sp.GetRequiredService<ArchiveQuarantineService>(), workspaces: resolver, maintenance: maintenance);
        var recipe = new FileProcessingPlan {Requirement = "required", Steps = [
            new() {Id = "s1", Op = "extractArchive", Selector = "outer.zip"},
            new() {Id = "s2", Op = "extractArchive", Input = "s1", Selector = "inner.zip"}]};
        var reviewRecipe = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extra", Op = "extractArchive", Selector = "review.zip"}]};
        var plan = Plan(); var first = plan.Parts[0] with {Processing = recipe};
        var workspace = Path.Combine(fixture.Directory, "storage"); Directory.CreateDirectory(workspace);
        plan = plan with {OutputDirectory = Path.Combine(fixture.Directory, "published"), Parts = [first, first with {Id = "second", SelectedSourceId = "mirror", Sources = [first.Sources[0] with {Id = "mirror"}]}],
            StoragePolicy = new() {CleanupIntermediates = false, SourceCleanup = "keep", ProcessingWorkspaceDirectory = workspace}};
        var task = await service.CreatePreparedPlanAsync(plan, []);
        var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
        db.WorkflowRuns.AddRange(new WorkflowRunDbModel {Id = 500, Status = WorkflowRunStatus.Waiting}, new WorkflowRunDbModel {Id = 501, Status = WorkflowRunStatus.Waiting});
        row.WorkflowRunId = 500; row.Status = AcquisitionStatus.Waiting; row.Phase = "attention";
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 500, Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString(), Revision = row.PlanRevision.ToString()});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var bindings = new ArtifactOperationBindingService(db);
        var workset = AcquisitionPlanCompiler.Workset(task.Id, task.Task.PlanRevision, task.Plan!, []);
        var all = new List<(ArtifactRecord Raw, ArtifactRecord First, ArtifactRecord Second, ArtifactRecord Result)>();
        var targets = new List<CloudDownloadTarget>(); var nodes = new List<WorkflowRunPlanNode>();
        var publishConfig = JsonSerializer.Serialize(new FileArtifactPublicationConfig {Source = "processed"}, WorkflowJson.Options);
        foreach (var t in workset.Targets)
        {
            var rawRoot = Directory.CreateDirectory(Path.Combine(fixture.Directory, "sources", t.Id)).FullName;
            var file = Path.Combine(rawRoot, "outer.zip"); await File.WriteAllTextAsync(file, "original-" + t.Id + Guid.NewGuid().ToString("N"));
            var raw = await store.AdoptAsync(new(ArtifactManifestFiles.Hash(rawRoot), "raw:" + t.Id, "download", "fixture"),new(rawRoot, [file], "download", true), "fixture");
            var target = t with {DownloadArtifactId = raw.Id, DownloadOperationId = raw.OperationId, Files = [file]};
            var processing = new FileArtifactProcessingConfig {StoragePolicy = target.StoragePolicy, StorageTitle = target.Title};
            async Task<ArtifactRecord> Run(ArtifactRecord input, FileProcessingPlan processingPlan, string? step, string intent)
            {
                var result = await processor.ExecuteAsync(intent, input, JsonSerializer.Serialize(processingPlan, WorkflowJson.Options), null,
                    processing, "workflow:500:" + t.Id, null, null, null, default, stopAfterStepId: step);
                Assert.IsNotNull(result.Artifact, result.WaitReason + " " + result.Message);
                return result.Artifact!;
            }
            var a = await Run(raw, recipe, "s1", AcquisitionPlanProcessingActivity.Intent(workset, target, recipe, "s1", resolver));
            var b = await Run(raw, recipe, "s2", AcquisitionPlanProcessingActivity.Intent(workset, target, recipe, "s2", resolver));
            var final = await Run(b, reviewRecipe, null, t.Id + ":delivery-review");
            Assert.AreEqual(Path.GetDirectoryName(a.RootDirectory), Path.GetDirectoryName(b.RootDirectory));
            foreach (var link in new[] {("download", raw), ("artifact.download", raw), ("process:s1", a), ("process:s2", b),
                ("process:delivery-input:" + b.Id, b), ("process:delivery-result:" + final.Id, final)})
                Assert.IsTrue(await bindings.BindAsync("workflow", "500", t.Id + ":" + link.Item1, link.Item2.OperationId));
            foreach (var boundary in new[] {a, b})
            {
                var committed = (await store.GetOperationAsync(boundary.OperationId))!;
                var reader = $"files.process:{committed.Id}:{committed.Attempt}:input:500";
                Assert.IsTrue(await store.PinConsumerAsync(raw.OperationId, reader, "workflow", "500"));
            }
            Assert.IsTrue(await store.PinConsumerAsync(b.OperationId, "review-old-input-" + t.Id, "workflow", "500"));
            var current = t.Id == "second";
            var review = new FileArtifactDeliveryReview(final.Id, final.Fingerprint, Accepted: !current && scenario != "not-all-other-parts-approved",
                WaitReason: current ? "FILE_DELIVERY_RESULT_UNCERTAIN" : null);
            target = target with {ProcessedArtifactId = final.Id, ProcessingOperationId = final.OperationId,
                ProcessingIntentKey = (await store.GetOperationAsync(final.OperationId))!.IntentKey,
                ProcessingStepArtifacts = new() {["s1"] = a.Id, ["s2"] = b.Id}, ProcessedDirectory = final.RootDirectory,
                ProcessedFiles = final.Files.ToList(), DeliveryReview = review};
            all.Add((raw, a, b, final)); targets.Add(target);
            foreach (var step in (string[])["s1", "s2"])
                nodes.Add(new() {NodeId = t.Id + "." + step, Kind = AcquisitionPlanProcessingActivity.ActivityKind,
                    ConfigJson = JsonSerializer.Serialize(new AcquisitionPlanProcessingConfig(t.Id, recipe, step), WorkflowJson.Options)});
        }
        workset = workset with {Targets = targets};
        var currentIndex = nodes.Count - 1;
        if (scenario == "another-processing-step-follows") nodes.Add(nodes[0] with {NodeId = "late"});
        nodes.Add(new() {NodeId = "publish", Kind = "fs.publishArtifacts", ConfigJson = publishConfig});
        var last = targets[^1]; var rev = last.DeliveryReview!; var token = AssessFileArtifactDeliveryActivity.ReviewToken(last.Id, rev);
        var prompt = JsonSerializer.Serialize(new {targetId = last.Id, reason = rev.WaitReason, artifactId = rev.ArtifactId, fingerprint = rev.Fingerprint,
            reviewToken = scenario == "wrong-review-token" ? "wrong" : token, deliveryReview = scenario != "unrelated-prompt", allowedArchivePolicies = new[] {"instructions","keep"}, allowedActions = Array.Empty<string>()}, WorkflowJson.Options);
        var wrapped = JsonSerializer.Serialize(new {type = typeof(DownloadWorkset).FullName + ", " + typeof(DownloadWorkset).Assembly.GetName().Name, item = workset},WorkflowJson.Options);
        var snapshot = WorkflowRunPlans.Serialize(new() {Name = "fixture", TriggerKind = "manual", Nodes = nodes});
        var run = (await db.WorkflowRuns.FindAsync(500))!; run.PlanSnapshotJson = snapshot; run.CurrentStepIndex = scenario == "earlier-step" ? 0 : currentIndex;
        run.WaitReason = "cloud.fileProcessing"; run.WaitPromptJson = prompt; run.CurrentItemJson = wrapped; run.PayloadJson = JsonSerializer.Serialize(workset,WorkflowJson.Options);
        if (scenario == "pending-signal") run.PendingSignalJson = "{}";
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        if (scenario == "foreign-binding") await bindings.BindAsync("workflow", "501", "another-owner", all[0].First.OperationId);
        if (scenario == "active-stage-reader") Assert.IsTrue(await store.PinConsumerAsync(all[0].First.OperationId, "import-reader", "workflow", "501"));
        if (scenario == "foreign-raw-consumer") Assert.IsTrue(await store.PinConsumerAsync(all[0].Raw.OperationId, "foreign-reader", "workflow", "501"));
        if (scenario == "missing-result") File.Delete(all[^1].Result.Files.Single());
        var cache = new AcquisitionStoragePolicyPreviewStore();
        Task<AcquisitionWorkspaceCleanupPreview> Preview(bool confirm) => service.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: confirm), cache, maintenance, Guard, default, ancestors);
        var ordinary = await Preview(false); var preview = await Preview(true); var selected = preview.Rows.Single();
        var rawIds = all.Select(g => g.Raw.Id).ToArray(); var media = all.Select(g => g.Result.Id).ToArray();
        Assert.IsFalse(selected.ArtifactIds.Intersect(rawIds.Concat(media).Concat(all.Select(g => g.Second.Id))).Any());
        var canVerifyPrivateFiles = OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() &&
            RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 or Architecture.X86 or Architecture.X64;
        if (!canVerifyPrivateFiles)
        {
            Assert.AreEqual(0, preview.EligibleCount);
            Assert.IsTrue(all.SelectMany(g => g.First.Files).All(File.Exists));
            return;
        }
        if (scenario is "earlier-step" or "another-processing-step-follows" or "not-all-other-parts-approved" or "missing-result" or "pending-signal" or "wrong-review-token" or "unrelated-prompt")
        {
            Assert.AreEqual(0, preview.EligibleCount, selected.ReasonCode);
            Assert.IsTrue(all.SelectMany(g => g.First.Files).All(File.Exists)); return;
        }
        if (scenario is "foreign-binding" or "active-stage-reader" or "foreign-raw-consumer")
        {
            Assert.IsFalse(selected.ArtifactIds.Contains(all[0].First.Id)); Assert.IsTrue(all[0].First.Files.Concat(all[0].Raw.Files).All(File.Exists)); return;
        }
        Assert.AreEqual("processed_checkpoint_requires_confirmation", ordinary.Rows.Single().ReasonCode);
        CollectionAssert.AreEquivalent(all.Select(g => g.First.Id).ToArray(), selected.ArtifactIds.ToArray(), selected.ReasonCode);
        var details = JsonSerializer.SerializeToElement(selected, WorkflowJson.Options).GetProperty("processedCheckpointCleanup");
        CollectionAssert.AreEquivalent(rawIds, details.GetProperty("retainedDownloadArtifactIds").EnumerateArray().Select(o => o.GetString()).ToArray());
        CollectionAssert.AreEquivalent(media, details.GetProperty("verifiedResultArtifactIds").EnumerateArray().Select(o => o.GetString()).ToArray());
        CollectionAssert.AreEquivalent(selected.ArtifactIds.ToArray(), details.GetProperty("intermediateArtifactIds").EnumerateArray().Select(o => o.GetString()).ToArray());
        if (scenario == "changed-media-after-preview")
        {
            var file = all[^1].Result.Files.Single(); var stamp = File.GetLastWriteTimeUtc(file);
            await File.WriteAllBytesAsync(file,new byte[checked((int)new FileInfo(file).Length)]);
            File.SetLastWriteTimeUtc(file,stamp);
        }
        if (scenario == "new-reader-after-preview") Assert.IsTrue(await store.PinConsumerAsync(all[0].First.OperationId, "late-reader", "workflow", "501"));
        if (scenario == "version-after-preview") await db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.PlanRevision, t => t.PlanRevision + 1));
        var id = scenario == "other-task" ? task.Id + 1 : task.Id;
        try { await service.ReclaimWorkspacesAsync(new(preview.PreviewId,[id]),cache,maintenance,Guard,default,ancestors); }
        catch (ArgumentException) when (scenario == "other-task") {}
        if (scenario is not ("publish-after-cleanup" or "publication-still-low"))
        { Assert.IsTrue(all.SelectMany(g => g.First.Files).All(File.Exists)); return; }
        foreach (var g in all)
        {
            Assert.AreEqual(ArtifactLocationState.Retired,(await store.GetArtifactAsync(g.First.Id))!.State);
            Assert.AreEqual(2, await db.Set<ArtifactConsumerDbModel>().CountAsync(c => c.OperationId == g.Raw.OperationId &&
                c.OwnerKind == "workflow" && c.OwnerId == "500" && c.ReleasedAt == null));
            Assert.IsTrue(g.First.Files.All(p => !File.Exists(p))); Assert.IsTrue(g.Raw.Files.Concat(g.Second.Files).Concat(g.Result.Files).All(File.Exists));
        }
        var unchanged = await db.WorkflowRuns.AsNoTracking().SingleAsync(r => r.Id == 500);
        Assert.AreEqual(prompt,unchanged.WaitPromptJson); Assert.AreEqual(wrapped,unchanged.CurrentItemJson); Assert.AreEqual(currentIndex,unchanged.CurrentStepIndex);
        var ctx = new WorkflowExecutionContext {RunId = 500, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = workset,
            ActivityConfigJson = nodes[currentIndex].ConfigJson, FollowingActivities = [new("fs.publishArtifacts",publishConfig)], ResumePromptJson = prompt, Services = sp, Logger = NullLogger.Instance};
        var signal = new FileArtifactDeliverySignal(rev.ArtifactId,rev.Fingerprint,token,new CloudFileProcessingSignal {TargetId = last.Id, ArchivePolicy = "keep"});
        var resumed = await new AcquisitionPlanProcessingActivity().ResumeAsync(ctx,workset,JsonSerializer.Serialize(signal,WorkflowJson.Options),default);
        Assert.IsNull(resumed.Suspension); var approved = (DownloadWorkset)resumed.Replacement!;
        Assert.IsTrue(approved.Targets.All(t => t.DeliveryReview!.Accepted));
        var writeGuard = scenario == "publication-still-low"
            ? new StorageWriteGuard(new SmallPublicationReserve(), _ => ("fixture", 10L), TimeSpan.FromMilliseconds(20)) : null;
        var publisher = new FileArtifactPublisher(store,storage,() => app.AppDataDirectory,sp.GetRequiredService<IServiceScopeFactory>(), writeGuard: writeGuard);
        foreach (var g in all)
        {
            var target = Path.Combine(plan.OutputDirectory!,g.Raw.Id);
            var pub = await publisher.PublishAsync(g.Result.OperationId,g.Result,target,"workflow:500:publish",500);
            if (scenario == "publication-still-low")
            {
                Assert.IsNull(pub.Artifact);
                Assert.AreEqual("StorageUnavailable",pub.WaitReason);
                StringAssert.Contains(pub.Message!,StorageWriteGuard.ReserveLowCode);
                Assert.IsTrue(g.Raw.Files.Concat(g.Result.Files).All(File.Exists));
                continue;
            }
            Assert.IsNotNull(pub.Artifact,pub.Message);
            Assert.AreEqual(g.Result.Manifest.Fingerprint,pub.Artifact!.Manifest.Fingerprint);
            Assert.IsTrue(g.Raw.Files.All(File.Exists));
        }
    }

    [TestMethod]
    public void Explicit_cleanup_selects_disjoint_physical_work_without_losing_same_artifact_retirement()
    {
        var root = Path.GetFullPath("cleanup-fixture");
        (string Path, long Bytes) FileAt(string name, long bytes) => (Path.Combine(root, name), bytes);
        var archive = FileAt("private.zip", 7);
        var nested = FileAt("nested.zip", 11);
        var registered = FileAt("registered.txt", 13);
        ArtifactWorkspaceCleanupPreview Private(string artifactId, string proof, params (string Path, long Bytes)[] files) =>
            new(artifactId, artifactId, root, true, null, files.Length, files.Sum(f => f.Bytes), proof) {PreviewFiles = files};
        var sameInput = Private("same-id", "smaller", archive);
        var fullInput = Private("other-id", "larger", archive, nested);
        var duplicateInput = Private("historical-id", "duplicate", archive, nested);
        var sameRegistration = new ArtifactWorkspaceCleanupPreview("same-id", "same-id", root, true, null, 1, registered.Bytes,
            "registered-proof", "publication") {RegisteredPreviewFiles = [registered]};
        var overlapping = Private("overlapping-id", "partial", registered, FileAt("otherwise-independent.txt", 17));
        var movedRegistration = sameRegistration with {ArtifactId = "moved-id", FileCount = 0, Bytes = 0, RegisteredPreviewFiles = []};
        var claimedPaths = new HashSet<string>(ArtifactManifestFiles.PathComparer);

        var selected = AcquisitionPlanService.SelectExplicitWorkspaceCleanup(
            [sameInput, fullInput, duplicateInput, sameRegistration, movedRegistration], claimedPaths)!;

        CollectionAssert.AreEqual(new[] {fullInput, sameRegistration, movedRegistration}, selected);
        CollectionAssert.AreEquivalent(new[] {archive.Path, nested.Path, registered.Path}, claimedPaths.ToArray());
        Assert.AreEqual(3, selected.Sum(c => c.FileCount));
        Assert.AreEqual(31L, selected.Sum(c => c.Bytes));
        // The same registration can also own a disjoint private file. A registration ID
        // alone gives no reason to remove either proof from the apply plan.
        var disjointInput = Private("same-id", "different-files", FileAt("other-private.zip", 19));
        var disjoint = AcquisitionPlanService.SelectExplicitWorkspaceCleanup([disjointInput, sameRegistration],
            new HashSet<string>(ArtifactManifestFiles.PathComparer));
        CollectionAssert.AreEqual(new[] {disjointInput, sameRegistration}, disjoint);
        Assert.IsNull(AcquisitionPlanService.SelectExplicitWorkspaceCleanup([sameInput, disjointInput], claimedPaths));
        Assert.AreEqual(3, claimedPaths.Count);

        var unclaimed = new HashSet<string>(ArtifactManifestFiles.PathComparer);
        Assert.IsNull(AcquisitionPlanService.SelectExplicitWorkspaceCleanup([overlapping, sameRegistration], unclaimed));
        Assert.IsNull(AcquisitionPlanService.SelectExplicitWorkspaceCleanup([sameRegistration,
            sameRegistration with {ArtifactId = "other-registered-id"}], unclaimed));
        Assert.AreEqual(0, unclaimed.Count);
    }

    [TestMethod]
    public async Task Completed_processing_only_cleanup_can_keep_previously_eligible_downloads()
    {
        using var fixture = new BoundaryCleanupTestDirectory();
        var sp = await TestServiceBuilder.BuildServiceProvider(fixture.Directory, s => s.AddScoped(_ =>
            new Bakabase.Service.Components.DownloadIntents.DownloadMethodRegistry([], [], null!, null!)));
        await using var lifetime = (IAsyncDisposable)sp;
        var service = sp.GetRequiredService<AcquisitionPlanService>(); var db = sp.GetRequiredService<BakabaseDbContext>();
        var store = sp.GetRequiredService<IArtifactOperationStore>(); var maintenance = sp.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var ancestors = new PublishedArtifactAncestorCleanup(db, store, sp.GetRequiredService<ArchiveQuarantineService>(), maintenance: maintenance);
        var task = await service.CreatePreparedPlanAsync(Plan() with {OutputDirectory = fixture.Directory}, []);
        var row = (await db.AcquisitionTasks.FindAsync(task.Id))!;
        db.WorkflowRuns.Add(new WorkflowRunDbModel {Id = 721, Status = WorkflowRunStatus.Success});
        row.Status = AcquisitionStatus.Completed; row.WorkflowRunId = 721;
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var binding = new ArtifactOperationBindingService(db);
        async Task<ArtifactRecord> Register(string label, string kind, ArtifactRecord? input = null)
        {
            var dir = Path.Combine(fixture.Directory, label); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "test.txt"); await File.WriteAllTextAsync(file,"completed fixture bytes");
            var id = ArtifactManifestFiles.Hash(dir); var op = kind == "download" ? "download" : kind == "processedFiles" ? "files.process" : "files.publish";
            return await store.AdoptAsync(new(id, "fixture-"+label, op, input?.Fingerprint ?? "fixture"),
                new(dir,[file],kind,kind != "publishedFiles",input == null ? [] : [input.Id],CreateMetadata: manifest =>
                    input == null ? null : kind == "publishedFiles" ?
                        JsonSerializer.Serialize(new FileArtifactPublicationMetadata(label,input.Id,input.OperationId,input.MetadataJson,
                            FileDispositionProofs.Preserve(input,manifest)),WorkflowJson.Options) :
                        JsonSerializer.Serialize(new FileArtifactProcessingMetadata(input.Id,"{}",null,
                            FileDisposition: FileDispositionProofs.Preserve(input,manifest)),WorkflowJson.Options)),
                "fixture");
        }
        var raw = await Register("raw","download");
        var result = await Register("processed","processedFiles",raw);
        var formal = await Register("delivery","publishedFiles",result);
        foreach (var link in new[] {("download",raw),("process:complete",result),("artifact.publish",formal)})
            Assert.IsTrue(await binding.BindAsync("workflow","721","main:"+link.Item1,link.Item2.OperationId));
        var cache = new AcquisitionStoragePolicyPreviewStore();
        var all = await service.PreviewWorkspaceCleanupAsync(new([task.Id], OverrideRetention: true),cache,maintenance,Guard,default,ancestors);
        CollectionAssert.Contains(all.Rows.Single().ArtifactIds.ToArray(),raw.Id);
        var request = JsonSerializer.Deserialize<AcquisitionStoragePolicyPreviewInput>(
            JsonSerializer.Serialize(new {taskIds = new[] {task.Id},overrideRetention = true,preserveOriginalDownloads = true}), WorkflowJson.Options)!;
        var onlyIntermediate = await service.PreviewWorkspaceCleanupAsync(request,cache,maintenance,Guard,default,ancestors);
        var previewRow = onlyIntermediate.Rows.Single();
        var expectedBytes = new FileInfo(result.Files.Single()).Length;
        CollectionAssert.AreEquivalent(new[] {result.Id},previewRow.ArtifactIds.ToArray());
        Assert.AreEqual(1, previewRow.FileCount);
        Assert.AreEqual(expectedBytes, previewRow.Bytes);
        var applied = await service.ReclaimWorkspacesAsync(new(onlyIntermediate.PreviewId,[task.Id]),cache,maintenance,Guard,default,ancestors);
        CollectionAssert.Contains(applied.CompletedTaskIds.ToArray(),task.Id);
        Assert.AreEqual(previewRow.FileCount, applied.RemovedFiles);
        Assert.AreEqual(previewRow.Bytes, applied.RemovedBytes);
        Assert.IsFalse(result.Files.Any(File.Exists)); Assert.IsTrue(raw.Files.Concat(formal.Files).All(File.Exists));
        Assert.AreEqual(ArtifactLocationState.Available,(await store.GetArtifactAsync(raw.Id))!.State);
        Assert.AreEqual(ArtifactLocationState.Retired,(await store.GetArtifactAsync(result.Id))!.State);
    }

    private sealed class SmallPublicationReserve : IStorageWritePolicyProvider
    {
        public Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default) =>
            Task.FromResult(100L);
    }

    private sealed class BoundaryCleanupTestDirectory : IDisposable
    {
        public string Directory {get;} = System.IO.Directory.CreateTempSubdirectory("retire-reviewed-boundary-").FullName;
        public void Dispose() => System.IO.Directory.Delete(Directory,true);
    }
}
