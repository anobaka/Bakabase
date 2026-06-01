import type { WorkflowActivityUI } from "../types";
import type { SubscriptionItemTitleContainsConfig } from "./types";

import ConfigForm from "./ConfigForm";
import Summary from "./Summary";

import { WorkflowActivityCategory } from "@/sdk/constants";

const DEFAULT: SubscriptionItemTitleContainsConfig = { keywords: [], exclude: false };

export const SubscriptionItemTitleContainsUI: WorkflowActivityUI<SubscriptionItemTitleContainsConfig> = {
  kind: "filter.subscription.itemTitleContains",
  displayNameKey: "workflow.activity.subscriptionItemTitleContains.displayName",
  category: WorkflowActivityCategory.Filter,
  defaultConfig: () => ({ ...DEFAULT, keywords: [] }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT, keywords: [] };
    try {
      const parsed = JSON.parse(json) as Partial<SubscriptionItemTitleContainsConfig>;
      return {
        keywords: Array.isArray(parsed.keywords) ? parsed.keywords.filter((k) => !!k) : [],
        exclude: parsed.exclude ?? false,
      };
    } catch {
      return { ...DEFAULT, keywords: [] };
    }
  },
  serializeConfig: (cfg) => JSON.stringify(cfg),
  // An empty-keywords filter is treated as a no-op by the backend (pass-through) — but
  // that's confusing in a workflow, so we flag the local form as invalid until at least
  // one keyword is filled in.
  isValid: (cfg) => cfg.keywords.length > 0,
  ConfigForm,
  Summary,
};
