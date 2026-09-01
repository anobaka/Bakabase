"use client";

import type { WorkflowTriggerUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { FsRootsPicker, parseExtensions } from "../FsShared";

import { Input, NumberInput, Select } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";
import { FsScanTarget, fsScanTargets } from "@/sdk/constants";

/** Mirror of the backend FsWatchTrigger.FsWatchFilter. */
export interface FsWatchFilter {
  roots: string[];
  target: FsScanTarget;
  extensionFilter: string[];
  /** Seconds an entry must stay quiet before it fires — mid-copy files must not fire. */
  settleSeconds: number;
}

const EMPTY: FsWatchFilter = {
  roots: [],
  target: FsScanTarget.Both,
  extensionFilter: [],
  settleSeconds: 10,
};

const FilterForm: React.FC<{
  value: FsWatchFilter;
  onChange: (v: FsWatchFilter) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      <FsRootsPicker roots={value.roots} onChange={(roots) => onChange({ ...value, roots })} />

      <div className="grid grid-cols-2 gap-2">
        <Select
          dataSource={fsScanTargets.map(({ value: v }) => ({
            value: String(v),
            label: t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[v]}`),
          }))}
          label={t<string>("workflow.trigger.fsManualScan.target.label")}
          selectedKeys={[String(value.target)]}
          onSelectionChange={(keys) => {
            const k = Array.from(keys)[0];

            if (k != undefined) onChange({ ...value, target: Number(k) as FsScanTarget });
          }}
        />
        <NumberInput
          description={t<string>("workflow.trigger.fsWatch.settle.description")}
          label={t<string>("workflow.trigger.fsWatch.settle.label")}
          minValue={1}
          value={value.settleSeconds}
          onValueChange={(v) =>
            onChange({ ...value, settleSeconds: Math.max(1, Math.trunc(v || 1)) })
          }
        />
      </div>

      <Input
        description={t<string>("workflow.trigger.fsManualScan.extensions.description")}
        label={t<string>("workflow.trigger.fsManualScan.extensions.label")}
        value={value.extensionFilter.join(", ")}
        onValueChange={(v) => onChange({ ...value, extensionFilter: parseExtensions(v) })}
      />
    </div>
  );
};

const FilterSummary: React.FC<{ filter: FsWatchFilter }> = ({ filter }) => {
  const { t } = useTranslation();

  const parts = [
    t<string>(`workflow.trigger.fsManualScan.target.${FsScanTarget[filter.target]}`),
    t<string>("workflow.trigger.fsWatch.summary.settle", { seconds: filter.settleSeconds }),
  ];

  if (filter.extensionFilter.length > 0) {
    parts.push(filter.extensionFilter.join(", "));
  }

  return (
    <div className="text-xs text-default-500 flex flex-wrap gap-x-2">
      <span className="break-all">{filter.roots.join(" ｜ ")}</span>
      <span>·</span>
      <span>{parts.join(" · ")}</span>
    </div>
  );
};

export const FsWatchTriggerUI: WorkflowTriggerUI<FsWatchFilter> = {
  kind: "fs.watch",
  displayNameKey: "workflow.trigger.fsWatch.displayName",
  defaultFilter: () => ({ ...EMPTY }),
  parseFilter: (json) => {
    if (!json) return { ...EMPTY };
    try {
      const parsed = JSON.parse(json) as Partial<FsWatchFilter>;

      return {
        roots: parsed.roots ?? [],
        target: parsed.target ?? FsScanTarget.Both,
        extensionFilter: parsed.extensionFilter ?? [],
        settleSeconds: parsed.settleSeconds ?? 10,
      };
    } catch {
      return { ...EMPTY };
    }
  },
  serializeFilter: (filter) => JSON.stringify(filter),
  isValid: (filter) => filter.roots.length > 0 && filter.settleSeconds >= 1,
  resolveOutputItemType: () => WorkflowItemTypes.FsEntry,
  FilterForm,
  FilterSummary,
};
