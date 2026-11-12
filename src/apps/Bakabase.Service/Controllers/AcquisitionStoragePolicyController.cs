using System;
using System.IO;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Models.ResponseModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/storage-policy")]
public sealed class AcquisitionStoragePolicyController(AcquisitionPlanService service,
    AcquisitionStoragePolicyPreviewStore previews, ArtifactWorkspaceMaintenance maintenance, IMediaPathGuard paths,
    PublishedArtifactAncestorCleanup ancestors) : ControllerBase
{
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionStoragePolicy")]
    public Task<SingletonResponse<AcquisitionStoragePolicyPreview>> Preview([FromBody] AcquisitionStoragePolicyPreviewInput input) =>
        Respond(() => service.PreviewStoragePolicyAsync(input, previews, Guard, HttpContext.RequestAborted));
    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "ApplyAcquisitionStoragePolicy")]
    public Task<SingletonResponse<AcquisitionStoragePolicyApplyResult>> Apply([FromBody] AcquisitionStoragePolicyApplyInput input) =>
        Respond(() => service.ApplyStoragePolicyAsync(input, previews, Guard, HttpContext.RequestAborted));
    [HttpPost("cleanup/preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionWorkspaceCleanup")]
    public Task<SingletonResponse<AcquisitionWorkspaceCleanupPreview>> PreviewCleanup([FromBody] AcquisitionStoragePolicyPreviewInput input) =>
        Respond(() => service.PreviewWorkspaceCleanupAsync(input, previews, maintenance, Guard, HttpContext.RequestAborted, ancestors));
    [HttpPost("cleanup/apply"), RemoteAccessible, SwaggerOperation(OperationId = "ReclaimAcquisitionWorkspaces")]
    public Task<SingletonResponse<AcquisitionWorkspaceCleanupResult>> Cleanup([FromBody] AcquisitionStoragePolicyApplyInput input) =>
        Respond(() => service.ReclaimWorkspacesAsync(input, previews, maintenance, Guard, HttpContext.RequestAborted, ancestors));
    private async Task Guard(string path)
    {
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await paths.IsServableAsync(path, HttpContext.RequestAborted)) throw new UnauthorizedAccessException("This directory is outside the media libraries Bakabase serves.");
    }
    private static async Task<SingletonResponse<T>> Respond<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<T>.BuildBadRequest(error.Message);}
    }
}
