namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Resource Profile property configuration - property references with optional per-property scope priority.
/// JSON backward compatible: old data without ScopePriority will deserialize with ScopePriority = null.
/// </summary>
public class ResourceProfilePropertyOptions
{
    /// <summary>
    /// List of property references with optional per-property scope priority override.
    /// </summary>
    public List<PropertyKeyWithScopePriority>? Properties { get; set; }
}