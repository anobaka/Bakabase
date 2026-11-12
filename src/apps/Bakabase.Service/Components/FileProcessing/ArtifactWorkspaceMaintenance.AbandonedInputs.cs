using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class ArtifactWorkspaceMaintenance
{
    internal Task<ArtifactWorkspaceCleanupJournal.Receipt> BeginAbandonedInputCleanupAsync(ArtifactRecord input,
        string failedOperationId, string checkpointDirectory, IReadOnlyDictionary<string, long> files,
        ArtifactDeliveryCleanupConsent consent, CancellationToken ct) =>
        journal.BeginAsync(input, files, ct, consent, relatedArtifactId: "operation:" + failedOperationId,
            checkpointDirectory: checkpointDirectory);
}
