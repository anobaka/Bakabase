import type { ComponentType, ReactNode } from "react";

/**
 * Every guide living in the help center is a "topic". Adding a new guide =
 * adding a topic definition to the registry in `topics.tsx`.
 */
export type HelpTopicId = "pathMark";

/** Sections of the path mark topic. Extend this union as more topics arrive. */
export type PathMarkHelpSectionId = "whatIs" | "examples" | "comparison" | "concepts";

export type HelpSectionId = PathMarkHelpSectionId;

/** Where a help entry point should land inside the help center. */
export interface HelpTarget {
  topic?: HelpTopicId;
  section?: HelpSectionId;
  /** Concept id to expand and scroll to when landing on a concepts section. */
  concept?: string;
}

export interface HelpTopicContentProps {
  section?: HelpSectionId;
  concept?: string;
  /** True when the help center was opened automatically for a first-time user. */
  firstRun?: boolean;
}

export interface HelpTopicDefinition {
  id: HelpTopicId;
  /** i18n key of the topic name shown in the topic switcher / modal title. */
  titleKey: string;
  icon: ReactNode;
  Content: ComponentType<HelpTopicContentProps>;
}
