using System.Collections.Generic;
using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Bakabase.Service.Components.Acquisition.Downloads;
using Bakabase.Service.Components.RemoteAccess;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/resource/{resourceId:int}/acquisition-leads")]
public class AcquisitionLeadController(
    IAcquisitionLeadService service,
    IResourceSourceLinkService sourceLinkService,
    IAcquisitionTorrentMetadataStore torrentMetadata,
    IResourceService resources) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetResourceAcquisitionLeads")]
    public async Task<ListResponse<AcquisitionLead>> GetAll(int resourceId)
    {
        var stored = await service.GetByResourceId(resourceId);

        // A platform the user holds the resource on is already an identity; it would be duplicated
        // state to store it a second time as a lead. It is derived here instead, so the UI can show
        // "you own this on DLsite" next to "someone shared it here" without the two ever
        // disagreeing.
        var links = await sourceLinkService.GetByResourceIds([resourceId]);
        var derived = links
            .Where(l => l.Source.IsPlatformHolding())
            .Select(l => new AcquisitionLead
            {
                ResourceId = resourceId,
                Kind = AcquisitionLeadKind.PlatformHolding,
                Value = $"{l.Source}:{l.SourceKey}",
                Origin = AcquisitionLeadOrigin.User,
                IsDerived = true,
                SourceName = l.Source.ToString()
            });

        return new ListResponse<AcquisitionLead>(derived.Concat(stored).ToList());
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddResourceAcquisitionLead")]
    public async Task<SingletonResponse<AcquisitionLead>> Add(int resourceId,
        [FromBody] AcquisitionLeadAddInputModel model)
    {
        var result = await service.Add(resourceId, model);
        if (result.Lead != null)
        {
            return new SingletonResponse<AcquisitionLead>(result.Lead);
        }

        return SingletonResponseBuilder<AcquisitionLead>.Build(ResponseCode.Conflict,
            $"This link is already attached to resource {result.ConflictingResourceId}.");
    }

    [HttpPost("torrent")]
    [RemoteAccessible]
    [RequestSizeLimit(AcquisitionTorrentMetadataStore.MaxMetadataBytes + 65536)]
    [SwaggerOperation(OperationId = "AddResourceAcquisitionTorrent")]
    public async Task<SingletonResponse<AcquisitionLead>> AddTorrent(int resourceId, IFormFile file,
        CancellationToken ct)
    {
        var resource = await resources.Get(resourceId);
        if (resource == null || resource.HasLocalPath)
            return SingletonResponseBuilder<AcquisitionLead>.Build(ResponseCode.InvalidPayloadOrOperation,
                "Choose a resource that does not have local files yet.");
        if (file.Length is 0 or > AcquisitionTorrentMetadataStore.MaxMetadataBytes)
            return SingletonResponseBuilder<AcquisitionLead>.Build(ResponseCode.InvalidPayloadOrOperation,
                "Choose a torrent file no larger than 4 MiB.");
        await using var stream = file.OpenReadStream();
        string reference;
        try
        {
            var bytes = await AcquisitionTorrentMetadataStore.ReadBoundedAsync(stream, ct);
            reference = await torrentMetadata.SaveAsync(bytes, ct);
        }
        catch (ArgumentException ex)
        {
            return SingletonResponseBuilder<AcquisitionLead>.BuildBadRequest(ex.Message);
        }
        var result = await service.Add(resourceId, new AcquisitionLeadAddInputModel
        {
            Kind = AcquisitionLeadKind.Torrent, Value = reference, Origin = AcquisitionLeadOrigin.User,
            Note = Path.GetFileName(file.FileName)
        });
        return result.Lead != null
            ? new SingletonResponse<AcquisitionLead>(result.Lead)
            : SingletonResponseBuilder<AcquisitionLead>.Build(ResponseCode.Conflict,
                $"This torrent is already attached to resource {result.ConflictingResourceId}.");
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteResourceAcquisitionLead")]
    public async Task<BaseResponse> Delete(int resourceId, int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }
}
