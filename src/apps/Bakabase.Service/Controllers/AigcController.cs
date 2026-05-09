using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Models.Input;
using Bakabase.Modules.AI.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// Provider CRUD lives on <c>AIController</c> (~/ai/providers); AIGC providers are just
/// AI providers with the AIGC capability flag enabled.
/// </summary>
[Route("~/aigc")]
public class AigcController(
    IAigcProviderService aigcProviderService,
    IAigcGeneratorService generatorService,
    IAigcArtifactService artifactService
) : Controller
{
    // ===== AIGC-capable providers (filtered subset; CRUD lives at /ai/providers) =====

    [HttpGet("providers")]
    [SwaggerOperation(OperationId = "GetEnabledAigcProviders")]
    public async Task<ListResponse<AiProviderDbModel>> GetEnabledProviders(CancellationToken ct) =>
        new(await aigcProviderService.GetEnabledAigcProvidersAsync(ct));

    // ===== Generators =====

    [HttpGet("generators")]
    [SwaggerOperation(OperationId = "GetAllAigcGenerators")]
    public async Task<ListResponse<AigcGeneratorView>> GetAllGenerators(CancellationToken ct) =>
        new(await generatorService.GetAllAsync(ct));

    [HttpGet("generators/{id:int}")]
    [SwaggerOperation(OperationId = "GetAigcGenerator")]
    public async Task<SingletonResponse<AigcGeneratorView?>> GetGenerator(int id, CancellationToken ct) =>
        new(await generatorService.GetAsync(id, ct));

    [HttpPost("generators")]
    [SwaggerOperation(OperationId = "AddAigcGenerator")]
    public async Task<SingletonResponse<AigcGeneratorView>> AddGenerator(
        [FromBody] AigcGeneratorAddInputModel model, CancellationToken ct) =>
        new(await generatorService.AddAsync(model, ct));

    [HttpPut("generators/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateAigcGenerator")]
    public async Task<SingletonResponse<AigcGeneratorView>> UpdateGenerator(
        int id, [FromBody] AigcGeneratorUpdateInputModel model, CancellationToken ct) =>
        new(await generatorService.UpdateAsync(id, model, ct));

    [HttpDelete("generators/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteAigcGenerator")]
    public async Task<BaseResponse> DeleteGenerator(int id, CancellationToken ct)
    {
        await generatorService.DeleteAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("generators/{id:int}/run")]
    [SwaggerOperation(OperationId = "TriggerAigcGeneration")]
    public async Task<SingletonResponse<int>> TriggerRun(
        int id, [FromBody] AigcGenerationTriggerInputModel? model, CancellationToken ct) =>
        new(await generatorService.TriggerRunAsync(id, model, ct));

    [HttpPost("generators/{id:int}/import")]
    [SwaggerOperation(OperationId = "ImportAigcArtifacts")]
    public async Task<SingletonResponse<int>> ImportArtifacts(
        int id, [FromBody] AigcArtifactImportInputModel model, CancellationToken ct) =>
        new(await generatorService.ImportArtifactsAsync(id, model, ct));

    // ===== Runs =====

    [HttpGet("runs")]
    [SwaggerOperation(OperationId = "GetAigcRuns")]
    public async Task<ListResponse<AigcGenerationRunDbModel>> GetRuns(
        [FromQuery] int? generatorId, CancellationToken ct) =>
        new(await artifactService.GetRunsAsync(generatorId, ct));

    [HttpGet("runs/{id:int}")]
    [SwaggerOperation(OperationId = "GetAigcRun")]
    public async Task<SingletonResponse<AigcGenerationRunDbModel?>> GetRun(int id, CancellationToken ct) =>
        new(await artifactService.GetRunAsync(id, ct));

    [HttpDelete("runs/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteAigcRun")]
    public async Task<BaseResponse> DeleteRun(int id, CancellationToken ct)
    {
        await artifactService.DeleteRunAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    // ===== Artifacts =====

    [HttpGet("artifacts")]
    [SwaggerOperation(OperationId = "GetAigcArtifacts")]
    public async Task<ListResponse<AigcArtifactDbModel>> GetArtifacts(
        [FromQuery] int? generatorId, [FromQuery] int? runId, CancellationToken ct) =>
        new(await artifactService.GetArtifactsAsync(generatorId, runId, ct));

    [HttpDelete("artifacts/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteAigcArtifact")]
    public async Task<BaseResponse> DeleteArtifact(int id, CancellationToken ct)
    {
        await artifactService.DeleteArtifactAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }
}
