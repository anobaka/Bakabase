using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.HealthScore.Abstractions.Services;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Models.Input;
using Bakabase.Modules.HealthScore.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("health-score")]
public class HealthScoreController(
    IHealthScoreService service,
    IFilePredicateRegistry predicates,
    HealthScoreMembershipCountCache membershipCounts) : Controller
{
    [HttpGet("profiles")]
    [SwaggerOperation(OperationId = "GetAllHealthScoreProfiles")]
    public async Task<ListResponse<HealthScoreProfileViewModel>> GetAll()
    {
        var dbs = await service.GetAllDbModels();
        return new ListResponse<HealthScoreProfileViewModel>(
            dbs.Select(db => HealthScoreProfileViewModel.From(db, membershipCounts.Get(db.Id))));
    }

    [HttpGet("profiles/{id:int}")]
    [SwaggerOperation(OperationId = "GetHealthScoreProfile")]
    public async Task<SingletonResponse<HealthScoreProfileViewModel?>> Get(int id)
    {
        var db = await service.GetDbModel(id);
        return new SingletonResponse<HealthScoreProfileViewModel?>(
            db is null ? null : HealthScoreProfileViewModel.From(db, membershipCounts.Get(db.Id)));
    }

    [HttpPost("profiles")]
    [SwaggerOperation(OperationId = "AddHealthScoreProfile")]
    public async Task<SingletonResponse<HealthScoreProfileViewModel>> Add()
    {
        var db = await service.Add();
        return new SingletonResponse<HealthScoreProfileViewModel>(HealthScoreProfileViewModel.From(db));
    }

    [HttpPatch("profiles/{id:int}")]
    [SwaggerOperation(OperationId = "PatchHealthScoreProfile")]
    public async Task<BaseResponse> Patch(int id, [FromBody] HealthScoreProfilePatchInputModel model)
    {
        await service.Patch(id, model);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("profiles/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteHealthScoreProfile")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("profiles/{id:int}/duplicate")]
    [SwaggerOperation(OperationId = "DuplicateHealthScoreProfile")]
    public async Task<BaseResponse> Duplicate(int id)
    {
        await service.Duplicate(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("profiles/{id:int}/clear-cache")]
    [SwaggerOperation(OperationId = "ClearHealthScoreProfileCache")]
    public async Task<BaseResponse> ClearProfileCache(int id)
    {
        await service.ClearProfileCache(id);
        await service.RunNow();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("clear-all-caches")]
    [SwaggerOperation(OperationId = "ClearAllHealthScoreCaches")]
    public async Task<BaseResponse> ClearAllCaches()
    {
        await service.ClearAllCaches();
        await service.RunNow();
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("run")]
    [SwaggerOperation(OperationId = "RunHealthScoringNow")]
    public async Task<BaseResponse> RunNow()
    {
        await service.RunNow();
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("scores")]
    [SwaggerOperation(OperationId = "GetHealthScores")]
    public async Task<SingletonResponse<Dictionary<int, decimal>>> GetScores([FromQuery] int[] resourceIds)
    {
        var scores = await service.GetAggregatedScores(resourceIds ?? []);
        return new SingletonResponse<Dictionary<int, decimal>>(scores);
    }

    [HttpGet("resource/{resourceId:int}/diagnosis")]
    [SwaggerOperation(OperationId = "GetHealthScoreDiagnosisForResource")]
    public async Task<ListResponse<ResourceHealthScoreRowViewModel>> Diagnosis(int resourceId)
    {
        var rows = await service.GetRowsForResource(resourceId);
        return new ListResponse<ResourceHealthScoreRowViewModel>(rows.Select(r => new ResourceHealthScoreRowViewModel
        {
            ProfileId = r.ProfileId,
            Score = r.Score,
            ProfileHash = r.ProfileHash,
            MatchedRulesJson = r.MatchedRulesJson,
            EvaluatedAt = r.EvaluatedAt,
        }));
    }

    [HttpGet("predicates")]
    [SwaggerOperation(OperationId = "GetAllFilePredicates")]
    public ListResponse<FilePredicateDescriptorViewModel> GetPredicates()
    {
        var list = predicates.All.Select(p => new FilePredicateDescriptorViewModel
        {
            Id = p.Id,
            DisplayNameKey = p.DisplayNameKey,
            ParametersTypeName = p.ParametersType.Name,
        });
        return new ListResponse<FilePredicateDescriptorViewModel>(list);
    }
}

public class ResourceHealthScoreRowViewModel
{
    public int ProfileId { get; set; }
    public decimal Score { get; set; }
    public string ProfileHash { get; set; } = string.Empty;
    public string? MatchedRulesJson { get; set; }
    public System.DateTime EvaluatedAt { get; set; }
}
