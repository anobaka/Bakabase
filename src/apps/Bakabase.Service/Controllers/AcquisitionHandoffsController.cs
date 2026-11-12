using System;
using System.Threading.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Acquisition.Handoffs;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/handoffs")]
public sealed class AcquisitionHandoffsController(PostAcquisitionHandoffService service, IMediaPathGuard paths) : ControllerBase
{
    [HttpPost("post-results/prepare-filter")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PrepareFilteredPostAcquisitionCatchUp")]
    public async Task<SingletonResponse<PostAcquisitionFilteredCatchUpPreview>> PrepareFilteredSavedResults(
        [FromBody] PostParserTaskFilterScope input)
    {
        try {return new(await service.PrepareFilteredSavedResultsAsync(input, HttpContext.RequestAborted));}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<PostAcquisitionFilteredCatchUpPreview>.BuildBadRequest(error.Message);}
    }

    [HttpPost("post-results/prepare")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreparePostAcquisitionCatchUp")]
    public async Task<SingletonResponse<PostAcquisitionCatchUpPreview>> PrepareSavedResults(
        [FromBody] PostAcquisitionCatchUpPrepareInput input)
    {
        try {return new(await service.PrepareSavedResultsAsync(input, HttpContext.RequestAborted));}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<PostAcquisitionCatchUpPreview>.BuildBadRequest(error.Message);}
    }

    [HttpPost("post-results/confirm")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ConfirmPostAcquisitionCatchUp")]
    public async Task<SingletonResponse<PostAcquisitionCatchUpResult>> ConfirmSavedResults(
        [FromBody] PostAcquisitionCatchUpConfirmInput input)
    {
        try
        {
            await EnsureDirectoryAllowedAsync();
            return new(await service.ConfirmSavedResultsAsync(input, HttpContext.RequestAborted));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<PostAcquisitionCatchUpResult>.BuildBadRequest(error.Message);}
    }

    [HttpGet("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionHandoff")]
    public async Task<SingletonResponse<AcquisitionHandoffView?>> Get(string id) =>
        new(await service.GetAsync(id, HttpContext.RequestAborted));

    [HttpPost("{id}/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryAcquisitionHandoff")]
    public async Task<SingletonResponse<AcquisitionHandoffView>> Retry(string id)
    {
        try
        {
            await EnsureDirectoryAllowedAsync();
            return new(await service.RetryAsync(id, HttpContext.RequestAborted));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<AcquisitionHandoffView>.BuildBadRequest(error.Message);}
    }

    private async Task EnsureDirectoryAllowedAsync()
    {
        var remote = HttpContext.GetRemoteAccessContext();
        var directory = service.RetryDirectory;
        if (!string.IsNullOrWhiteSpace(directory) && remote is not ({IsUnrestricted: true} or {IsPaired: true}) &&
            !await paths.IsServableAsync(directory, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("HANDOFF_DIRECTORY_FORBIDDEN");
    }
}
