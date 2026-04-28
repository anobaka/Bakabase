using Bakabase.Modules.HealthScore.Models;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.HealthScore.Models.Input;

namespace Bakabase.Modules.HealthScore.Abstractions.Services;

public interface IHealthScoreService
{
    /// <summary>Domain shape (typed property values). Used by the engine.</summary>
    Task<HealthScoreProfile?> Get(int id);

    /// <summary>Domain shape list. Used by the engine.</summary>
    Task<List<HealthScoreProfile>> GetAll();

    /// <summary>DB shape (StandardValue-serialized strings). Used by the controller for view round-trips.</summary>
    Task<HealthScoreProfileDbModel?> GetDbModel(int id);

    /// <summary>DB shape list. Used by the controller for view round-trips.</summary>
    Task<List<HealthScoreProfileDbModel>> GetAllDbModels();

    Task<HealthScoreProfileDbModel> Add();
    Task Patch(int id, HealthScoreProfilePatchInputModel model);
    Task Delete(int id);
    Task Duplicate(int id);

    /// <summary>Compute the aggregated final score for a single resource. Null = no profile matched.</summary>
    Task<decimal?> GetAggregatedScore(int resourceId);

    /// <summary>Bulk variant of <see cref="GetAggregatedScore"/>; returns a sparse map.</summary>
    Task<Dictionary<int, decimal>> GetAggregatedScores(int[] resourceIds);

    /// <summary>List all rows for a resource (for diagnostic UI).</summary>
    Task<List<ResourceHealthScoreDbModel>> GetRowsForResource(int resourceId);

    /// <summary>Reset hash for one profile so the next run re-evaluates all its rows.</summary>
    Task ClearProfileCache(int profileId);

    /// <summary>Reset hash for all profiles.</summary>
    Task ClearAllCaches();

    /// <summary>Trigger a fresh one-shot scoring run via BTaskManager.</summary>
    Task RunNow();

    /// <summary>Run the scoring pass synchronously; called by the BTask.</summary>
    Task RunScoring(Func<int, string?, Task>? progress, CancellationToken ct);
}
