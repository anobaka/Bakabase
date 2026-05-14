using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

public interface IPropertyValueScopePreferenceService
{
    Task<PropertyValueScopePreference?> Get(int resourceId, PropertyPool pool, int propertyId);
    Task<List<PropertyValueScopePreference>> GetByResourceIds(IEnumerable<int> resourceIds);
    Task<PropertyValueScopePreference> Upsert(PropertyValueScopePreference preference);
    Task Delete(int resourceId, PropertyPool pool, int propertyId);
    Task RemoveByResourceIds(IEnumerable<int> resourceIds);
}
