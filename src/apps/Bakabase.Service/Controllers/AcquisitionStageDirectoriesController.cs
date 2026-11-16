using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/plans/{id:int}/directories")]
public sealed class AcquisitionStageDirectoriesController(AcquisitionPlanService service, IMediaPathGuard paths) : ControllerBase
{
    [HttpGet, RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionStageDirectories")]
    public Task<SingletonResponse<AcquisitionStageDirectorySnapshot>> Get(int id) =>
        Respond(() => service.GetStageDirectoriesAsync(id, HttpContext.RequestAborted));

    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionStageDirectoryChange")]
    public Task<SingletonResponse<AcquisitionStageDirectoryChangePreview>> Preview(int id,
        [FromBody] AcquisitionStageDirectoryChangeInput input) =>
        Respond(() => service.PreviewStageDirectoriesAsync(id, input, Guard, HttpContext.RequestAborted));

    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "ApplyAcquisitionStageDirectoryChange")]
    public Task<SingletonResponse<AcquisitionStageDirectoryChangeResult>> Apply(int id,
        [FromBody] AcquisitionStageDirectoryChangeInput input, [FromServices] IFileArtifactMaterializer materializer) =>
        Respond(() => service.ChangeStageDirectoriesAsync(id, input, Guard, HttpContext.RequestAborted, materializer));

    private async Task Guard(string path)
    {
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await paths.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("STAGE_DIRECTORY_PATH_NOT_ALLOWED");
    }
    private static async Task<SingletonResponse<T>> Respond<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (OperationCanceledException) {return SingletonResponseBuilder<T>.BuildBadRequest("STAGE_DIRECTORY_MOVE_INTERRUPTED");}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var code = error.Message;
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^STAGE_DIRECTORY_[A-Z_]{2,100}$")) code = "STAGE_DIRECTORY_UNAVAILABLE";
            return SingletonResponseBuilder<T>.BuildBadRequest(code);
        }
    }
}
