using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;

namespace Bakabase.Modules.AI.Components.Enhancer;

public interface IEnhancementPostProcessor
{
    Task ProcessAsync(EnhancementPostProcessorContext context, CancellationToken ct);
}

public record EnhancementPostProcessorContext
{
    public required int ResourceId { get; init; }
    public required int EnhancerId { get; init; }
    public required IReadOnlyList<EnhancementValue> Values { get; init; }
    public required EnhancerTranslationOptions TranslationOptions { get; init; }
}

/// <summary>
/// Represents an enhancement value that may be translated.
/// The post-processor will convert translatable types to List&lt;string&gt;,
/// translate them, then convert back.
/// </summary>
public record EnhancementValue
{
    public int Target { get; init; }
    public string? DynamicTarget { get; init; }
    public required object? Value { get; set; }
    public StandardValueType ValueType { get; init; }
}
