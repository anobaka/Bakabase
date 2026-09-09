"use client";

import {
  AiOutlineBulb,
  AiOutlineFileSearch,
  AiOutlineFolderOpen,
  AiOutlineLink,
  AiOutlineNumber,
  AiOutlineTag,
} from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

const k = (key: string) => `helpCenter.unmaterializedResource.${key}`;

/** The three ways to say "I am missing this", mirroring POST resource/placeholder. */
const ways = [
  {
    id: "title",
    icon: <AiOutlineTag className="text-lg" />,
    titleKey: k("way.title.title"),
    descKey: k("way.title.desc"),
    tone: "bg-primary/10 text-primary",
  },
  {
    id: "identity",
    icon: <AiOutlineNumber className="text-lg" />,
    titleKey: k("way.identity.title"),
    descKey: k("way.identity.desc"),
    tone: "bg-success/10 text-success",
  },
  {
    id: "sharedLink",
    icon: <AiOutlineLink className="text-lg" />,
    titleKey: k("way.sharedLink.title"),
    descKey: k("way.sharedLink.desc"),
    tone: "bg-warning/10 text-warning",
  },
];

const steps = [
  {
    id: "find",
    icon: <AiOutlineFileSearch className="text-lg" />,
    titleKey: k("step.find.title"),
    descKey: k("step.find.desc"),
  },
  {
    id: "link",
    icon: <AiOutlineFolderOpen className="text-lg" />,
    titleKey: k("step.link.title"),
    descKey: k("step.link.desc"),
  },
];

const UnmaterializedResourceTopic = () => (
  <div className="flex flex-col gap-4">
    <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />

    <TopicCards
      cards={ways}
      columns={3}
      subtitleKey={k("way.subtitle")}
      titleKey={k("way.title")}
    />

    <TopicSteps steps={steps} titleKey={k("step.title")} />

    <TopicCallout
      icon={<AiOutlineBulb className="text-sm" />}
      textKey={k("independenceTip")}
      tone="primary"
    />
  </div>
);

UnmaterializedResourceTopic.displayName = "UnmaterializedResourceTopic";

export default UnmaterializedResourceTopic;
