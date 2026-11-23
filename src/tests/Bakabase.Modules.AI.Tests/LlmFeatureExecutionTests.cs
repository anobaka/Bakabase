using System.Collections.Concurrent;
using System.Text.Json;
using Bakabase.Modules.AI.Extensions;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.AI.Tests;

[TestClass]
public class LlmFeatureExecutionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Text(JsonElement request) => request.GetProperty("messages")[1].GetProperty("content").GetString()!;

    [TestMethod]
    public void FileProcessorPolicyIsSharedAcrossScopesAndExposesItsOwnCapacity()
    {
        var services = new ServiceCollection();
        services.AddAI<DbContext>();
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var policy = first.ServiceProvider.GetServices<ILlmFeatureExecutionPolicy>().Single();
        Assert.AreSame(policy, second.ServiceProvider.GetServices<ILlmFeatureExecutionPolicy>().Single());
        Assert.AreEqual(new LlmFeatureExecutionSnapshot("ai.fileProcessor", AiFeature.FileProcessor, 1, 0, 0),
            policy.GetSnapshot());
    }

    [TestMethod]
    public async Task DifferentServiceScopesShareProviderCapacityAndQueuedCancellationDoesNotSubmit()
    {
        await using var server = new FakeOpenAiServer();
        var release = Signal();
        server.Respond = async request =>
        {
            if (Text(request) == "first") await release.Task;
            return (200, FakeOpenAiServer.Completion());
        };
        var policy = new FileProcessorLlmExecutionPolicy();
        var firstScope = new LlmHarness(server.Endpoint, enableCache: false, policy);
        var secondScope = new LlmHarness(server.Endpoint, enableCache: false, policy);
        using var lifetime = new CancellationTokenSource(Timeout);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var first = firstScope.CompleteAsync(AiFeature.FileProcessor, text: "first", ct: lifetime.Token);
        Task<ChatResponse>? second = null;
        try
        {
            await server.FirstRequest.Task.WaitAsync(lifetime.Token);
            var queuedSignal = Signal();
            var events = new ConcurrentQueue<LlmFeatureExecutionStatus>();
            Task<ChatResponse> cancelled;
            using (LlmExecutionScope.Begin((state, _) =>
                   { events.Enqueue(state.Status); queuedSignal.TrySetResult(); return Task.CompletedTask; }))
                cancelled = secondScope.CompleteAsync(AiFeature.FileProcessor, text: "cancelled", ct: cancellation.Token);
            await queuedSignal.Task.WaitAsync(lifetime.Token);
            Assert.AreEqual(new LlmFeatureExecutionSnapshot("ai.fileProcessor", AiFeature.FileProcessor, 1, 1, 1),
                policy.GetSnapshot());
            Assert.AreEqual(1, server.Requests.Count);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
            CollectionAssert.AreEqual(new[] { LlmFeatureExecutionStatus.Queued }, events.ToArray());
            Assert.AreEqual(0, policy.GetSnapshot().Waiting);

            var nextQueued = Signal();
            var nextEvents = new ConcurrentQueue<LlmFeatureExecutionStatus>();
            using (LlmExecutionScope.Begin((state, _) =>
                   { nextEvents.Enqueue(state.Status); nextQueued.TrySetResult(); return Task.CompletedTask; }))
                second = secondScope.CompleteAsync(AiFeature.FileProcessor, text: "second", ct: lifetime.Token);
            await nextQueued.Task.WaitAsync(lifetime.Token);
            Assert.AreEqual(1, server.Requests.Count);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(lifetime.Token);
            CollectionAssert.AreEqual(new[] { LlmFeatureExecutionStatus.Queued, LlmFeatureExecutionStatus.Running,
                LlmFeatureExecutionStatus.Released }, nextEvents.ToArray());
            Assert.AreEqual(2, server.Requests.Count);
            Assert.AreEqual(0, policy.GetSnapshot().Active);
            Assert.AreEqual(0, policy.GetSnapshot().Waiting);
        }
        finally
        {
            release.TrySetResult();
            await first;
            if (second != null) await second;
        }
    }

    [TestMethod]
    public async Task CacheHitsAndOtherFeaturesDoNotWaitForFileProcessorCapacity()
    {
        await using var server = new FakeOpenAiServer();
        var policy = new FileProcessorLlmExecutionPolicy();
        var harness = new LlmHarness(server.Endpoint, executionPolicy: policy);
        using var lifetime = new CancellationTokenSource(Timeout);
        await harness.CompleteAsync(AiFeature.FileProcessor, text: "cached", ct: lifetime.Token);
        var entered = Signal();
        var release = Signal();
        server.Respond = async request =>
        {
            if (Text(request) == "blocked") { entered.TrySetResult(); await release.Task; }
            return (200, FakeOpenAiServer.Completion());
        };
        var blocked = harness.CompleteAsync(AiFeature.FileProcessor, text: "blocked", ct: lifetime.Token);
        try
        {
            await entered.Task.WaitAsync(lifetime.Token);
            var states = new List<LlmFeatureExecutionState>();
            using (LlmExecutionScope.Begin((state, _) => { states.Add(state); return Task.CompletedTask; }))
            {
                await harness.CompleteAsync(AiFeature.FileProcessor, text: "cached", ct: lifetime.Token);
                await harness.CompleteAsync(AiFeature.PostParser, text: "parser", ct: lifetime.Token);
                await harness.CompleteAsync(AiFeature.Default, text: "chat", ct: lifetime.Token);
            }
            Assert.AreEqual(0, states.Count, "Only actual calls for the declared feature acquire capacity.");
            Assert.IsFalse(blocked.IsCompleted);
            Assert.AreEqual(1, policy.GetSnapshot().Active);
            Assert.AreEqual(4, server.Requests.Count);
        }
        finally { release.TrySetResult(); await blocked; }
    }

    [TestMethod]
    public async Task ProviderFailureAndActiveCancellationReleaseCapacity()
    {
        await using var server = new FakeOpenAiServer();
        var policy = new FileProcessorLlmExecutionPolicy();
        var harness = new LlmHarness(server.Endpoint, enableCache: false, policy);
        using var lifetime = new CancellationTokenSource(Timeout);
        server.Respond = _ => Task.FromResult((400, """{"error":{"message":"invalid input","type":"invalid_request_error"}}"""));
        await Assert.ThrowsAsync<Exception>(() => harness.CompleteAsync(AiFeature.FileProcessor, ct: lifetime.Token));
        Assert.AreEqual(0, policy.GetSnapshot().Active);
        var entered = Signal();
        var release = Signal();
        server.Respond = async request =>
        {
            if (Text(request) == "cancel") { entered.TrySetResult(); await release.Task; }
            return (200, FakeOpenAiServer.Completion());
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var active = harness.CompleteAsync(AiFeature.FileProcessor, text: "cancel", ct: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(lifetime.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => active);
            Assert.AreEqual(0, policy.GetSnapshot().Active);
            await harness.CompleteAsync(AiFeature.FileProcessor, text: "next", ct: lifetime.Token);
            Assert.AreEqual(0, policy.GetSnapshot().Active);
            Assert.AreEqual(0, policy.GetSnapshot().Waiting);
        }
        finally { release.TrySetResult(); }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ReleasedNotificationFollowsCapacityReleaseAndCannotMaskProviderOutcome(bool providerFails, bool observerCancels)
    {
        await using var server = new FakeOpenAiServer();
        server.Respond = _ => Task.FromResult(providerFails
            ? (400, """{"error":{"message":"provider outcome","type":"invalid_request_error"}}""")
            : (200, FakeOpenAiServer.Completion("{\"ok\":true}")));
        var policy = new FileProcessorLlmExecutionPolicy();
        var harness = new LlmHarness(server.Endpoint, enableCache: false, policy);
        var released = 0;
        var activeWhenReleased = -1;
        using var scope = LlmExecutionScope.Begin((state, _) =>
        {
            if (state.Status != LlmFeatureExecutionStatus.Released) return Task.CompletedTask;
            released++;
            activeWhenReleased = policy.GetSnapshot().Active;
            if (observerCancels) throw new OperationCanceledException("observer cleanup cancelled");
            throw new InvalidOperationException("observer cleanup failed");
        });
        if (providerFails)
        {
            var error = await Assert.ThrowsAsync<Exception>(() => harness.CompleteAsync(AiFeature.FileProcessor).WaitAsync(Timeout));
            StringAssert.Contains(error.Message, "provider outcome");
        }
        else
            Assert.AreEqual("{\"ok\":true}", (await harness.CompleteAsync(AiFeature.FileProcessor).WaitAsync(Timeout)).Text);
        Assert.AreEqual(1, released);
        Assert.AreEqual(0, activeWhenReleased);
        Assert.AreEqual(0, policy.GetSnapshot().Active);
    }

    [TestMethod]
    public async Task StreamingHoldsCapacityUntilTheConsumerDisposesItsEnumerator()
    {
        await using var server = new FakeOpenAiServer();
        server.ResponseContentType = "text/event-stream";
        server.Respond = _ => Task.FromResult((200,
            "data: {\"id\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"qwen-local-test\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"));
        var policy = new FileProcessorLlmExecutionPolicy();
        var harness = new LlmHarness(server.Endpoint, enableCache: false, policy);
        using var lifetime = new CancellationTokenSource(Timeout);
        await using var stream = harness.Service.CompleteStreamingForFeatureAsync(AiFeature.FileProcessor,
            [new ChatMessage(ChatRole.User, "stream")], ct: lifetime.Token).GetAsyncEnumerator(lifetime.Token);
        Assert.IsTrue(await stream.MoveNextAsync());
        Assert.AreEqual(1, policy.GetSnapshot().Active);
        server.ResponseContentType = "application/json";
        server.Respond = _ => Task.FromResult((200, FakeOpenAiServer.Completion()));
        var queuedSignal = Signal();
        Task<ChatResponse> next;
        using (LlmExecutionScope.Begin((_, _) => { queuedSignal.TrySetResult(); return Task.CompletedTask; }))
            next = harness.CompleteAsync(AiFeature.FileProcessor, text: "next", ct: lifetime.Token);
        await queuedSignal.Task.WaitAsync(lifetime.Token);
        Assert.AreEqual(1, server.Requests.Count);
        await stream.DisposeAsync();
        await next.WaitAsync(lifetime.Token);
        Assert.AreEqual(2, server.Requests.Count);
        Assert.AreEqual(0, policy.GetSnapshot().Active);
        Assert.AreEqual(0, policy.GetSnapshot().Waiting);
    }
}
