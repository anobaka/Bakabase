"use client";

import type { WorkflowTriggerUI } from "../types";
import type { FsManualScanFilter } from "../FsManualScan/types";

import React from "react";
import { useTranslation } from "react-i18next";

import ScanFilterForm from "../FsManualScan/FilterForm";
import ScanFilterSummary from "../FsManualScan/FilterSummary";

import { NumberInput } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import { FsScanTarget } from "@/sdk/constants";

/** The manual scan's configuration plus a schedule. */
export interface FsScheduledScanFilter extends FsManualScanFilter {
  /** 0 = manual-only (schedule off); >= 1 = run every N minutes. */
  intervalMinutes: number;
}

const EMPTY: FsScheduledScanFilter = {
  roots: [],
  target: FsScanTarget.Both,
  depth: 1,
  extensionFilter: [],
  intervalMinutes: 60,
};

/**
 * Reuses the manual scan's form/summary verbatim — the scan half of the filter must stay
 * pixel-identical to fs.manualScan — and appends only the schedule. The spread-based onChange
 * of the inner form preserves intervalMinutes untouched.
 */
const FilterForm: React.FC<{
  value: FsScheduledScanFilter;
  onChange: (v: FsScheduledScanFilter) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      <ScanFilterForm value={value} onChange={(v) => onChange(v as FsScheduledScanFilter)} />
      <NumberInput
        description={t<string>("workflow.trigger.fsScheduledScan.interval.description")}
        label={t<string>("workflow.trigger.fsScheduledScan.interval.label")}
        minValue={0}
        value={value.intervalMinutes}
        onValueChange={(v) =>
          onChange({ ...value, intervalMinutes: Math.max(0, Math.trunc(v || 0)) })
        }
      />
    </div>
  );
};

const FilterSummary: React.FC<{ filter: FsScheduledScanFilter }> = ({ filter }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-0.5">
      <ScanFilterSummary filter={filter} />
      <div className="text-xs text-default-500">
        {filter.intervalMinutes >= 1
          ? t<string>("workflow.trigger.fsScheduledScan.summary.every", {
              minutes: filter.intervalMinutes,
            })
          : t<string>("workflow.trigger.fsScheduledScan.summary.manualOnly")}
      </div>
    </div>
  );
};

export const FsScheduledScanTriggerUI: WorkflowTriggerUI<FsScheduledScanFilter> = {
  kind: "fs.scheduledScan",
  displayNameKey: "workflow.trigger.fsScheduledScan.displayName",
  defaultFilter: () => ({ ...EMPTY }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<FsScheduledScanFilter>;

      return {
        roots: parsed.roots ?? [],
        target: parsed.target ?? FsScanTarget.Both,
        depth: parsed.depth ?? 1,
        extensionFilter: parsed.extensionFilter ?? [],
        intervalMinutes: parsed.intervalMinutes ?? 60,
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
