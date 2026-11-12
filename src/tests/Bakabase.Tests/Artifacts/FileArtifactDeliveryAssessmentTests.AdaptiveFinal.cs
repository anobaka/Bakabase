using System.Text.Json;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    private async Task UseAdaptiveFinalModel(ILlmService model)
    {
        await services.DisposeAsync();
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "test.db")).Options;
        services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => appData))
            .AddSingleton(new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance))
            .AddSingleton(_ => processor).BuildServiceProvider();
        processor = new(store, new(archives), new(archives), () => appData, services.GetRequiredService<IServiceScopeFactory>());
    }

    private WorkflowExecutionContext AdaptiveFinalContext(DownloadWorkset item, string? prompt = null) => new()
    {
        RunId = 12, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger, Payload = item,
        ActivityConfigJson = Json(new FileArtifactDeliveryAssessmentConfig("part", EnableAdaptiveAssessment: true,
            MaxAdaptiveIterations: 3, RequiresAdaptiveDecision: true)), ResumePromptJson = prompt,
        Services = services, Logger = NullLogger.Instance
    };

    [TestMethod]
    public async Task Model_final_cannot_publish_mixed_media_with_an_unclassified_remaining_archive()
    {
        var model = new FinalModel(); await UseAdaptiveFinalModel(model);
        var dir = Directory.CreateDirectory(Path.Combine(root, "mixed-final")).FullName;
        var files = new List<string>();
        foreach (var name in Enumerable.Range(0, 12).Select(i => $"audio/{i}.mp3")
                     .Concat(["audio/caption.srt", "subtitle.r删除ar"]))
        {
            var path = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, name.EndsWith("r删除ar")
                ? [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00]
                : "finished media or subtitle"u8.ToArray());
            files.Add(path);
        }
        var artifact = await store.AdoptAsync(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "files.process", "mixed"),
            new(dir, files, "processedFiles", true, MetadataJson: Json(new FileArtifactProcessingMetadata(null, Json(Extract), null))), "fixture");
        var input = Workset(artifact);
        var result = await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default);
        Assert.IsNotNull(result.Suspension);
        var prompt = JsonDocument.Parse(result.Suspension.PromptJson).RootElement;
        Assert.AreEqual(FileArtifactDeliveryAssessment.ResultUncertain, prompt.GetProperty("reason").GetString());
        CollectionAssert.AreEqual(new[] {"subtitle.r删除ar"}, prompt.GetProperty("files").EnumerateArray().Select(v => v.GetString()).ToArray());
        var waiting = (DownloadWorkset)result.Suspension.Item;
        Assert.AreEqual(artifact.Id, waiting.Targets.Single().ProcessedArtifactId);
        Assert.AreEqual(artifact.Id, waiting.Targets.Single().DeliveryReview!.ArtifactId);
        Assert.AreEqual(1, model.Calls);
        Assert.AreEqual(0, archives.Count, "A final guard must never start another extraction automatically.");
        Assert.IsTrue(files.All(File.Exists));
        Assert.AreEqual(0, await db.DownloadTasks.CountAsync());
    }

    [TestMethod]
    public async Task Finished_audio_with_subtitles_and_cover_passes_final_review_without_file_probes()
    {
        var model = new FinalModel(); await UseAdaptiveFinalModel(model);
        var artifact = await Register(Enumerable.Range(0, 10).SelectMany(i => new[] {$"audio/{i}.mp3", $"audio/{i}.wav", $"audio/{i}.srt"})
            .Concat(["cover.jpg", "other.ass", "other.ssa", "other.vtt"]).ToArray());
        var input = Workset(artifact);
        var result = await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default);
        Assert.IsNull(result.Suspension);
        Assert.AreEqual(artifact.Id, ((DownloadWorkset)result.Replacement!).Targets.Single().ProcessedArtifactId);
        Assert.AreEqual(1, model.Calls); Assert.AreEqual(0, archives.Count);
        Assert.IsTrue(artifact.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Application_packaged_data_does_not_grant_unpack_authority_and_non_ai_review_retains_its_boundary()
    {
        var model = new FinalModel(); await UseAdaptiveFinalModel(model);
        var artifact = await Register(["game.exe", "data.zip"]);
        var input = Workset(artifact);
        Assert.IsNull((await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default)).Suspension);
        Assert.IsTrue(artifact.Files.All(File.Exists));
        var original = await Register(["original.zip"], extracted: false);
        Assert.AreEqual(0, (await FileArtifactDeliveryAssessment.AssessAsync(original, default)).Files.Count);
        Assert.AreEqual(0, archives.Count);
    }

    [DataTestMethod]
    [DataRow("renameFile")]
    [DataRow("rebaseDirectory")]
    public async Task Model_final_reviews_remaining_archives_when_latest_operation_metadata_no_longer_contains_extraction(string operation)
    {
        var model = new FinalModel(); await UseAdaptiveFinalModel(model);
        var extracted = await Register(["nested.7z删除"]);
        var directory = Directory.CreateDirectory(Path.Combine(root, "after-layout")).FullName;
        var path = Path.Combine(directory, "nested.r删除ar");
        await File.WriteAllBytesAsync(path, [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x01, 0x00]);
        var plan = new FileProcessingPlan {Requirement = "required", Steps = [operation == "renameFile"
            ? new() {Id = "layout", Op = operation, Selector = "nested.7z删除", TargetName = "nested.r删除ar"}
            : new() {Id = "layout", Op = operation, SourceDirectory = "outer", TargetDirectory = "."}]};
        var metadata = Json(new FileArtifactProcessingMetadata(extracted.Id, Json(plan), extracted.MetadataJson));
        var current = await store.AdoptAsync(new("layout-operation", "layout-intent", "files.process", "layout"),
            new(directory, [path], "processedFiles", true, [extracted.Id], metadata), "fixture");
        Assert.IsFalse(FileArtifactDeliveryAssessment.HasExecutedExtraction(current.MetadataJson),
            "A layout operation has its own plan and cannot rely on the latest metadata containing extraction.");
        var input = Workset(current);
        var result = await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default);
        Assert.IsNotNull(result.Suspension);
        var reason = JsonDocument.Parse(result.Suspension.PromptJson).RootElement.GetProperty("reason").GetString();
        Assert.IsTrue(reason is FileArtifactDeliveryAssessment.ResultUncertain or FileArtifactDeliveryAssessment.ReviewRequired);
        var waiting = ((DownloadWorkset)result.Suspension.Item).Targets.Single();
        Assert.AreEqual(current.Id, waiting.ProcessedArtifactId);
        Assert.AreEqual(current.Id, waiting.DeliveryReview!.ArtifactId);
        Assert.AreEqual(0, archives.Count); Assert.AreEqual(1, model.Calls);
        Assert.IsTrue(File.Exists(path)); Assert.IsTrue(extracted.Files.All(File.Exists));
    }

    [TestMethod]
    public async Task Human_can_explicitly_keep_guarded_outputs_and_accepted_review_is_not_reinterpreted()
    {
        var model = new FinalModel(); await UseAdaptiveFinalModel(model);
        var artifact = await Register(["nested.zip"]); var input = Workset(artifact);
        var blocked = await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default);
        Assert.IsNotNull(blocked.Suspension);
        var waiting = (DownloadWorkset)blocked.Suspension.Item;
        var accepted = await activity.ResumeAsync(AdaptiveFinalContext(waiting, blocked.Suspension.PromptJson), waiting,
            Answer(waiting, new() {TargetId = "part", ArchivePolicy = "keep"}), default);
        Assert.IsNull(accepted.Suspension);
        var kept = (DownloadWorkset)accepted.Replacement!;
        Assert.IsTrue(kept.Targets.Single().DeliveryReview!.Accepted);
        Assert.IsNull((await activity.ProcessItemAsync(AdaptiveFinalContext(kept), kept, default)).Suspension);
        Assert.AreEqual(1, model.Calls); Assert.AreEqual(0, archives.Count);
    }

    [TestMethod]
    public async Task Final_guard_preserves_the_newest_successful_extraction_for_continuation()
    {
        var model = new ExtractThenFinalModel(); await UseAdaptiveFinalModel(model);
        var directory = Directory.CreateDirectory(Path.Combine(root, "outer")).FullName;
        var path = Path.Combine(directory, "outer.zip"); await File.WriteAllTextAsync(path, "middle");
        var input = await store.AdoptAsync(new("outer-operation", "outer-intent", "files.process", "input"),
            new(directory, [path], "processedFiles", true, MetadataJson: Json(new FileArtifactProcessingMetadata(null, Json(Extract), null))), "fixture");
        var item = Workset(input);
        var result = await activity.ProcessItemAsync(AdaptiveFinalContext(item), item, default);
        Assert.IsNotNull(result.Suspension);
        var waiting = ((DownloadWorkset)result.Suspension.Item).Targets.Single();
        Assert.AreNotEqual(input.Id, waiting.ProcessedArtifactId);
        Assert.AreEqual(waiting.ProcessedArtifactId, waiting.DeliveryReview!.ArtifactId);
        var latest = await store.ResolveByIdAsync(waiting.ProcessedArtifactId!);
        Assert.IsNotNull(latest); Assert.AreEqual("inner.zip", Path.GetFileName(latest.Files.Single()));
        Assert.AreEqual(1, archives.Count); Assert.AreEqual(2, model.Calls);
        Assert.IsTrue(File.Exists(path)); Assert.IsTrue(latest.Files.All(File.Exists));
    }

    private sealed class ExtractThenFinalModel : ILlmService
    {
        internal int Calls;
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            ++Calls == 1 ? """{"status":"continue","operations":[{"type":"extract","inputId":1,"password":null}]}""" :
                """{"status":"final","reason":"The password check succeeded.","operations":[]}""")));
        public Task<ChatResponse> CompleteAsync(int id, string model, IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature f, IList<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
