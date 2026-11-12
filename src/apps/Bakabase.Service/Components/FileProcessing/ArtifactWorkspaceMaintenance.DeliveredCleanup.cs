using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class ArtifactWorkspaceMaintenance
{
    internal Task<ArtifactWorkspaceCleanupJournal.Receipt> BeginDeliveredCleanupAsync(ArtifactRecord publication,
        IReadOnlyDictionary<string, long> files, ArtifactDeliveryCleanupConsent consent, CancellationToken ct) =>
        journal.BeginAsync(publication, files, ct, consent);

    internal Task<ArtifactWorkspaceCleanupJournal.Receipt> FinishDeliveredCleanupAsync(
        ArtifactWorkspaceCleanupJournal.Receipt receipt, string status, CancellationToken ct) => journal.FinishAsync(receipt, status, ct);
}
