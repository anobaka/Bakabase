using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Describes a predefined metadata field from an external source.
/// </summary>
public record SourceMetadataFieldInfo(string Name, StandardValueType ValueType);

/// <summary>
/// Result of fetching detailed metadata from an external source for a single item.
/// </summary>
public class SourceDetailedMetadata
{
    /// <summary>
    /// Values for predefined fields. Key = field name, Value = extracted value (typed per field definition).
    /// </summary>
    public Dictionary<string, object?> PredefinedFieldValues { get; set; } = new();

    /// <summary>
    /// Dynamic/extra fields not covered by predefined definitions.
    /// Key = field name, Value = list of string values.
    /// </summary>
    public Dictionary<string, List<string>> CustomFieldValues { get; set; } = new();

    /// <summary>
    /// Cover URLs discovered during metadata fetch (to update ResourceSourceLink.CoverUrls).
    /// </summary>
    public List<string>? CoverUrls { get; set; }

    /// <summary>
    /// Raw JSON to store as MetadataJson in the source DbModel.
    /// </summary>
    public string? RawJson { get; set; }
}
