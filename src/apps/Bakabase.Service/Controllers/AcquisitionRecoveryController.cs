using System;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks/recovery")]
public sealed class AcquisitionRecoveryController(AcquisitionRecoveryService service, IMediaPathGuard pathGuard,
    AcquisitionRecoveryDiagnosisSessions sessions) : ControllerBase
{
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionTaskRecovery")]
    public async Task<SingletonResponse<AcquisitionRecoveryPreview>> Preview([FromBody] AcquisitionRecoveryPreviewInput input)
    {
        try
        {
            using var diagnosis = input.DiagnosisId == null ? null : sessions.Begin(input.DiagnosisId, HttpContext.RequestAborted);
            return new(await service.PreviewAsync(input, diagnosis?.Token ?? HttpContext.RequestAborted, GuardDirectory));
        }
        catch (ArgumentException error) {return SingletonResponseBuilder<AcquisitionRecoveryPreview>.BuildBadRequest(error.Message);}
        catch (UnauthorizedAccessException error) {return SingletonResponseBuilder<AcquisitionRecoveryPreview>.BuildBadRequest(error.Message);}
    }
    [HttpPost("diagnoses/{diagnosisId}/cancel"), RemoteAccessible, SwaggerOperation(OperationId = "CancelAcquisitionTaskRecoveryDiagnosis")]
    public async Task<SingletonResponse<AcquisitionRecoveryDiagnosisCancellation>> CancelDiagnosis(string diagnosisId)
    {
        try {return new(await sessions.CancelAsync(diagnosisId, HttpContext.RequestAborted));}
        catch (ArgumentException error) {return SingletonResponseBuilder<AcquisitionRecoveryDiagnosisCancellation>.BuildBadRequest(error.Message);}
    }
    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "ApplyAcquisitionTaskRecovery")]
    public async Task<SingletonResponse<AcquisitionRecoveryResult>> Apply([FromBody] AcquisitionRecoveryApplyInput input)
    {
        try {return new(await service.ApplyAsync(input, HttpContext.RequestAborted, GuardDirectory));}
        catch (ArgumentException error) {return SingletonResponseBuilder<AcquisitionRecoveryResult>.BuildBadRequest(error.Message);}
        catch (UnauthorizedAccessException error) {return SingletonResponseBuilder<AcquisitionRecoveryResult>.BuildBadRequest(error.Message);}
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        {return SingletonResponseBuilder<AcquisitionRecoveryResult>.BuildBadRequest("RECOVERY_OUTCOME_UNKNOWN");}
    }
    private async Task GuardDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await pathGuard.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("This directory is outside the media libraries Bakabase serves.");
    }
}
