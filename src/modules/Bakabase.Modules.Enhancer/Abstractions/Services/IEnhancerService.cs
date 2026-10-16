using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Components.Tasks;

namespace Bakabase.Modules.Enhancer.Abstractions.Services;

public interface IEnhancerService
{
    Task EnhanceResource(int resourceId, HashSet<int>? enhancerIds, PauseToken pt, CancellationToken ct);

    Task EnhanceAll(Func<int, Task>? onProgress, Func<string, Task>? onProcessChange, PauseToken pt,
        CancellationToken ct);
    
    Task ReapplyEnhancementsByResources(int[] resourceIds, int[] enhancerIds, CancellationToken ct);
    Task ReapplyEnhancementsByResources(Dictionary<int, int[]> resourceIdsEnhancerIdsMap, CancellationToken ct);
    Task Enhance(Resource resource, Dictionary<int, EnhancerFullOptions> optionsMap);
    Task EnhanceResourceWithOptions(int resourceId, List<EnhancerFullOptions> enhancerOptionsList, CancellationToken ct);
    Task ApplyEnhancementsToResources(Dictionary<int, HashSet<int>> resourceIdEnhancerIdsMap,
        List<Enhancement> enhancements, CancellationToken ct);

    /// <summary>Apply owner-validated, pinned target mappings without resolving worker/local profiles.</summary>
    Task ApplyPreparedEnhancementsToResource(int resourceId, EnhancerFullOptions options,
        List<Enhancement> enhancements, CancellationToken ct) => throw new NotSupportedException();

    /// <summary>Pure post-processing of computed values; does not write resource metadata.</summary>
    Task ProcessComputedValuesAsync(List<EnhancementRawValue> values, EnhancerFullOptions options,
        int resourceId, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Deletes the applied-but-empty enhancement records of the given resources, so their enhancers
    /// run again next round. An enhancer that found nothing still records a result, and an applied
    /// record is never retried — which is correct while the resource is unchanged, and wrong the
    /// moment it changes in a way the enhancer can now read (most obviously: its files arrive).
    /// </summary>
    /// <returns>How many records were deleted.</returns>
    Task<int> ClearEmptyEnhancementRecords(IReadOnlyCollection<int> resourceIds, CancellationToken ct);
}