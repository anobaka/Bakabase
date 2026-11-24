using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class PublishedArtifactAncestorCleanupTests
{
    private void UseAutomaticCleanupWithPrivateWorkspaces()
    {
        var workspaces = new FileProcessingWorkspaceResolver(root);
        var extraction = new NoDeletionExtraction();
        var maintenance = new ArtifactWorkspaceMaintenance(db, store, new(extraction), new(extraction), root, workspaces);
        cleanup = new(db, store, new ArchiveQuarantineService(store, root, new Storage()),
            workspaces: workspaces, maintenance: maintenance);
    }

    private async Task<ArtifactRecord> AutomaticCleanupPrivateResultAsync(int owner, ArtifactRecord? parent = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var result = Path.Combine(root, id, "result");
        Directory.CreateDirectory(result);
        var file = Path.Combine(result, "content.bin");
        await File.WriteAllTextAsync(file, "same bytes");
        var metadata = JsonSerializer.Serialize(new FileArtifactProcessingMetadata(parent?.Id,
            """{"requirement":"notRequired","steps":[]}""", null,
            StoragePolicy: new() {CleanupIntermediates = true, SourceCleanup = "delete"}), Json);
        var artifact = await store.AdoptAsync(new(id, id, "files.process", "fixture"),
            new(result, [file], "processedFiles", true, parent == null ? [] : [parent.Id], metadata), "fixture");
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", owner.ToString(), id, artifact.OperationId));
        return artifact;
    }

    private async Task StageAutomaticCleanupAsync(params int[] owners)
    {
        var observer = new PublishedArtifactCleanupCompletionObserver(db);
        foreach (var owner in owners)
            await observer.StageAsync(new() {Id = owner, Status = WorkflowRunStatus.Success}, new object(), default);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private async Task<JsonObject> AutomaticCleanupMetadataAsync(ArtifactRecord publication) =>
        JsonNode.Parse((await store.GetArtifactAsync(publication.Id))!.MetadataJson!)!.AsObject();

    [TestMethod]
    public async Task Retained_private_checkpoints_settle_without_starving_the_next_publication()
    {
        UseAutomaticCleanupWithPrivateWorkspaces();
        var first = await Artifact("processedFiles");
        var second = await Artifact("processedFiles", first);
        var third = await Artifact("processedFiles", second);
        var blocked = await Artifact("publishedFiles", third);
        var available = await AutomaticCleanupPrivateResultAsync(2);
        var next = await Artifact("publishedFiles", available, owner: 2);
        var candidates = await Preview(blocked);
        CollectionAssert.AreEquivalent(new[] {first.Id, second.Id, third.Id},
            candidates.Where(candidate => candidate.Eligible).Select(candidate => candidate.ArtifactId).ToArray());
        var preparation = await cleanup.PrepareAutomaticPrivateCleanupAsync(blocked.Id, [1], default);
        Assert.AreEqual(3, preparation.Count);
        Assert.IsTrue(preparation.All(result => !result.Completed && result.Reason == "IntermediateResultRetained"));

        await StageAutomaticCleanupAsync(1, 2);
        var earlier = DateTime.UtcNow.AddMinutes(-1);
        await db.Artifacts.Where(artifact => artifact.Id == blocked.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(artifact => artifact.UpdatedAt, earlier));
        CollectionAssert.AreEqual(new[] {blocked.Id}, (await cleanup.GetRequestedIdsAsync(default)).ToArray());
        for (var pass = 0; pass < 4; pass++)
        {
            var requested = await cleanup.GetRequestedIdsAsync(default);
            if (requested.Count == 0) break;
            await cleanup.ReclaimRequestedAsync(requested.Single(), default);
        }

        var metadata = await AutomaticCleanupMetadataAsync(blocked);
        Assert.IsFalse(metadata[PublishedArtifactCleanupCompletionObserver.PendingKey]!.GetValue<bool>());
        var progress = metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey]!;
        Assert.AreEqual("retained", progress["status"]!.GetValue<string>());
        Assert.IsTrue(progress["reasons"]!.AsArray().Any(reason => reason!.GetValue<string>() == "IntermediateResultRetained"));
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        Assert.IsTrue(new[] {first, second, third, blocked, next}.All(artifact => artifact.Files.All(File.Exists)));
        Assert.IsFalse(File.Exists(available.Files.Single()), "The unrelated publication still reclaims its verified result.");
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(available.Id))!.State);
    }

    [TestMethod]
    public async Task Automatic_cleanup_keeps_inputs_until_a_temporary_reader_releases_them()
    {
        UseAutomaticCleanupWithPrivateWorkspaces();
        var source = await AutomaticCleanupPrivateResultAsync(1);
        var publication = await Artifact("publishedFiles", source);
        const string reader = "temporary-reader";
        Assert.IsTrue(await store.AttachConsumerAsync(source.OperationId, reader, TimeSpan.FromMinutes(5)));
        await StageAutomaticCleanupAsync(1);

        await cleanup.ReclaimRequestedAsync(publication.Id, default);

        var pending = await AutomaticCleanupMetadataAsync(publication);
        Assert.IsTrue(pending[PublishedArtifactCleanupCompletionObserver.PendingKey]!.GetValue<bool>());
        Assert.IsTrue(pending[PublishedArtifactCleanupCompletionObserver.ProgressKey]!["reasons"]!.AsArray()
            .Any(reason => reason!.GetValue<string>() == "ActiveConsumer"));
        Assert.IsTrue(File.Exists(source.Files.Single()));

        await store.ReleaseConsumerAsync(source.OperationId, reader);
        await cleanup.ReclaimRequestedAsync(publication.Id, default);
        Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(source.Id))!.State);
        Assert.IsFalse(File.Exists(source.Files.Single()));
        Assert.IsTrue(File.Exists(publication.Files.Single()));
        await cleanup.ReclaimRequestedAsync(publication.Id, default);
        var settled = await AutomaticCleanupMetadataAsync(publication);
        Assert.IsFalse(settled[PublishedArtifactCleanupCompletionObserver.PendingKey]!.GetValue<bool>());
        Assert.AreEqual("completed", settled[PublishedArtifactCleanupCompletionObserver.ProgressKey]!["status"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Automatic_cleanup_visits_more_than_two_eligible_results_with_real_private_maintenance()
    {
        UseAutomaticCleanupWithPrivateWorkspaces();
        var first = await AutomaticCleanupPrivateResultAsync(1);
        var second = await AutomaticCleanupPrivateResultAsync(1, first);
        var third = await AutomaticCleanupPrivateResultAsync(1, second);
        var publication = await Artifact("publishedFiles", third);
        var sources = new[] {first, second, third};
        Assert.AreEqual(3, (await Preview(publication)).Count(candidate => candidate.Eligible));
        await StageAutomaticCleanupAsync(1);

        await cleanup.ReclaimRequestedAsync(publication.Id, default);
        Assert.IsTrue(File.Exists(first.Files.Single()));
        Assert.IsFalse(File.Exists(second.Files.Single()));
        Assert.IsFalse(File.Exists(third.Files.Single()));
        Assert.IsTrue((await AutomaticCleanupMetadataAsync(publication))[PublishedArtifactCleanupCompletionObserver.PendingKey]!.GetValue<bool>());

        await cleanup.ReclaimRequestedAsync(publication.Id, default);
        foreach (var source in sources)
        {
            Assert.AreEqual(ArtifactLocationState.Retired, (await store.GetArtifactAsync(source.Id))!.State);
            Assert.IsFalse(File.Exists(source.Files.Single()));
        }
        await cleanup.ReclaimRequestedAsync(publication.Id, default);
        Assert.AreEqual(0, (await cleanup.GetRequestedIdsAsync(default)).Count);
        Assert.IsTrue(File.Exists(publication.Files.Single()));
        var metadata = await AutomaticCleanupMetadataAsync(publication);
        Assert.AreEqual(3, metadata[PublishedArtifactCleanupCompletionObserver.ProgressKey]!["removedFiles"]!.GetValue<int>());
    }
}
