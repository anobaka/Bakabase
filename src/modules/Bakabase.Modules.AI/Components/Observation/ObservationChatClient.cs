using System.Diagnostics;
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
