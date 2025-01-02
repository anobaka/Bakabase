using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;
using Bakabase.Modules.Property.Models.View;

namespace Bakabase.Modules.BulkModification.Models.View;

public record BulkModificationProcessValueViewModel
{
    public BulkModificationProcessorValueType Type { get; set; }
    public PropertyPool? PropertyPool { get; set; }
    public int? PropertyId { get; set; }
    public PropertyType? EditorPropertyType { get; set; }

    /// <summary>
    /// Static Text / Variable Key / Serialized BizValue / Serialized DbValue
    /// </summary>
    public string? Value { get; set; }

    public bool FollowPropertyChanges { get; set; } = true;

    public PropertyViewModel? Property { get; set; }
}