using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests.Artifacts;

public sealed partial class FileArtifactDeliveryAssessmentTests
{
    [DataTestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Next_assessment_receives_execution_facts_only_after_a_successfully_committed_operation(bool locked)
    {
        var model = new ExecutionAwareModel(); await UseAdaptiveFinalModel(model);
        var directory = Directory.CreateDirectory(Path.Combine(root, "execution-evidence")).FullName;
        var path = Path.Combine(directory, "outer.zip"); await File.WriteAllTextAsync(path, locked ? "locked" : "middle");
        var artifact = await store.AdoptAsync(new("evidence-input", "evidence-intent", "files.process", "input"),
            new(directory, [path], "processedFiles", true), "fixture");
        var input = Workset(artifact);
        var result = await activity.ProcessItemAsync(AdaptiveFinalContext(input), input, default);
        Assert.IsNotNull(result.Suspension);
        Assert.AreEqual(locked ? 1 : 2, model.Contexts.Count);
        using var first = JsonDocument.Parse(model.Contexts[0]);
        Assert.AreEqual(JsonValueKind.Null, first.RootElement.GetProperty("executionEvidence").ValueKind);
        if (locked)
        {
            Assert.AreEqual(0, archives.Count);
            Assert.AreEqual(artifact.Id, ((DownloadWorkset)result.Suspension.Item).Targets.Single().ProcessedArtifactId);
            return;
        }
        var waiting = ((DownloadWorkset)result.Suspension.Item).Targets.Single();
        using var second = JsonDocument.Parse(model.Contexts[1]);
        var evidence = second.RootElement.GetProperty("executionEvidence");
        Assert.AreEqual(waiting.ProcessedArtifactId, evidence.GetProperty("currentArtifactId").GetString());
        var completed = evidence.GetProperty("completedProcessing")[0];
        Assert.AreEqual(artifact.Id, completed.GetProperty("inputArtifactId").GetString());
        Assert.AreEqual(1, completed.GetProperty("iteration").GetInt32());
        Assert.AreEqual("extractArchive", completed.GetProperty("operations")[0].GetProperty("kind").GetString());
        Assert.AreEqual(1, archives.Count);
    }

    private sealed class ExecutionAwareModel : ILlmService
    {
        internal List<string> Contexts { get; } = [];
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            var body = messages.Last(message => message.Role == ChatRole.User).Text!;
            Contexts.Add(body.Split("\n\nContext evidence:\n", 2)[1].Split("\n\nAssessment iteration:", 2)[0]);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Contexts.Count == 1
                ? """{"status":"continue","operations":[{"type":"extract","inputId":1,"password":null}]}"""
                : """{"status":"humanReview","reason":"Inspect the remaining package.","operations":[]}""")));
        }
        public Task<ChatResponse> CompleteAsync(int id, string model, IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature f, IList<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
