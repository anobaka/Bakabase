import type { WorkflowTriggerUI } from "../types";
import type { SubscriptionUpdatedFilter } from "./types";

import FilterForm from "./FilterForm";
import FilterSummary from "./FilterSummary";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

const EMPTY: SubscriptionUpdatedFilter = { subscriptionIds: [], kinds: [] };

export const SubscriptionUpdatedTriggerUI: WorkflowTriggerUI<SubscriptionUpdatedFilter> = {
  kind: "subscription.updated",
  displayNameKey: "workflow.trigger.subscriptionUpdated.displayName",
  defaultFilter: () => ({ subscriptionIds: [], kinds: [] }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<SubscriptionUpdatedFilter>;
      return {
        subscriptionIds: parsed.subscriptionIds ?? [],
        kinds: parsed.kinds ?? [],
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) => {
    // null = "match all" — no need to roundtrip an empty filter through the DB.
    if (filter.subscriptionIds.length === 0 && filter.kinds.length === 0) return null;
    return JSON.stringify(filter);
  },
  isValid: () => true, // empty = match-all; always valid
  // Every source emits the same thing now: by the time the event fires, each item it listed has
  // already become a resource. The per-kind table this replaced needed updating in two places
  // whenever a provider was added, and both said "resource" in the end.
  resolveOutputItemType: () => WorkflowItemTypes.Resource,
  FilterForm,
  FilterSummary,
};
