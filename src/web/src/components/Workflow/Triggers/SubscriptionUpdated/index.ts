import type { WorkflowTriggerUI } from "../types";
import type { SubscriptionUpdatedFilter } from "./types";

import FilterForm from "./FilterForm";
import FilterSummary from "./FilterSummary";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

const EMPTY: SubscriptionUpdatedFilter = { subscriptionIds: [], kinds: [] };

// Mirror of SubscriptionUpdatedTrigger.KindToItemType (backend).
const KIND_TO_ITEM_TYPE: Record<string, string> = {
  "exhentai.search": WorkflowItemTypes.ExHentaiGallery,
  "exhentai.gallery": WorkflowItemTypes.ExHentaiGallery,
  "pixiv.followLatest": WorkflowItemTypes.PixivIllust,
};

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
  resolveOutputItemType: (filter) => {
    if (filter.kinds.length === 0) return WorkflowItemTypes.SubscriptionAny;
    const mapped = [...new Set(filter.kinds.map((k) => KIND_TO_ITEM_TYPE[k]))];
    return mapped.length === 1 && mapped[0] ? mapped[0] : WorkflowItemTypes.SubscriptionAny;
  },
  FilterForm,
  FilterSummary,
};
