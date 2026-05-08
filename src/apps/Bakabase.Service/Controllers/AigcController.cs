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

[Route("~/aigc")]
public class AigcController(
    IAigcProviderService providerService,
    IAigcGeneratorService generatorService,
    IAigcArtifactService artifactService
) : Controller
{
    // ===== Providers =====

    [HttpGet("providers")]
    [SwaggerOperation(OperationId = "GetAllAigcProviders")]
    public async Task<ListResponse<AigcProviderConfigDbModel>> GetAllProviders(CancellationToken ct) =>
        new(await providerService.GetAllAsync(ct));

    [HttpGet("providers/{id:int}")]
    [SwaggerOperation(OperationId = "GetAigcProvider")]
    public async Task<SingletonResponse<AigcProviderConfigDbModel?>> GetProvider(int id, CancellationToken ct) =>
        new(await providerService.GetAsync(id, ct));

    [HttpPost("providers")]
    [SwaggerOperation(OperationId = "AddAigcProvider")]
    public async Task<SingletonResponse<AigcProviderConfigDbModel>> AddProvider(
        [FromBody] AigcProviderConfigAddInputModel model, CancellationToken ct) =>
        new(await providerService.AddAsync(model, ct));

    [HttpPut("providers/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateAigcProvider")]
    public async Task<SingletonResponse<AigcProviderConfigDbModel>> UpdateProvider(
        int id, [FromBody] AigcProviderConfigUpdateInputModel model, CancellationToken ct) =>
        new(await providerService.UpdateAsync(id, model, ct));

    [HttpDelete("providers/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteAigcProvider")]
    public async Task<BaseResponse> DeleteProvider(int id, CancellationToken ct)
    {
        await providerService.DeleteAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("providers/{id:int}/test")]
    [SwaggerOperation(OperationId = "TestAigcProvider")]
    public async Task<SingletonResponse<bool>> TestProvider(int id, CancellationToken ct) =>
        new(await providerService.TestConnectionAsync(id, ct));

    [HttpGet("provider-kinds")]
    [SwaggerOperation(OperationId = "GetAigcProviderKinds")]
    public ListResponse<AigcProviderKindInfo> GetProviderKinds() =>
        new(providerService.GetProviderKinds());

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
