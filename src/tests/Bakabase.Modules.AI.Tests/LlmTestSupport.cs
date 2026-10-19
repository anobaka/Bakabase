using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.AI.Components.Cache;
using Bakabase.Modules.AI.Components.Observation;
using Bakabase.Modules.AI.Components.Providers.OpenAI;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.AI.Tests;

internal sealed class FakeOpenAiServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _handlers = [];
    private readonly Task _accept;
    internal readonly ConcurrentQueue<JsonElement> Requests = [];
    internal readonly TaskCompletionSource FirstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Func<JsonElement, Task<(int Status, string Body)>> Respond { get; set; } = _ => Task.FromResult((200, Completion()));
    internal string ResponseContentType { get; set; } = "application/json";
    internal string Endpoint { get; }

    internal FakeOpenAiServer()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        Endpoint = $"http://127.0.0.1:{port}/v1";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _accept = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
                _handlers.Add(HandleAsync(await _listener.GetContextAsync().WaitAsync(_stop.Token)));
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream);
            using var json = JsonDocument.Parse(await reader.ReadToEndAsync());
            var request = json.RootElement.Clone();
            Requests.Enqueue(request);
            FirstRequest.TrySetResult();
            var response = await Respond(request).WaitAsync(_stop.Token);
            context.Response.StatusCode = response.Status;
            context.Response.ContentType = ResponseContentType;
            var bytes = Encoding.UTF8.GetBytes(response.Body);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
        finally { context.Response.Close(); }
    }

    internal static string Completion(string text = "{}", string finishReason = "stop") => JsonSerializer.Serialize(new
    {
        id = "test-completion", @object = "chat.completion", created = 1, model = "local-test",
        choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = finishReason } },
        usage = new { prompt_tokens = 10, completion_tokens = 4, total_tokens = 14 }
    });

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Close();
        await _accept;
        await Task.WhenAll(_handlers);
        _stop.Dispose();
    }
}

internal sealed class MemoryLlmCache : ILlmCacheService
{
    internal readonly ConcurrentDictionary<string, LlmCallCacheEntryDbModel> Entries = [];
    public Task<LlmCallCacheEntryDbModel?> GetAsync(string cacheKey, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Entries.GetValueOrDefault(cacheKey));
    }
    public Task SetAsync(LlmCallCacheEntryDbModel entry, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Entries[entry.CacheKey] = entry;
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<LlmCallCacheEntryDbModel>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LlmCallCacheEntryDbModel>>(Entries.Values.ToList());
    public Task DeleteAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task ClearAllAsync(CancellationToken ct = default) { Entries.Clear(); return Task.CompletedTask; }
    public Task CleanupExpiredAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class LlmHarness
{
    internal readonly MemoryLlmCache Cache = new();
    internal readonly LlmCacheRequestCoordinator Coordinator = new();
    internal readonly AiFeatureConfigDbModel DefaultConfig = new()
    {
        Feature = AiFeature.Default, ProviderConfigId = 2, ModelId = "qwen-local-test", MaxTokens = 200000,
        Temperature = 0.2f, TopP = 0.8f
    };
    internal readonly AiProviderDbModel Provider;
    internal LlmService Service { get; }

    internal LlmHarness(string endpoint, bool enableCache = true)
    {
        Provider = new() { Id = 2, Kind = AiProviderKind.OpenAI, Endpoint = endpoint, ApiKey = "test-key", LlmEnabled = true };
        var providers = new Providers(Provider);
        var options = new Options(new AiModuleOptions { EnableCache = enableCache });
        var usage = new Usage();
        Service = new LlmService(providers, providers, usage, Cache, new LlmQuotaManager(options, usage),
            new Features(DefaultConfig), options, Coordinator, NullLogger<LlmService>.Instance);
    }

    internal Task<ChatResponse> CompleteAsync(AiFeature feature = AiFeature.PostParser, int? maxTokens = null,
        bool json = true, string text = "post", CancellationToken ct = default) => Service.CompleteForFeatureAsync(feature,
        [new ChatMessage(ChatRole.System, "Extract JSON"), new ChatMessage(ChatRole.User, text)],
        new LlmModelParameters { MaxTokens = maxTokens, UseJsonResponseFormat = json }, ct);

    private sealed class Options(AiModuleOptions value) : IOptionsMonitor<AiModuleOptions>
    {
        public AiModuleOptions CurrentValue => value;
        public AiModuleOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AiModuleOptions, string?> listener) => null;
    }

    private sealed class Features(AiFeatureConfigDbModel defaultConfig) : IAiFeatureService
    {
        public Task<AiFeatureConfigDbModel?> GetConfigAsync(AiFeature feature, CancellationToken ct = default) =>
            Task.FromResult<AiFeatureConfigDbModel?>(feature == AiFeature.Default ? defaultConfig : new() { Feature = feature, UseDefault = true });
        public Task<IReadOnlyList<AiFeatureConfigDbModel>> GetAllConfigsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AiFeatureConfigDbModel> SaveConfigAsync(AiFeatureConfigDbModel config, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteConfigAsync(AiFeature feature, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Providers(AiProviderDbModel config) : IAiProviderService, ILlmProviderService
    {
        public Task<AiProviderDbModel?> GetAsync(int id, CancellationToken ct = default) => Task.FromResult<AiProviderDbModel?>(config);
        public IChatClient CreateChatClient(AiProviderDbModel provider, string modelId) =>
            new OpenAIProviderFactory(NullLogger<OpenAIProviderFactory>.Instance).CreateClient(provider, modelId);
        public Task<IReadOnlyList<AiProviderDbModel>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AiProviderDbModel> AddAsync(AiProviderAddInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AiProviderDbModel> UpdateAsync(int id, AiProviderUpdateInputModel input, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AiProviderTestResult> TestAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
        public IReadOnlyList<AiProviderKindInfo> GetKinds() => throw new NotSupportedException();
        public Task<IReadOnlyList<AiProviderDbModel>> GetEnabledLlmProvidersAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LlmModelInfo>> GetModelsAsync(int providerId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Usage : ILlmUsageService
    {
        public Task TrackAsync(LlmUsageLogDbModel log, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LlmUsageLogDbModel>> SearchAsync(LlmUsageSearchCriteria criteria, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LlmUsageSummary> GetSummaryAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> GetTodayTokenUsageAsync(CancellationToken ct = default) => Task.FromResult(0L);
        public Task<long> GetMonthTokenUsageAsync(CancellationToken ct = default) => Task.FromResult(0L);
    }
}
