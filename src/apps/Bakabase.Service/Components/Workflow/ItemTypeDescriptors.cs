using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow;

/// <summary>Item type for the heterogeneous "any subscription source" fallback.</summary>
public class SubscriptionAnyItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.SubscriptionAny;
    public string DisplayName => "Subscription item (any source)";
    public System.Type ClrType => typeof(SubscriptionItem);
}

public class PixivIllustItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.PixivIllust;
    public string DisplayName => "Pixiv illustration";
    public System.Type ClrType => typeof(SubscriptionItem);
}

public class ExHentaiGalleryItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.ExHentaiGallery;
    public string DisplayName => "ExHentai gallery";
    public System.Type ClrType => typeof(SubscriptionItem);
}

public class SearchQueryItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.SearchQuery;
    public string DisplayName => "Search query";
    public System.Type ClrType => typeof(WorkflowQueryItem);
}

public class DownloaderCompletedItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.DownloaderCompleted;
    public string DisplayName => "Downloader: completed task";
    public System.Type ClrType => typeof(DownloaderCompletedPayload);
}
