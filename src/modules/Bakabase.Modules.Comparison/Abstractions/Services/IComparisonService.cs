using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Comparison.Models.Domain;
using Bakabase.Modules.Comparison.Models.Input;
using Bakabase.Modules.Comparison.Models.View;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Comparison.Abstractions.Services;

public interface IComparisonService
{
    // Plan CRUD
    Task<(ComparisonPlanDbModel Plan, List<ComparisonRuleDbModel> Rules, int GroupCount)?> GetPlanDbModelAsync(int id);
    Task<List<(ComparisonPlanDbModel Plan, List<ComparisonRuleDbModel> Rules, int GroupCount)>> GetAllPlanDbModelsAsync();
    Task<ComparisonPlan> CreatePlanAsync(ComparisonPlanCreateInputModel input);
    Task UpdatePlanAsync(int id, ComparisonPlanPatchInputModel input);
    Task DeletePlanAsync(int id);
    Task<ComparisonPlan> DuplicatePlanAsync(int id);

    // Comparison execution
    Task<string> StartComparisonTaskAsync(int planId);
    Task ExecuteComparisonAsync(int planId, Func<int, string, Task> progressCallback, PauseToken pt, CancellationToken ct);

    // Results
    Task<ComparisonResultSearchResponse> SearchResultGroupsAsync(int planId, ComparisonResultSearchInputModel input);
    Task<ComparisonResultGroupViewModel?> GetResultGroupAsync(int groupId);
    Task<List<int>> GetResultGroupResourceIdsAsync(int groupId);
    Task<ComparisonResultPairsResponse> GetResultGroupPairsAsync(int groupId, int limit = 1000);
    Task ClearResultsAsync(int planId);
    Task HideResultGroupAsync(int groupId, bool hidden);

    // Similarity calculation
    double CalculateSimilarity(Resource r1, Resource r2, ComparisonPlan plan);
}
