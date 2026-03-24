using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Maps a metadata field from an external source to a resource property.
/// When configured, external source metadata is automatically synced to resource properties.
/// </summary>
public class SourceMetadataMapping
{
    public int Id { get; set; }
    public ResourceSource Source { get; set; }
    /// <summary>
    /// The metadata field name from the external source (e.g., "Name", "Rating", "Tags", "Introduction").
    /// </summary>
    public string MetadataField { get; set; } = null!;
    public PropertyPool TargetPool { get; set; }
    public int TargetPropertyId { get; set; }
}
