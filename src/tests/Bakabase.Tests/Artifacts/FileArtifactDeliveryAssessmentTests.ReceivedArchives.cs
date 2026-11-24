using System.IO.Compression;
using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    private async Task<ArtifactRecord> CreateReceivedDeliveryResultAsync(string name, bool isArchive)
    {
        var rootDirectory = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, name);
        if (isArchive)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using var file = zip.CreateEntry("audio.mp3").Open();
            file.Write([0xff, 0xfb, 0x90, 0xc4]);
        }
        else
            await File.WriteAllBytesAsync(path, [0xff, 0xfb, 0x90, 0xc4]);

        var key = Guid.NewGuid().ToString("N");
        return await store.AdoptAsync(new(key, key, "files.process", "fixture"),
            new(rootDirectory, [path], "processedFiles", true, MetadataJson: Json(new FileArtifactProcessingMetadata(null,
                Json(new FileProcessingPlan {Requirement = "notRequired"}), null))), "fixture");
    }

    [DataTestMethod]
    [DataRow("download.zip", true, "manual", false, true)]
    [DataRow("download.mp3", true, "none", false, true)]
    [DataRow("download.zip", true, "manual", true, false)]
    [DataRow("download.zip", true, "historical", false, false)]
    [DataRow("download.mp3", false, "manual", false, false)]
    [DataRow("download.zip", false, "manual", false, false)]
    [DataRow("book.epub", true, "manual", false, false)]
    public async Task An_inferred_noop_reviews_received_archives_without_changing_explicit_delivery_choices(
        string name, bool isArchive, string input, bool explicitlyKeep, bool waits)
    {
        var artifact = await CreateReceivedDeliveryResultAsync(name, isArchive);
        var plan = new FileProcessingPlan {Requirement = "notRequired"};
        var continuation = input == "none" ? null :
            new AcquisitionPlanArtifactInput(11, input == "manual" ? 0 : 10, artifact.Id, artifact.OperationId, artifact.Fingerprint)
                {ManualDownload = input == "manual" ? true : null};
        var config = new AcquisitionPlanProcessingConfig("part", plan, ContinueFrom: continuation,
            ExtractionLayout: FileExtractionLayouts.ArchiveDirectory, ExplicitlyKeepFiles: explicitlyKeep);
        var item = Workset(artifact) with {Id = "acquisition:11:1"};
        var source = new CloudSource {Id = "s", Kind = "http", Url = "https://example.test/received"};
        var target = item.Targets.Single() with {Sources = [source], SelectedSourceId = source.Id};
        var intent = AcquisitionPlanProcessingActivity.Intent(item, target, plan, null, null, continuation);
        item = item with {Targets = [target with {ProcessingIntentKey = intent}]};
        WorkflowExecutionContext AsContext(DownloadWorkset payload, string? prompt = null) => new()
        {
            RunId = 12, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
            ActivityConfigJson = Json(config), ResumePromptJson = prompt,
            Services = services, Logger = NullLogger.Instance, FollowingActivities = [new("fs.publishArtifacts", "{}")]
        };

        var processing = new AcquisitionPlanProcessingActivity();
        var outcome = await processing.ProcessItemAsync(AsContext(item), item, default);
        Assert.AreEqual(waits, outcome.Suspension != null);
        Assert.AreEqual(0, archives.Count);
        if (!waits)
        {
            var delivered = ((DownloadWorkset)outcome.Replacement!).Targets.Single();
            Assert.AreEqual(artifact.Id, delivered.ProcessedArtifactId);
            Assert.AreEqual(explicitlyKeep, delivered.DeliveryReview?.Accepted == true);
            return;
        }

        var suspension = outcome.Suspension!;
        var waiting = (DownloadWorkset)suspension.Item;
        var review = waiting.Targets.Single().DeliveryReview!;
        Assert.AreEqual(artifact.Id, review.ArtifactId);
        Assert.AreEqual(artifact.Fingerprint, review.Fingerprint);
        Assert.AreEqual(FileExtractionLayouts.ArchiveDirectory, review.ExtractionLayout);
        using var prompt = JsonDocument.Parse(suspension.PromptJson!);
        Assert.AreEqual(FileArtifactDeliveryAssessment.ReviewRequired, prompt.RootElement.GetProperty("reason").GetString());
        Assert.AreEqual(".zip", prompt.RootElement.GetProperty("archiveSignatures").GetProperty(name).GetString());

        var contextOnly = new CloudFileProcessingSignal {TargetId = "part", UserContext = "Please review the packaging."};
        var stillWaiting = await processing.ResumeAsync(AsContext(waiting, suspension.PromptJson), waiting,
            Answer(waiting, contextOnly), default);
        Assert.IsNotNull(stillWaiting.Suspension);

        var keep = new CloudFileProcessingSignal {TargetId = "part", ArchivePolicy = "keep"};
        var kept = await processing.ResumeAsync(AsContext(waiting, suspension.PromptJson), waiting, Answer(waiting, keep), default);
        Assert.IsTrue(((DownloadWorkset)kept.Replacement!).Targets.Single().DeliveryReview!.Accepted);
        Assert.AreEqual(0, archives.Count);
        Assert.IsTrue(File.Exists(artifact.Files.Single()));
    }

    [TestMethod]
    public async Task A_downloaded_application_keeps_its_companion_archive()
    {
        var artifact = await CreateReceivedDeliveryResultAsync("assets.zip", true);
        var executable = Path.Combine(artifact.RootDirectory, "Game.exe");
        await File.WriteAllBytesAsync(executable, [0x4d, 0x5a]);
        var key = Guid.NewGuid().ToString("N");
        var bundle = await store.AdoptAsync(new(key, key, "files.process", "fixture"),
            new(artifact.RootDirectory, [executable, artifact.Files.Single()], "processedFiles", true), "fixture");
        var item = Workset(bundle);
        var config = new FileArtifactDeliveryAssessmentConfig("part") {InspectUnprocessedArchives = true};
        var outcome = await activity.ProcessItemAsync(Context(item).ForPlannedActivity(Json(config), []), item, default);

        Assert.IsNull(outcome.Suspension);
        CollectionAssert.AreEquivalent(bundle.Files.ToArray(), ((DownloadWorkset)outcome.Replacement!).Targets.Single().ProcessedFiles.ToArray());
        Assert.AreEqual(0, archives.Count);
    }

    [TestMethod]
    public async Task Dedicated_assessment_can_extract_the_exact_received_zip_after_a_signed_answer()
    {
        var artifact = await CreateReceivedDeliveryResultAsync("received.zip", true);
        var item = Workset(artifact);
        var config = new FileArtifactDeliveryAssessmentConfig("part") {InspectUnprocessedArchives = true};
        WorkflowExecutionContext WithConfig(DownloadWorkset payload, string? prompt = null) => new()
        {
            RunId = 12, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger, Payload = payload,
            ActivityConfigJson = Json(config), ResumePromptJson = prompt, Services = services, Logger = NullLogger.Instance
        };
        var context = WithConfig(item);
        var outcome = await activity.ProcessItemAsync(context, item, default);
        var waiting = (DownloadWorkset)outcome.Suspension!.Item;
        Assert.AreEqual(0, archives.Count, "The received ZIP alone grants no authority to extract it.");
        var signal = new CloudFileProcessingSignal {TargetId = "part", ArchivePolicy = "instructions", ExtractionPlanJson = Json(Extract)};
        var resumeContext = WithConfig(waiting, outcome.Suspension.PromptJson);
        var completed = await activity.ResumeAsync(resumeContext, waiting, Answer(waiting, signal), default);
        Assert.IsNull(completed.Suspension);
        var target = ((DownloadWorkset)completed.Replacement!).Targets.Single();
        Assert.AreEqual("payload", await File.ReadAllTextAsync(target.ProcessedFiles.Single()));
        Assert.AreEqual(1, archives.Count);
        Assert.IsTrue(File.Exists(artifact.Files.Single()));
    }
}
