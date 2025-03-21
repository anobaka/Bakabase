using Bakabase.Abstractions.Components.Tasks;
using Bootstrap.Components.Tasks;

namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, PauseToken pt, CancellationToken ct);
    Task EnhanceAll(Func<int, Task>? onProgress, PauseToken pt, CancellationToken ct);
    Task ReapplyEnhancementsByCategory(int categoryId, int enhancerId, CancellationToken ct);
    Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct);
}