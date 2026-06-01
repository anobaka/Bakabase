using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.InsideWorld.Business.Workflow;

/// <summary>
/// Workflow kind constants owned by the legacy InsideWorld Downloader module.
/// </summary>
public static class DownloaderWorkflowKinds
{
    public const string Module = "downloader";

    public static readonly string TriggerCompleted =
        WorkflowTriggerKinds.Build(Module, "completed");
}
