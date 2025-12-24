using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Resource Profile property configuration - simple property reference list without ValueLocators
/// </summary>
public class ResourceProfilePropertyOptions
{
    /// <summary>
    /// List of property references (Pool + Id only, no ValueLocators)
    /// </summary>
    public List<ResourceProfilePropertyReference>? Properties { get; set; }
}

/// <summary>
/// Simple property reference (Pool + Id)
/// </summary>
public record ResourceProfilePropertyReference
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
}
