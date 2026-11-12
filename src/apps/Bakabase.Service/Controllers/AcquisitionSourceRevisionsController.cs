using System;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/{id:int}/source-revisions")]
public sealed class AcquisitionSourceRevisionsController(AcquisitionSourceRevisionService service, IMediaPathGuard paths) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionSourceRevisions")]
    public Task<SingletonResponse<AcquisitionSourceRevisionOptions>> Get(int id) =>
        Respond(() => service.GetAsync(id, HttpContext.RequestAborted));

    [HttpPost("preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewAcquisitionSourceRevision")]
    public Task<SingletonResponse<AcquisitionSourceRevisionPreview>> Preview(int id, [FromBody] AcquisitionSourceRevisionPreviewInput input) =>
        Respond(() => service.PreviewAsync(id, input, HttpContext.RequestAborted));

    [HttpPost("apply")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ApplyAcquisitionSourceRevision")]
    public Task<SingletonResponse<AcquisitionSourceRevisionResult>> Apply(int id, [FromBody] AcquisitionSourceRevisionApplyInput input) =>
        Respond(() => service.ApplyAsync(id, input, HttpContext.RequestAborted, async (directories, ct) =>
        {
            var remote = HttpContext.GetRemoteAccessContext();
            if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
            foreach (var directory in directories)
                if (!await paths.IsServableAsync(directory, ct)) throw new UnauthorizedAccessException("This directory is outside the media libraries Bakabase serves.");
        }));

    private static async Task<SingletonResponse<T>> Respond<T>(Func<Task<T>> operation)
    {
        try {return new(await operation());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<T>.BuildBadRequest(error.Message);}
    }
}
