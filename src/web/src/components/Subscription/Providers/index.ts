import type { SubscriptionProviderUI } from "./types";

import { ExHentaiSearchUI } from "./ExHentaiSearch";
import { ExHentaiGalleryUI } from "./ExHentaiGallery";
import { PixivFollowLatestUI } from "./PixivFollowLatest";

/**
 * Registry of provider UIs keyed by their backend `kind`.
 *
 * To add a new provider: drop a folder under `./Providers/<Name>/` with a `Form.tsx`,
 * `Summary.tsx`, `types.ts`, and an `index.ts` exporting a SubscriptionProviderUI,
 * then add it to this map. The Kind picker pulls from the backend's
 * `/subscription/providers` endpoint, so any provider listed by the server but missing
 * here falls back to a read-only / disabled state in the UI.
 */
export const subscriptionProviderRegistry: Record<string, SubscriptionProviderUI<any>> = {
  [ExHentaiSearchUI.kind]: ExHentaiSearchUI,
  [ExHentaiGalleryUI.kind]: ExHentaiGalleryUI,
  [PixivFollowLatestUI.kind]: PixivFollowLatestUI,
};

export function getProviderUI(kind: string): SubscriptionProviderUI<any> | undefined {
  return subscriptionProviderRegistry[kind];
}

export type { SubscriptionProviderUI } from "./types";
