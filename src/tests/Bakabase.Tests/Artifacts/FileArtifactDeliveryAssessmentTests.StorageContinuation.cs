using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
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
    [DataTestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 0)]
    public async Task SavedAdaptiveStoragePlanReusesItsAttemptWithoutExtractingAgainAndRetainsRemainingBudget(int iteration, int remainingCalls)
        => await ResumeSavedAdaptiveStoragePlan(iteration, remainingCalls, verifyExecutionEvidence: false);

    [TestMethod]
    public async Task ResumedAdaptiveStoragePlanSuppliesItsCommittedActionsToTheNextAssessment()
        => await ResumeSavedAdaptiveStoragePlan(0, 1, verifyExecutionEvidence: true);

    private async Task ResumeSavedAdaptiveStoragePlan(int iteration, int remainingCalls, bool verifyExecutionEvidence)
    {
        var input = await Register(["outer.zip"]);
        var locations = new FileProcessingWorkspaceResolver(appData, new Storage());
        Directory.CreateDirectory(Path.Combine(root, "processing"));
        var materializer = new StorageInterruptedMaterializer();
        var model = new FinalModel();
        await services.DisposeAsync();
        var options = new DbContextOptionsBuilder<BakabaseDbContext>().UseSqlite("Data Source=" + Path.Combine(root, "test.db")).Options;
        services = new ServiceCollection().AddScoped(_ => new BakabaseDbContext(options))
            .AddScoped<IArtifactOperationStore>(sp => new ArtifactOperationStore(sp.GetRequiredService<BakabaseDbContext>(), () => appData))
            .AddSingleton(locations).AddSingleton(new AdaptiveFileProcessingAssessmentService(model,
                NullLogger<AdaptiveFileProcessingAssessmentService>.Instance))
            .AddSingleton(_ => processor).BuildServiceProvider();
        processor = new(store, new(archives, materializer), new(archives), () => appData,
            services.GetRequiredService<IServiceScopeFactory>(), () => Path.Combine(root, "processing"), new Storage());
        var plan = Extract with {Steps = [new() {Id = "adaptive-1", Op = "extractArchive", Input = "download", Selector = "outer.zip"}],
            Evidence = ["adaptive-file-processing", "saved-decision"]};
        var planJson = Json(plan);
        var policy = new ArtifactStoragePolicy {CleanupIntermediates = true, ProcessingWorkspaceDirectory = Path.Combine(root, "processing")};
        var config = new FileArtifactProcessingConfig {StoragePolicy = policy};
        var intent = FileArtifactProcessor.Intent("adaptive-file-processing-v1:" + input.OperationId + ":" + input.Fingerprint + ":saved-decision",
            planJson, null, config);
        var consumer = "workflow:12:adaptive:part:" + iteration;
        Assert.IsTrue(await new ArtifactOperationBindingService(db).BindAsync("workflow", "12", "part:adaptive:input:" + input.Id, input.OperationId));
        var first = await processor.ExecuteAsync(intent, input, planJson, null, config, consumer, null, null, null, default);
        Assert.IsNull(first.Artifact); Assert.AreEqual("StorageUnavailable", first.WaitReason); Assert.AreEqual(1, archives.Count);
        var key = FileArtifactProcessor.OperationKey(intent, input, plan, null, config);
        var before = await store.GetOperationAsync(key);
        Assert.AreEqual(1, before!.Attempt); Assert.AreEqual(ArtifactOperationState.Running, before.State);
        var checkpoint = await locations.InspectExistingAsync("operations", key, 1, default);
        Assert.IsNotNull(checkpoint);
        var inputCopies = materializer.Copies;
        var review = new FileArtifactDeliveryReview(input.Id, input.Fingerprint, planJson, intent, "StorageUnavailable");
        var workset = Workset(input) with {Targets = [Workset(input).Targets.Single() with {DeliveryReview = review, StoragePolicy = policy}]};
        var prompt = Json(new {targetId = "part", artifactId = input.Id, fingerprint = input.Fingerprint,
            reason = review.WaitReason, reviewToken = AssessFileArtifactDeliveryActivity.ReviewToken("part", review),
            deliveryReview = true, message = StorageWriteGuard.ReserveLowCode});
        var context = new WorkflowExecutionContext {RunId = 12, WorkflowDefinitionId = 1, TriggerKind = CloudWorkflow.Trigger,
            Payload = workset, ActivityConfigJson = Json(new FileArtifactDeliveryAssessmentConfig("part", EnableAdaptiveAssessment: true,
                MaxAdaptiveIterations: 2, RequiresAdaptiveDecision: true)), ResumePromptJson = prompt,
            Services = services, Logger = NullLogger.Instance};
        materializer.BlockFinal = false;
        var result = await activity.ResumeAsync(context, workset, Answer(workset, new() {TargetId = "part"}), default);
        Assert.AreEqual(1, archives.Count, "The saved extraction must not run again.");
        Assert.AreEqual(inputCopies, materializer.Copies, "Continuation must not make another input snapshot or final copy.");
        Assert.AreEqual(remainingCalls, model.Calls, "Continuation must use only the remaining model budget.");
        var after = await store.GetOperationAsync(key);
        Assert.AreEqual(1, after!.Attempt); Assert.AreEqual(ArtifactOperationState.Completed, after.State);
        Assert.AreEqual(checkpoint.Directory, (await locations.InspectExistingAsync("operations", key, 1, default))!.Directory);
        var completed = (DownloadWorkset)(result.Replacement ?? result.Suspension!.Item);
        Assert.AreEqual("payload", await File.ReadAllTextAsync(completed.Targets.Single().ProcessedFiles.Single()));
        if (verifyExecutionEvidence)
        {
            var contextJson = model.LastUserMessage.Split("\n\nContext evidence:\n", 2)[1]
                .Split("\n\nAssessment iteration:", 2)[0];
            using var description = JsonDocument.Parse(contextJson);
            var evidence = description.RootElement.GetProperty("executionEvidence");
            Assert.AreEqual(completed.Targets.Single().ProcessedArtifactId, evidence.GetProperty("currentArtifactId").GetString());
            var execution = evidence.GetProperty("completedProcessing")[0];
            Assert.AreEqual(input.Id, execution.GetProperty("inputArtifactId").GetString());
            Assert.AreEqual(iteration + 1, execution.GetProperty("iteration").GetInt32());
            Assert.AreEqual("extractArchive", execution.GetProperty("operations")[0].GetProperty("kind").GetString());
            Assert.AreEqual(1, execution.GetProperty("operations")[0].GetProperty("count").GetInt32());
        }
        if (remainingCalls > 0) Assert.IsNull(result.Suspension);
        else
        {
            Assert.IsNotNull(result.Suspension);
            Assert.IsTrue(JsonDocument.Parse(result.Suspension.PromptJson).RootElement.GetProperty("message").GetString()!.Contains("次数上限"));
        }
        Assert.IsTrue(input.Files.All(File.Exists));
        Assert.AreEqual(0, await db.DownloadTasks.CountAsync());
    }

    private sealed class StorageInterruptedMaterializer : IFileArtifactMaterializer
    {
        internal bool BlockFinal = true;
        internal int Copies;
        public async Task<FileMaterializationResult> MaterializeAsync(string source, string target, string mode, CancellationToken ct)
        {
            if (BlockFinal && target.Contains("result.pending")) throw new LocalStorageException(StorageWriteGuard.ReserveLowCode);
            Copies++;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var read = File.OpenRead(source);
            await using var write = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            await read.CopyToAsync(write, ct);
            return new("copy", read.Length);
        }
        public Task<bool> TryMoveAsync(string source, string target, CancellationToken ct)
        {
            if (BlockFinal && target.Contains("result.pending")) throw new LocalStorageException(StorageWriteGuard.ReserveLowCode);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Move(source, target);
            return Task.FromResult(true);
        }
    }
    private sealed class FinalModel : ILlmService
    {
        internal int Calls;
        internal string LastUserMessage = "";
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Calls++;
            LastUserMessage = messages.Last(message => message.Role == ChatRole.User).Text!;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"status":"final","reason":"The final media is present.","operations":[]}""")));
        }
        public Task<ChatResponse> CompleteAsync(int id, string model, IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature f, IList<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
