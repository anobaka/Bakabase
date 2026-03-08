using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.AI.Models.Domain;

public record ResourceTranslationResult
{
    public int ResourceId { get; init; }
    public List<PropertyTranslation> Translations { get; init; } = [];
}

public record PropertyTranslation
{
    public string PropertyKey { get; init; } = string.Empty;
    public string OriginalText { get; init; } = string.Empty;
    public string TranslatedText { get; init; } = string.Empty;
    public int PropertyId { get; init; }
    public PropertyPool PropertyPool { get; init; }
    public string? PropertyName { get; init; }
    public PropertyType? PropertyType { get; init; }
    public StandardValueType? DbValueType { get; init; }
    public StandardValueType? BizValueType { get; init; }
}
