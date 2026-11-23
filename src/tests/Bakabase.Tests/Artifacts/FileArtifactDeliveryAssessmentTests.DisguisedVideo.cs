using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business.Components.Artifacts;
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
    private static byte[] MediaBox(string type, byte[] data)
    {
        var box = new byte[8 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        data.CopyTo(box, 8);
        return box;
    }

    private async Task<ArtifactRecord> VideoArtifact(string packaging)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var writer = archive.CreateEntry("game.zip", CompressionLevel.NoCompression).Open())
            writer.Write(new byte[65536]);
        var zip = buffer.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(zip.AsSpan(zip.Length - 2), 220);
        zip = [..zip, ..new byte[220], ..new byte[14353]];
        var ftyp = MediaBox("ftyp", "isom\0\0\0\0isom"u8.ToArray());
        var moov = MediaBox("moov", MediaBox("mvhd", new byte[100]));
        var mdat = MediaBox("mdat", new byte[128]);
        byte[] file = packaging switch
        {
            "appended" => [..ftyp, ..MediaBox("free", []), ..mdat, ..moov, ..zip],
            "standalone" => zip,
            "embedded" => [..ftyp, ..moov, ..MediaBox("mdat", zip)],
            "fake" => [..ftyp, ..mdat, ..moov, ..zip[^14595..]],
            _ => throw new ArgumentOutOfRangeException(nameof(packaging))
        };
        var dir = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "download.mp4");
        await File.WriteAllBytesAsync(path, file);
        return await store.AdoptAsync(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "files.process", "fixture"),
            new(dir, [path], "processedFiles", true, MetadataJson: Json(new FileArtifactProcessingMetadata(null,
                Json(new FileProcessingPlan {Requirement = "notRequired"}), null))), "fixture");
    }

    [DataTestMethod]
    [DataRow("appended", true)]
    [DataRow("standalone", true)]
    [DataRow("embedded", false)]
    [DataRow("fake", false)]
    public async Task Only_an_independently_structured_outer_ZIP_requires_video_delivery_review(string packaging, bool expected)
    {
        var artifact = await VideoArtifact(packaging);
        var assessment = await FileArtifactDeliveryAssessment.AssessAsync(artifact, default);
        Assert.AreEqual(expected, assessment.Files.Count == 1);
        Assert.IsFalse(assessment.Automatic);
        var ai = await AdaptiveFileProcessingAssessmentService.DescribeFormatEvidenceAsync(artifact, ["download.mp4"], default);
        Assert.AreEqual(expected, ai.Contains("recognizedOuterZipInVideoFile"));
    }

    [TestMethod]
    public async Task Delivery_review_explains_offset_zero_archive_signatures_without_changing_the_answer()
    {
        var dir = Path.Combine(root, "review-signatures");
        Directory.CreateDirectory(dir);
        var bytes = new Dictionary<string, byte[]>
        {
            ["01.mp4"] = [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c],
            ["02.mp4"] = [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00],
            ["03.mov"] = [0x50, 0x4b, 0x03, 0x04],
            ["04.mp4"] = MediaBox("ftyp", "isom"u8.ToArray()),
            ["05.zip"] = "only the filename says zip"u8.ToArray(),
            ["06.mp4"] = [0x00, 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c],
            ["07.mp4"] = [0x37, 0x7a]
        };
        foreach (var (name, content) in bytes)
            await File.WriteAllBytesAsync(Path.Combine(dir, name), content);
        var artifact = await store.AdoptAsync(new("review-signatures", "review-signatures", "files.process", "fixture"),
            new(dir, Directory.GetFiles(dir), "processedFiles", true), "fixture");
        var item = Workset(artifact);
        var config = new FileArtifactDeliveryAssessmentConfig("part", RequiresAdaptiveDecision: true);
        var outcome = await activity.ProcessItemAsync(Context(item).ForPlannedActivity(Json(config), []), item, default);
        var suspension = outcome.Suspension!;
        var prompt = JsonNode.Parse(suspension.PromptJson!)!.AsObject();
        Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain, prompt["reason"]!.GetValue<string>());
        var signatures = prompt["archiveSignatures"]!.Deserialize<Dictionary<string, string>>()!;
        Assert.AreEqual(3, signatures.Count);
        Assert.AreEqual(".7z", signatures["01.mp4"]);
        Assert.AreEqual(".rar", signatures["02.mp4"]);
        Assert.AreEqual(".zip", signatures["03.mov"]);
        var waiting = (DownloadWorkset)suspension.Item;
        var answer = Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"});
        prompt.Remove("archiveSignatures"); // The same answer works for prompts persisted before signatures were displayed.
        var accepted = await activity.ResumeAsync(Context(waiting, prompt.ToJsonString()), waiting, answer, default);
        Assert.IsTrue(((DownloadWorkset)accepted.Replacement!).Targets.Single().DeliveryReview!.Accepted);
        Assert.AreEqual(0, archives.Count);
    }

    [TestMethod]
    public async Task Review_signature_sample_excludes_unlisted_changed_and_over_budget_files()
    {
        var names = Enumerable.Range(1, 10).Select(i => $"{i:00}.7z删除").ToArray();
        var artifact = await Register(names);
        File.Delete(Path.Combine(artifact.RootDirectory, names[2]));
        await File.AppendAllTextAsync(Path.Combine(artifact.RootDirectory, names[3]), "changed length");
        var shown = names.Skip(1).ToArray();
        var signatures = await FileArtifactDeliveryAssessment.ReadReviewSignaturesAsync(artifact, shown, default);
        CollectionAssert.AreEquivalent(new[] {names[1], names[4], names[5], names[6], names[7], names[8]}, signatures.Keys.ToArray());
        Assert.IsTrue(signatures.Values.All(s => s == ".7z"));
    }

    [TestMethod]
    public async Task Initial_producer_noop_checks_the_retained_video_and_honors_an_explicit_keep()
    {
        var artifact = await VideoArtifact("appended");
        var plan = new FileProcessingPlan {Requirement = "notRequired"};
        var item = Workset(artifact) with {Id = "acquisition:11:1"};
        var source = new CloudSource {Id = "s", Kind = "http", Url = "https://example.test/original"};
        var target = item.Targets.Single() with {Sources = [source], SelectedSourceId = "s"};
        item = item with {Targets = [target with {ProcessingIntentKey = AcquisitionPlanProcessingActivity.Intent(item, target, plan, null, null)}]};
        WorkflowExecutionContext ContextFor(bool keep) => new()
        {
            RunId = 12, WorkflowDefinitionId = 0, TriggerKind = CloudWorkflow.Trigger, Payload = item,
            ActivityConfigJson = Json(new AcquisitionPlanProcessingConfig("part", plan, ExplicitlyKeepFiles: keep)),
            Services = services, Logger = NullLogger.Instance, FollowingActivities = [new("fs.publishArtifacts", "{}")]
        };
        var processing = new AcquisitionPlanProcessingActivity();
        var inferred = await processing.ProcessItemAsync(ContextFor(false), item, default);
        Assert.AreEqual("cloud.fileProcessing", inferred.Suspension?.Reason);
        StringAssert.Contains(inferred.Suspension!.PromptJson!, FileArtifactDeliveryAssessment.ReviewRequired);
        var accepted = await processing.ProcessItemAsync(ContextFor(true), item, default);
        Assert.IsNull(accepted.Suspension);
        Assert.IsTrue(((DownloadWorkset)accepted.Replacement!).Targets.Single().DeliveryReview?.Accepted);
    }
}
