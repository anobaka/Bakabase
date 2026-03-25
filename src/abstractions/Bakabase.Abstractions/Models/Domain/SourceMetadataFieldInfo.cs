using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Describes an available metadata field from an external source.
/// </summary>
public record SourceMetadataFieldInfo(string Name, StandardValueType ValueType);
