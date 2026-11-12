using System.Text.Json;
using Bakabase.Modules.AI.Components.Providers.OpenAI;
using Microsoft.Extensions.AI;

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
        OpenAIBackgroundCompletion.ApplyThinking(options, mode == PostParserThinkingMode.Low);
    }
}
