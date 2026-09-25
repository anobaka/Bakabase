import type { ComponentType, ReactNode } from "react";

/**
 * Every guide living in the help center is a "topic". Adding a new guide =
 * adding a topic definition to the registry in `topics.tsx`. Each topic
 * contributes one overview entry to the left navigation, plus (optionally)
 * a group of concept entries rendered below it.
 */
export type HelpTopicId =
  | "gettingStarted"
  | "pathMark"
  | "workflow"
  | "resourceProfile"
  | "unmaterializedResource"
  | "collection"
  | "subscription"
  | "acquisition"
  | "bulkModification"
  | "multiDevice"
  | "notices";

/** Horizontal tabs inside the path mark overview. Extend as more topics arrive. */
export type PathMarkHelpSectionId = "whatIs" | "examples" | "comparison";

/** Horizontal tabs inside the workflow overview. */
export type WorkflowHelpSectionId = "whatIs" | "examples" | "triggers";

/** Horizontal tabs inside the multi-device overview. */
export type MultiDeviceHelpSectionId = "whatIs" | "browse" | "switch" | "setup" | "dataSync";

export type HelpSectionId =
  | PathMarkHelpSectionId
  | WorkflowHelpSectionId
  | MultiDeviceHelpSectionId;

/** Where a help entry point should land inside the help center. */
export interface HelpTarget {
  topic?: HelpTopicId;
  /** Tab to open inside the topic's overview. */
  section?: HelpSectionId;
  /** Concept entry (left navigation) to select instead of the overview. */
  concept?: string;
}

export interface HelpTopicContentProps {
  section?: HelpSectionId;
  /** Open an application feature and dismiss the hosting help dialog. */
  onNavigate?: (path: string) => void;
  /** Show another entry of the help center in place, e.g. a guide a topic points to. */
  onOpenTopic?: (target: HelpTarget) => void;
  /** True when the help center was opened automatically for a first-time user. */
  firstRun?: boolean;
}

export interface HelpConceptNavItem {
  id: string;
  /** i18n key of the concept name shown in the left navigation. */
  labelKey: string;
}

export interface HelpTopicDefinition {
  id: HelpTopicId;
  /** i18n key of the topic name shown as its overview entry in the navigation. */
  titleKey: string;
  icon: ReactNode;
  /** Overview content (rendered when the topic's own entry is selected). */
  Content: ComponentType<HelpTopicContentProps>;
  /** i18n key of the group label above the concept entries. */
  conceptGroupLabelKey?: string;
  /** Concept entries listed under the topic in the left navigation. */
  concepts?: HelpConceptNavItem[];
  /** Renders the detail of one concept entry. */
  ConceptContent?: ComponentType<{ conceptId: string }>;
}
