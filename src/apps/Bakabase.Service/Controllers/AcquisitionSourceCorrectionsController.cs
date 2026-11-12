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
[Route("~/acquisition/{id:int}/source-corrections")]
public sealed class AcquisitionSourceCorrectionsController(AcquisitionSourceCorrectionService service,
    IMediaPathGuard paths) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionSourceCorrections")]
    public async Task<SingletonResponse<AcquisitionSourceCorrections>> Get(int id) =>
        new(await service.GetAsync(id, HttpContext.RequestAborted));

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "CorrectAcquisitionSourceAccessCode")]
    public async Task<SingletonResponse<AcquisitionSourceCorrectionResult>> Correct(int id,
        [FromBody] AcquisitionSourceCorrectionInput input)
    {
        try
        {
            var remote = HttpContext.GetRemoteAccessContext();
            return new(await service.CorrectAsync(id, input, HttpContext.RequestAborted, async (directories, token) =>
            {
                if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
                foreach (var directory in directories)
                    if (!await paths.IsServableAsync(directory, token))
                        throw new UnauthorizedAccessException("This directory is outside the media libraries Bakabase serves.");
            }));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { return SingletonResponseBuilder<AcquisitionSourceCorrectionResult>.BuildBadRequest(error.Message); }
    }
}
