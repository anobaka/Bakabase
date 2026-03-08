using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Services;

public class AiTranslationService(
    ILlmService llmService,
    ILogger<AiTranslationService> logger
) : IAiTranslationService
{

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        var prompt = BuildTranslationPrompt(text, targetLanguage, sourceLanguage);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(targetLanguage, sourceLanguage)),
            new(ChatRole.User, prompt)
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.Translation, messages, ct: ct);
        var translatedText = response.Text?.Trim() ?? text;

        return new TranslationResult
        {
            OriginalText = text,
            TranslatedText = translatedText,
            DetectedSourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage
        };
    }

    public async Task<BatchTranslationResult> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return new BatchTranslationResult
            {
                Results = [],
                TotalCount = 0,
                SuccessCount = 0
            };
        }

        // For small batches, use a single call with JSON response
        if (texts.Count <= 20)
        {
            return await TranslateBatchSingleCallAsync(texts, targetLanguage, sourceLanguage, ct);
        }

        // For large batches, chunk into groups
        var results = new List<TranslationResult>();
        var chunks = texts.Chunk(20).ToList();

        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var batchResult = await TranslateBatchSingleCallAsync(chunk.ToList(), targetLanguage, sourceLanguage, ct);
            results.AddRange(batchResult.Results);
        }

        return new BatchTranslationResult
        {
            Results = results,
            TotalCount = texts.Count,
            SuccessCount = results.Count(r => r.TranslatedText != r.OriginalText)
        };
    }

    private async Task<BatchTranslationResult> TranslateBatchSingleCallAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage,
        CancellationToken ct)
    {
        var numberedTexts = texts.Select((t, i) => $"{i + 1}. {t}").ToList();
        var prompt =
            $"Translate the following texts to {targetLanguage}. Return ONLY translated texts in the same numbered format, one per line:\n\n{string.Join("\n", numberedTexts)}";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(targetLanguage, sourceLanguage)),
            new(ChatRole.User, prompt)
        };

        var response = await llmService.CompleteForFeatureAsync(AiFeature.Translation, messages, ct: ct);
        var responseText = response.Text?.Trim() ?? string.Empty;

        var translatedLines = ParseNumberedResponse(responseText, texts.Count);

        var results = texts.Select((original, i) => new TranslationResult
        {
            OriginalText = original,
            TranslatedText = i < translatedLines.Count ? translatedLines[i] : original,
            DetectedSourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage
        }).ToList();

        return new BatchTranslationResult
        {
            Results = results,
            TotalCount = texts.Count,
            SuccessCount = results.Count(r => r.TranslatedText != r.OriginalText)
        };
    }

    private static string BuildSystemPrompt(string targetLanguage, string? sourceLanguage)
    {
        var sourcePart = sourceLanguage != null ? $" from {sourceLanguage}" : string.Empty;
        return
            $"You are a professional translator. Translate text{sourcePart} to {targetLanguage}. " +
            "Preserve formatting, proper nouns, and technical terms. " +
            "Return ONLY the translated text without any explanations or additional content.";
    }

    private static string BuildTranslationPrompt(string text, string targetLanguage, string? sourceLanguage)
    {
        return $"Translate to {targetLanguage}:\n\n{text}";
    }

    private static List<string> ParseNumberedResponse(string response, int expectedCount)
    {
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            // Remove numbering prefix like "1. " or "1) "
            var dotIdx = trimmed.IndexOf(". ", StringComparison.Ordinal);
            var parenIdx = trimmed.IndexOf(") ", StringComparison.Ordinal);

            var idx = -1;
            if (dotIdx >= 0 && dotIdx <= 3) idx = dotIdx + 2;
            else if (parenIdx >= 0 && parenIdx <= 3) idx = parenIdx + 2;

            results.Add(idx >= 0 ? trimmed[idx..] : trimmed);
        }

        return results;
    }
}
