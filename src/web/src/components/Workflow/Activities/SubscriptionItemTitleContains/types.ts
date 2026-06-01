/**
 * Mirrors the backend SubscriptionItemTitleContainsActivity.Config record.
 */
export interface SubscriptionItemTitleContainsConfig {
  keywords: string[];
  /** Invert the predicate: drop matches, keep non-matches. */
  exclude: boolean;
}
