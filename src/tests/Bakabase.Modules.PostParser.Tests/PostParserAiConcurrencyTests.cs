using System.Collections.Concurrent;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Extensions;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.PostParser.Tests;

[TestClass]
public class PostParserAiConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task AvailabilityAndExtractionShareOneLimitAcrossScopesAndReportTheirOwnStages()
    {
        var llm = new BlockingLlm();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILlmService>(llm);
        services.AddPostParserCapabilities();
        await using var provider = services.BuildServiceProvider();
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstStages = new List<string>();
        var secondStages = new List<string>();
        var first = AnalyzeAsync();
        await llm.Started.Task.WaitAsync(Timeout);
        var second = ExtractAsync();
        CollectionAssert.AreEqual(new[] {"checkingAvailability"}, firstStages);
        CollectionAssert.AreEqual(new[] {"waitingForAi"}, secondStages);
        Assert.AreEqual(1, llm.Calls);
        llm.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(Timeout);
        Assert.AreEqual(2, llm.Calls);
        Assert.AreEqual(1, llm.MaxActive);
        CollectionAssert.AreEqual(new[] {"waitingForAi", "extracting"}, secondStages);
        await PostParserExecutionScope.ReportStageAsync("outside");
        Assert.AreEqual(1, firstStages.Count);
        Assert.AreEqual(2, secondStages.Count);

        async Task AnalyzeAsync()
        {
            using var progress = PostParserExecutionScope.Begin((stage, _) => { firstStages.Add(stage); return Task.CompletedTask; });
            await firstScope.ServiceProvider.GetRequiredService<IPostAvailabilityAnalyzer>().AnalyzeAsync(new PostContent());
        }
        async Task ExtractAsync()
        {
            using var progress = PostParserExecutionScope.Begin((stage, _) => { secondStages.Add(stage); return Task.CompletedTask; });
            await secondScope.ServiceProvider.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(new PostContent());
        }
    }

    [TestMethod]
    public async Task LiveLimitCanIncreaseWhileAnotherAiCallRemainsInFlight()
    {
        var limit = 1;
        var concurrency = new PostParserAiConcurrency(() => Volatile.Read(ref limit));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = concurrency.ExecuteAsync("extracting", () => release.Task);
        var second = concurrency.ExecuteAsync("checkingAvailability", () => Task.FromResult(2));
        Assert.IsFalse(second.IsCompleted);
        Volatile.Write(ref limit, 2);
        Assert.AreEqual(2, await second.WaitAsync(Timeout));
        Assert.IsFalse(first.IsCompleted);
        release.SetResult(1);
        Assert.AreEqual(1, await first.WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task CancelledQueuedCallNeverInvokesAiAndFailureDoesNotKeepCapacity()
    {
        var concurrency = new PostParserAiConcurrency();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = concurrency.ExecuteAsync("extracting", () => release.Task);
        using var cancellation = new CancellationTokenSource();
        var invoked = false;
        var second = concurrency.ExecuteAsync("extracting", () => { invoked = true; return Task.FromResult(2); }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => second);
        Assert.IsFalse(invoked);
        release.SetException(new InvalidOperationException("model failed"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
        Assert.AreEqual(3, await concurrency.ExecuteAsync("extracting", () => Task.FromResult(3)).WaitAsync(Timeout));
    }

    [TestMethod]
    public async Task NestedObserversRestoreParentAndConcurrentExecutionsDoNotMixStages()
    {
        var parent = new List<string>();
        var child = new ConcurrentBag<string>();
        using (PostParserExecutionScope.Begin((stage, _) => { parent.Add(stage); return Task.CompletedTask; }))
        {
            await PostParserExecutionScope.ReportStageAsync("parent-before");
            await Task.WhenAll(ObserveChild("a"), ObserveChild("b"));
            await PostParserExecutionScope.ReportStageAsync("parent-after");
        }
        await PostParserExecutionScope.ReportStageAsync("outside");
        CollectionAssert.AreEqual(new[] {"parent-before", "parent-after"}, parent);
        CollectionAssert.AreEquivalent(new[] {"a", "b"}, child.ToArray());

        async Task ObserveChild(string name)
        {
            using var scope = PostParserExecutionScope.Begin((stage, _) => { child.Add(stage); return Task.CompletedTask; });
            await Task.Yield();
            await PostParserExecutionScope.ReportStageAsync(name);
        }
    }

    private sealed class BlockingLlm : ILlmService
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public int MaxActive;
        private int _active;

        public async Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref _active));
            Started.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(ct);
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public Task<ChatResponse> CompleteAsync(int providerConfigId, string modelId, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages,
            LlmModelParameters? parameters = null, AiFeature? feature = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature feature,
            IList<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
