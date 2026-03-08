namespace Bakabase.Modules.AI.Models.Input;

public record TranslateResourcePropertiesInputModel
{
    public required string TargetLanguage { get; init; }
    public string? SourceLanguage { get; init; }
}
