import type { WorkflowTriggerUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

/**
 * The entry point of a recipe. It has nothing to configure: a recipe does not listen for anything,
 * it runs because someone asked for a particular resource, and everything that varies between runs
 * arrives with that ask.
 */
const Explanation: React.FC = () => {
  const { t } = useTranslation();

  return (
    <div className="text-xs text-default-500">
      {t<string>("workflow.trigger.acquisitionRequested.description")}
    </div>
  );
};

export const AcquisitionRequestedTriggerUI: WorkflowTriggerUI<Record<string, never>> = {
  kind: "acquisition.requested",
  displayNameKey: "workflow.trigger.acquisitionRequested.displayName",
  defaultFilter: () => ({}),
  parseFilter: () => ({}),
  serializeFilter: () => null,
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.Acquisition,
  FilterForm: Explanation,
  FilterSummary: Explanation,
};
