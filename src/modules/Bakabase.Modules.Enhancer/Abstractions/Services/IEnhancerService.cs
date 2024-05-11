namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds);
    Task EnhanceAll();
    Task ReapplyEnhancementsToResources(int categoryId);
}