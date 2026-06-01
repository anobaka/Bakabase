using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities;

/// <summary>
/// Activity kind strings owned by Downloader-related workflow actions.
/// Adding a new downloader action: declare it here, reference the constant from the
/// Activity's <c>Kind</c> property.
/// </summary>
public static class DownloaderWorkflowActivityKinds
{
    private const string Module = "downloader.exhentai";

    public static readonly string EnqueueGallery = WorkflowActivityKinds.Action(Module, "enqueue");
}
