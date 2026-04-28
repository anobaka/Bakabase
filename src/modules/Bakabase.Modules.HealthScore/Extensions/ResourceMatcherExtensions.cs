using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.HealthScore.Models;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.StandardValue.Extensions;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.HealthScore.Extensions;

/// <summary>
/// Domain ↔ DB conversion. The boundary handles StandardValue
/// serialization for property-value leaves so the outer JSON only ever sees
/// strings — no <see cref="System.Text.Json.JsonElement"/> round-trips.
/// </summary>
public static class ResourceMatcherExtensions
{
    public static ResourceMatcherDbModel ToDbModel(
        this ResourceMatcher m,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap) => new()
    {
        Combinator = m.Combinator,
        Disabled = m.Disabled,
        Groups = m.Groups?.Select(g => g.ToDbModel(propertyMap)).ToList(),
        Leaves = m.Leaves?.Select(l => l.ToDbModel(propertyMap)).ToList(),
    };

    public static ResourceMatcher ToDomainModel(
        this ResourceMatcherDbModel m,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap) => new()
    {
        Combinator = m.Combinator,
        Disabled = m.Disabled,
        Groups = m.Groups?.Select(g => g.ToDomainModel(propertyMap)).ToList(),
        Leaves = m.Leaves?.Select(l => l.ToDomainModel(propertyMap)).ToList(),
    };

    public static ResourceMatcherLeafDbModel ToDbModel(
        this ResourceMatcherLeaf l,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap)
    {
        var dto = new ResourceMatcherLeafDbModel
        {
            Kind = l.Kind,
            Negated = l.Negated,
            Disabled = l.Disabled,
            PropertyPool = l.PropertyPool,
            PropertyId = l.PropertyId,
            Operation = l.Operation,
            FilePredicateId = l.FilePredicateId,
            FilePredicateParametersJson = l.FilePredicateParametersJson,
        };

        if (TryResolveStandardValueType(l.Kind, l.PropertyPool, l.PropertyId, l.Operation, propertyMap, out var stdType)
            && l.PropertyDbValue is not null)
        {
            dto.PropertyValue = l.PropertyDbValue.SerializeAsStandardValue(stdType);
        }

        return dto;
    }

    public static ResourceMatcherLeaf ToDomainModel(
        this ResourceMatcherLeafDbModel l,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap)
    {
        var leaf = new ResourceMatcherLeaf
        {
            Kind = l.Kind,
            Negated = l.Negated,
            Disabled = l.Disabled,
            PropertyPool = l.PropertyPool,
            PropertyId = l.PropertyId,
            Operation = l.Operation,
            FilePredicateId = l.FilePredicateId,
            FilePredicateParametersJson = l.FilePredicateParametersJson,
        };

        if (!string.IsNullOrEmpty(l.PropertyValue)
            && TryResolveStandardValueType(l.Kind, l.PropertyPool, l.PropertyId, l.Operation, propertyMap, out var stdType))
        {
            leaf.PropertyDbValue = l.PropertyValue.DeserializeAsStandardValue(stdType);
        }

        return leaf;
    }

    /// <summary>
    /// Property-leaf StandardValue type depends on operation (e.g. SingleChoice
    /// + In requires List&lt;string&gt;, not string). Mirrors the same lookup
    /// done by <c>ResourceSearchExtensions</c>.
    /// </summary>
    private static bool TryResolveStandardValueType(
        ResourceMatcherLeafKind kind,
        PropertyPool? pool,
        int? propertyId,
        SearchOperation? operation,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap,
        out StandardValueType stdType)
    {
        stdType = default;
        if (kind != ResourceMatcherLeafKind.Property) return false;
        if (pool is not { } p || propertyId is not { } pid || operation is not { } op) return false;
        if (!propertyMap.TryGetValue((p, pid), out var property)) return false;

        var psh = PropertySystem.Property.TryGetSearchHandler(property.Type);
        var asType = psh?.SearchOperations.GetValueOrDefault(op)?.AsType ?? property.Type;
        stdType = asType.GetDbValueType();
        return true;
    }
}
