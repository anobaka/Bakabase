using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Tracks the readiness status of a specific data type from a specific origin for a resource.
/// Composite key: (ResourceId, DataType, Origin).
/// </summary>
public record ResourceDataState
{
    public int ResourceId { get; set; }
    public ResourceDataType DataType { get; set; }
    public DataOrigin Origin { get; set; }
    public DataStatus Status { get; set; }
}
