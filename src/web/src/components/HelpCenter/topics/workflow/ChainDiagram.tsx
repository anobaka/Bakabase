"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDown } from "react-icons/ai";

/**
 * Node categories mirror the real editor's coloring (trigger = secondary,
 * filter = warning, transform = success, action = primary), so what users see
 * in the guide matches what they will see on the canvas. "result" is not a
 * node — it renders as a dashed outcome box closing the chain.
 */
export type ChainNodeCategory = "trigger" | "filter" | "transform" | "action" | "result";

export interface ChainNode {
  category: ChainNodeCategory;
  /** i18n key of the node's display name. */
  nameKey: string;
  /** i18n key of a short config annotation shown inside the node (optional). */
  badgeKey?: string;
  /** i18n key of a note on the edge to the NEXT node — a type change, a 1→N fan-out… */
  edgeNoteKey?: string;
}

const categoryStyle: Record<Exclude<ChainNodeCategory, "result">, string> = {
  trigger: "border-secondary/40 bg-secondary/5",
  filter: "border-warning/40 bg-warning/5",
  transform: "border-success/40 bg-success/5",
  action: "border-primary/40 bg-primary/5",
};

const categoryDot: Record<Exclude<ChainNodeCategory, "result">, string> = {
  trigger: "bg-secondary",
  filter: "bg-warning",
  transform: "bg-success",
  action: "bg-primary",
};

/**
 * Renders a workflow chain vertically: trigger on top, then the activities in
 * order, closing with the outcome. The guide's counterpart of the path mark
 * topic's DirectoryTree — every example is drawn with this one component.
 */
const ChainDiagram: React.FC<{ nodes: ChainNode[] }> = ({ nodes }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col rounded-lg bg-default-50 border border-default-200 px-3 py-2.5">
      {nodes.map((node, i) => (
        <React.Fragment key={i}>
          {node.category === "result" ? (
            <div className="rounded-md border border-dashed border-default-300 px-2.5 py-1.5 text-xs text-default-600">
              {t(node.nameKey)}
            </div>
          ) : (
            <div
              className={`flex items-center gap-2 rounded-md border px-2.5 py-1.5 ${categoryStyle[node.category]}`}
            >
              <span className={`w-2 h-2 rounded-full shrink-0 ${categoryDot[node.category]}`} />
              <span className="text-xs font-medium text-default-800 shrink-0">
                {t(node.nameKey)}
              </span>
              {node.badgeKey && (
                <span className="text-xs text-default-500 truncate">{t(node.badgeKey)}</span>
              )}
            </div>
          )}

          {i < nodes.length - 1 && (
            <div className="flex items-center gap-1.5 pl-3 py-0.5 min-h-5">
              <AiOutlineDown className="text-default-300 text-xs shrink-0" />
              {node.edgeNoteKey && (
                <span className="text-[11px] text-default-400">{t(node.edgeNoteKey)}</span>
              )}
            </div>
          )}
        </React.Fragment>
      ))}
    </div>
  );
};

ChainDiagram.displayName = "ChainDiagram";

export default ChainDiagram;
