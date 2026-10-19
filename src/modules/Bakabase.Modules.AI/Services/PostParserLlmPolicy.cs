using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Bakabase.Modules.AI.Components.Providers.OpenAI;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Bakabase.Modules.AI.Services;

internal enum PostParserThinkingMode
{
    None,
    Low
}

/// <summary>Bounded, non-retrying inference for background post extraction.</summary>
internal static class PostParserLlmPolicy
{
    internal const int MaxOutputTokens = 16384;
    internal static readonly TimeSpan NetworkTimeout = TimeSpan.FromMinutes(5);
    private static readonly ChatResponseFormatJson JsonObjectFormat = new(
        JsonSerializer.SerializeToElement(new { type = "object" }), "post_analysis");

    internal static int ResolveMaxTokens(params int?[] limits) => limits
        .Where(limit => limit is > 0)
        .Select(limit => limit!.Value)
        .Append(MaxOutputTokens)
        .Min();

    internal static void ApplyOptions(ChatOptions options, bool usesLocalQwenTemplate,
        PostParserThinkingMode? thinkingMode = null)
    {
        options.MaxOutputTokens = ResolveMaxTokens(options.MaxOutputTokens);
        if (!usesLocalQwenTemplate) return;
        options.Temperature ??= 0;
        // The verified llama.cpp Qwen handler skips grammar for the empty schema
        // behind json_object. A nonempty object schema activates JSON constraints.
        if (options.ResponseFormat is ChatResponseFormatJson { Schema: null })
            options.ResponseFormat = JsonObjectFormat;
        // Keep limited reasoning for extraction accuracy. Explicit None remains
        // available to internal callers, without changing the server's defaults.
        var mode = thinkingMode ?? (options.Reasoning?.Effort == ReasoningEffort.None
            ? PostParserThinkingMode.None : PostParserThinkingMode.Low);
        options.Reasoning ??= new ReasoningOptions();
        options.Reasoning.Effort = mode == PostParserThinkingMode.None ? ReasoningEffort.None : ReasoningEffort.Low;
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties.TryGetValue(OpenAIChatTemplateClient.TemplateKwargsKey, out var existing);
        var kwargs = OpenAIChatTemplateClient.MergeKwargs(existing);
        kwargs["enable_thinking"] = mode != PostParserThinkingMode.None;
        // Override CLI/saved template effort for Low. The template does not
        // accept "none" here, so its None mode controls only enable_thinking.
        if (mode == PostParserThinkingMode.Low) kwargs["reasoning_effort"] = "low";
        else kwargs.Remove("reasoning_effort");
        options.AdditionalProperties[OpenAIChatTemplateClient.TemplateKwargsKey] = kwargs;
    }

    internal static bool UsesLocalQwenTemplate(AiProviderDbModel config, string modelId, IChatClient client)
    {
        if (config.Kind != AiProviderKind.OpenAI || !modelId.Contains("qwen", StringComparison.OrdinalIgnoreCase) ||
            client.GetService<ChatClient>() is not { } openAiClient) return false;
#pragma warning disable OPENAI001 // Inspect the effective endpoint selected by the provider factory.
        return UsesLocalQwenTemplate(config.Kind, modelId, openAiClient.Endpoint);
#pragma warning restore OPENAI001
    }

    internal static bool UsesLocalQwenTemplate(AiProviderKind kind, string modelId, Uri endpoint)
    {
        if (kind != AiProviderKind.OpenAI || !modelId.Contains("qwen", StringComparison.OrdinalIgnoreCase)) return false;
        if (endpoint.IsLoopback) return true;
        if (!IPAddress.TryParse(endpoint.DnsSafeHost, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc;
    }

    internal static IChatClient ConfigureTransport(IChatClient client, AiProviderDbModel config, string modelId,
        TimeSpan? networkTimeout = null)
    {
        if (client.GetService<ChatClient>() is not { } openAiClient) return client;

        var options = new OpenAIClientOptions
        {
#pragma warning disable OPENAI001 // Preserve the endpoint resolved by the provider factory.
            Endpoint = openAiClient.Endpoint,
#pragma warning restore OPENAI001
            NetworkTimeout = networkTimeout ?? NetworkTimeout,
            // Retrying a timed-out generation repeats expensive work. Let the
            // task surface the failure and leave an explicit retry to the user.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        };
        IChatClient configured = new OpenAIClient(new ApiKeyCredential(config.ApiKey!), options).GetChatClient(modelId).AsIChatClient();
        return UsesLocalQwenTemplate(config, modelId, client) ? new OpenAIChatTemplateClient(configured) : configured;
    }
}
