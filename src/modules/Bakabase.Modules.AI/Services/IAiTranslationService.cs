namespace Bakabase.Modules.AI.Services;

public interface IAiTranslationService
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default);

    Task<BatchTranslationResult> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        string targetLanguage,
        string? sourceLanguage = null,
        CancellationToken ct = default);
}

public record TranslationResult
{
    public required string OriginalText { get; init; }
    public required string TranslatedText { get; init; }
    public string? DetectedSourceLanguage { get; init; }
    public string TargetLanguage { get; init; } = string.Empty;
}

public record BatchTranslationResult
{
    public required IReadOnlyList<TranslationResult> Results { get; init; }
    public int TotalCount { get; init; }
    public int SuccessCount { get; init; }
}
