namespace Bakabase.Modules.AI.Models.Input;

public record TranslateInputModel
{
    public required string Text { get; init; }
    public required string TargetLanguage { get; init; }
    public string? SourceLanguage { get; init; }
}

public record TranslateBatchInputModel
{
    public required IReadOnlyList<string> Texts { get; init; }
    public required string TargetLanguage { get; init; }
    public string? SourceLanguage { get; init; }
}
