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

[Route("~/ai")]
public class AIController(ILlmProviderService providerService) : Controller
{
    [HttpPost("providers")]
    [SwaggerOperation(OperationId = "AddLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel>> AddProvider(
        [FromBody] LlmProviderConfigAddInputModel model, CancellationToken ct)
    {
        return new(await providerService.AddProviderAsync(model, ct));
    }

    [HttpGet("providers")]
    [SwaggerOperation(OperationId = "GetAllLlmProviders")]
    public async Task<ListResponse<LlmProviderConfigDbModel>> GetAllProviders(CancellationToken ct)
    {
        return new(await providerService.GetAllProvidersAsync(ct));
    }

    [HttpGet("providers/{id:int}")]
    [SwaggerOperation(OperationId = "GetLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel?>> GetProvider(int id, CancellationToken ct)
    {
        return new(await providerService.GetProviderAsync(id, ct));
    }

    [HttpPut("providers/{id:int}")]
    [SwaggerOperation(OperationId = "UpdateLlmProvider")]
    public async Task<SingletonResponse<LlmProviderConfigDbModel>> UpdateProvider(
        int id, [FromBody] LlmProviderConfigUpdateInputModel model, CancellationToken ct)
    {
        return new(await providerService.UpdateProviderAsync(id, model, ct));
    }

    [HttpDelete("providers/{id:int}")]
    [SwaggerOperation(OperationId = "DeleteLlmProvider")]
    public async Task<BaseResponse> DeleteProvider(int id, CancellationToken ct)
    {
        await providerService.DeleteProviderAsync(id, ct);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("providers/{id:int}/test")]
    [SwaggerOperation(OperationId = "TestLlmProvider")]
    public async Task<SingletonResponse<bool>> TestProvider(int id, CancellationToken ct)
    {
        return new(await providerService.TestConnectionAsync(id, ct));
    }

    [HttpGet("providers/{id:int}/models")]
    [SwaggerOperation(OperationId = "GetLlmProviderModels")]
    public async Task<ListResponse<LlmModelInfo>> GetProviderModels(int id, CancellationToken ct)
    {
        return new(await providerService.GetModelsAsync(id, ct));
    }

    [HttpGet("provider-types")]
    [SwaggerOperation(OperationId = "GetLlmProviderTypes")]
    public ListResponse<LlmProviderTypeInfo> GetProviderTypes()
    {
        return new(providerService.GetProviderTypes());
    }
}
