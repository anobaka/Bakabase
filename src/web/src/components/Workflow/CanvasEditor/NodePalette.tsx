"use client";

import type { DescriptorWithFit } from "../activityFit";
import type { components } from "@/sdk/BApi2";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import GroupLabel from "../GroupLabel";
import { activityDisplayName } from "../displayNames";

import { Chip, Input } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

type ActivityDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityDescriptorViewModel"];

const categoryDot: Record<WorkflowActivityCategory, string> = {
  [WorkflowActivityCategory.Filter]: "bg-warning",
  [WorkflowActivityCategory.Transform]: "bg-success",
  [WorkflowActivityCategory.Action]: "bg-primary",
};

export interface PaletteEntry {
  descriptor: ActivityDescriptorVm;
  /** Fit at the chain tail; null = incompatible there (dimmed, with the reason). */
  fit: DescriptorWithFit["fit"] | null;
  /** Human explanation for a dimmed entry. */
  reason?: string;
}

interface Props {
  entries: PaletteEntry[];
  onAdd: (entry: PaletteEntry) => void;
  onDragStart: (ev: React.PointerEvent, kind: string) => void;
}

/**
 * The always-visible node library (design §2): every activity grouped by domain, searchable.
 * Compatible entries are draggable onto the canvas and clickable to append at the tail;
 * incompatible ones stay visible but dimmed with the reason — capabilities discoverable,
 * constraints explained.
 */
const NodePalette: React.FC<Props> = ({ entries, onAdd, onDragStart }) => {
  const { t } = useTranslation();
  const [search, setSearch] = useState("");
  const [shakingKind, setShakingKind] = useState<string | null>(null);
  const [reasonKind, setReasonKind] = useState<string | null>(null);

  const groups = useMemo(() => {
    const query = search.trim().toLowerCase();
    const filtered = entries.filter((entry) => {
      if (!query) return true;
      const name = activityDisplayName(t, entry.descriptor.kind, entry.descriptor.displayName);

      return (
        name.toLowerCase().includes(query) || entry.descriptor.kind.toLowerCase().includes(query)
      );
    });
    const byGroup = new Map<string, PaletteEntry[]>();

    for (const entry of filtered) {
      const group = entry.descriptor.group || "";

      if (!byGroup.has(group)) byGroup.set(group, []);
      byGroup.get(group)!.push(entry);
    }

    return Array.from(byGroup.entries());
  }, [entries, search, t]);

  return (
    <div className="flex flex-col gap-2 h-full min-h-0">
      <Input
        placeholder={t<string>("workflow.activity.picker.searchPlaceholder")}
        size="sm"
        value={search}
        onValueChange={setSearch}
      />
      <div className="flex-1 min-h-0 overflow-y-auto pr-1 flex flex-col gap-1">
        {groups.map(([group, groupEntries]) => (
          <div key={group || "_"} className="flex flex-col gap-1">
            <div className="text-xs text-default-400 mt-2 first:mt-0 px-1">
              <GroupLabel group={group} />
            </div>
            {groupEntries.map((entry) => {
              const { descriptor, fit } = entry;
              const name = activityDisplayName(t, descriptor.kind, descriptor.displayName);
              const disabled = fit === null;

              return (
                <div key={descriptor.kind}>
                  <div
                    className={`flex items-center gap-2 rounded-lg border border-default-200
                      bg-content2 px-2.5 py-1.5 text-[12.5px] select-none touch-none
                      ${disabled ? "opacity-40 cursor-not-allowed" : "cursor-grab active:cursor-grabbing hover:border-default-400"}
                      ${shakingKind === descriptor.kind ? "animate-[wfshake_.3s]" : ""}`}
                    role="button"
                    tabIndex={disabled ? -1 : 0}
                    onKeyDown={(e) => {
                      if (!disabled && e.key === "Enter") onAdd(entry);
                    }}
                    onPointerDown={(ev) => {
                      if (disabled) {
                        setShakingKind(null);
                        requestAnimationFrame(() => setShakingKind(descriptor.kind));
                        setReasonKind(descriptor.kind);
                        setTimeout(
                          () => setReasonKind((k) => (k === descriptor.kind ? null : k)),
                          2500,
                        );

                        return;
                      }
                      onDragStart(ev, descriptor.kind);
                    }}
                  >
                    <span
                      className={`w-2 h-2 rounded-full shrink-0 ${categoryDot[descriptor.category as WorkflowActivityCategory] ?? "bg-default-400"}`}
                    />
                    <span className="flex-1 min-w-0 truncate">{name}</span>
                    {fit === "bridge" && (
                      <Chip color="secondary" size="sm" variant="flat">
                        {t<string>("workflow.activity.picker.needsBridge")}
                      </Chip>
                    )}
                  </div>
                  {reasonKind === descriptor.kind && entry.reason && (
                    <div className="text-[10.5px] text-danger px-1 pt-0.5">{entry.reason}</div>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>
      <div className="text-[10.5px] text-default-400 px-1">
        {t<string>("workflow.editor.palette.hint")}
      </div>
      {/* Local keyframes for the refusal shake. */}
      <style>{"@keyframes wfshake{25%{transform:translateX(-4px)}75%{transform:translateX(4px)}}"}</style>
    </div>
  );
};

NodePalette.displayName = "NodePalette";

export default NodePalette;
