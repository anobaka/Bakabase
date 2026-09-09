import type { WorkflowTriggerUI } from "../types";
import type { ResourceMaterializedFilter } from "./types";

import FilterForm from "./FilterForm";
import FilterSummary from "./FilterSummary";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

const EMPTY: ResourceMaterializedFilter = { sources: [] };

export const ResourceMaterializedTriggerUI: WorkflowTriggerUI<ResourceMaterializedFilter> = {
  kind: "resource.materialized",
  displayNameKey: "workflow.trigger.resourceMaterialized.displayName",
  defaultFilter: () => ({ sources: [] }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<ResourceMaterializedFilter>;

      return { sources: parsed.sources ?? [] };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) => (filter.sources.length === 0 ? null : JSON.stringify(filter)),
  isValid: () => true,
  // The filter narrows WHICH resources fire, not WHAT shape they produce.
  resolveOutputItemType: () => WorkflowItemTypes.Resource,
  FilterForm,
  FilterSummary,
};
