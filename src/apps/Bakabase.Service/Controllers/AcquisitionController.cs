using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition")]
public class AcquisitionController(
    IAcquisitionService service,
    IAcquisitionLeadService leads) : ControllerBase
{
    [HttpPost]
    [SwaggerOperation(OperationId = "CreateAcquisition")]
    public async Task<SingletonResponse<AcquisitionTask>> Create([FromBody] AcquisitionCreationInputModel model)
    {
        AcquisitionLeadKind kind;
        string value;

        if (model.AcquisitionLeadId is { } leadId)
        {
            var lead = await leads.Get(leadId);

            if (lead == null)
            {
                return SingletonResponseBuilder<AcquisitionTask>.BuildBadRequest(
                    $"Lead #{leadId} no longer exists.");
            }

            (kind, value) = (lead.Kind, lead.Value);
        }
        else if (model.LeadKind is { } k && !string.IsNullOrWhiteSpace(model.LeadValue))
        {
            (kind, value) = (k, model.LeadValue);
        }
        else
        {
            return SingletonResponseBuilder<AcquisitionTask>.BuildBadRequest(
                "Say where to get it from: either a stored lead, or a kind and a value.");
        }

        try
        {
            var task = await service.CreateAsync(model.ResourceId, kind, value, model.AcquisitionLeadId,
                model.RecipeDefinitionId, model.CollectionId);

            return new SingletonResponse<AcquisitionTask>(task);
        }
        catch (InvalidOperationException e)
        {
            // Already has files, already being acquired, no such recipe — all things the user can
            // act on, none of them a server fault.
            return SingletonResponseBuilder<AcquisitionTask>.BuildBadRequest(e.Message);
        }
    }

    [HttpGet]
    [SwaggerOperation(OperationId = "SearchAcquisitions")]
    public async Task<ListResponse<AcquisitionTask>> Search([FromQuery] AcquisitionStatus? status,
        [FromQuery] int? resourceId) =>
        new(await service.SearchAsync(status, resourceId));

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetAcquisition")]
    public async Task<SingletonResponse<AcquisitionTask?>> Get(int id) =>
        new(await service.GetAsync(id));

    [HttpPost("{id:int}/resume")]
    [SwaggerOperation(OperationId = "ResumeAcquisition")]
    public async Task<BaseResponse> Resume(int id, [FromBody] AcquisitionResumeInputModel model)
    {
        try
        {
            await service.ResumeAsync(id, model.SignalJson);
        }
        catch (InvalidOperationException e)
        {
            return BaseResponseBuilder.BuildBadRequest(e.Message);
        }

        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/retry")]
    [SwaggerOperation(OperationId = "RetryAcquisition")]
    public async Task<SingletonResponse<AcquisitionTask>> Retry(int id)
    {
        try
        {
            return new SingletonResponse<AcquisitionTask>(await service.RetryAsync(id));
        }
        catch (InvalidOperationException e)
        {
            return SingletonResponseBuilder<AcquisitionTask>.BuildBadRequest(e.Message);
        }
    }

    [HttpPost("{id:int}/cancel")]
    [SwaggerOperation(OperationId = "CancelAcquisition")]
    public async Task<BaseResponse> Cancel(int id)
    {
        try
        {
            await service.CancelAsync(id);
        }
        catch (InvalidOperationException e)
        {
            return BaseResponseBuilder.BuildBadRequest(e.Message);
        }

        return BaseResponseBuilder.Ok;
    }

    [HttpGet("recipes")]
    [SwaggerOperation(OperationId = "GetAcquisitionRecipes")]
    public async Task<ListResponse<AcquisitionRecipeSummary>> GetRecipes() =>
        new(await service.GetRecipesAsync());
}
