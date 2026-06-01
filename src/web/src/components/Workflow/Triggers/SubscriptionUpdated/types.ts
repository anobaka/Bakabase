/**
 * Mirrors the backend SubscriptionUpdatedTrigger.Filter record.
 * Empty arrays (or absent fields) match all subscriptions.
 */
export interface SubscriptionUpdatedFilter {
  subscriptionIds: number[];
  kinds: string[];
}
