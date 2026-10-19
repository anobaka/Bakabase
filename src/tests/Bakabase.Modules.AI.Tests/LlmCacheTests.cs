using System.Text.Json;
using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Models.Db;
using Microsoft.Extensions.AI;
using CachingChatClient = Bakabase.Modules.AI.Components.Cache.CachingChatClient;

namespace Bakabase.Modules.AI.Tests;

[TestClass]
public class LlmCacheTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ConcurrentMissesAcrossClientsRecheckCacheAndGenerateOnce()
    {
        await using var server = new FakeOpenAiServer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond = async _ => { await release.Task; return (200, FakeOpenAiServer.Completion()); };
        var harness = new LlmHarness(server.Endpoint);
        var first = harness.CompleteAsync();
        await server.FirstRequest.Task.WaitAsync(Timeout);
        var others = Enumerable.Range(0, 12).Select(_ => harness.CompleteAsync()).ToArray();
        Assert.AreEqual(1, server.Requests.Count);
        Assert.IsFalse(server.Requests.Single().GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        release.SetResult();
        await Task.WhenAll(others.Append(first)).WaitAsync(Timeout);
        Assert.AreEqual(1, server.Requests.Count);
        Assert.AreEqual(1, harness.Cache.Entries.Count);
        Assert.AreEqual(0, harness.Coordinator.PendingKeyCount);
        StringAssert.StartsWith(harness.Cache.Entries.Single().Key, "v2:");
    }

    [TestMethod]
    public async Task CancelledWaiterDoesNotCancelOwnerOrBlockUnrelatedRequest()
    {
        await using var server = new FakeOpenAiServer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond = async request =>
        {
            if (request.GetProperty("messages")[1].GetProperty("content").GetString() == "blocked") await release.Task;
            return (200, FakeOpenAiServer.Completion());
        };
        var harness = new LlmHarness(server.Endpoint);
        var owner = harness.CompleteAsync(text: "blocked");
        await server.FirstRequest.Task.WaitAsync(Timeout);
        using var cancellation = new CancellationTokenSource();
        var waiter = harness.CompleteAsync(text: "blocked", ct: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter);
        await harness.CompleteAsync(text: "independent").WaitAsync(Timeout);
        Assert.IsFalse(owner.IsCompleted);
        Assert.AreEqual(2, server.Requests.Count);
        release.SetResult();
        await owner.WaitAsync(Timeout);
        await harness.CompleteAsync(text: "blocked");
        Assert.AreEqual(2, server.Requests.Count);
        Assert.AreEqual(0, harness.Coordinator.PendingKeyCount);
    }

    [TestMethod]
    public async Task OwnerCancellationReleasesKeySoAnotherWaiterCanGenerate()
    {
        var cache = new MemoryLlmCache();
        var coordinator = new LlmCacheRequestCoordinator();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new DelegateClient(async (call, ct) =>
        {
            if (call == 1)
            {
                started.SetResult();
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct);
            }
            return Response();
        });
        using var firstClient = new CachingChatClient(inner, cache, 2, "test", coordinator);
        using var secondClient = new CachingChatClient(inner, cache, 2, "test", coordinator);
        using var cancellation = new CancellationTokenSource();
        var first = firstClient.GetResponseAsync(Messages(), cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(Timeout);
        var second = secondClient.GetResponseAsync(Messages());
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await second.WaitAsync(Timeout);
        Assert.AreEqual(2, inner.Calls);
        Assert.AreEqual(1, cache.Entries.Count);
        Assert.AreEqual(0, coordinator.PendingKeyCount);
    }

    [TestMethod]
    public async Task TruncatedAndInvalidJsonResponsesAreNeverCached()
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        server.Respond = _ => Task.FromResult((200, server.Requests.Count switch
        {
            1 => FakeOpenAiServer.Completion("{\"resources\":[", "length"),
            2 => FakeOpenAiServer.Completion("invalid JSON"),
            _ => FakeOpenAiServer.Completion()
        }));
        Assert.AreEqual(ChatFinishReason.Length, (await harness.CompleteAsync()).FinishReason);
        Assert.AreEqual(0, harness.Cache.Entries.Count);
        await harness.CompleteAsync();
        Assert.AreEqual(0, harness.Cache.Entries.Count);
        await harness.CompleteAsync();
        await harness.CompleteAsync();
        Assert.AreEqual(3, server.Requests.Count);
        Assert.AreEqual(0, harness.Coordinator.PendingKeyCount);
    }

    [TestMethod]
    public async Task CacheSeparatesReasoningFormatSchemaSamplingBudgetAndMessages()
    {
        var cache = new MemoryLlmCache();
        var coordinator = new LlmCacheRequestCoordinator();
        var inner = new DelegateClient((_, _) => Task.FromResult(Response()));
        using var client = new CachingChatClient(inner, cache, 2, "test", coordinator);
        var baseline = new ChatOptions { MaxOutputTokens = 100, Temperature = 0.2f, TopP = 0.9f };
        await client.GetResponseAsync(Messages(), baseline);
        await client.GetResponseAsync(Messages(), baseline);
        Assert.AreEqual(1, inner.Calls);
        var none = baseline.Clone();
        none.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.None };
        await client.GetResponseAsync(Messages(), none);
        var high = baseline.Clone();
        high.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High };
        await client.GetResponseAsync(Messages(), high);
        var templateOff = none.Clone();
        templateOff.AdditionalProperties = new() { ["chat_template_kwargs"] = new Dictionary<string, object?> { ["enable_thinking"] = false } };
        await client.GetResponseAsync(Messages(), templateOff);
        await client.GetResponseAsync(Messages(), templateOff);
        var templateOn = none.Clone();
        templateOn.AdditionalProperties = new() { ["chat_template_kwargs"] = new Dictionary<string, object?> { ["enable_thinking"] = true } };
        await client.GetResponseAsync(Messages(), templateOn);
        var json = baseline.Clone();
        json.ResponseFormat = ChatResponseFormat.Json;
        await client.GetResponseAsync(Messages(), json);
        var schema = baseline.Clone();
        using var schemaDocument = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"}}}");
        schema.ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaDocument.RootElement);
        await client.GetResponseAsync(Messages(), schema);
        var sampling = baseline.Clone(); sampling.Temperature = 0.3f;
        await client.GetResponseAsync(Messages(), sampling);
        var topP = baseline.Clone(); topP.TopP = 0.8f;
        await client.GetResponseAsync(Messages(), topP);
        var budget = baseline.Clone(); budget.MaxOutputTokens = 50;
        await client.GetResponseAsync(Messages(), budget);
        await client.GetResponseAsync([new ChatMessage(ChatRole.System, "same text")], baseline);
        Assert.AreEqual(11, inner.Calls);
        Assert.AreEqual(11, cache.Entries.Count);
        Assert.AreEqual(0, coordinator.PendingKeyCount);
    }

    [TestMethod]
    public async Task LegacyThinkingEntriesCannotMatchNewRequests()
    {
        var cache = new MemoryLlmCache();
        var legacyPayload = "2|test||100||user:same text;";
        var legacyKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyPayload)));
        cache.Entries[legacyKey] = new LlmCallCacheEntryDbModel
        {
            CacheKey = legacyKey, ResponseJson = JsonSerializer.Serialize(Response("old thinking result"), JsonSerializerOptions.Web)
        };
        var inner = new DelegateClient((_, _) => Task.FromResult(Response()));
        using var client = new CachingChatClient(inner, cache, 2, "test", new LlmCacheRequestCoordinator());
        Assert.AreEqual("{}", (await client.GetResponseAsync(Messages(), new() { MaxOutputTokens = 100 })).Text);
        Assert.AreEqual(1, inner.Calls);
    }

    [TestMethod]
    public async Task FinishedAndCancelledUniqueKeysLeaveNoCoordinatorEntries()
    {
        var coordinator = new LlmCacheRequestCoordinator();
        await Task.WhenAll(Enumerable.Range(0, 100).Select(async i =>
        {
            using var lease = await coordinator.AcquireAsync(i.ToString(), CancellationToken.None);
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            using var lease = await coordinator.AcquireAsync("cancelled", cancellation.Token);
        });
        Assert.AreEqual(0, coordinator.PendingKeyCount);
    }

    private static ChatMessage[] Messages() => [new(ChatRole.User, "same text")];
    private static ChatResponse Response(string text = "{}") => new(new ChatMessage(ChatRole.Assistant, text)) { FinishReason = ChatFinishReason.Stop };

    private sealed class DelegateClient(Func<int, CancellationToken, Task<ChatResponse>> respond) : IChatClient
    {
        internal int Calls;
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            respond(Interlocked.Increment(ref Calls), cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
