import type { HelpTopicDefinition, HelpTopicId } from "./types";

import { AiOutlineTags } from "react-icons/ai";

import PathMarkTopic from "./topics/pathMark";
import PathMarkConceptDetail from "./topics/pathMark/ConceptDetail";
import { pathMarkConcepts } from "./topics/pathMark/concepts";

/**
 * Registry of all help center topics. Other guides (onboarding, resource
 * profile, file mover, ...) join the help center by adding an entry here.
 */
export const helpTopics: HelpTopicDefinition[] = [
  {
    id: "pathMark",
    titleKey: "helpCenter.topic.pathMark",
    icon: <AiOutlineTags className="text-lg" />,
    Content: PathMarkTopic,
    conceptGroupLabelKey: "helpCenter.pathMark.section.concepts",
    concepts: pathMarkConcepts.map((concept) => ({
      id: concept.id,
      labelKey: `helpCenter.pathMark.concept.${concept.id}.name`,
    })),
    ConceptContent: PathMarkConceptDetail,
  },
];

export const getHelpTopic = (id: HelpTopicId): HelpTopicDefinition =>
  helpTopics.find((topic) => topic.id === id) ?? helpTopics[0]!;
