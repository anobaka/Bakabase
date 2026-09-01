import type { ChainNode, ChainNodeCategory } from "./ChainDiagram";

const node = (key: string) => `helpCenter.workflow.node.${key}`;
const badge = (key: string) => `helpCenter.workflow.badge.${key}`;
const edge = (key: string) => `helpCenter.workflow.edge.${key}`;
const outcome = (key: string) => `helpCenter.workflow.outcome.${key}`;

export type WorkflowAbility =
  | "preview"
  | "undo"
  | "vocabulary"
  | "variables"
  | "expand"
  | "watch"
  | "schedule"
  | "event"
  | "typing";

export interface WorkflowExample {
  id: string;
  abilities: WorkflowAbility[];
  chain: ChainNode[];
  /**
   * Per-step explanation lines below the diagram, in display order. The i-th
   * entry's text is `helpCenter.workflow.examples.{id}.note{i+1}` and its dot
   * takes the category's color.
   */
  noteCategories: Exclude<ChainNodeCategory, "result">[];
}

/**
 * The example gallery ("recipes"). Ordered from the first thing everyone does
 * (clean one folder's names with preview) up to full automation, so reading
 * top-to-bottom is itself the tutorial.
 */
export const workflowExamples: WorkflowExample[] = [
  {
    id: "basicClean",
    abilities: ["preview", "undo"],
    noteCategories: ["trigger", "transform", "action"],
    chain: [
      { category: "trigger", nameKey: node("manualScan"), badgeKey: badge("scanDownloads") },
      { category: "transform", nameKey: node("fileNameOp"), badgeKey: badge("removeQualityTag"), edgeNoteKey: edge("fsEntry") },
      { category: "action", nameKey: node("saveName") },
      { category: "result", nameKey: outcome("planPreview") },
    ],
  },
  {
    id: "vocabularyClean",
    abilities: ["vocabulary", "preview"],
    noteCategories: ["trigger", "transform", "transform", "transform"],
    chain: [
      { category: "trigger", nameKey: node("manualScan"), badgeKey: badge("scanAnime") },
      { category: "transform", nameKey: node("removeWrapped"), badgeKey: badge("removeSubGroups") },
      { category: "transform", nameKey: node("removeTexts"), badgeKey: badge("removeAds") },
      { category: "transform", nameKey: node("trim"), badgeKey: badge("trimAll") },
      { category: "action", nameKey: node("saveName") },
      { category: "result", nameKey: outcome("planPreview") },
    ],
  },
  {
    id: "crossLevelRename",
    abilities: ["variables", "expand"],
    noteCategories: ["trigger", "transform", "transform", "transform", "transform"],
    chain: [
      { category: "trigger", nameKey: node("manualScan"), badgeKey: badge("scanSeasonDirs") },
      { category: "transform", nameKey: node("capture"), badgeKey: badge("captureTitleSeason") },
      {
        category: "transform",
        nameKey: node("expandChildren"),
        badgeKey: badge("expandFiles"),
        edgeNoteKey: edge("oneToMany"),
      },
      { category: "transform", nameKey: node("capture"), badgeKey: badge("captureEpisode") },
      { category: "transform", nameKey: node("template"), badgeKey: badge("episodeTemplate") },
      { category: "action", nameKey: node("saveName") },
      { category: "result", nameKey: outcome("planPreview") },
    ],
  },
  {
    id: "watchAutoClean",
    abilities: ["watch", "preview"],
    noteCategories: ["trigger", "transform", "action"],
    chain: [
      { category: "trigger", nameKey: node("watch"), badgeKey: badge("watchDownloads"), edgeNoteKey: edge("settled") },
      { category: "transform", nameKey: node("removeWrapped"), badgeKey: badge("removeSubGroups") },
      { category: "transform", nameKey: node("trim"), badgeKey: badge("trimAll") },
      { category: "action", nameKey: node("saveName") },
      { category: "result", nameKey: outcome("planAuto") },
    ],
  },
  {
    id: "scheduledClean",
    abilities: ["schedule"],
    noteCategories: ["trigger", "action"],
    chain: [
      { category: "trigger", nameKey: node("scheduledScan"), badgeKey: badge("nightly") },
      { category: "transform", nameKey: node("fileNameOp"), badgeKey: badge("removeQualityTag") },
      { category: "action", nameKey: node("saveName") },
      { category: "result", nameKey: outcome("planAuto") },
    ],
  },
  {
    id: "subscriptionDownload",
    abilities: ["event", "typing"],
    noteCategories: ["trigger", "filter", "action"],
    chain: [
      { category: "trigger", nameKey: node("subscriptionUpdated"), edgeNoteKey: edge("subscriptionItem") },
      { category: "filter", nameKey: node("titleContains"), badgeKey: badge("keepKeywords") },
      { category: "action", nameKey: node("exhentaiEnqueue") },
      { category: "result", nameKey: outcome("queued") },
    ],
  },
  {
    id: "downloadNotify",
    abilities: ["event"],
    noteCategories: ["trigger", "action"],
    chain: [
      { category: "trigger", nameKey: node("downloaderCompleted") },
      { category: "action", nameKey: node("notification"), badgeKey: badge("notifyTemplate") },
      { category: "result", nameKey: outcome("notified") },
    ],
  },
];
