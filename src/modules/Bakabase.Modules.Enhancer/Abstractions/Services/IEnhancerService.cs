namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, CancellationToken ct);
    Task EnhanceAll(CancellationToken ct);
    Task ReapplyEnhancementsByCategory(int categoryId, int enhancerId, CancellationToken ct);
    Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct);
}