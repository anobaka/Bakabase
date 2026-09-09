using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Models.Input;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition")]
public class AcquisitionController(
    IAcquisitionService service,
    IAcquisitionLeadService leads,
    Bakabase.Abstractions.Services.IPlaceholderResourceService placeholders,
    Components.Acquisition.AcquisitionInboxService inbox,
    Components.Acquisition.AcquisitionSetupService setup,
    IBOptionsManager<Bakabase.Modules.Acquisition.Models.Domain.AcquisitionOptions> options)
    : ControllerBase
{
    /// <summary>
    /// Start from a link and nothing else. The link is resolved to a resource — matched against one
    /// that already exists, or created — and then acquired the ordinary way. This is what the
    /// browser script calls, and it is the shortest path from "I found this" to "it is coming".
    /// </summary>
    [HttpPost("from-url")]
    [SwaggerOperation(OperationId = "CreateAcquisitionFromUrl")]
    public async Task<SingletonResponse<AcquisitionTask>> CreateFromUrl(
        [FromBody] AcquisitionFromUrlInputModel model)
    {
        try
        {
            var placeholder = await placeholders.CreateOrMatchBySharedUrl(model.Url);
            var lead = await leads.FindByValue(AcquisitionLeadKind.SharedPage, model.Url);

            var task = await service.CreateAsync(placeholder.ResourceId, AcquisitionLeadKind.SharedPage,
                model.Url, lead?.Id, model.RecipeDefinitionId, model.CollectionId);

            return new SingletonResponse<AcquisitionTask>(task);
        }
        catch (InvalidOperationException e)
        {
            return SingletonResponseBuilder<AcquisitionTask>.BuildBadRequest(e.Message);
        }
    }

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

    /// <summary>
    /// What is sitting in the inbox, and what each waiting acquisition makes of it. The scores are
    /// the watcher's own reasoning, shown so a user can see why it did or did not claim something.
    /// </summary>
    [HttpGet("inbox")]
    [SwaggerOperation(OperationId = "GetAcquisitionInbox")]
    public async Task<ListResponse<Components.Acquisition.InboxCandidate>> GetInbox() =>
        new(await inbox.ListAsync());

    /// <summary>
    /// Undoes a claim the watcher got wrong: the files go back to the inbox and the acquisition
    /// stops, so they are free for whichever one actually wanted them.
    /// </summary>
    [HttpPost("{id:int}/unclaim")]
    [SwaggerOperation(OperationId = "UnclaimAcquisitionFiles")]
    public async Task<BaseResponse> Unclaim(int id)
    {
        try
        {
            await inbox.UnclaimAsync(id);
        }
        catch (InvalidOperationException e)
        {
            return BaseResponseBuilder.BuildBadRequest(e.Message);
        }

        return BaseResponseBuilder.Ok;
    }

    [HttpGet("options")]
    [SwaggerOperation(OperationId = "GetAcquisitionOptions")]
    public SingletonResponse<Bakabase.Modules.Acquisition.Models.Domain.AcquisitionOptions> GetOptions() =>
        new(options.Value);

    [HttpPut("options")]
    [SwaggerOperation(OperationId = "PutAcquisitionOptions")]
    public async Task<BaseResponse> PutOptions(
        [FromBody] Bakabase.Modules.Acquisition.Models.Domain.AcquisitionOptions model)
    {
        await options.SaveAsync(model);

        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Everything the first-run wizard asked for, applied at once — including the one path mark a
    /// user of this pipeline should never have to learn about.
    /// </summary>
    [HttpPost("setup")]
    [SwaggerOperation(OperationId = "SetUpAcquisition")]
    public async Task<SingletonResponse<Components.Acquisition.AcquisitionSetupResult>> SetUp(
        [FromBody] Components.Acquisition.AcquisitionSetupInputModel model)
    {
        try
        {
            return new SingletonResponse<Components.Acquisition.AcquisitionSetupResult>(
                await setup.ApplyAsync(model));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return SingletonResponseBuilder<Components.Acquisition.AcquisitionSetupResult>
                .BuildBadRequest(e.Message);
        }
    }

    [HttpGet("recipes")]
    [SwaggerOperation(OperationId = "GetAcquisitionRecipes")]
    public async Task<ListResponse<AcquisitionRecipeSummary>> GetRecipes() =>
        new(await service.GetRecipesAsync());
}
