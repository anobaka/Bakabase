using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.FileProcessing;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Owner-only local artifact lifecycle administration. No client-supplied filesystem paths.</summary>
[ApiController]
[Route("~/artifacts/cleanup")]
public sealed class ArtifactsController(ArchiveQuarantineService cleanup, ArtifactWorkspaceMaintenance workspaces) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetArtifactCleanupReceipts")]
    public async Task<SingletonResponse<IReadOnlyList<ArchiveQuarantineReceipt>>> List(CancellationToken ct) => new(await cleanup.GetReceiptsAsync(ct));

    [HttpPost("{receiptId}/restore")]
    [SwaggerOperation(OperationId = "RestoreArtifactCleanup")]
    public async Task<SingletonResponse<ArchiveQuarantineResult>> Restore(string receiptId, CancellationToken ct)
    {
        try {return new(await cleanup.RestoreAsync(receiptId, ct));}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<ArchiveQuarantineResult>.BuildBadRequest(error.Message);}
    }
    [HttpGet("workspaces/preview")]
    [SwaggerOperation(OperationId = "PreviewArtifactWorkspaceCleanup")]
    public async Task<SingletonResponse<IReadOnlyList<ArtifactWorkspaceCleanupPreview>>> PreviewWorkspaces(CancellationToken ct) =>
        new(await workspaces.PreviewAsync(ct));

    [HttpPost("workspaces/reclaim")]
    [SwaggerOperation(OperationId = "ReclaimArtifactWorkspaces")]
    public async Task<SingletonResponse<IReadOnlyList<ArtifactWorkspaceCleanupResult>>> ReclaimWorkspaces(
        [FromBody] ArtifactWorkspaceCleanupInput input, CancellationToken ct)
    {
        try { return new(await workspaces.ReclaimAsync(input.ArtifactIds, ct)); }
        catch (ArgumentException error)
        { return SingletonResponseBuilder<IReadOnlyList<ArtifactWorkspaceCleanupResult>>.BuildBadRequest(error.Message); }
    }
}
