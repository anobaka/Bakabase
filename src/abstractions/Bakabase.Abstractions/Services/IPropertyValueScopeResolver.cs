using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

/// <summary>
/// Resolves a resource property's per-scope values down to the single effective value: the
/// per-resource PropertyValueScopePreference wins over the effective profile's scope order and then
/// the configured global priority; empty scopes are skipped. Implementations own where global priority comes from
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
