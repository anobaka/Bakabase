using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bootstrap.Components.Tasks;

namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, PauseToken pt, CancellationToken ct);
    Task EnhanceAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt, CancellationToken ct);
    Task ReapplyEnhancementsByCategory(int categoryId, int enhancerId, CancellationToken ct);
    Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct);
    Task ReapplyEnhancementsByResources(Dictionary<int, int[]> resourceIdsEnhancerIdsMap, CancellationToken ct);
    Task Enhance(Resource resource, Dictionary<int, EnhancerFullOptions> optionsMap);
}