using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceDiff : IPropertyKeyHolder
{
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public object? Value1 { get; set; }
    public object? Value2 { get; set; }

    /// <summary>
    /// Redundancy
    /// </summary>
    public string? SerializedValue1 { get; set; }

    /// <summary>
    /// Redundancy
    /// </summary>
    public string? SerializedValue2 { get; set; }
}