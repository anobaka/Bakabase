using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities;

/// <summary>
/// Activity kind strings owned by Downloader-related workflow actions.
/// Adding a new downloader action: declare it here, reference the constant from the
/// Activity's <c>Kind</c> property.
/// </summary>
public static class DownloaderWorkflowActivityKinds
{
    private const string Module = "downloader";
    private const string ExHentaiModule = "downloader.exhentai";

    /// <summary>Hand a link to whichever downloader can fetch it.</summary>
    public static readonly string Enqueue = WorkflowActivityKinds.Action(Module, "enqueue");

    /// <summary>
    /// The ExHentai-only predecessor of <see cref="Enqueue"/>. Kept because saved workflows name
    /// their activities by kind: dropping it would silently break every recipe that uses it.
    /// </summary>
    public static readonly string EnqueueGallery = WorkflowActivityKinds.Action(ExHentaiModule, "enqueue");
}
