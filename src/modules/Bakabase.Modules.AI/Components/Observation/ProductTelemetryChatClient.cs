using System.Diagnostics;
using System.Runtime.CompilerServices;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Observation;

/// <summary>Observes actual inference, inside the cache and outside the provider.
/// No prompts, model names, endpoint URLs, responses or provider configuration IDs leave this layer.</summary>
internal sealed class ProductTelemetryChatClient(
    IChatClient innerClient,
    IProductTelemetry telemetry,
    ITelemetryActorAccessor? actorAccessor,
    AiProviderKind providerKind,
    AiFeature? feature) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var actor = AiRequestTelemetry.Actor(actorAccessor);
        var attemptId = Guid.NewGuid().ToString("D");
        var started = Stopwatch.GetTimestamp();
        var outcome = "failed";
        ChatResponse? response = null;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
            outcome = response.FinishReason == ChatFinishReason.Length ? "partial" : "success";
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            AiRequestTelemetry.Track(telemetry, actor, providerKind, feature, "llm", outcome,
                started, attemptId, false, response?.Usage);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var actor = AiRequestTelemetry.Actor(actorAccessor);
        var attemptId = Guid.NewGuid().ToString("D");
        var started = Stopwatch.GetTimestamp();
        var outcome = "interrupted";
        var partial = false;
        UsageDetails? usage = null;
        try
        {
            IAsyncEnumerator<ChatResponseUpdate> iterator;
            try
            {
                iterator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = "cancelled";
                throw;
            }
            catch
            {
                outcome = "failed";
                throw;
            }
            await using var ownedIterator = iterator;
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await iterator.MoveNextAsync();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    outcome = "cancelled";
                    throw;
                }
                catch
                {
                    outcome = "failed";
                    throw;
                }

                if (!hasNext)
                {
                    outcome = partial ? "partial" : "success";
                    break;
                }

                var update = iterator.Current;
                partial |= update.FinishReason == ChatFinishReason.Length;
                foreach (var content in update.Contents.OfType<UsageContent>())
                {
                    usage ??= new UsageDetails();
                    usage.InputTokenCount = Add(usage.InputTokenCount, content.Details.InputTokenCount);
                    usage.OutputTokenCount = Add(usage.OutputTokenCount, content.Details.OutputTokenCount);
                    usage.TotalTokenCount = Add(usage.TotalTokenCount, content.Details.TotalTokenCount);
                }
                yield return update;
            }
        }
        finally
        {
            // Disposing a partially consumed stream is not a successful completion.
            if (outcome == "interrupted" && cancellationToken.IsCancellationRequested) outcome = "cancelled";
            AiRequestTelemetry.Track(telemetry, actor, providerKind, feature, "llm", outcome,
                started, attemptId, true, usage);
        }
    }

    private static long? Add(long? total, long? value) => value is >= 0
        ? Math.Min(1_000_000_000_000L, (total ?? 0) + Math.Min(value.Value, 1_000_000_000_000L))
        : total;
}

internal static class AiRequestTelemetry
{
    internal static TelemetryActor? Actor(ITelemetryActorAccessor? accessor)
    {
        try { return accessor?.CurrentActor; }
        catch { return null; }
    }

    internal static void Track(IProductTelemetry? telemetry, TelemetryActor? actor,
        AiProviderKind provider, AiFeature? feature, string requestKind, string outcome,
        long started, string attemptId, bool streaming, UsageDetails? usage = null)
    {
        if (telemetry == null) return;
        try
        {
            var properties = new Dictionary<string, object?>
            {
                ["provider_type"] = provider switch
                {
                    AiProviderKind.OpenAI => "openai", AiProviderKind.Claude => "claude",
                    AiProviderKind.Ollama => "ollama", AiProviderKind.DashScope => "dashscope",
                    AiProviderKind.Gemini => "gemini", AiProviderKind.StableDiffusionWebUI => "stable_diffusion_webui",
                    AiProviderKind.ComfyUI => "comfyui", AiProviderKind.HttpCustom => "http_custom", _ => "unknown"
                },
                ["ai_feature"] = requestKind == "aigc" ? "aigc" : feature switch
                {
                    AiFeature.Default => "default", AiFeature.Enhancer => "enhancer",
                    AiFeature.Translation => "translation", AiFeature.FileProcessor => "file_processor",
                    AiFeature.PostParser => "post_parser", AiFeature.Chat => "chat", _ => "unknown"
                },
                ["request_kind"] = requestKind,
                ["outcome"] = outcome,
                ["duration_ms"] = (long) Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                ["attempt_id"] = attemptId,
                ["is_streaming"] = streaming,
                ["token_usage_available"] = usage is { InputTokenCount: not null } or
                    { OutputTokenCount: not null } or { TotalTokenCount: not null }
            };
            if (usage?.InputTokenCount is >= 0) properties["input_tokens"] = usage.InputTokenCount;
            if (usage?.OutputTokenCount is >= 0) properties["output_tokens"] = usage.OutputTokenCount;
            if (usage?.TotalTokenCount is >= 0) properties["total_tokens"] = usage.TotalTokenCount;
            telemetry.Track("ai_request_completed", properties, actor);
        }
        catch { /* Product analytics never changes a provider result. */ }
    }
}
