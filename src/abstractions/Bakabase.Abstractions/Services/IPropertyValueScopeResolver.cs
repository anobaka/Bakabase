using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Resolves a resource property's per-scope values down to the single effective value: the
/// per-resource PropertyValueScopePreference wins, the configured global scope priority is the
/// fallback, empty scopes are skipped. Implementations own where the global priority comes from
/// (ResourceOptions), so callers never pass priority configuration.
/// </summary>
public interface IPropertyValueScopeResolver
{
    /// <summary>
    /// Returns the effective property value for <paramref name="pool"/>/<paramref name="propertyId"/>
    /// on the resource, or null when the property has no non-empty value.
    /// </summary>
    Resource.Property.PropertyValue? Resolve(Resource resource, PropertyPool pool, int propertyId);
}
