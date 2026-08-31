"use client";

import type { TreeLine } from "./DirectoryTree";

import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight, AiOutlineFileAdd, AiOutlineTags } from "react-icons/ai";
import { MdVideoLibrary } from "react-icons/md";

import DirectoryTree from "./DirectoryTree";

const k = (key: string) => `helpCenter.pathMark.whatIs.${key}`;

/** The same sample tree as the movieGenre example, kept small for the diagram. */
const diagramTree: TreeLine[] = [
  {
    depth: 0,
    kind: "dir",
    nameKey: "helpCenter.pathMark.node.movies",
    mark: "mediaLibrary",
    badgeKey: "helpCenter.pathMark.badge.libraryMovies",
  },
  {
    depth: 1,
    kind: "dir",
    nameKey: "helpCenter.pathMark.node.scifi",
    mark: "property",
    badgeKey: "helpCenter.pathMark.badge.genreDynamic",
  },
  { depth: 2, kind: "dir", nameKey: "helpCenter.pathMark.node.interstellar", mark: "resource" },
  {
    depth: 1,
    kind: "dir",
    nameKey: "helpCenter.pathMark.node.drama",
    mark: "property",
    badgeKey: "helpCenter.pathMark.badge.genreDynamic",
  },
  { depth: 2, kind: "dir", nameKey: "helpCenter.pathMark.node.shawshank", mark: "resource" },
];

const markTypeCards = [
  {
    id: "resource",
    icon: <AiOutlineFileAdd className="text-lg" />,
    color: "success",
    style: "bg-success/5 border-success/20 text-success",
  },
  {
    id: "property",
    icon: <AiOutlineTags className="text-lg" />,
    color: "primary",
    style: "bg-primary/5 border-primary/20 text-primary",
  },
  {
    id: "mediaLibrary",
    icon: <MdVideoLibrary className="text-lg" />,
    color: "secondary",
    style: "bg-secondary/5 border-secondary/20 text-secondary",
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

      {/* Tree -> result diagram */}
      <div className="flex flex-col md:flex-row items-stretch md:items-center gap-2">
        <div className="flex-1 min-w-0">
          <div className="text-xs text-default-400 mb-1">{t(k("diagram.treeTitle"))}</div>
          <DirectoryTree lines={diagramTree} />
        </div>
        <AiOutlineArrowRight className="hidden md:block text-2xl text-default-300 shrink-0" />
        <div className="flex-1 min-w-0">
          <div className="text-xs text-default-400 mb-1">{t(k("diagram.resultTitle"))}</div>
          <div className="flex flex-col gap-1.5 rounded-lg bg-default-50 border border-default-200 p-3 text-sm">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-success shrink-0" />
              <span className="text-default-700">{t(k("diagram.resultResources"))}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-primary shrink-0" />
              <span className="text-default-700">{t(k("diagram.resultProperties"))}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-secondary shrink-0" />
              <span className="text-default-700">{t(k("diagram.resultLibrary"))}</span>
            </div>
            <div className="text-xs text-default-400 mt-1">{t(k("diagram.resultNote"))}</div>
          </div>
        </div>
      </div>

      {/* Three mark types */}
      <div>
        <h4 className="text-sm font-semibold mb-2">{t(k("markTypes.title"))}</h4>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
          {markTypeCards.map((card) => (
            <div key={card.id} className={`rounded-lg border p-3 ${card.style}`}>
              <div className="flex items-center gap-2 font-medium text-sm mb-1">
                {card.icon}
                {t(k(`markTypes.${card.id}.name`))}
              </div>
              <p className="text-xs text-default-600">{t(k(`markTypes.${card.id}.desc`))}</p>
            </div>
          ))}
        </div>
        <div className="mt-2 px-3 py-2 rounded-lg bg-warning/5 border border-warning/20 text-sm text-default-700">
          {t(k("markTypes.resourceIsCore"))}
        </div>
      </div>

      {/* Workflow */}
      <div>
        <h4 className="text-sm font-semibold mb-2">{t(k("workflow.title"))}</h4>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-2">
          {[1, 2, 3].map((step) => (
            <div key={step} className="flex items-start gap-2 rounded-lg bg-default-100 px-3 py-2">
              <div className="shrink-0 w-6 h-6 flex items-center justify-center rounded-full bg-warning/15 text-warning font-bold text-xs">
                {step}
              </div>
              <div className="min-w-0">
                <div className="text-sm font-medium text-default-700">
                  {t(k(`workflow.step${step}.title`))}
                </div>
                <div className="text-xs text-default-500">{t(k(`workflow.step${step}.desc`))}</div>
              </div>
            </div>
          ))}
        </div>
        <p className="text-xs text-default-400 mt-2">{t(k("workflow.syncNote"))}</p>
      </div>
    </div>
  );
};

WhatIsSection.displayName = "WhatIsSection";

export default WhatIsSection;
