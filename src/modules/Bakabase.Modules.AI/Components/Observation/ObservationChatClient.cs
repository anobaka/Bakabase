using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;

namespace Bakabase.Modules.AI.Components.Observation;

public class ObservationChatClient(
    IChatClient innerClient,
    ILlmUsageService usageService,
    int providerConfigId,
    string modelId,
    string? feature = null,
    bool auditRequestContent = false
) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = new CancellationToken())
    {
        var chatMessages = new List<ChatMessage>(messages);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
            sw.Stop();

            var log = new LlmUsageLogDbModel
            {
                ProviderConfigId = providerConfigId,
                ModelId = modelId,
                Feature = feature,
                InputTokens = (int)(response.Usage?.InputTokenCount ?? 0),
                OutputTokens = (int)(response.Usage?.OutputTokenCount ?? 0),
                TotalTokens = (int)(response.Usage?.TotalTokenCount ?? 0),
                DurationMs = (int)sw.ElapsedMilliseconds,
                CacheHit = false,
                Status = LlmCallStatus.Success,
                CreatedAt = DateTime.Now
            };

            if (auditRequestContent)
            {
                log.RequestSummary = SummarizeMessages(chatMessages);
                log.ResponseSummary = response.Text;
            }

            await usageService.TrackAsync(log, CancellationToken.None);

            return response;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await TrackError(sw, LlmCallStatus.Cancelled, "Operation cancelled", chatMessages);
            throw;
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            await TrackError(sw, LlmCallStatus.Timeout, ex.Message, chatMessages);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            await TrackError(sw, LlmCallStatus.Error, ex.Message, chatMessages);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var chatMessages = new List<ChatMessage>(messages);
        var sw = Stopwatch.StartNew();
        var responseText = new StringBuilder();
        int inputTokens = 0, outputTokens = 0, totalTokens = 0;
        var completed = false;

        // C# allows yield inside try-finally (no catch), exceptions propagate to caller
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
            {
                if (update.Text != null)
                    responseText.Append(update.Text);

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        inputTokens += (int)(usageContent.Details.InputTokenCount ?? 0);
                        outputTokens += (int)(usageContent.Details.OutputTokenCount ?? 0);
                        totalTokens += (int)(usageContent.Details.TotalTokenCount ?? 0);
                    }
                }

                yield return update;
            }

            completed = true;
        }
        finally
        {
            sw.Stop();
            var log = new LlmUsageLogDbModel
            {
                ProviderConfigId = providerConfigId,
                ModelId = modelId,
                Feature = feature,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = totalTokens,
                DurationMs = (int)sw.ElapsedMilliseconds,
                CacheHit = false,
                Status = completed ? LlmCallStatus.Success : LlmCallStatus.Error,
                CreatedAt = DateTime.Now
            };

            if (auditRequestContent)
            {
                log.RequestSummary = SummarizeMessages(chatMessages);
                log.ResponseSummary = responseText.Length > 0 ? responseText.ToString() : null;
            }

            await usageService.TrackAsync(log, CancellationToken.None);
        }
    }

    private async Task TrackError(Stopwatch sw, LlmCallStatus status, string? errorMessage,
        IList<ChatMessage> chatMessages)
    {
        var log = new LlmUsageLogDbModel
        {
            ProviderConfigId = providerConfigId,
            ModelId = modelId,
            Feature = feature,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Status = status,
            ErrorMessage = errorMessage?.Length > 500 ? errorMessage[..500] : errorMessage,
            CreatedAt = DateTime.Now
        };

        if (auditRequestContent)
        {
            log.RequestSummary = SummarizeMessages(chatMessages);
        }

        await usageService.TrackAsync(log, CancellationToken.None);
    }

    private static string? SummarizeMessages(IList<ChatMessage> messages)
    {
        if (messages.Count == 0) return null;
        var parts = new List<string>(messages.Count);
        foreach (var msg in messages)
        {
            parts.Add($"[{msg.Role}] {msg.Text}");
        }

        return string.Join("\n\n", parts);
    }
}
