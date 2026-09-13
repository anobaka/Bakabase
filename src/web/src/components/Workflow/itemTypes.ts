import type { TFunction } from "i18next";

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
  FsEntry: "item.fs.entry",
  Resource: "item.resource",
  Acquisition: "item.acquisition",
  AcquisitionStatusChange: "item.acquisition.statusChange",
  CollectionMember: "item.collection.member",
} as const;

/** Shared by the canvas and type selectors; unknown types retain the server's readable name. */
export function workflowItemTypeDisplayName(
  t: TFunction,
  itemType: string,
  fallback?: string | null,
): string {
  const key = `workflow.itemType.${itemType}.displayName`;
  const defaultValue = fallback && fallback !== key ? fallback : itemType;
  const label = t(key, { defaultValue });

  // The app's missing-key handler can return the key even when defaultValue is supplied.
  return label === key ? defaultValue : label;
}
