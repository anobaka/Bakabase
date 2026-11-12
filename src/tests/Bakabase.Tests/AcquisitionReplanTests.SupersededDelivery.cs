using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.TestKit.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

public sealed partial class AcquisitionReplanTests
{
    [TestMethod]
    public void Superseded_delivery_legacy_layout_proof_counts_duplicate_file_contents()
    {
        ArtifactManifest M(params string[] hashes) => new(hashes.Select((h, n) =>
            new ArtifactManifestEntry($"file-{n}", 10, h)).ToList(), "unused");
        Assert.IsTrue(AcquisitionSupersededDeliveryService.SameContents(M("a", "b", "a"), M("a", "a", "b")));
        Assert.IsFalse(AcquisitionSupersededDeliveryService.SameContents(M("a", "a", "b"), M("a", "b", "b")));
    }

    [DataTestMethod]
    [DataRow("replace")]
    [DataRow("retention-enabled")]
    [DataRow("stale-revision")]
    [DataRow("modified-new-file")]
    [DataRow("unknown-old-file")]
    [DataRow("active-consumer")]
    [DataRow("another-resource")]
    [DataRow("library-unrelated")]
    [DataRow("library-parent")]
    [DataRow("library-child")]
    [DataRow("library-volume-root")]
    [DataRow("download-result-file")]
    [DataRow("download-result-invalid-json")]
    [DataRow("download-result-invalid-shape")]
    [DataRow("active-run-invalid-json")]
    [DataRow("interrupted-purge")]
    [DataRow("finder-metadata")]
    [DataRow("alias-only")]
    [DataRow("alias-resource")]
    [DataRow("alias-active-run")]
    [DataRow("alias-active-key")]
    [DataRow("alias-artifact")]
    [DataRow("alias-sibling-path")]
    [DataRow("alias-invalid-map")]
    [DataRow("alias-uncertain")]
    [DataRow("new-revision-purge")]
    [DataRow("new-revision-before-move")]
    [DataRow("new-revision-completed")]
    [DataRow("new-revision-reference")]
    public async Task Superseded_delivery_uses_the_completed_replacement_and_preserves_history(string situation)
    {
        var (task, oldRun, _, old, _) = await CompletedInput(frozenLayout: true);
        var revised = await Plans.ReplanAsync(task.Id, ContinueInput(task, oldRun.Id, old.Id), default);
        var directory = revised.Plan!.DeliveryLayout!.PartDirectories["main"];
        var destination = Path.Combine(Directory.CreateDirectory(directory).FullName, "inner.r");
        File.Copy(old.Files.Single(), destination);
        var current = await Artifacts.AdoptAsync(new("new-publish", "new-publish", "files.publish", old.Fingerprint),
            new(directory, [destination], "publishedFiles", false, [old.Id]), "new-owner");
        var run = new WorkflowRunDbModel {Status = WorkflowRunStatus.Success, CompletedAt = DateTime.UtcNow,
            Sources = [new() {Module = "acquisition", EntityKind = "task", EntityId = task.Id.ToString()}]};
        Db.WorkflowRuns.Add(run); await Db.SaveChangesAsync();
        var bindings = new ArtifactOperationBindingService(Db);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.continuation", old.OperationId);
        await bindings.BindAsync("workflow", run.Id.ToString(), "main:artifact.publish", current.OperationId);
        await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.Status, AcquisitionStatus.Completed).SetProperty(t => t.WorkflowRunId, run.Id));
        if (situation == "modified-new-file") await File.WriteAllTextAsync(destination, "edited");
        if (situation == "unknown-old-file") await File.WriteAllTextAsync(Path.Combine(old.RootDirectory, "installer.key"), "keep");
        if (situation == "active-consumer")
            Assert.IsTrue(await Artifacts.AttachConsumerAsync(old.OperationId, "independent-workflow", TimeSpan.FromHours(1)));
        if (situation == "another-resource")
        {
            Db.ResourcesV2.Add(new() {Path = old.Files.Single(), IsFile = true});
            await Db.SaveChangesAsync();
        }
        if (situation.StartsWith("library-"))
        {
            // Persist the actual pipe-delimited on-disk format. The second root exercises multi-root parsing.
            var folder = situation switch
            {
                "library-parent" => Path.GetDirectoryName(old.RootDirectory)!,
                "library-child" => Path.Combine(old.RootDirectory, "nested"),
                "library-volume-root" => Path.GetPathRoot(old.RootDirectory)!,
                _ => old.RootDirectory + "-sibling"
            };
            Db.MediaLibrariesV2.Add(new() {Name = "Two roots", Paths = Path.Combine(_root, "library") + "|" + folder});
            await Db.SaveChangesAsync();
        }
        if (situation.StartsWith("download-result-"))
        {
            Db.DownloadResults.Add(new() {SourceKey = "prior download", DeduplicationKey = "prior",
                Path = Path.Combine(_root, "unrelated-download"), FilesJson = situation switch {
                    "download-result-invalid-json" => "/not-a-json-document",
                    "download-result-invalid-shape" => "{}",
                    _ => System.Text.Json.JsonSerializer.Serialize(old.Files)}});
            await Db.SaveChangesAsync();
        }
        if (situation == "active-run-invalid-json")
        {
            Db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Waiting, CurrentItemJson = "/not-a-json-document"});
            await Db.SaveChangesAsync();
        }
        var options = new TestBOptionsManager<AcquisitionOptions>(new() {KeepSupersededDeliveryFiles = situation == "retention-enabled"});
        var workspaces = _services.GetRequiredService<FileProcessingWorkspaceResolver>();
        var retirement = new AcquisitionSupersededDeliveryService(Db, Artifacts, _services.GetRequiredService<IUserStoragePolicy>(),
            options, _services.GetRequiredService<Bakabase.Infrastructures.Components.App.AppService>(), workspaces);
        var request = new AcquisitionSupersededDeliveryInput(situation == "stale-revision" ? 99 : 2, run.Id, old.Id, current.Id);
        if (situation.StartsWith("alias-"))
        {
            var alias = Path.Combine(_root, "old-nas-delivery");
            var metadataDirectory = Directory.CreateDirectory(Path.Combine(_root, "appdata", "artifact-workspaces")).FullName;
            await File.WriteAllTextAsync(Path.Combine(metadataDirectory, "relocations.json"), System.Text.Json.JsonSerializer.Serialize(
                new FileProcessingWorkspaceResolver.Relocations(1, [new(alias,
                    situation == "alias-uncertain" ? Path.GetDirectoryName(old.RootDirectory)! : old.RootDirectory)]),
                System.Text.Json.JsonSerializerOptions.Web));
            if (situation == "alias-invalid-map") await File.WriteAllTextAsync(Path.Combine(metadataDirectory, "relocations.json"), "{");
            if (situation == "alias-resource") Db.ResourcesV2.Add(new() {Path = Path.Combine(alias, "inner.r"), IsFile = true});
            if (situation == "alias-active-key") Db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Running,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, bool> {[Path.Combine(alias, "inner.r")] = true})});
            if (situation is "alias-active-run" or "alias-sibling-path") Db.WorkflowRuns.Add(new() {Status = WorkflowRunStatus.Running,
                PlanSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new {configJson = System.Text.Json.JsonSerializer.Serialize(new
                    {file = Path.Combine(situation == "alias-active-run" ? alias : alias + "-unrelated", "inner.r")})})});
            await Db.SaveChangesAsync();
            if (situation == "alias-artifact")
            {
                Directory.CreateDirectory(alias);
                var file = Path.Combine(alias, "inner.r");
                File.Copy(old.Files.Single(), file);
                await Artifacts.AdoptAsync(new("independent-alias", "independent-alias", "download", old.Fingerprint),
                    new(_root, [file], "download", false), "independent-owner");
            }
        }
        if (situation == "finder-metadata")
            await File.WriteAllBytesAsync(Path.Combine(old.RootDirectory, ".DS_Store"), "\0\0\0\u0001Bud1"u8.ToArray());
        var interrupted = situation == "interrupted-purge" || situation.StartsWith("new-revision-");
        string? privateDirectory = null;
        if (interrupted)
        {
            var stage = Guid.NewGuid().ToString("N");
            privateDirectory = Path.Combine(Path.GetDirectoryName(old.RootDirectory)!, ".bakabase-retire-" + stage);
            var metadata = System.Text.Json.JsonSerializer.Serialize(new { supersededDeliveryRetirement =
                new {taskId = task.Id, input = request, stageId = stage, purging = situation != "new-revision-before-move"} }, System.Text.Json.JsonSerializerOptions.Web);
            await Db.Set<ArtifactDbModel>().Where(a => a.Id == old.Id).ExecuteUpdateAsync(s => s
                .SetProperty(a => a.State, ArtifactLocationState.Quarantined).SetProperty(a => a.MetadataJson, metadata));
            if (situation != "new-revision-before-move")
            {
                Directory.Move(old.RootDirectory, privateDirectory);
                File.Delete(Path.Combine(privateDirectory, "inner.r"));
                if (situation == "new-revision-completed")
                {
                    Directory.Delete(privateDirectory);
                    await Db.Set<ArtifactDbModel>().Where(a => a.Id == old.Id).ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.State, ArtifactLocationState.Retired));
                }
            }
        }
        if (situation.StartsWith("new-revision-"))
        {
            var state = await RetirementState(task.Id);
            var stateJson = System.Text.Json.JsonSerializer.Serialize(state with {PendingDeliveryRetirements =
                [new(2, oldRun.Id, "main", old.Id)]}, Bakabase.Modules.Workflow.Abstractions.Components.WorkflowJson.Options);
            var currentPlan = System.Text.Json.JsonSerializer.Serialize(revised.Plan,
                Bakabase.Modules.Workflow.Abstractions.Components.WorkflowJson.Options);
            if (situation != "new-revision-reference") currentPlan = currentPlan.Replace(old.Id, current.Id);
            await Db.AcquisitionTasks.Where(t => t.Id == task.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.PlanRevision, 3).SetProperty(t => t.Status, AcquisitionStatus.Waiting)
                .SetProperty(t => t.WorkflowRunId, (int?)null).SetProperty(t => t.PlanJson, currentPlan)
                .SetProperty(t => t.BuilderStateJson, stateJson));
            var finished = await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, 2, default);
            Assert.AreEqual(situation != "new-revision-reference", finished);
            if (!finished)
                for (var retry = 0; retry < 4; retry++)
                    Assert.IsFalse(await AcquisitionDeliveryRetirementWorker.ProcessAsync(_services, task.Id, 2, default),
                        "A started retirement must retain its restartable receipt beyond the ordinary retry limit.");
            var stateAfter = await RetirementState(task.Id);
            if (finished) Assert.AreEqual("retired", stateAfter.DeliveryRetirementResults!.Single().Status);
            else Assert.AreEqual("referenced", stateAfter.PendingDeliveryRetirements!.Single().Status);
        }
        var outcome = await retirement.RetireIfEnabledAsync(task.Id, request, default);
        var expected = situation is "replace" or "library-unrelated" or "interrupted-purge" or "finder-metadata" or "alias-only" or "alias-sibling-path"
            or "new-revision-purge" or "new-revision-before-move" or "new-revision-completed";
        Assert.AreEqual(expected, outcome.Retired, outcome.Status + ": " + outcome.Message);
        if (situation is "library-parent" or "library-child" or "library-volume-root" or "download-result-file")
            Assert.AreEqual("referenced", outcome.Status);
        if (situation is "download-result-invalid-json" or "download-result-invalid-shape" or "active-run-invalid-json")
            Assert.AreEqual("retained_or_pending", outcome.Status);
        Assert.AreEqual(!expected && !interrupted, Directory.Exists(old.RootDirectory));
        if (privateDirectory != null) Assert.AreEqual(!expected, Directory.Exists(privateDirectory));
        Assert.IsTrue(File.Exists(destination));
        var stored = await Artifacts.GetArtifactAsync(old.Id);
        Assert.AreEqual(old.Fingerprint, stored!.Manifest.Fingerprint);
        Assert.AreEqual(expected ? ArtifactLocationState.Retired : interrupted ? ArtifactLocationState.Quarantined : ArtifactLocationState.Available, stored.State);
        if (expected)
        {
            Assert.IsTrue((await retirement.RetireAsync(task.Id, request, default)).Retired);
            Assert.IsNull(await Artifacts.ResolveByIdAsync(old.Id));
            Assert.IsNotNull(await Artifacts.ResolveByIdAsync(current.Id));
        }
    }
}
