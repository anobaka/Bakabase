import type { WorkflowTriggerUI } from "../types";
import type { DownloaderCompletedFilter } from "./types";

import FilterForm from "./FilterForm";
import FilterSummary from "./FilterSummary";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

const EMPTY: DownloaderCompletedFilter = { thirdPartyIds: [] };

export const DownloaderCompletedTriggerUI: WorkflowTriggerUI<DownloaderCompletedFilter> = {
  kind: "downloader.completed",
  displayNameKey: "workflow.trigger.downloaderCompleted.displayName",
  defaultFilter: () => ({ thirdPartyIds: [] }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<DownloaderCompletedFilter>;
      return { thirdPartyIds: parsed.thirdPartyIds ?? [] };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) =>
    filter.thirdPartyIds.length === 0 ? null : JSON.stringify(filter),
  isValid: () => true,
  // Output is always the same item type — the filter narrows WHICH tasks fire, not WHAT
  // shape they produce.
  resolveOutputItemType: () => WorkflowItemTypes.DownloaderCompleted,
  FilterForm,
  FilterSummary,
};
