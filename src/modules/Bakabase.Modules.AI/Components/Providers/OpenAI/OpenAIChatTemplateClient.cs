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
            // Only the feature's explicit thinking switch takes precedence.
#pragma warning disable SCME0001 // SDK-supported JSON patch for llama.cpp's nonstandard request field.
            JsonElement? existingKwargs = null;
            if (rawOptions.Patch.TryGetJson("$.chat_template_kwargs"u8, out var existingJson))
            {
                using var existing = JsonDocument.Parse(existingJson);
                existingKwargs = existing.RootElement.Clone();
            }
            var merged = MergeKwargs(existingKwargs, kwargs);
            rawOptions.Patch.Set("$.chat_template_kwargs"u8, JsonSerializer.SerializeToUtf8Bytes(merged));
#pragma warning restore SCME0001
#pragma warning disable OPENAI001 // The target local template accepts the per-request none setting.
            rawOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.None;
#pragma warning restore OPENAI001
            rawOptions.MaxOutputTokenCount = PostParserLlmPolicy.ResolveMaxTokens(
                rawOptions.MaxOutputTokenCount, configured.MaxOutputTokens);
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
        merged["enable_thinking"] = false;
        return merged;
    }
}
