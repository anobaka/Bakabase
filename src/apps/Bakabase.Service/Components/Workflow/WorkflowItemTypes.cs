namespace Bakabase.Service.Components.Workflow;

/// <summary>
/// Well-known semantic item-type tags flowing through workflow chains. These are
/// app-level concepts (the Workflow module itself treats them as opaque strings).
/// Convention: <c>item.{source}.{thing}</c>.
/// </summary>
public static class WorkflowItemTypes
{
    /// <summary>A heterogeneous batch of subscription items (mixed sources) — only generic,
    /// source-agnostic activities accept it.</summary>
    public const string SubscriptionAny = "item.subscription.any";

    /// <summary>A Pixiv illustration.</summary>
    public const string PixivIllust = "item.pixiv.illust";

    /// <summary>An ExHentai gallery (its <c>Url</c> points at a gallery page).</summary>
    public const string ExHentaiGallery = "item.exhentai.gallery";

    /// <summary>A free-text search query produced by an upstream transform (e.g. an LLM).</summary>
    public const string SearchQuery = "item.searchQuery";

    /// <summary>A completed download task — emitted by the downloader.completed trigger.</summary>
    public const string DownloaderCompleted = "item.downloader.completed";
}
