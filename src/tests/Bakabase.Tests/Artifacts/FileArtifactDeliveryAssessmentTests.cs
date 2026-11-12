using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed partial class FileArtifactDeliveryAssessmentTests
{
    private string root = null!;
    private string appData = null!;
    private BakabaseDbContext db = null!;
    private ArtifactOperationStore store = null!;
    private ServiceProvider services = null!;
    private FileArtifactProcessor processor = null!;
    private readonly Archives archives = new();
    private readonly AssessFileArtifactDeliveryActivity activity = new();
    private static readonly FileProcessingPlan Extract = new() {Requirement = "required", Steps = [new() {Id = "open", Op = "extractArchive"}]};
    private static string Json(object value) => JsonSerializer.Serialize(value, WorkflowJson.Options);

    [TestInitialize]
    public async Task Initialize()
    {
        root = Path.Combine(Path.GetTempPath(), "delivery-assessment-" + Guid.NewGuid().ToString("N"));
        appData = Path.Combine(root, "appdata"); Directory.CreateDirectory(appData);
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "test.db")).Options;
        db = new(options); await db.Database.EnsureCreatedAsync();
        db.Add(new WorkflowRunDbModel {Id = 12, WorkflowDefinitionId = 1, Status = WorkflowRunStatus.Running});
        await db.SaveChangesAsync();
        store = new(db, () => appData);
        services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => appData))
            .AddSingleton(_ => processor).BuildServiceProvider();
        processor = new(store, new(archives), new(archives), () => appData, services.GetRequiredService<IServiceScopeFactory>());
    }
    [TestCleanup]
    public async Task Cleanup()
    {
        await services.DisposeAsync(); await db.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    private WorkflowExecutionContext Context(DownloadWorkset item, string? prompt = null, bool auto = false) => new()
    {
        RunId = 12, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger, Payload = item,
        ActivityConfigJson = Json(new FileArtifactDeliveryAssessmentConfig("part", auto)), ResumePromptJson = prompt,
        Services = services, Logger = NullLogger.Instance
    };
    private async Task<ArtifactRecord> Register(string[] names, bool extracted = true)
    {
        var dir = Path.Combine(root, "input", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var name in names)
        {
            var file = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, name.EndsWith("删除") ? [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 0] : "content"u8.ToArray());
            files.Add(file);
        }
        return await store.AdoptAsync(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "files.process", "fixture"),
            new(dir, files, "processedFiles", true, MetadataJson: Json(new FileArtifactProcessingMetadata(null,
                Json(extracted ? Extract : new FileProcessingPlan {Requirement = "notRequired"}), null))), "fixture");
    }
    private static DownloadWorkset Workset(ArtifactRecord artifact) => new()
    {
        OutputDirectory = artifact.RootDirectory,
        Targets = [new() {Id = "part", ProcessedArtifactId = artifact.Id, ProcessingOperationId = artifact.OperationId,
            ProcessingIntentKey = "previous", ProcessedDirectory = artifact.RootDirectory, ProcessedFiles = artifact.Files.ToList()}]
    };
    private static string Answer(DownloadWorkset item, CloudFileProcessingSignal processing, string? fingerprint = null)
    {
        var review = item.Targets.Single().DeliveryReview!;
        return Json(new FileArtifactDeliverySignal(review.ArtifactId, fingerprint ?? review.Fingerprint, AssessFileArtifactDeliveryActivity.ReviewToken("part", review), processing));
    }

    [TestMethod]
    public async Task Frozen_dynamic_processing_route_requires_bound_review_of_a_remaining_outer_archive()
    {
        var artifact = await Register(["another-name.7z删除"]);
        var src = new CloudSource {Id = "s", Kind = "http", Url = "https://example.test/download"};
        var item = Workset(artifact) with {Id = "acquisition:11:1"};
        var config = new AcquisitionPlanProcessingConfig("part", new FileProcessingPlan {Requirement = "unknown"});
        var target = item.Targets.Single() with {Sources = [src], SelectedSourceId = "s", ProcessingPlanJson = Json(Extract)};
        var intent = AcquisitionPlanProcessingActivity.Intent(item, target, Extract, "open", null);
        item = item with {Targets = [target with {ProcessingIntentKey = intent}]};
        WorkflowExecutionContext ctx(DownloadWorkset payload, string? prompt = null, int definition = 0,
            bool explicitReview = false) => new()
        {
            RunId = 12, WorkflowDefinitionId = definition, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
            ActivityConfigJson = Json(config), ResumePromptJson = prompt, Services = services, Logger = NullLogger.Instance,
            FollowingActivities = explicitReview ? [new("fs.assessDelivery", Json(new FileArtifactDeliveryAssessmentConfig("part"))),
                new("fs.publishArtifacts", "{}")] : [new("fs.publishArtifacts", "{}")]
        };
        var processing = new AcquisitionPlanProcessingActivity();
        Assert.IsNull((await processing.ProcessItemAsync(ctx(item, definition: 2), item, default)).Suspension);
        Assert.IsNull((await processing.ProcessItemAsync(ctx(item, explicitReview: true), item, default)).Suspension);
        var first = await processing.ProcessItemAsync(ctx(item), item, default);
        Assert.AreEqual("cloud.fileProcessing", first.Suspension!.Reason);
        var waiting = (DownloadWorkset)first.Suspension.Item;
        var keep = new CloudFileProcessingSignal {TargetId = "part", ArchivePolicy = "keep"};
        await Assert.ThrowsAsync<ArgumentException>(() => processing.ResumeAsync(ctx(waiting, first.Suspension.PromptJson),
            waiting, Answer(waiting, keep, "wrong-input"), default));
        var accepted = await processing.ResumeAsync(ctx(waiting, first.Suspension.PromptJson), waiting, Answer(waiting, keep), default);
        Assert.IsTrue(((DownloadWorkset)accepted.Replacement!).Targets.Single().DeliveryReview!.Accepted);
        Assert.AreEqual(0, archives.Count);
    }

    [DataTestMethod]
    [DataRow("signed")]
    [DataRow("unsigned")]
    [DataRow("mixed")]
    [DataRow("many")]
    [DataRow("media")]
    [DataRow("application")]
    public async Task Frozen_known_final_step_uses_shared_delivery_assessment_without_new_extraction_authority(string result)
    {
        var names = result switch
        {
            "signed" => new[] {"inner.7z删除"},
            "unsigned" => ["inner.zip"],
            "mixed" => ["inner.zip", "license.key"],
            "many" => Enumerable.Range(0, 9).Select(n => n + ".zip").ToArray(),
            "media" => ["01.jpg", "audio.mp3", "readme.txt"],
            _ => ["game.exe", "data.zip"]
        };
        var artifact = await Register(names);
        var source = new CloudSource {Id = "source", Kind = "http", Url = "https://example.test/outer.zip"};
        var item = Workset(artifact) with {Id = "acquisition:11:1"};
        var target = item.Targets.Single() with {Sources = [source], SelectedSourceId = source.Id};
        item = item with {Targets = [target with {ProcessingIntentKey =
            AcquisitionPlanProcessingActivity.Intent(item, target, Extract, "open", null)}]};
        var config = new AcquisitionPlanProcessingConfig("part", Extract, "open");
        WorkflowExecutionContext ContextFor(DownloadWorkset value, string? prompt = null) => new()
        {
            RunId = 12, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = value,
            ActivityConfigJson = Json(config), ResumePromptJson = prompt, Services = services, Logger = NullLogger.Instance,
            FollowingActivities = [new("fs.publishArtifacts", "{}")]
        };
        var processing = new AcquisitionPlanProcessingActivity();
        var outcome = await processing.ProcessItemAsync(ContextFor(item), item, default);
        if (result is "media" or "application") Assert.IsNull(outcome.Suspension);
        else
        {
            Assert.AreEqual("cloud.fileProcessing", outcome.Suspension!.Reason);
            var waiting = (DownloadWorkset)outcome.Suspension.Item;
            var review = waiting.Targets.Single().DeliveryReview!;
            Assert.AreEqual(artifact.Id, review.ArtifactId); Assert.IsFalse(review.AutomaticOuter);
            var keep = await processing.ResumeAsync(ContextFor(waiting, outcome.Suspension.PromptJson), waiting,
                Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"}), default);
            Assert.IsNull(keep.Suspension);
            Assert.IsTrue(((DownloadWorkset)keep.Replacement!).Targets.Single().DeliveryReview!.Accepted);
        }
        Assert.AreEqual(0, archives.Count); Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow("same-target", false)]
    [DataRow("other-target", true)]
    [DataRow("assessment", false)]
    [DataRow("barrier", false)]
    [DataRow("user-workflow", false)]
    [DataRow("download-publication", false)]
    public void Frozen_delivery_review_is_only_at_the_last_generated_processing_boundary(string next, bool expected)
    {
        var config = new AcquisitionPlanProcessingConfig("part", Extract, "open");
        WorkflowPlannedActivity following = next switch
        {
            "same-target" => new(AcquisitionPlanProcessingActivity.ActivityKind, Json(config)),
            "other-target" => new(AcquisitionPlanProcessingActivity.ActivityKind, Json(config with {TargetId = "another"})),
            "assessment" => new(AssessFileArtifactDeliveryActivity.ActivityKind, Json(new FileArtifactDeliveryAssessmentConfig("part"))),
            "barrier" => new("files.unknown", "{}"),
            _ => new("fs.publishArtifacts", next == "download-publication" ? "{\"source\":\"download\"}" : "{}")
        };
        var context = new WorkflowExecutionContext
        {
            RunId = 12, WorkflowDefinitionId = next == "user-workflow" ? 2 : 0,
            TriggerKind = CloudWorkflow.Trigger, Payload = new DownloadWorkset(), ActivityConfigJson = Json(config),
            Services = services, Logger = NullLogger.Instance,
            FollowingActivities = [following, new("fs.publishArtifacts", "{}")]
        };
        Assert.AreEqual(expected, AcquisitionPlanProcessingActivity.NeedsInlineReview(context,
            new DownloadWorkset {Id = "acquisition:11:1"}, config));
    }

    [TestMethod]
    [DataRow("inner.zip")]
    [DataRow("层/课件.7z删除")]
    public async Task Small_archive_only_results_wait_without_extracting_or_downloading(string path)
    {
        var artifact = await Register([path]); var workset = Workset(artifact);
        var outcome = await activity.ProcessItemAsync(Context(workset), workset, default);
        Assert.IsNotNull(outcome.Suspension);
        var prompt = JsonDocument.Parse(outcome.Suspension.PromptJson!).RootElement;
        Assert.AreEqual(path.EndsWith("删除") ? FileArtifactDeliveryAssessment.ReviewRequired : FileArtifactDeliveryAssessment.ResultUncertain, prompt.GetProperty("reason").GetString());
        Assert.AreEqual(artifact.Id, prompt.GetProperty("artifactId").GetString());
        var waiting = (DownloadWorkset)outcome.Suspension.Item;
        Assert.AreEqual("cloudDownload.deliveryReview.pendingFiles", waiting.GetWorkflowOutputMetadata(activity.Kind)
            .Single(b => b.Key == "part:processed").LabelKey);
        Assert.AreEqual(0, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [TestMethod]
    [DataRow("game.exe")]
    public async Task Normal_siblings_prevent_archive_only_alarm(string sibling)
    {
        var artifact = await Register(["data.zip", sibling]);
        Assert.AreEqual(0, (await FileArtifactDeliveryAssessment.ArchiveOnlyResultAsync(artifact, default)).Count);
    }

    [TestMethod]
    public async Task Explicit_keep_delivery_and_large_manifests_are_not_probed_as_packaging()
    {
        var originalArchive = await Register(["original.zip"], extracted: false);
        Assert.AreEqual(0, (await FileArtifactDeliveryAssessment.ArchiveOnlyResultAsync(originalArchive, default)).Count);
        var many = await Register(Enumerable.Range(0, 9).Select(i => i + ".zip").ToArray());
        foreach (var file in many.Files) File.Delete(file);
        Assert.AreEqual(8, (await FileArtifactDeliveryAssessment.ArchiveOnlyResultAsync(many, default)).Count,
            "A large uncertain manifest must wait without opening any file.");
    }

    [TestMethod]
    [DataRow("author.url", 9)]
    [DataRow("license.key", 9)]
    [DataRow("author.url", 1)]
    public async Task Review_lists_unclassified_companions_instead_of_finished_media(string companion, int mediaCount)
    {
        var relative = "zz-companions/" + companion;
        var artifact = await Register(Enumerable.Range(0, mediaCount).Select(i => $"{i:00}.mp4")
            .Concat(["readme.txt", relative]).ToArray());

        var result = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default);

        CollectionAssert.AreEqual(new[] {relative}, result.Files.ToArray());
        Assert.IsTrue(result.Uncertain, "An unknown companion still requires review.");
        Assert.IsFalse(result.Automatic);
        Assert.AreEqual(0, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists), "Review hints must preserve every companion and media file.");
    }

    [TestMethod]
    public async Task Checkpoint_retry_reassesses_existing_review_and_keeps_precise_reason()
    {
        var artifact = await Register(Enumerable.Range(0, 10).Select(i => $"{i:00}.jpg")
            .Concat(Enumerable.Range(0, 2).Select(i => $"translation_map_{i}.json"))
            .ToArray());
        var item = Workset(artifact);

        var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        using var firstPrompt = JsonDocument.Parse(first.Suspension.PromptJson!);
        Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain,
            firstPrompt.RootElement.GetProperty("reason").GetString());

        // A checkpoint retry has no new user answer.  It must re-evaluate the
        // current artifact instead of falling through to the old archive-only
        // fallback, which used to list all files and report the wrong reason.
        var retried = await activity.ProcessItemAsync(Context(waiting), waiting, default);
        Assert.IsNotNull(retried.Suspension);
        using var retryPrompt = JsonDocument.Parse(retried.Suspension!.PromptJson!);
        Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain,
            retryPrompt.RootElement.GetProperty("reason").GetString());
        var files = retryPrompt.RootElement.GetProperty("files").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] {"translation_map_0.json", "translation_map_1.json"}, files);
        Assert.AreNotEqual(FileArtifactDeliveryAssessment.ReviewRequired,
            retryPrompt.RootElement.GetProperty("reason").GetString());
    }

    [TestMethod]
    public async Task Checkpoint_retry_clears_stale_review_when_current_artifact_is_finished()
    {
        var artifact = await Register(Enumerable.Range(0, 10).Select(i => $"{i:00}.jpg").ToArray());
        var target = Workset(artifact).Targets.Single() with
        {
            DeliveryReview = new FileArtifactDeliveryReview(artifact.Id, artifact.Fingerprint,
                WaitReason: FileArtifactDeliveryAssessment.ResultUncertain),
            ProcessingWaitReason = FileArtifactDeliveryAssessment.ResultUncertain,
            Problem = FileArtifactDeliveryAssessment.ResultUncertain
        };
        var waiting = new DownloadWorkset {OutputDirectory = artifact.RootDirectory, Targets = [target]};

        var resumed = await activity.ProcessItemAsync(Context(waiting), waiting, default);
        Assert.IsNull(resumed.Suspension);
        var replacement = (DownloadWorkset)resumed.Replacement!;
        var resultTarget = replacement.Targets.Single();
        Assert.IsNull(resultTarget.DeliveryReview);
        Assert.IsNull(resultTarget.ProcessingWaitReason);
        Assert.IsNull(resultTarget.Problem);
        Assert.AreEqual("completed", resultTarget.ExecutionStatus);
        Assert.AreEqual(artifact.Id, resultTarget.ProcessedArtifactId);
    }

    [TestMethod]
    public async Task Keep_requires_the_saved_prompt_and_fingerprint_and_does_not_reprocess()
    {
        var artifact = await Register(["inner.zip"]); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        var keep = new CloudFileProcessingSignal {TargetId = "part", ArchivePolicy = "keep"};
        await Assert.ThrowsAsync<ArgumentException>(() => activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson),
            waiting, Answer(waiting, keep, "stale"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => activity.ResumeAsync(Context(waiting), waiting, Answer(waiting, keep), default));
        var changedTarget = waiting with {Targets = [waiting.Targets.Single() with {ProcessedArtifactId = "other-artifact"}]};
        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.ResumeAsync(Context(changedTarget, first.Suspension.PromptJson),
            changedTarget, Answer(changedTarget, keep), default));
        var result = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson), waiting, Answer(waiting, keep), default);
        var accepted = (DownloadWorkset)result.Replacement!;
        Assert.IsTrue(accepted.Targets.Single().DeliveryReview!.Accepted);
        Assert.AreEqual("cloudDownload.deliveryReview.confirmedFiles", accepted.GetWorkflowOutputMetadata(activity.Kind)
            .Single(b => b.Key == "part:processed").LabelKey);
        Assert.IsNull((await activity.ProcessItemAsync(Context(accepted), accepted, default)).Suspension);
        await Assert.ThrowsAsync<ArgumentException>(() => activity.ResumeAsync(Context(accepted, first.Suspension.PromptJson),
            accepted, Answer(accepted, keep), default));
        Assert.AreEqual(0, archives.Count);
        Assert.AreEqual(artifact.Id, accepted.Targets.Single().ProcessedArtifactId);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Changed_payload_cannot_be_accepted_using_an_old_confirmation()
    {
        var artifact = await Register(["inner.zip"]); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        await File.WriteAllTextAsync(artifact.Files.Single(), "edited");
        await Assert.ThrowsAsync<InvalidOperationException>(() => activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson),
            waiting, Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"}), default));
        Assert.AreEqual("edited", await File.ReadAllTextAsync(artifact.Files.Single()));
    }

    [TestMethod]
    public async Task Two_explicit_layers_continue_only_from_retained_processed_artifacts()
    {
        var artifact = await Register(["inner.zip"]);
        await File.WriteAllTextAsync(artifact.Files.Single(), "middle");
        // Register the actual execution input after setting its test content.
        artifact = await store.AdoptAsync(new("middle", "middle", "files.process", "fixture"),
            new(artifact.RootDirectory, artifact.Files, "processedFiles", true, MetadataJson: artifact.MetadataJson), "fixture");
        var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        var process = new CloudFileProcessingSignal {TargetId = "part", ExtractionPlanJson = Json(Extract)};
        var second = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson), waiting, Answer(waiting, process), default);
        Assert.IsNotNull(second.Suspension);
        var innerWaiting = (DownloadWorkset)second.Suspension.Item;
        Assert.AreNotEqual(artifact.Id, innerWaiting.Targets.Single().DeliveryReview!.ArtifactId);
        Assert.AreEqual(1, archives.Count, "Another archive is a new review, never implicit recursion.");
        var final = await activity.ResumeAsync(Context(innerWaiting, second.Suspension.PromptJson), innerWaiting, Answer(innerWaiting, process), default);
        Assert.IsNull(final.Suspension);
        var finalTarget = ((DownloadWorkset)final.Replacement!).Targets.Single();
        Assert.AreEqual(2, archives.Count);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(finalTarget.ProcessedFiles.Single()));
        Assert.IsNull(finalTarget.DownloadTaskId);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Password_wait_keeps_the_exact_continuation_input_and_plan()
    {
        var artifact = await Register(["inner.zip"]);
        await File.WriteAllTextAsync(artifact.Files.Single(), "locked");
        artifact = await store.AdoptAsync(new("locked", "locked", "files.process", "fixture"),
            new(artifact.RootDirectory, artifact.Files, "processedFiles", true, MetadataJson: artifact.MetadataJson), "fixture");
        var item = Workset(artifact); var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        var next = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson), waiting,
            Answer(waiting, new() {TargetId = "part", ExtractionPlanJson = Json(Extract)}), default);
        var passwordWait = (DownloadWorkset)next.Suspension!.Item;
        Assert.AreEqual("PasswordUnknown", passwordWait.Targets.Single().DeliveryReview!.WaitReason);
        Assert.AreEqual(artifact.Id, passwordWait.Targets.Single().DeliveryReview!.ArtifactId);
        var done = await activity.ResumeAsync(Context(passwordWait, next.Suspension.PromptJson), passwordWait,
            Answer(passwordWait, new() {TargetId = "part", Password = "secret"}), default);
        Assert.IsNull(done.Suspension);
        Assert.AreEqual(1, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Published_manifest_is_reused_then_reviewed_when_original_inputs_have_been_cleaned(bool projected)
    {
        var input = await Register(["outer.zip"]);
        await File.WriteAllTextAsync(input.Files.Single(), projected ? "middle-projected" : "middle");
        input = await store.AdoptAsync(new("published-source", "published-source", "files.local", "fixture"),
            new(input.RootDirectory, input.Files, "localFiles", true), "fixture");
        var source = new CloudSource {Id = "source", ProviderId = "http", Kind = "http", Url = "https://example.test/outer.zip"};
        var target = new CloudDownloadTarget {Id = "part", Sources = [source], SelectedSourceId = source.Id};
        var item = new DownloadWorkset {OutputDirectory = Path.Combine(root, "not-downloaded"), Targets = [target]};
        var intent = AcquisitionPlanProcessingActivity.Intent(item, target, Extract, "open", null);
        var processed = (await processor.ExecuteAsync(intent, input, Json(Extract), null, new(), "fixture",
            null, null, null, default)).Artifact!;
        var publisher = new FileArtifactPublisher(store, new Storage(), () => appData, services.GetRequiredService<IServiceScopeFactory>());
        var destination = Path.Combine(root, "delivery");
        var policy = projected ? new ArtifactDeliveryPolicy {CollapseSingleRootDirectory = true} : null;
        var published = (await publisher.PublishAsync(intent, processed, destination, "fixture", deliveryPolicy: policy)).Artifact!;
        Assert.AreEqual(projected, processed.Fingerprint != published.Fingerprint);
        foreach (var file in input.Files.Concat(processed.Files)) File.Delete(file);
        Assert.IsNull(await store.ResolveByIdAsync(input.Id));
        Assert.IsNull(await store.ResolveByIdAsync(processed.Id));
        var processActivity = new AcquisitionPlanProcessingActivity();
        var config = Json(new AcquisitionPlanProcessingConfig("part", Extract, "open"));
        using var planningServices = new ServiceCollection().AddSingleton<IArtifactOperationStore>(store)
            .AddSingleton(processor).AddSingleton(publisher)
            .AddSingleton<IWorkflowActivityRegistry>(new WorkflowActivityRegistry([processActivity, activity, new PublishFileArtifactsActivity()]))
            .BuildServiceProvider();
        var context = new WorkflowExecutionContext {RunId = 12, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger,
            ActivityConfigJson = config, Payload = item, Services = planningServices, Logger = NullLogger.Instance,
            FollowingActivities = [new(activity.Kind, Json(new FileArtifactDeliveryAssessmentConfig("part"))),
                new("fs.publishArtifacts", Json(new FileArtifactPublicationConfig {TargetDirectory = destination, ContentLayout = policy}))]};
        var reused = (DownloadWorkset)await processActivity.TrySatisfyAsync(context, item, config, default);
        Assert.AreEqual(published.Id, reused.Targets.Single().ProcessedArtifactId);
        Assert.AreEqual(published.Id, reused.Targets.Single().PublishedArtifactId);
        var review = await activity.ProcessItemAsync(Context(reused), reused, default);
        Assert.IsNotNull(review.Suspension, "A reused delivery must still review its original executed extraction metadata.");
        var waiting = (DownloadWorkset)review.Suspension.Item;
        Assert.AreEqual(published.Id, waiting.Targets.Single().DeliveryReview!.ArtifactId);
        Assert.AreEqual(1, archives.Count, "Discovering a verified publication must not reprocess its deleted inputs.");
        var done = await activity.ResumeAsync(Context(waiting, review.Suspension.PromptJson), waiting,
            Answer(waiting, new() {TargetId = "part", ExtractionPlanJson = Json(Extract)}), default);
        var final = ((DownloadWorkset)done.Replacement!).Targets.Single();
        Assert.IsNull(final.PublishedArtifactId); Assert.IsNull(final.PublishedDirectory);
        Assert.IsNull(final.PublicationOperationId); Assert.IsNull(final.PublicationTargetDirectory);
        Assert.AreEqual(0, final.PublishedFiles.Count);
        Assert.IsNull(final.DownloadTaskId);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(final.ProcessedFiles.Single()));
        Assert.IsTrue(published.Files.All(File.Exists), "Review must preserve an existing formal delivery.");
    }

    [DataTestMethod]
    [DataRow("content")]
    [DataRow("source-metadata")]
    [DataRow("source-operation")]
    [DataRow("missing-layout")]
    [DataRow("changed-layout")]
    public async Task Projected_publication_review_rejects_changed_files_or_unbound_receipts(string fault)
    {
        var source = await Register(["wrapper/inner.zip"]);
        var publisher = new FileArtifactPublisher(store, new Storage(), () => appData, services.GetRequiredService<IServiceScopeFactory>());
        var published = (await publisher.PublishAsync("projected", source, Path.Combine(root, "delivery"), "fixture",
            deliveryPolicy: new() {CollapseSingleRootDirectory = true})).Artifact!;
        Assert.AreNotEqual(source.Fingerprint, published.Fingerprint);
        var metadata = JsonSerializer.Deserialize<FileArtifactPublicationMetadata>(published.MetadataJson!, WorkflowJson.Options)!;
        if (fault == "content") await File.WriteAllTextAsync(published.Files.Single(), "changed");
        else
        {
            metadata = fault switch
            {
                "source-metadata" => metadata with {SourceMetadataJson = Json(new FileArtifactProcessingMetadata("different", Json(Extract), null))},
                "source-operation" => metadata with {SourceOperationId = "different"},
                "missing-layout" => metadata with {DeliveryLayout = null},
                _ => metadata with {DeliveryLayout = metadata.DeliveryLayout! with {ProjectionFingerprint = "changed"}}
            };
            await db.Set<ArtifactDbModel>().Where(a => a.Id == published.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MetadataJson, Json(metadata)));
        }
        var item = Workset(published);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => activity.ProcessItemAsync(Context(item), item, default));
        Assert.AreEqual("FILE_DELIVERY_INPUT_CHANGED", error.Message);
        Assert.IsTrue(published.Files.All(File.Exists)); Assert.IsTrue(source.Files.All(File.Exists));
        Assert.AreEqual(0, archives.Count);
    }

    [TestMethod]
    public async Task Review_token_binds_plan_revision_and_waiting_input_is_pinned_to_workflow_lifetime()
    {
        var artifact = await Register(["inner.zip"]); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        var oldAnswer = Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"});
        var changedReview = waiting.Targets.Single().DeliveryReview! with {PlanJson = "updated-plan"};
        var changed = waiting with {Targets = [waiting.Targets.Single() with {DeliveryReview = changedReview}]};
        await Assert.ThrowsAsync<ArgumentException>(() => activity.ResumeAsync(Context(changed, first.Suspension.PromptJson), changed, oldAnswer, default));
        var pin = await db.Set<ArtifactConsumerDbModel>().AsNoTracking().SingleAsync(c => c.OperationId == artifact.OperationId && c.OwnerKind == "workflow");
        Assert.AreEqual("12", pin.OwnerId); Assert.IsNull(pin.ReleasedAt);
        Assert.IsTrue(await store.HasActiveConsumersAsync(artifact.OperationId));
        Assert.AreEqual(0, archives.Count);
    }

    private async Task<ArtifactRecord> SignedInput(string content, string path = "outer.zip")
    {
        var artifact = await Register([path]);
        await File.WriteAllTextAsync(artifact.Files.Single(), "PK\u0003\u0004" + content);
        return await store.AdoptAsync(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "files.process", "fixture"),
            new(artifact.RootDirectory, artifact.Files, "processedFiles", true, MetadataJson: artifact.MetadataJson), "fixture");
    }

    [TestMethod]
    public async Task Confirmed_outer_layers_continue_with_bounded_recursion_and_preserve_actual_inner_paths()
    {
        var artifact = await SignedInput("auto:2"); var item = Workset(artifact);
        var done = await activity.ProcessItemAsync(Context(item, auto: true), item, default);
        Assert.IsNull(done.Suspension);
        var target = ((DownloadWorkset)done.Replacement!).Targets.Single();
        Assert.AreEqual(2, archives.Count);
        CollectionAssert.AreEqual(new[] {"ActualInner/game.exe"}, target.ProcessedFiles.Select(f =>
            Path.GetRelativePath(target.ProcessedDirectory!, f).Replace('\\', '/')).ToArray());
        Assert.IsNull(target.DownloadTaskId); Assert.IsTrue(artifact.Files.All(File.Exists));
        var replay = await activity.ProcessItemAsync(Context((DownloadWorkset)done.Replacement!, auto: true), done.Replacement!, default);
        Assert.IsNull(replay.Suspension); Assert.AreEqual(2, archives.Count);
    }

    [TestMethod]
    public async Task Unknown_key_and_multiple_archives_require_confirmation_without_touching_any_sibling()
    {
        foreach (var names in new[] {new[] {"archive.zip", "license.key"}, new[] {"archive.zip", "cover.jpg"}, new[] {"archive.zip", "book.epub"}, new[] {"one.zip", "two.zip"}, new[] {"unknown.bin"}})
        {
            var artifact = await Register(names); var item = Workset(artifact);
            var result = await activity.ProcessItemAsync(Context(item, auto: true), item, default);
            var waiting = (DownloadWorkset)result.Suspension!.Item;
            Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain, waiting.Targets.Single().DeliveryReview!.WaitReason);
            Assert.AreEqual(0, archives.Count); Assert.IsTrue(artifact.Files.All(File.Exists));
        }
    }

    [TestMethod]
    public async Task Automatic_password_resume_is_bound_to_the_retained_archive_and_does_not_redownload()
    {
        var artifact = await SignedInput("locked"); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item, auto: true), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item; var review = waiting.Targets.Single().DeliveryReview!;
        Assert.IsTrue(review.AutomaticOuter); Assert.AreEqual("PasswordUnknown", review.WaitReason);
        Assert.IsNotNull(review.ArchiveId); Assert.AreEqual(0, archives.Count);
        await Assert.ThrowsAsync<ArgumentException>(() => activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson, true), waiting,
            Answer(waiting, new() {TargetId = "part", ArchiveId = "another", Password = "secret"}), default));
        var done = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson, true), waiting,
            Answer(waiting, new() {TargetId = "part", ArchiveId = review.ArchiveId, Password = "secret"}), default);
        Assert.IsNull(done.Suspension); Assert.AreEqual(1, archives.Count);
        Assert.IsNull(((DownloadWorkset)done.Replacement!).Targets.Single().DownloadTaskId);
    }

    [TestMethod]
    public async Task Retrying_automatic_depth_limit_does_not_reset_budget_or_repeat_completed_layers()
    {
        var artifact = await SignedInput("auto:5"); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item, auto: true), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item; var review = waiting.Targets.Single().DeliveryReview!;
        Assert.IsTrue(review.AutomaticOuter); Assert.IsTrue(review.WaitReason is "DepthLimit" or "ArchiveCountLimit");
        Assert.AreEqual(3, archives.Count); Assert.AreEqual(3, review.Limits!.MaxDepth);
        Assert.AreEqual(20L * 1024 * 1024 * 1024, review.Limits.MaxExpandedBytes);
        var retry = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson, true), waiting,
            Answer(waiting, new() {TargetId = "part"}), default);
        Assert.AreEqual(3, archives.Count);
        var retained = (DownloadWorkset)retry.Suspension!.Item;
        Assert.AreEqual(review.Intent, retained.Targets.Single().DeliveryReview!.Intent);
        var done = await activity.ResumeAsync(Context(retained, retry.Suspension.PromptJson, true), retained,
            Answer(retained, new() {TargetId = "part", RecursiveOptions = review.Limits with {MaxDepth = 5, MaxArchiveGroups = 5}}), default);
        Assert.IsNull(done.Suspension); Assert.AreEqual(5, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Automatic_repeated_content_waits_and_can_be_explicitly_kept_without_restarting()
    {
        var artifact = await SignedInput("repeat"); var item = Workset(artifact);
        var first = await activity.ProcessItemAsync(Context(item, auto: true), item, default);
        var waiting = (DownloadWorkset)first.Suspension!.Item;
        Assert.AreEqual("RepeatedContent", waiting.Targets.Single().DeliveryReview!.WaitReason);
        Assert.AreEqual(1, archives.Count);
        var done = await activity.ResumeAsync(Context(waiting, first.Suspension.PromptJson, true), waiting,
            Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"}), default);
        Assert.IsNull(done.Suspension); Assert.AreEqual(1, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    private sealed class Storage : IUserStoragePolicy
    {
        public bool IsRestricted => false;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => true;
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }

    private sealed class Archives : IArchiveExtractionService
    {
        public int Count { get; private set; }
        public async Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) =>
            !((await File.ReadAllTextAsync(file, ct)).Contains("locked")) || candidates.Contains("secret")
                ? new(true, "secret", [], "", "") : new(false, null, [], "", "");
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Count++; Directory.CreateDirectory(request.Directory);
            var content = await File.ReadAllTextAsync(request.Files[0], ct);
            if (content.StartsWith("PK\u0003\u0004auto:"))
            {
                var remaining = int.Parse(content[9..]) - 1;
                if (remaining > 0) await File.WriteAllTextAsync(Path.Combine(request.Directory, "next.zip"), "PK\u0003\u0004auto:" + remaining, ct);
                else
                {
                    Directory.CreateDirectory(Path.Combine(request.Directory, "ActualInner"));
                    await File.WriteAllTextAsync(Path.Combine(request.Directory, "ActualInner", "game.exe"), "payload", ct);
                }
            }
            else if (content.Contains("repeat")) await File.WriteAllTextAsync(Path.Combine(request.Directory, "next.zip"), content, ct);
            else
            {
                var middle = content is "middle" or "middle-projected";
                var directory = content == "middle-projected" ? Path.Combine(request.Directory, "wrapper") : request.Directory;
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, middle ? "inner.zip" : "game.exe"), middle ? "plain" : "payload", ct);
            }
            return new(true, request.Directory, "");
        }
    }
}
