/**
 * Mirror of the backend WorkflowItemTypes constants. Used by the editor to walk the chain
 * and decide which activities are addable at each position. Keep in sync with
 * src/apps/Bakabase.Service/Components/Workflow/WorkflowItemTypes.cs.
 */
export const WorkflowItemTypes = {
  SubscriptionAny: "item.subscription.any",
  PixivIllust: "item.pixiv.illust",
  ExHentaiGallery: "item.exhentai.gallery",
  SearchQuery: "item.searchQuery",
  DownloaderCompleted: "item.downloader.completed",
} as const;
