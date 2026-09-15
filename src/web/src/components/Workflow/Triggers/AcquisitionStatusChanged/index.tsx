import type { WorkflowTriggerUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Select } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import { acquisitionStatusLabel } from "@/components/Workflow/valueLabels";
import { acquisitionStatuses } from "@/sdk/constants";

interface Filter {
  statuses: number[];
}

const EMPTY: Filter = { statuses: [] };

const FilterForm: React.FC<{ value: Filter; onChange: (v: Filter) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Select
      dataSource={acquisitionStatuses.map(({ value: id }) => ({
        value: String(id),
        label: acquisitionStatusLabel(t, id),
        textValue: acquisitionStatusLabel(t, id),
      }))}
      description={t<string>("workflow.trigger.acquisitionStatusChanged.statuses.description")}
      label={t<string>("workflow.trigger.acquisitionStatusChanged.statuses.label")}
      selectedKeys={value.statuses.map(String)}
      selectionMode="multiple"
      onSelectionChange={(keys) => {
        const ids = Array.from(keys)
          .map((k) => Number(k))
          .filter((n) => !isNaN(n));

        onChange({ ...value, statuses: ids });
      }}
    />
  );
};

const FilterSummary: React.FC<{ filter: Filter }> = ({ filter }) => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {filter.statuses.length === 0
        ? t<string>("workflow.trigger.acquisitionStatusChanged.summary.matchAll")
        : filter.statuses.map((s) => acquisitionStatusLabel(t, s)).join(", ")}
    </span>
  );
};

export const AcquisitionStatusChangedTriggerUI: WorkflowTriggerUI<Filter> = {
  kind: "acquisition.statusChanged",
  displayNameKey: "workflow.trigger.acquisitionStatusChanged.displayName",
  defaultFilter: () => ({ statuses: [] }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<Filter>;

      return { statuses: parsed.statuses ?? [] };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) => (filter.statuses.length === 0 ? null : JSON.stringify(filter)),
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.AcquisitionStatusChange,
  FilterForm,
  FilterSummary,
};
