using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>
/// File-processing capability consumed by workflow adapters. The owner verifies reusable artifacts,
/// validates checkpoint evidence and enforces execution policy; callers supply their exact inputs.
/// Kept internal because a checkpoint continuation is a private, verified execution proof.
/// </summary>
internal interface IFileArtifactProcessor
{
    Task<ArtifactRecord?> ResolveAsync(string intent, CancellationToken ct);

    Task<IncompleteArchiveGroup?> InspectIncompleteGroupAsync(string intent, ArtifactRecord input, FileProcessingPlan plan,
        string? defaultPassword, FileArtifactProcessingConfig config, CancellationToken ct);

    Task<FileArtifactCheckpointContinuation?> ReadCheckpointContinuationAsync(string intent,
        ArtifactRecord input, string planJson, string? defaultPassword, FileArtifactProcessingConfig config,
        ArtifactRecord previous, string previousStep, CancellationToken ct);

    Task<FileArtifactProcessingResult> ExecuteAsync(string intent, ArtifactRecord input,
        string planJson, string? defaultPassword, FileArtifactProcessingConfig config, string consumerKey,
        RecursiveArchivePasswordAnswer? answer, string? planPasswordAnswer,
        Action<int>? progress, CancellationToken ct, bool replan = false, string? stopAfterStepId = null,
        FileArtifactProcessingRecovery? recovery = null, FileArtifactCheckpointContinuation? checkpointContinuation = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? passwordEvidence = null,
        Func<string, Task>? reportStage = null, string? originalDownloadPassword = null);
}
