using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Models;

namespace Bakabase.Modules.HealthScore.Components;

/// <summary>
/// Pure-function evaluator. Pass property values via <see cref="ResourceMatcherEvaluationContext.GetPropertyValues"/>
/// so this module stays decoupled from how values are loaded.
/// </summary>
public interface IResourceMatcherEvaluator
{
    bool Evaluate(ResourceMatcher matcher, ResourceMatcherEvaluationContext context);
}

public sealed class ResourceMatcherEvaluationContext
{
    public required ResourceFsSnapshot Snapshot { get; init; }

    /// <summary>
    /// Returns the list of values for (pool, propertyId) on the resource.
    /// - null  → property has no value at all (treated as null per existing IsMatch semantics)
    /// - empty → resource has the property but no entries
    /// - any   → match if any value matches
    /// </summary>
    public required Func<PropertyPool, int, List<object?>?> GetPropertyValues { get; init; }

    /// <summary>
    /// Test a single value with the property-search-handler IsMatch.
    /// The host wires this to PropertySystem.Property.TryGetSearchHandler(...).IsMatch.
    /// Returns null when the property type has no search handler.
    /// </summary>
    public required Func<PropertyPool, int, SearchOperation, object?, object?, bool>? PropertyValueMatcher { get; init; }
}
