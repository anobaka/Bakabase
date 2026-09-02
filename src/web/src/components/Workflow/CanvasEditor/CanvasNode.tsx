"use client";

import type { ActivityDraft } from "./types";
import type { components } from "@/sdk/BApi2";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineClose } from "react-icons/ai";

import { getWorkflowActivityUI } from "../Activities";
import { activityDisplayName } from "../displayNames";

import { WorkflowActivityCategory } from "@/sdk/constants";

type ActivityDescriptorVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowActivityDescriptorViewModel"];

export const CategoryTone: Record<
  WorkflowActivityCategory,
  { border: string; text: string; labelKey: string }
> = {
  [WorkflowActivityCategory.Filter]: {
    border: "border-warning/50",
    text: "text-warning",
    labelKey: "workflow.editor.category.filter",
  },
  [WorkflowActivityCategory.Transform]: {
    border: "border-success/50",
    text: "text-success",
    labelKey: "workflow.editor.category.transform",
  },
  [WorkflowActivityCategory.Action]: {
    border: "border-primary/50",
    text: "text-primary",
    labelKey: "workflow.editor.category.action",
  },
};

interface Props {
  draft: ActivityDraft;
  index: number;
  descriptor?: ActivityDescriptorVm;
  selected: boolean;
  /** The chain walk says this node can't accept the type reaching it. */
  incompatible: boolean;
  /** True while this node is the drag source (rendered dimmed in place). */
  dragSource: boolean;
  onPointerDown: (ev: React.PointerEvent) => void;
  onDelete: () => void;
  onMove: (direction: -1 | 1) => void;
  onSelect: () => void;
}

/**
 * One activity on the canvas rail: category color, name, config summary, status. The whole
 * card is the drag handle; a plain click (below the drag threshold) selects it into the
 * inspector. Keyboard: ←/→ move, Delete removes, Enter selects — dragging is an enhancement,
 * never the only path (design §3).
 */
const CanvasNode: React.FC<Props> = ({
  draft,
  descriptor,
  selected,
  incompatible,
  dragSource,
  onPointerDown,
  onDelete,
  onMove,
  onSelect,
}) => {
  const { t } = useTranslation();
  const ui = getWorkflowActivityUI(draft.kind);
  const category = ui?.category ?? descriptor?.category;
  const tone = category != null ? CategoryTone[category as WorkflowActivityCategory] : undefined;
  const name = activityDisplayName(t, draft.kind, descriptor?.displayName);

  let configInvalid = false;
  let SummaryComponent: React.ReactNode = null;

  if (ui) {
    try {
      const config = ui.parseConfig(draft.configJson);

      configInvalid = !ui.isValid(config);
      SummaryComponent = <ui.Summary config={config} />;
    } catch {
      configInvalid = true;
    }
  }

  return (
    <div
      className={`group relative rounded-xl border-1.5 bg-content1 px-3 py-2 min-w-[150px] max-w-[200px]
        cursor-grab active:cursor-grabbing select-none touch-none outline-none
        ${incompatible ? "border-danger" : (tone?.border ?? "border-default-300")}
        ${selected ? "ring-2 ring-primary/60" : ""}
        ${dragSource ? "opacity-30" : ""}`}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === "Enter") onSelect();
        else if (e.key === "Delete" || e.key === "Backspace") onDelete();
        else if (e.key === "ArrowLeft") onMove(-1);
        else if (e.key === "ArrowRight") onMove(1);
        else return;
        e.preventDefault();
      }}
      onPointerDown={onPointerDown}
    >
      <div className={`text-[10px] tracking-wide ${tone?.text ?? "text-default-400"}`}>
        {tone ? t<string>(tone.labelKey) : draft.kind}
        {configInvalid && (
          <span
            className="ml-1.5 inline-block w-1.5 h-1.5 rounded-full bg-warning align-middle"
            title={t<string>("workflow.editor.node.configIncomplete")}
          />
        )}
      </div>
      <div className="text-[13px] font-semibold leading-tight">{name}</div>
      <div className="text-[10.5px] text-default-500 truncate">{SummaryComponent}</div>

      <button
        aria-label={t<string>("workflow.editor.node.remove")}
        className="absolute -top-2 -right-2 hidden group-hover:flex w-[18px] h-[18px] items-center
          justify-center rounded-full bg-danger text-white text-[10px]"
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          onDelete();
        }}
        onPointerDown={(e) => e.stopPropagation()}
      >
        <AiOutlineClose />
      </button>
    </div>
  );
};

CanvasNode.displayName = "CanvasNode";

export default CanvasNode;
