"use client";

import type { ChainNode } from "./ChainDiagram";

import { useTranslation } from "react-i18next";
import {
  AiOutlineArrowRight,
  AiOutlineFilter,
  AiOutlineThunderbolt,
  AiOutlineTool,
} from "react-icons/ai";
import { MdOutlineSwapHoriz } from "react-icons/md";

import ChainDiagram from "./ChainDiagram";

const k = (key: string) => `helpCenter.workflow.whatIs.${key}`;

/** The same chain as the basicClean example, kept short for the diagram. */
const diagramChain: ChainNode[] = [
  {
    category: "trigger",
    nameKey: "helpCenter.workflow.node.manualScan",
    badgeKey: "helpCenter.workflow.badge.scanDownloads",
  },
  {
    category: "transform",
    nameKey: "helpCenter.workflow.node.fileNameOp",
    badgeKey: "helpCenter.workflow.badge.removeQualityTag",
    edgeNoteKey: "helpCenter.workflow.edge.fsEntry",
  },
  { category: "action", nameKey: "helpCenter.workflow.node.saveName" },
];

const partCards = [
  {
    id: "trigger",
    icon: <AiOutlineThunderbolt className="text-lg" />,
    style: "bg-secondary/5 border-secondary/20 text-secondary",
  },
  {
    id: "filter",
    icon: <AiOutlineFilter className="text-lg" />,
    style: "bg-warning/5 border-warning/20 text-warning",
  },
  {
    id: "transform",
    icon: <MdOutlineSwapHoriz className="text-lg" />,
    style: "bg-success/5 border-success/20 text-success",
  },
  {
    id: "action",
    icon: <AiOutlineTool className="text-lg" />,
    style: "bg-primary/5 border-primary/20 text-primary",
  },
] as const;

const WhatIsSection = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      {/* Mental model */}
      <div>
        <h3 className="text-lg font-semibold mb-1">{t(k("headline"))}</h3>
        <p className="text-sm text-default-600">{t(k("intro"))}</p>
      </div>

      {/* Chain -> result diagram */}
      <div className="flex flex-col md:flex-row items-stretch md:items-center gap-2">
        <div className="flex-1 min-w-0">
          <div className="text-xs text-default-400 mb-1">{t(k("diagram.chainTitle"))}</div>
          <ChainDiagram nodes={diagramChain} />
        </div>
        <AiOutlineArrowRight className="hidden md:block text-2xl text-default-300 shrink-0" />
        <div className="flex-1 min-w-0">
          <div className="text-xs text-default-400 mb-1">{t(k("diagram.resultTitle"))}</div>
          <div className="flex flex-col gap-1.5 rounded-lg bg-default-50 border border-default-200 p-3 text-sm">
            <div className="text-default-700 font-mono text-xs">
              {t(k("diagram.resultDiffBefore"))}
            </div>
            <div className="text-default-700 font-mono text-xs">
              {t(k("diagram.resultDiffAfter"))}
            </div>
            <div className="text-xs text-default-500 mt-1">{t(k("diagram.resultNote"))}</div>
          </div>
        </div>
      </div>

      {/* The four kinds of parts */}
      <div>
        <h4 className="text-sm font-semibold mb-2">{t(k("parts.title"))}</h4>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-2">
          {partCards.map((card) => (
            <div key={card.id} className={`rounded-lg border p-3 ${card.style}`}>
              <div className="flex items-center gap-2 font-medium text-sm mb-1">
                {card.icon}
                {t(k(`parts.${card.id}.name`))}
              </div>
              <p className="text-xs text-default-600">{t(k(`parts.${card.id}.desc`))}</p>
            </div>
          ))}
        </div>
        <div className="mt-2 px-3 py-2 rounded-lg bg-warning/5 border border-warning/20 text-sm text-default-700">
          {t(k("parts.typingNote"))}
        </div>
      </div>

      {/* How it works */}
      <div>
        <h4 className="text-sm font-semibold mb-2">{t(k("steps.title"))}</h4>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
          {[1, 2, 3].map((step) => (
            <div key={step} className="flex items-start gap-2 rounded-lg bg-default-100 px-3 py-2">
              <div className="shrink-0 w-6 h-6 flex items-center justify-center rounded-full bg-secondary/15 text-secondary font-bold text-xs">
                {step}
              </div>
              <div className="min-w-0">
                <div className="text-sm font-medium text-default-700">
                  {t(k(`steps.step${step}.title`))}
                </div>
                <div className="text-xs text-default-500">{t(k(`steps.step${step}.desc`))}</div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Two-phase safety */}
      <div className="px-3 py-2 rounded-lg bg-success/5 border border-success/20 text-sm text-default-700">
        {t(k("twoPhaseNote"))}
      </div>
    </div>
  );
};

WhatIsSection.displayName = "WhatIsSection";

export default WhatIsSection;
