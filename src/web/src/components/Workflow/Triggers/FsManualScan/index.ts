import type { WorkflowTriggerUI } from "../types";
import type { FsManualScanFilter } from "./types";

import FilterForm from "./FilterForm";
import FilterSummary from "./FilterSummary";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import { FsScanTarget } from "@/sdk/constants";

const EMPTY: FsManualScanFilter = {
  roots: [],
  target: FsScanTarget.Both,
  depth: 1,
  extensionFilter: [],
};

/**
 * Unlike the event triggers, this one is manual-only: the filter is the scan configuration
 * itself, so an empty filter is not "match all" — it is an unrunnable definition, and isValid
 * says so.
 */
export const FsManualScanTriggerUI: WorkflowTriggerUI<FsManualScanFilter> = {
  kind: "fs.manualScan",
  displayNameKey: "workflow.trigger.fsManualScan.displayName",
  defaultFilter: () => ({ ...EMPTY }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<FsManualScanFilter>;

      return {
        roots: parsed.roots ?? [],
        target: parsed.target ?? FsScanTarget.Both,
        depth: parsed.depth ?? 1,
        extensionFilter: parsed.extensionFilter ?? [],
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) => JSON.stringify(filter),
  isValid: (filter) => filter.roots.length > 0 && filter.depth >= 1,
  resolveOutputItemType: () => WorkflowItemTypes.FsEntry,
  FilterForm,
  FilterSummary,
};
