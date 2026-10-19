using System.Text.Json;
using Bakabase.Modules.AI.Services;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Bakabase.Modules.AI.Components.Providers.OpenAI;

/// <summary>
/// Applies a known local chat-template override at the SDK boundary. The outer
/// cache sees the serializable kwargs, before this layer creates an SDK factory.
/// </summary>
internal sealed class OpenAIChatTemplateClient(IChatClient innerClient) : DelegatingChatClient(innerClient)
{
    internal const string TemplateKwargsKey = "chat_template_kwargs";

    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        base.GetResponseAsync(messages, ConfigureSdkOptions(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        base.GetStreamingResponseAsync(messages, ConfigureSdkOptions(options), cancellationToken);

    private static ChatOptions? ConfigureSdkOptions(ChatOptions? options)
    {
        if (options?.AdditionalProperties?.TryGetValue(TemplateKwargsKey, out var kwargs) != true) return options;
        var configured = options.Clone();
        var originalFactory = options.RawRepresentationFactory;
        configured.RawRepresentationFactory = client =>
        {
            var rawOptions = originalFactory?.Invoke(client) as ChatCompletionOptions ?? new ChatCompletionOptions();
            // Keep other caller-supplied template arguments and raw SDK settings.
            // The feature's explicit thinking mode and effort take precedence.
#pragma warning disable SCME0001 // SDK-supported JSON patch for llama.cpp's nonstandard request field.
            JsonElement? existingKwargs = null;
            if (rawOptions.Patch.TryGetJson("$.chat_template_kwargs"u8, out var existingJson))
            {
                using var existing = JsonDocument.Parse(existingJson);
                existingKwargs = existing.RootElement.Clone();
            }
            var merged = MergeKwargs(existingKwargs, kwargs);
            if (configured.Reasoning?.Effort == ReasoningEffort.None) merged.Remove("reasoning_effort");
            rawOptions.Patch.Set("$.chat_template_kwargs"u8, JsonSerializer.SerializeToUtf8Bytes(merged));
#pragma warning restore SCME0001
#pragma warning disable OPENAI001 // Match the feature's explicit effort instead of conflicting raw SDK defaults.
            rawOptions.ReasoningEffortLevel = configured.Reasoning?.Effort == ReasoningEffort.None
                ? ChatReasoningEffortLevel.None : ChatReasoningEffortLevel.Low;
#pragma warning restore OPENAI001
            rawOptions.MaxOutputTokenCount = PostParserLlmPolicy.ResolveMaxTokens(
                rawOptions.MaxOutputTokenCount, configured.MaxOutputTokens);
            if (configured.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } jsonFormat)
            {
                // M.E.AI's default mapping transforms schemas for OpenAI strict
                // mode, which would forbid arbitrary fields in a plain object.
                // Keep the actual caller schema intact for this local endpoint.
                bool? strict = configured.AdditionalProperties?.TryGetValue("strict", out var value) == true && value is bool flag
                    ? flag : null;
                rawOptions.ResponseFormat ??= global::OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonFormat.SchemaName ?? "json_schema", BinaryData.FromString(schema.GetRawText()),
                    jsonFormat.SchemaDescription, strict);
            }
            return rawOptions;
        };
        return configured;
    }

    internal static Dictionary<string, object?> MergeKwargs(params object?[] sources)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (source is null) continue;
            var json = JsonSerializer.SerializeToElement(source);
            if (json.ValueKind == JsonValueKind.Null) continue;
            if (json.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("chat_template_kwargs must be a JSON object.");
            foreach (var property in json.EnumerateObject()) merged[property.Name] = property.Value.Clone();
        }
        return merged;
    }
}
