using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("source/{source}/metadata-mapping")]
[ApiController]
public class SourceMetadataMappingController(
    ISourceMetadataSyncService metadataSyncService,
    BTaskManager btm,
    IBakabaseLocalizer localizer
) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetSourceMetadataMappings")]
    public async Task<ListResponse<SourceMetadataMapping>> GetMappings(ResourceSource source)
    {
        var mappings = await metadataSyncService.GetMappings(source);
        return new ListResponse<SourceMetadataMapping>(mappings);
    }

    [HttpPut]
    [SwaggerOperation(OperationId = "SaveSourceMetadataMappings")]
    public async Task<BaseResponse> SaveMappings(ResourceSource source,
        [FromBody] List<SourceMetadataMapping> mappings)
    {
        await metadataSyncService.SaveMappings(source, mappings);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("available-fields")]
    [SwaggerOperation(OperationId = "GetSourceAvailableMetadataFields")]
    public ListResponse<string> GetAvailableFields(ResourceSource source)
    {
        return new ListResponse<string>(metadataSyncService.GetAvailableMetadataFields(source));
    }

    [HttpPost("apply-all")]
    [SwaggerOperation(OperationId = "ApplySourceMetadataToAllResources")]
    public async Task<BaseResponse> ApplyAll(ResourceSource source)
    {
        var taskId = $"ApplySourceMetadata_{source}";
        await btm.Start(taskId, () => new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => $"Apply {source} Metadata",
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ISourceMetadataSyncService>();
                await svc.SyncMetadataToPropertiesBatch(source,
                    percentage => args.UpdateTask(t => t.Percentage = percentage).Wait(),
                    args.CancellationToken);
            },
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            IsPersistent = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            RootServiceProvider = HttpContext.RequestServices
        });
        return BaseResponseBuilder.Ok;
    }
}
