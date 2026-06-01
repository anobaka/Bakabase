namespace Bakabase.Service.Components.Workflow;

/// <summary>
/// Group tags exposed on <c>IWorkflowActivity.Group</c>. Used by the editor's activity
/// picker to bucket activities into sections (the frontend maps known values to
/// ThirdPartyLabel etc.).
/// </summary>
public static class WorkflowActivityGroups
{
    public const string Subscription = "subscription";
    public const string Pixiv = "pixiv";
    public const string ExHentai = "exhentai";
    public const string Ai = "ai";
    public const string Notification = "notification";
    public const string Downloader = "downloader";
}
