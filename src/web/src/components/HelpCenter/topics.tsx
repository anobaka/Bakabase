import type { HelpTopicDefinition, HelpTopicId } from "./types";

import {
  AiOutlineApartment,
  AiOutlineEdit,
  AiOutlineProfile,
  AiOutlineRocket,
  AiOutlineTags,
} from "react-icons/ai";

import BulkModificationTopic from "./topics/bulkModification";
import GettingStartedTopic from "./topics/gettingStarted";
import PathMarkTopic from "./topics/pathMark";
import PathMarkConceptDetail from "./topics/pathMark/ConceptDetail";
import { pathMarkConcepts } from "./topics/pathMark/concepts";
import ResourceProfileTopic from "./topics/resourceProfile";
import WorkflowTopic from "./topics/workflow";
import WorkflowConceptDetail from "./topics/workflow/ConceptDetail";
import { workflowConcepts } from "./topics/workflow/concepts";

/**
 * Registry of all help center topics. A guide joins the help center by adding an
 * entry here; nothing else needs to know it exists.
 *
 * Order is the reading order in the left navigation, so "getting started" leads —
 * it is also the topic the first-run help opens at.
 */
export const helpTopics: HelpTopicDefinition[] = [
  {
    id: "gettingStarted",
    titleKey: "helpCenter.topic.gettingStarted",
    icon: <AiOutlineRocket className="text-lg" />,
    Content: GettingStartedTopic,
  },
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
  {
    id: "workflow",
    titleKey: "helpCenter.topic.workflow",
    icon: <AiOutlineApartment className="text-lg" />,
    Content: WorkflowTopic,
    conceptGroupLabelKey: "helpCenter.workflow.section.concepts",
    concepts: workflowConcepts.map((concept) => ({
      id: concept.id,
      labelKey: `helpCenter.workflow.concept.${concept.id}.name`,
    })),
    ConceptContent: WorkflowConceptDetail,
  },
  {
    id: "resourceProfile",
    titleKey: "helpCenter.topic.resourceProfile",
    icon: <AiOutlineProfile className="text-lg" />,
    Content: ResourceProfileTopic,
  },
  {
    id: "bulkModification",
    titleKey: "helpCenter.topic.bulkModification",
    icon: <AiOutlineEdit className="text-lg" />,
    Content: BulkModificationTopic,
  },
];

export const getHelpTopic = (id: HelpTopicId): HelpTopicDefinition =>
  helpTopics.find((topic) => topic.id === id) ?? helpTopics[0]!;
