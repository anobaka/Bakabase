using System;
using System.IO;
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
[Route("~/acquisition/tasks/download-destination")]
public sealed class AcquisitionDownloadDestinationController(AcquisitionDownloadDestinationService service,
    IMediaPathGuard paths) : ControllerBase
{
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionDownloadDestinationRevision")]
    public Task<SingletonResponse<AcquisitionDownloadDestinationPreview>> Preview([FromBody] AcquisitionDownloadDestinationPreviewInput input) =>
        Respond(() => service.PreviewAsync(input, Guard, HttpContext.RequestAborted));

    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "ApplyAcquisitionDownloadDestinationRevision")]
    public Task<SingletonResponse<AcquisitionDownloadDestinationResult>> Apply([FromBody] AcquisitionDownloadDestinationApplyInput input) =>
        Respond(() => service.ApplyAsync(input, Guard, HttpContext.RequestAborted));

    private async Task Guard(string path)
    {
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await paths.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("DOWNLOAD_DESTINATION_PATH_NOT_ALLOWED");
    }
    private static async Task<SingletonResponse<T>> Respond<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (OperationCanceledException) {return SingletonResponseBuilder<T>.BuildBadRequest("DOWNLOAD_DESTINATION_OUTCOME_UNKNOWN");}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var code = error.Message;
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{2,100}$")) code = "DOWNLOAD_DESTINATION_INSPECTION_INCOMPLETE";
            return SingletonResponseBuilder<T>.BuildBadRequest(code);
        }
    }
}
