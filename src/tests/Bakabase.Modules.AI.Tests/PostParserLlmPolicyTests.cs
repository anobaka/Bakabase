using System.Text.Json;
using Bakabase.Modules.AI.Components.Providers.OpenAI;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace Bakabase.Modules.AI.Tests;

[TestClass]
public class PostParserLlmPolicyTests
{
    [TestMethod]
    [DataRow(200000, 16384, 16384)]
    [DataRow(200000, 2048, 2048)]
    [DataRow(512, 16384, 512)]
    [DataRow(200000, 0, 16384)]
    [DataRow(0, 0, 16384)]
    public async Task CapturesActualReasoningAndBudgetJsonWithoutChangingSampling(int configuredLimit, int overrideLimit, int expected)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        harness.DefaultConfig.MaxTokens = configuredLimit == 0 ? null : configuredLimit;
        await harness.CompleteAsync(maxTokens: overrideLimit == 0 ? null : overrideLimit).WaitAsync(TimeSpan.FromSeconds(10));
        var request = server.Requests.Single();
        Assert.AreEqual("low", request.GetProperty("reasoning_effort").GetString());
        var kwargs = request.GetProperty("chat_template_kwargs");
        Assert.IsTrue(kwargs.GetProperty("enable_thinking").GetBoolean());
        Assert.AreEqual("low", kwargs.GetProperty("reasoning_effort").GetString());
        Assert.IsFalse(kwargs.TryGetProperty("reasoning_budget_tokens", out _));
        Assert.AreEqual(expected, request.GetProperty("max_completion_tokens").GetInt32());
        AssertLocalJsonObjectSchema(request);
        Assert.AreEqual(0.2, request.GetProperty("temperature").GetDouble(), 0.0001);
        Assert.AreEqual(0.8, request.GetProperty("top_p").GetDouble(), 0.0001);
    }

    [TestMethod]
    [DataRow("low", true)]
    [DataRow("none", false)]
    public async Task ExplicitInternalModeReachesActualNonStreamingJson(string effort, bool thinking)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        var raw = new OpenAIProviderFactory(NullLogger<OpenAIProviderFactory>.Instance)
            .CreateClient(harness.Provider, "qwen-local-test");
        using var client = PostParserLlmPolicy.ConfigureTransport(raw, harness.Provider, "qwen-local-test");
        var options = new ChatOptions();
        PostParserLlmPolicy.ApplyOptions(options, true,
            thinking ? PostParserThinkingMode.Low : PostParserThinkingMode.None);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "post")], options);
        var request = server.Requests.Single();
        Assert.AreEqual(effort, request.GetProperty("reasoning_effort").GetString());
        var kwargs = request.GetProperty("chat_template_kwargs");
        Assert.AreEqual(thinking, kwargs.GetProperty("enable_thinking").GetBoolean());
        if (thinking) Assert.AreEqual("low", kwargs.GetProperty("reasoning_effort").GetString());
        else Assert.IsFalse(kwargs.TryGetProperty("reasoning_effort", out _));
        Assert.IsFalse(kwargs.TryGetProperty("reasoning_budget_tokens", out _));
    }

    [TestMethod]
    [DataRow(AiProviderKind.OpenAI, "gpt-4o")]
    [DataRow(AiProviderKind.Gemini, "qwen-proxy")]
    [DataRow(AiProviderKind.DashScope, "qwen-plus")]
    [DataRow(AiProviderKind.Claude, "claude-sonnet")]
    public async Task UnknownOrCloudProvidersDoNotReceiveLocalThinkingParameters(AiProviderKind kind, string model)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        harness.Provider.Kind = kind;
        harness.DefaultConfig.ModelId = model;
        await harness.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var request = server.Requests.Single();
        Assert.IsFalse(request.TryGetProperty("reasoning_effort", out _));
        Assert.IsFalse(request.TryGetProperty("chat_template_kwargs", out _));
        Assert.AreEqual("json_object", request.GetProperty("response_format").GetProperty("type").GetString());
        Assert.AreEqual(16384, request.GetProperty("max_completion_tokens").GetInt32());
    }

    [TestMethod]
    [DataRow("schema")]
    [DataRow("text")]
    [DataRow("unspecified")]
    public async Task LocalQwenPreservesExplicitSchemaAndOtherResponseFormats(string format)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        var raw = new OpenAIProviderFactory(NullLogger<OpenAIProviderFactory>.Instance)
            .CreateClient(harness.Provider, "qwen-local-test");
        using var client = PostParserLlmPolicy.ConfigureTransport(raw, harness.Provider, "qwen-local-test");
        var customSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object", properties = new { title = new { type = "string" } }, additionalProperties = true
        });
        var options = new ChatOptions
        {
            ResponseFormat = format switch
            {
                "schema" => new ChatResponseFormatJson(customSchema, "caller_schema", "caller description"),
                "text" => ChatResponseFormat.Text,
                _ => null
            }
        };
        var originalFormat = options.ResponseFormat;
        PostParserLlmPolicy.ApplyOptions(options, true);
        Assert.AreSame(originalFormat, options.ResponseFormat);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "post")], options);
        var request = server.Requests.Single();
        if (format == "unspecified") Assert.IsFalse(request.TryGetProperty("response_format", out _));
        else if (format == "text") Assert.AreEqual("text", request.GetProperty("response_format").GetProperty("type").GetString());
        else
        {
            var wire = request.GetProperty("response_format");
            Assert.AreEqual("json_schema", wire.GetProperty("type").GetString());
            var wrapper = wire.GetProperty("json_schema");
            Assert.AreEqual("caller_schema", wrapper.GetProperty("name").GetString());
            Assert.AreEqual("caller description", wrapper.GetProperty("description").GetString());
            Assert.IsTrue(JsonElement.DeepEquals(customSchema, wrapper.GetProperty("schema")));
            Assert.IsFalse(wrapper.TryGetProperty("strict", out _));
        }
    }

    [TestMethod]
    [DataRow(true, "qwen-local-test")]
    [DataRow(false, "qwen-local-test")]
    [DataRow(true, "gpt-4o")]
    [DataRow(false, "gpt-4o")]
    public async Task FeatureOverridesPropagateSchemaOnlyForLocalQwenJsonRequests(bool useJson, string model)
    {
        await using var server = new FakeOpenAiServer();
        var harness = new LlmHarness(server.Endpoint);
        harness.DefaultConfig.ModelId = model;
        var schema = JsonSerializer.Deserialize<JsonElement>("""
            {"type":"object","properties":{"resources":{"type":"array","items":{"type":"object","properties":{"password":{"type":["string","null"]},"extraction":{"type":"object"}},"required":["password","extraction"],"additionalProperties":false}}},"required":["resources"],"additionalProperties":false}
            """);
        await harness.Service.CompleteForFeatureAsync(AiFeature.PostParser,
            [new ChatMessage(ChatRole.User, "post")], new LlmModelParameters
            {
                UseJsonResponseFormat = useJson, JsonResponseSchema = schema,
                MaxTokens = 2048, Temperature = 0.3f, TopP = 0.7f
            }).WaitAsync(TimeSpan.FromSeconds(5));
        var request = server.Requests.Single();
        Assert.AreEqual(2048, request.GetProperty("max_completion_tokens").GetInt32());
        Assert.AreEqual(0.3, request.GetProperty("temperature").GetDouble(), 0.0001);
        Assert.AreEqual(0.7, request.GetProperty("top_p").GetDouble(), 0.0001);
        if (!useJson) Assert.IsFalse(request.TryGetProperty("response_format", out _));
        else if (model == "gpt-4o")
            Assert.AreEqual("json_object", request.GetProperty("response_format").GetProperty("type").GetString());
        else
        {
            var format = request.GetProperty("response_format");
            Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
            var wrapper = format.GetProperty("json_schema");
            Assert.AreEqual("post_analysis", wrapper.GetProperty("name").GetString());
            Assert.IsTrue(JsonElement.DeepEquals(schema, wrapper.GetProperty("schema")));
            Assert.IsFalse(wrapper.TryGetProperty("strict", out _));
        }
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:11435/v1", true)]
    [DataRow("http://localhost:11435/v1", true)]
    [DataRow("http://10.1.2.3:11435/v1", true)]
    [DataRow("http://172.16.1.1:11435/v1", true)]
    [DataRow("http://172.31.255.255:11435/v1", true)]
    [DataRow("http://192.168.3.134:11435/v1", true)]
    [DataRow("http://[fd00::1234]:11435/v1", true)]
    [DataRow("https://api.openai.com/v1", false)]
    [DataRow("https://dashscope.aliyuncs.com/compatible-mode/v1", false)]
    [DataRow("http://172.32.0.1/v1", false)]
    [DataRow("http://8.8.8.8/v1", false)]
    public void RecognizesOnlyExplicitLocalQwenTargets(string endpoint, bool expected)
    {
        Assert.AreEqual(expected, PostParserLlmPolicy.UsesLocalQwenTemplate(AiProviderKind.OpenAI, "my-Qwen-model", new Uri(endpoint)));
        Assert.IsFalse(PostParserLlmPolicy.UsesLocalQwenTemplate(AiProviderKind.Ollama, "qwen", new Uri(endpoint)));
    }

    [TestMethod]
    [DataRow("low", true)]
    [DataRow("none", false)]
    public async Task StreamingTemplateOverridePreservesOtherCallerOptionsAndRawSdkParameters(string effort, bool thinking)
    {
        await using var server = new FakeOpenAiServer();
        server.ResponseContentType = "text/event-stream";
        server.Respond = _ => Task.FromResult((200,
            "data: {\"id\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"qwen-local-test\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"{}\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"));
        var harness = new LlmHarness(server.Endpoint);
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            AdditionalProperties = new() { ["chat_template_kwargs"] = new Dictionary<string, object?>
            {
                ["option_custom"] = 2, ["enable_thinking"] = !thinking, ["reasoning_effort"] = "xhigh"
            } },
            Reasoning = thinking ? null : new ReasoningOptions { Effort = ReasoningEffort.None },
            RawRepresentationFactory = _ =>
            {
                var raw = new ChatCompletionOptions { Temperature = 0.1f, MaxOutputTokenCount = 99 };
#pragma warning disable OPENAI001
                raw.ReasoningEffortLevel = OpenAI.Chat.ChatReasoningEffortLevel.High;
#pragma warning restore OPENAI001
#pragma warning disable SCME0001
                raw.Patch.Set("$.chat_template_kwargs"u8, "{\"raw_custom\":1,\"enable_thinking\":true,\"reasoning_effort\":\"xhigh\"}"u8);
#pragma warning restore SCME0001
                return raw;
            }
        };
        await foreach (var _ in harness.Service.CompleteStreamingForFeatureAsync(AiFeature.PostParser,
                           [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "post")], options)) { }
        var request = server.Requests.Single();
        var kwargs = request.GetProperty("chat_template_kwargs");
        Assert.AreEqual(thinking, kwargs.GetProperty("enable_thinking").GetBoolean());
        Assert.AreEqual(1, kwargs.GetProperty("raw_custom").GetInt32());
        Assert.AreEqual(2, kwargs.GetProperty("option_custom").GetInt32());
        Assert.AreEqual(effort, request.GetProperty("reasoning_effort").GetString());
        if (thinking) Assert.AreEqual("low", kwargs.GetProperty("reasoning_effort").GetString());
        else Assert.IsFalse(kwargs.TryGetProperty("reasoning_effort", out _));
        Assert.IsFalse(kwargs.TryGetProperty("reasoning_budget_tokens", out _));
        Assert.AreEqual(99, request.GetProperty("max_completion_tokens").GetInt32());
        Assert.AreEqual(0.1, request.GetProperty("temperature").GetDouble(), 0.0001);
        AssertLocalJsonObjectSchema(request);
    }

    [TestMethod]
    public async Task PostParserDoesNotRetry503AndFailureIsNotCached()
    {
        await using var server = new FakeOpenAiServer();
        server.Respond = _ => Task.FromResult((503, "{\"error\":{\"message\":\"busy\"}}"));
        var harness = new LlmHarness(server.Endpoint);
        await Assert.ThrowsAsync<Exception>(() => harness.CompleteAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(1, server.Requests.Count);
        Assert.AreEqual(0, harness.Cache.Entries.Count);
        Assert.AreEqual(0, harness.Coordinator.PendingKeyCount);
        server.Respond = _ => Task.FromResult((200, FakeOpenAiServer.Completion()));
        await harness.CompleteAsync();
        Assert.AreEqual(2, server.Requests.Count);
    }

    [TestMethod]
    public async Task OtherFeaturesKeepConfiguredBudgetAndSdkRetryWithoutReasoningOverride()
    {
        await using var server = new FakeOpenAiServer();
        server.Respond = _ => Task.FromResult(server.Requests.Count == 1
            ? (503, "{\"error\":{\"message\":\"busy\"}}") : (200, FakeOpenAiServer.Completion()));
        var harness = new LlmHarness(server.Endpoint);
        await harness.CompleteAsync(AiFeature.Translation).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(2, server.Requests.Count);
        var request = server.Requests.First();
        Assert.IsFalse(request.TryGetProperty("reasoning_effort", out _));
        Assert.IsFalse(request.TryGetProperty("chat_template_kwargs", out _));
        Assert.AreEqual(200000, request.GetProperty("max_completion_tokens").GetInt32());
    }

    [TestMethod]
    public async Task ConfiguredTransportTimeoutCancelsOnceWithoutRetrying()
    {
        Assert.AreEqual(TimeSpan.FromMinutes(5), PostParserLlmPolicy.NetworkTimeout);
        await using var server = new FakeOpenAiServer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Respond = async _ => { await release.Task; return (200, FakeOpenAiServer.Completion()); };
        var harness = new LlmHarness(server.Endpoint);
        var raw = new OpenAIProviderFactory(NullLogger<OpenAIProviderFactory>.Instance).CreateClient(harness.Provider, "local-test");
        using var client = PostParserLlmPolicy.ConfigureTransport(raw, harness.Provider, "local-test", TimeSpan.FromMilliseconds(200));
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "post")]).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.AreEqual(1, server.Requests.Count);
        }
        finally { release.TrySetResult(); }
    }

    private static void AssertLocalJsonObjectSchema(JsonElement request)
    {
        var format = request.GetProperty("response_format");
        Assert.AreEqual("json_schema", format.GetProperty("type").GetString());
        var wrapper = format.GetProperty("json_schema");
        Assert.AreEqual("post_analysis", wrapper.GetProperty("name").GetString());
        var schema = wrapper.GetProperty("schema");
        Assert.AreEqual("object", schema.GetProperty("type").GetString());
        Assert.AreEqual(1, schema.EnumerateObject().Count());
        Assert.IsFalse(wrapper.TryGetProperty("strict", out _));
    }
}
