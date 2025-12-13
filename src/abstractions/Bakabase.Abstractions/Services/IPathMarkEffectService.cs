using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Services;

public interface IPathMarkEffectService
{
    #region ResourceMarkEffect Operations

    Task<List<ResourceMarkEffect>> GetResourceEffectsByMarkId(int markId);
    Task<List<ResourceMarkEffect>> GetResourceEffectsByMarkIds(IEnumerable<int> markIds);
    Task<List<ResourceMarkEffect>> GetResourceEffectsByPath(string path);
    Task<List<ResourceMarkEffect>> GetResourceEffectsByPaths(IEnumerable<string> paths);
    Task AddResourceEffects(IEnumerable<ResourceMarkEffect> effects);
    Task DeleteResourceEffectsByMarkId(int markId);
    Task DeleteResourceEffects(IEnumerable<int> effectIds);

    #endregion

    #region PropertyMarkEffect Operations

    Task<List<PropertyMarkEffect>> GetPropertyEffectsByMarkId(int markId);
    Task<List<PropertyMarkEffect>> GetPropertyEffectsByMarkIds(IEnumerable<int> markIds);
    Task<List<PropertyMarkEffect>> GetPropertyEffectsByResource(int resourceId, PropertyPool? pool = null, int? propertyId = null);
    Task<List<PropertyMarkEffect>> GetPropertyEffectsByResources(IEnumerable<int> resourceIds, PropertyPool? pool = null, int? propertyId = null);
    Task AddPropertyEffects(IEnumerable<PropertyMarkEffect> effects);
    Task UpdatePropertyEffects(IEnumerable<PropertyMarkEffect> effects);
    Task DeletePropertyEffectsByMarkId(int markId);
    Task DeletePropertyEffects(IEnumerable<int> effectIds);

    #endregion

    #region Value Calculation

    /// <summary>
    /// Get the aggregated value for a multi-select property across all marks.
    /// Returns union of all values from all marks.
    /// </summary>
    Task<List<string>> GetAggregatedMultiSelectValue(int resourceId, PropertyPool pool, int propertyId);

    /// <summary>
    /// Get the winning value for a single-select property based on priority.
    /// Returns the value from the mark with highest priority.
    /// </summary>
    Task<(string? Value, int? WinningMarkId)> GetWinningSingleSelectValue(int resourceId, PropertyPool pool, int propertyId);

    #endregion
}
