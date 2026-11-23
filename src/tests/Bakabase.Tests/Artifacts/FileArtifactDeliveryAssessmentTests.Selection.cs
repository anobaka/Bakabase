using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    private static (DownloadWorkset Item, FileArtifactDeliverySignal Answer, string Prompt) SelectionReview(
        ArtifactRecord source, IReadOnlyList<string> excluded)
    {
        var review = new FileArtifactDeliveryReview(source.Id, source.Fingerprint, WaitReason: FileArtifactDeliveryAssessment.ResultUncertain);
        var original = Workset(source);
        var item = original with {Targets = [original.Targets.Single() with {DeliveryReview = review}]};
        var token = AssessFileArtifactDeliveryActivity.ReviewToken("part", review);
        var answer = new FileArtifactDeliverySignal(source.Id, source.Fingerprint, token,
            new() {TargetId = "part", ArchivePolicy = "keep"}, excluded);
        return (item, answer, Json(new {targetId = "part", artifactId = source.Id, fingerprint = source.Fingerprint,
            reason = review.WaitReason, reviewToken = token}));
    }

    [TestMethod]
    public async Task Selected_delivery_publishes_exact_files_replays_without_another_operation_and_retains_excluded_source()
    {
        var original = await Register(["output/photo.jpg", "extra.key"]);
        var (item, answer, prompt) = SelectionReview(original, ["extra.key"]);
        async Task<DownloadWorkset> Confirm() => (DownloadWorkset)(await activity.ResumeAsync(Context(item, prompt), item, Json(answer), default)).Replacement!;
        var accepted = await Confirm();
        var choice = accepted.Targets.Single().DeliveryReview!.Selection!;
        Assert.AreEqual(original.Id, accepted.Targets.Single().ProcessedArtifactId);
        Assert.AreEqual(choice, (await Confirm()).Targets.Single().DeliveryReview!.Selection);
        var view = (await store.ResolveByIdAsync(choice.ArtifactId))!;
        Assert.IsFalse(view.Managed);
        CollectionAssert.AreEqual(new[] {original.Id}, view.ParentArtifactIds.ToArray());
        CollectionAssert.AreEqual(new[] {"output/photo.jpg"}, view.Manifest.Entries.Select(e => e.RelativePath).ToArray());
        var destination = Path.Combine(root, "delivery");
        var publisher = new FileArtifactPublisher(store, new Storage(), () => appData, services.GetRequiredService<IServiceScopeFactory>());
        using var host = new ServiceCollection().AddSingleton<IArtifactOperationStore>(store).AddSingleton(publisher).BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = 12, WorkflowDefinitionId = 1, Payload = accepted,
            Services = host, Logger = NullLogger.Instance, TriggerKind = CloudWorkflow.Trigger,
            ActivityConfigJson = Json(new FileArtifactPublicationConfig {TargetDirectory = destination})};
        var publication = await new PublishFileArtifactsActivity().ProcessItemAsync(context, accepted, default);
        Assert.IsNull(publication.Suspension);
        var delivered = ((DownloadWorkset)publication.Replacement!).Targets.Single();
        Assert.AreEqual(1, delivered.PublishedFiles.Count);
        Assert.IsTrue(File.Exists(Path.Combine(destination, "output", "photo.jpg")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, "extra.key")));
        Assert.IsTrue(File.Exists(Path.Combine(original.RootDirectory, "extra.key")));
        Assert.AreEqual(choice.ArtifactId, (await store.GetArtifactAsync(delivered.PublishedArtifactId!))!.ParentArtifactIds.Single());
        var cleanup = new PublishedArtifactAncestorCleanup(db, store, new ArchiveQuarantineService(store, root, new Storage()));
        var previews = await cleanup.PreviewAsync([delivered.PublishedArtifactId!], [12], default);
        Assert.IsFalse(previews.Any(p => p.ArtifactId == original.Id));
    }

    [TestMethod]
    public async Task Selected_delivery_rejects_changed_fingerprints_unsafe_paths_empty_sets_and_other_signals()
    {
        var original = await Register(["ok.jpg", "extra.key"]);
        var (item, valid, prompt) = SelectionReview(original, ["extra.key"]);
        var context = Context(item, prompt);
        foreach (var paths in new string[][] {[], ["extra.key", "extra.key"], ["EXTRA.KEY"], ["../extra.key"],
                     ["absent"], ["ok.jpg", "extra.key"], Enumerable.Range(0, 257).Select(i => i.ToString()).ToArray()})
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => activity.ResumeAsync(context, item, Json(valid with {ExcludedFiles = paths}), default));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => activity.ResumeAsync(context, item, Json(valid with {Fingerprint = "outdated"}), default));
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => activity.ResumeAsync(context, item,
            Json(valid with {Processing = valid.Processing with {UserContext = "new plan"}}), default));
        await File.AppendAllTextAsync(Path.Combine(original.RootDirectory, "extra.key"), "changed");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => activity.ResumeAsync(context, item, Json(valid), default));
    }
}
