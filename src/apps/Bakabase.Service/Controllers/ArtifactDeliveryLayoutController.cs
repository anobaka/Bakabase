using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Services;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>A bounded, ledger-only preview; never verifies, acquires or changes physical files.</summary>
public sealed record WorkflowArtifactDeliveryLayoutPreview(string InputArtifactId, string InputFingerprint,
    string ProjectionFingerprint, ArtifactDeliveryPolicy Policy, bool Changed, int FileCount, int ChangedFileCount, int DirectoryCount,
    IReadOnlyList<ArtifactDeliveryPathMapping> FileMappings,
    IReadOnlyList<ArtifactDeliveryPathMapping> DirectoryMappings, IReadOnlyList<string> RemovedDirectories,
    bool Truncated, string State);

[ApiController]
[Route("~/workflow/run/{runId:int}/artifacts/{artifactId}/delivery-layout-preview")]
public sealed class ArtifactDeliveryLayoutController(
    WorkflowOutputQueryService<BakabaseDbContext> outputs, IArtifactOperationStore artifacts) : ControllerBase
{
    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewArtifactDeliveryLayout")]
    public async Task<SingletonResponse<WorkflowArtifactDeliveryLayoutPreview>> Preview(int runId, string artifactId,
        [FromBody] ArtifactDeliveryPolicy policy, CancellationToken ct)
    {
        try
        {
            // The existing output query enforces that this run explicitly references the artifact.
            // GetArtifactAsync is read-only; ResolveByIdAsync would hash payloads and update state.
            if (await outputs.GetArtifactFilesAsync(runId, artifactId, 1, 1, ct) == null)
                return SingletonResponseBuilder<WorkflowArtifactDeliveryLayoutPreview>.NotFound;
            var artifact = await artifacts.GetArtifactAsync(artifactId, ct);
            if (artifact == null) return SingletonResponseBuilder<WorkflowArtifactDeliveryLayoutPreview>.NotFound;
            if (policy == null) throw new ArgumentException("DELIVERY_CONTENT_LAYOUT_INVALID");
            var preview = FileArtifactDeliveryLayout.Preview(artifact, policy);
            const int limit = 100;
            var changed = preview.FileMappings.Where(m => m.SourceRelativePath != m.TargetRelativePath).ToArray();
            // Prefer changed paths so a small transformation is visible even in a large artifact.
            var examples = changed.Length > 0 ? changed : preview.FileMappings;
            return new(new WorkflowArtifactDeliveryLayoutPreview(artifact.Id, artifact.Fingerprint, preview.ProjectionFingerprint, policy, preview.Changed,
                preview.FileMappings.Count, changed.Length, preview.DirectoryMappings.Count,
                examples.Take(limit).ToArray(), preview.DirectoryMappings.Take(limit).ToArray(),
                preview.RemovedDirectories.Take(limit).ToArray(), examples.Count > limit ||
                    preview.DirectoryMappings.Count > limit || preview.RemovedDirectories.Count > limit,
                artifact.State.ToString().ToLowerInvariant()));
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        { return SingletonResponseBuilder<WorkflowArtifactDeliveryLayoutPreview>.BuildBadRequest(error.Message); }
    }
}
