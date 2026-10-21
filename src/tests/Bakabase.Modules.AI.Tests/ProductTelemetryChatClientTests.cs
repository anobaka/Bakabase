using System.Runtime.CompilerServices;
using System.Text.Json;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Components.Observation;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using CachingChatClient = Bakabase.Modules.AI.Components.Cache.CachingChatClient;

namespace Bakabase.Modules.AI.Tests;

[TestClass]
public sealed class ProductTelemetryChatClientTests
{
    private readonly Collector _telemetry = new();
    private readonly ActorAccessor _actor = new();
    private static readonly ChatMessage[] Messages = [new(ChatRole.User, "private prompt")];

    private ProductTelemetryChatClient Observe(IChatClient inner) =>
        new(inner, _telemetry, _actor, AiProviderKind.OpenAI, AiFeature.Chat);

    [TestMethod]
    public async Task RecordsActualResponseWithoutContentAndRetainsAnonymousRequestContext()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "private answer"))
        {
            ModelId = "private-model-name", FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = 10 }
        };
        using var client = Observe(new ScriptClient(response));
        Assert.AreSame(response, await client.GetResponseAsync(Messages));
        var result = _telemetry.Events.Single();
        Assert.AreEqual("ai_request_completed", result.Name);
        Assert.AreEqual("success", result.Properties["outcome"]);
        Assert.AreEqual("openai", result.Properties["provider_type"]);
        Assert.AreEqual("chat", result.Properties["ai_feature"]);
        Assert.AreEqual(7L, result.Properties["input_tokens"]);
        Assert.AreEqual(true, result.Properties["token_usage_available"]);
        Assert.AreSame(_actor.CurrentActor, result.Actor);
        Assert.IsTrue(Guid.TryParse(result.Properties["attempt_id"] as string, out _));
        Assert.IsFalse(JsonSerializer.Serialize(result.Properties).Contains("private"));
    }

    [TestMethod]
    public async Task NoUsageMeansUnknownRatherThanInventingZeroTokens()
    {
        using var client = Observe(new ScriptClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "text"))));
        await client.GetResponseAsync(Messages);
        var result = _telemetry.Events.Single().Properties;
        Assert.AreEqual(false, result["token_usage_available"]);
        Assert.IsFalse(result.ContainsKey("input_tokens"));
        Assert.IsFalse(result.ContainsKey("total_tokens"));
    }

    [TestMethod]
    public async Task ProviderFailureIsNotSuccessAndTelemetryFailureCannotReplaceIt()
    {
        var failure = new InvalidOperationException("private endpoint and response");
        using var client = Observe(new ScriptClient(error: failure));
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(Messages));
        Assert.AreSame(failure, thrown);
        Assert.AreEqual("failed", _telemetry.Events.Single().Properties["outcome"]);
        Assert.IsFalse(JsonSerializer.Serialize(_telemetry.Events.Single().Properties).Contains("private endpoint"));
        _telemetry.Throw = true;
        thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetResponseAsync(Messages));
        Assert.AreSame(failure, thrown);
    }

    [TestMethod]
    public async Task TelemetryFailureCannotTurnSuccessfulInferenceIntoFailure()
    {
        _telemetry.Throw = true;
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"));
        using var client = Observe(new ScriptClient(response));
        Assert.AreSame(response, await client.GetResponseAsync(Messages));
    }

    [TestMethod]
    public async Task CacheHitDoesNotReportAnotherProviderRequest()
    {
        var raw = new ScriptClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer")));
        using var client = new CachingChatClient(Observe(raw), new MemoryLlmCache(), 1, "model",
            new LlmCacheRequestCoordinator());
        await client.GetResponseAsync(Messages);
        await client.GetResponseAsync(Messages);
        Assert.AreEqual(1, raw.Calls);
        Assert.AreEqual(1, _telemetry.Events.Count);
    }

    [TestMethod]
    public async Task StreamingOnlyCompletesWhenEnumerationFinishes()
    {
        using var client = Observe(new ScriptClient(stream: Stream));
        await using (var iterator = client.GetStreamingResponseAsync(Messages).GetAsyncEnumerator())
        {
            Assert.IsTrue(await iterator.MoveNextAsync());
            Assert.AreEqual(0, _telemetry.Events.Count);
        }
        Assert.AreEqual("interrupted", _telemetry.Events.Single().Properties["outcome"]);
        _telemetry.Events.Clear();
        await foreach (var _ in client.GetStreamingResponseAsync(Messages)) { }
        Assert.AreEqual("success", _telemetry.Events.Single().Properties["outcome"]);
        Assert.AreEqual(true, _telemetry.Events.Single().Properties["is_streaming"]);
    }

    [TestMethod]
    public async Task StreamingExceptionsAndCancellationAreNotSuccessfulCompletions()
    {
        using var failure = Observe(new ScriptClient(stream: FailingStream));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in failure.GetStreamingResponseAsync(Messages)) { }
        });
        Assert.AreEqual("failed", _telemetry.Events.Single().Properties["outcome"]);
        _telemetry.Events.Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var cancelled = Observe(new ScriptClient(stream: Stream));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in cancelled.GetStreamingResponseAsync(Messages, cancellationToken: cancellation.Token)) { }
        });
        Assert.AreEqual("cancelled", _telemetry.Events.Single().Properties["outcome"]);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Stream(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "private token");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailingStream(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "private partial response");
        throw new InvalidOperationException("private provider URL");
    }

    private sealed class ScriptClient(ChatResponse? response = null, Exception? error = null,
        Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>? stream = null) : IChatClient
    {
        internal int Calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return response != null ? Task.FromResult(response) : Task.FromException<ChatResponse>(error!);
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => stream!(cancellationToken);
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ActorAccessor : ITelemetryActorAccessor
    {
        public TelemetryActor CurrentActor { get; } = new(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
            Guid.NewGuid().ToString("D"), "browser");
    }

    private sealed class Collector : IProductTelemetry
    {
        internal readonly List<(string Name, IReadOnlyDictionary<string, object?> Properties, TelemetryActor? Actor)> Events = [];
        internal bool Throw;
        public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null, TelemetryActor? actor = null)
        {
            if (Throw) throw new InvalidOperationException("telemetry unavailable");
            Events.Add((eventName, properties!, actor));
        }
    }
}
