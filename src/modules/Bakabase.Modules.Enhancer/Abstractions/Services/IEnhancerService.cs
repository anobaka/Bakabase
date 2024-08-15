namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, CancellationToken ct);
    Task EnhanceAll(CancellationToken ct);
    Task ReapplyEnhancementsToResources(int categoryId, CancellationToken ct);
}