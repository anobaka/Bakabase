using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

/// <summary>
/// Input model for bulk updating property values across multiple resources
/// </summary>
public class BulkResourcePropertyValuePutInputModel
{
    /// <summary>
    /// Resource IDs to update
    /// </summary>
    public List<int> ResourceIds { get; set; } = new();

    /// <summary>
    /// Property ID to update
    /// </summary>
    public int PropertyId { get; set; }

    /// <summary>
    /// Whether this is a custom property (true) or reserved/internal property (false)
    /// </summary>
    public bool IsCustomProperty { get; set; }

    /// <summary>
    /// Serialized property value (StandardValue format)
    /// </summary>
    public string? Value { get; set; }
}
