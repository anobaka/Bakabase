import type { HelpTopicDefinition, HelpTopicId } from "./types";

import { AiOutlineTags } from "react-icons/ai";

import PathMarkTopic from "./topics/pathMark";

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
  },
];

export const getHelpTopic = (id: HelpTopicId): HelpTopicDefinition =>
  helpTopics.find((topic) => topic.id === id) ?? helpTopics[0]!;
