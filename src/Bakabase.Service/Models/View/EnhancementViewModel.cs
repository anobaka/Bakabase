using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.View;

public record EnhancementViewModel
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int EnhancerId { get; set; }
    public StandardValueType ValueType { get; set; }

    /// <summary>
    /// <see cref="EnhancementRawValue.Target"/>
    /// </summary>
    public int Target { get; set; }

    public string? DynamicTarget { get; set; }

    /// <summary>
    /// <see cref="EnhancementRawValue.Value"/>
    /// </summary>
    public object? Value { get; set; }

    /// <summary>
    /// <inheritdoc cref="Db.Enhancement.PropertyPool"/>
    /// </summary>
    public PropertyPool? PropertyPool { get; set; }

    /// <summary>
    /// <inheritdoc cref="PropertyPool"/>
    /// </summary>
    public int? PropertyId { get; set; }

    public CustomPropertyValue? CustomPropertyValue { get; set; }
    public ReservedPropertyValue? ReservedPropertyValue { get; set; }
    public PropertyViewModel? Property { get; set; }
}