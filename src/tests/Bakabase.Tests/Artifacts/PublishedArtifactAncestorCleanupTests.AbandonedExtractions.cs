using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private sealed class FailedNestedExtraction : IArchiveExtractionService
    {
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string entry, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(true, null, [], "", ""));

        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct)
        {
            if (Path.GetFileName(request.Files[0]) == "nested.zip")
                return new(false, request.Directory, "Second extraction stopped.");
            Directory.CreateDirectory(request.Directory);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "nested.zip"), "intermediate archive", ct);
            return new(true, request.Directory, "");
        }
    }

    private sealed record AbandonedExtractionCase(int Id, string Operation, string StateFile, string PrivateInput,
        string PrivateOutput, ArtifactRecord Source, ArtifactRecord Publication, AcquisitionTaskDeletionService Service)
    {
        public AcquisitionTaskDeletePreviewInput Options => new([Id], CleanupFiles: true,
            SkipFileVerification: false, RetryRetainedFiles: true, DeleteDeliveredFiles: false);
    }

    private async Task<AbandonedExtractionCase> MakeAbandonedExtractionAsync(bool captured = false, bool boundary = false, bool legacyPending = false, bool processedArchive = false)
    {
        var f = await AbandonedDeletionFixture("workflow:1:adaptive:part-fixture:0",
            currentInputScenario: captured ? "captured" : null,
            privateStorage: Directory.CreateDirectory(Path.Combine(root, "processing-workspace")).FullName, processedArchive: processedArchive);
        var oldCheckpoint = Path.GetDirectoryName(f.StateFile)!;
        var directory = Path.GetDirectoryName(oldCheckpoint)!;
        Directory.Delete(oldCheckpoint, true); // Replace only the fixture's previously generated one-step journal.
        var executor = new FileProcessingPlanExecutor(new FailedNestedExtraction());
        FileProcessingPlan steps = new() {Requirement = "required", Steps = [
            new() {Id = "outer", Op = "extractArchive", Selector = "archive.zip"},
            new() {Id = "inner", Op = "extractArchive", Input = "outer", Selector = "nested.zip"}]};
        if (boundary)
        {
            var result = await executor.ExecuteAsync(steps, f.Raw.RootDirectory, f.Raw.Files, directory, stopAfterStepId: "outer");
            Assert.IsTrue(result.Completed, result.Message);
        }
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => executor.ExecuteAsync(steps,
            f.Raw.RootDirectory, f.Raw.Files, directory));
        Assert.AreEqual("Second extraction stopped.", error.Message);
        var stateFile = Directory.GetFiles(directory, "state.json", SearchOption.AllDirectories).Single();
        var state = JsonSerializer.Deserialize<FileProcessingPlanExecutor.State>(await File.ReadAllTextAsync(stateFile), Json)!;
        Assert.AreEqual(1, state.Operations.Values.Count(o => o.Completed && o.OutputManifest != null));
        Assert.IsFalse(state.Completed);
        Assert.IsTrue(state.Operations.Values.Any(o => !o.Completed && o.OutputManifest == null));
        Assert.AreEqual(boundary, state.BoundaryResults.Count != 0);
        if (legacyPending)
        {
            // Older persisted executors kept the failed group's operation key after the writer stopped.
            var json = JsonNode.Parse(await File.ReadAllTextAsync(stateFile))!;
            json["pendingOperation"] = state.Operations.Single(entry => !entry.Value.Completed).Key;
            await File.WriteAllTextAsync(stateFile, json.ToJsonString());
        }
        var privateInput = state.Outputs["download"].Single().Path;
        var privateOutput = state.Outputs["outer"].Single().Path;
        var oldStage = Path.Combine(root, "old-visible-stage");
        var plan = new AcquisitionPlan { Parts = [new() {Id = "part-fixture"}],
            StageDirectories = new() {TaskId = f.Task.Id, Parts = new() {["part-fixture"] = new()
                {Processing = new(oldStage, "borrowed", "userDirectoryRevision") {BindingId = Guid.NewGuid().ToString("N")}}}}};
        await db.AcquisitionTasks.Where(t => t.Id == f.Task.Id)
            .ExecuteUpdateAsync(q => q.SetProperty(t => t.PlanJson, JsonSerializer.Serialize(plan, Json)));
        db.Set<WorkflowRunSourceDbModel>().Add(new() {WorkflowRunId = 1, Module = "acquisition", EntityKind = "task",
            EntityId = f.Task.Id.ToString(), Revision = "2"});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.ArtifactOperations.Where(o => o.Id == f.OperationId).ExecuteUpdateAsync(q =>
            q.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddHours(-2)));
        return new(f.Task.Id, f.OperationId, stateFile, privateInput, privateOutput, f.Raw, f.Final, f.Service);
    }

    private static AcquisitionTaskDeleteCleanupGroup[] EligibleExtractionGroups(AcquisitionTaskDeletePreview preview) =>
        (preview.Rows.Single().Cleanup?.Groups ?? []).Where(g => g.Eligible &&
            g.ArtifactId.StartsWith("abandoned-extraction:", StringComparison.Ordinal)).ToArray();

    private static AcquisitionTaskDeleteInput Select(AbandonedExtractionCase f, AcquisitionTaskDeletePreview p,
        AcquisitionTaskDeleteCleanupGroup group) => new(p.PreviewId, CleanupFiles: true, RetryRetainedFiles: true,
            DeleteDeliveredFiles: false, SelectedCleanupDirectories: new Dictionary<int, IReadOnlyList<string>> {[f.Id] = [group.Directory]});

    [DataTestMethod]
    [DataRow("legacy-pending")]
    [DataRow("available")]
    [DataRow("missing-source")]
    [DataRow("retired")]
    [DataRow("captured")]
    public async Task Historical_private_extraction_can_reclaim_verified_input_and_completed_group_with_missing_or_retired_source(string source)
    {
        var f = await MakeAbandonedExtractionAsync(captured: source == "captured", legacyPending: source == "legacy-pending", processedArchive: source == "retired");
        if (source == "retired") await RetireStagedInputAsync(f.Source, f.Publication);
        if (source is "missing-source" or "captured") File.Delete(f.Source.Files.Single());
        var originalState = await File.ReadAllTextAsync(f.StateFile);
        var claim = await store.GetOperationAsync(f.Operation);
        var input = await store.GetArtifactAsync(f.Source.Id);
        var p = await f.Service.PreviewAsync(f.Options);
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(0, EligibleExtractionGroups(p).Length);
            Assert.IsTrue(File.Exists(f.PrivateInput) && File.Exists(f.PrivateOutput));
            return;
        }
        var group = EligibleExtractionGroups(p).Single();
        Assert.AreEqual("intermediate", group.Kind);
        CollectionAssert.AreEquivalent(new[] {f.PrivateInput, f.PrivateOutput}, group.Files.Select(file => file.Path).ToArray());
        var result = await f.Service.DeleteAsync(Select(f, p, group));
        Assert.IsTrue(result.CleanupResults!.Single().Completed);
        Assert.AreEqual(2, result.RemovedFiles); Assert.AreEqual(group.Bytes, result.RemovedBytes);
        Assert.IsFalse(File.Exists(f.PrivateInput)); Assert.IsFalse(File.Exists(f.PrivateOutput));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.AreEqual(source is "available" or "legacy-pending", File.Exists(f.Source.Files.Single()));
        Assert.AreEqual(originalState, await File.ReadAllTextAsync(f.StateFile));
        var after = await store.GetOperationAsync(f.Operation);
        Assert.AreEqual(claim!.State, after!.State); Assert.AreEqual(claim.LeaseToken, after.LeaseToken);
        Assert.IsNull(after.OutputArtifactId);
        Assert.AreEqual(input!.State, (await store.GetArtifactAsync(f.Source.Id))!.State);
        var next = await f.Service.PreviewAsync(f.Options);
        Assert.AreEqual(0, EligibleExtractionGroups(next).Length);
    }

    [DataTestMethod]
    [DataRow("foreign-leaf")]
    [DataRow("symlink")]
    [DataRow("hardlink")]
    [DataRow("duplicate-claim")]
    [DataRow("resource")]
    [DataRow("foreign-artifact")]
    [DataRow("consumer")]
    [DataRow("live-lease")]
    [DataRow("shared-boundary")]
    [DataRow("changed-manifest")]
    public async Task Historical_extraction_retains_the_group_when_a_leaf_claim_or_scope_is_unsafe(string scenario)
    {
        var f = await MakeAbandonedExtractionAsync(boundary: scenario == "shared-boundary");
        var extra = Path.Combine(Path.GetDirectoryName(f.PrivateOutput)!, "unregistered.txt");
        switch (scenario)
        {
            case "foreign-leaf": await File.WriteAllTextAsync(extra, "manual sidecar"); break;
            case "symlink":
                File.Delete(f.PrivateOutput);
                File.CreateSymbolicLink(f.PrivateOutput, f.Publication.Files.Single());
                break;
            case "hardlink":
                MakeFixtureHardLink(f.PrivateOutput, Path.Combine(root, "separate-link"));
                break;
            case "duplicate-claim":
                Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "2", "foreign", f.Operation));
                break;
            case "resource":
                db.ResourcesV2.Add(new() {Path = f.PrivateOutput, IsFile = true}); await db.SaveChangesAsync();
                break;
            case "foreign-artifact":
                await store.AdoptAsync(new("foreign-publication", "foreign-publication", "files.publish", "other-source"),
                    new(Path.GetDirectoryName(f.PrivateOutput)!, [f.PrivateOutput], "publishedFiles", false), "other-owner");
                break;
            case "consumer":
                Assert.IsTrue(await store.AttachConsumerAsync(f.Operation, "reader", TimeSpan.FromMinutes(3)));
                break;
            case "live-lease":
                await db.ArtifactOperations.Where(o => o.Id == f.Operation).ExecuteUpdateAsync(q =>
                    q.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(5)));
                break;
            case "changed-manifest":
                var st = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
                var complete = st["operations"]!.AsObject().Select(x => x.Value!).Single(o => (bool)o["completed"]!);
                complete["outputManifest"]!["entries"]![0]!["length"] = new FileInfo(f.PrivateOutput).Length + 1;
                await File.WriteAllTextAsync(f.StateFile, st.ToJsonString());
                break;
        }
        var preview = await f.Service.PreviewAsync(f.Options);
        Assert.AreEqual(0, EligibleExtractionGroups(preview).Length, scenario);
        Assert.IsTrue(File.Exists(f.PrivateInput)); Assert.IsTrue(File.Exists(f.PrivateOutput));
        Assert.IsTrue(f.Publication.Files.All(File.Exists));
        Assert.IsTrue(File.Exists(f.StateFile));
    }

    [DataTestMethod]
    [DataRow("input-consumer")]
    [DataRow("retirement-receipt")]
    [DataRow("capture-slot")]
    [DataRow("capture-producer")]
    [DataRow("borrowed-source")]
    [DataRow("volume-borrow")]
    public async Task Historical_extraction_requires_independent_input_provenance_and_unborrowed_staging(string scenario)
    {
        var f = await MakeAbandonedExtractionAsync(captured: scenario.StartsWith("capture-", StringComparison.Ordinal),
            processedArchive: scenario == "retirement-receipt");
        switch (scenario)
        {
            case "input-consumer":
                Assert.IsTrue(await store.AttachConsumerAsync(f.Source.OperationId, "reader", TimeSpan.FromMinutes(5)));
                break;
            case "retirement-receipt":
                File.Delete(await RetireStagedInputAsync(f.Source, f.Publication));
                break;
            case "capture-slot":
                var changed = await db.ArtifactOperationBindings.Where(b => b.OperationId == f.Source.OperationId)
                    .ExecuteUpdateAsync(q => q.SetProperty(b => b.Slot, "part-fixture:adaptive:other:" + f.Source.Id));
                Assert.AreEqual(1, changed);
                break;
            case "capture-producer":
                await db.ArtifactOperations.Where(o => o.Id == f.Source.OperationId)
                    .ExecuteUpdateAsync(q => q.SetProperty(o => o.Kind, "files.other"));
                break;
            case "borrowed-source":
            case "volume-borrow":
                var state = JsonNode.Parse(await File.ReadAllTextAsync(f.StateFile))!;
                state[scenario == "borrowed-source" ? "snapshotSourceArtifactId" : "readOnlyVolumeArtifactId"] = f.Source.Id;
                await File.WriteAllTextAsync(f.StateFile, state.ToJsonString());
                break;
        }
        var p = await f.Service.PreviewAsync(f.Options);
        Assert.AreEqual(0, EligibleExtractionGroups(p).Length, scenario);
        Assert.IsTrue(File.Exists(f.PrivateInput)); Assert.IsTrue(File.Exists(f.PrivateOutput));
        Assert.IsTrue(f.Publication.Files.All(File.Exists)); Assert.IsTrue(File.Exists(f.StateFile));
    }

    [DataTestMethod]
    [DataRow("leaf")]
    [DataRow("content")]
    [DataRow("renewal")]
    public async Task Historical_extraction_revalidates_the_selected_group_before_removing_any_member(string change)
    {
        var f = await MakeAbandonedExtractionAsync();
        var p = await f.Service.PreviewAsync(f.Options);
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(0, EligibleExtractionGroups(p).Length);
            Assert.IsTrue(File.Exists(f.PrivateInput) && File.Exists(f.PrivateOutput));
            return;
        }
        var group = EligibleExtractionGroups(p).Single();
        if (change == "leaf")
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(f.PrivateInput)!, "manual.txt"), "must stay");
        if (change == "content")
        {
            var stamp = File.GetLastWriteTimeUtc(f.PrivateOutput);
            await File.WriteAllBytesAsync(f.PrivateOutput, new byte[checked((int)new FileInfo(f.PrivateOutput).Length)]);
            File.SetLastWriteTimeUtc(f.PrivateOutput, stamp);
        }
        if (change == "renewal")
            await db.ArtifactOperations.Where(o => o.Id == f.Operation).ExecuteUpdateAsync(q =>
                q.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(5)));
        var result = await f.Service.DeleteAsync(Select(f,p,group));
        Assert.AreEqual(0, result.RemovedFiles, change);
        Assert.IsTrue(File.Exists(f.PrivateInput)); Assert.IsTrue(File.Exists(f.PrivateOutput));
        Assert.IsTrue(f.Publication.Files.All(File.Exists)); Assert.IsTrue(File.Exists(f.StateFile));
    }

    private static void MakeFixtureHardLink(string source, string target)
    {
        if (OperatingSystem.IsWindows()) Assert.IsTrue(CreateFixtureHardLinkWindows(target, source, IntPtr.Zero));
        else Assert.AreEqual(0, CreateFixtureHardLinkUnix(source, target));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateFixtureHardLinkWindows(string target, string source, IntPtr reserved);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int CreateFixtureHardLinkUnix(string source, string target);
}
