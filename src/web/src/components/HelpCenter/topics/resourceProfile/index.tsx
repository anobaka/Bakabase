"use client";

import {
  AiOutlineBulb,
  AiOutlineEye,
  AiOutlineLink,
  AiOutlinePlayCircle,
  AiOutlineThunderbolt,
} from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline } from "../../components/TopicBlocks";

const k = (key: string) => `helpCenter.resourceProfile.${key}`;

const areas = [
  {
    id: "matching",
    icon: <AiOutlineThunderbolt className="text-lg" />,
    titleKey: k("area.matching.title"),
    descKey: k("area.matching.desc"),
    tone: "bg-warning/10 text-warning",
  },
  {
    id: "display",
    icon: <AiOutlineEye className="text-lg" />,
    titleKey: k("area.display.title"),
    descKey: k("area.display.desc"),
    tone: "bg-primary/10 text-primary",
  },
  {
    id: "playback",
    icon: <AiOutlinePlayCircle className="text-lg" />,
    titleKey: k("area.playback.title"),
    descKey: k("area.playback.desc"),
    tone: "bg-success/10 text-success",
  },
  {
    id: "binding",
    icon: <AiOutlineLink className="text-lg" />,
    titleKey: k("area.binding.title"),
    descKey: k("area.binding.desc"),
    tone: "bg-secondary/10 text-secondary",
  },
];

const ResourceProfileTopic = () => (
  <div className="flex flex-col gap-4">
    <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />

    <TopicCards cards={areas} subtitleKey={k("area.subtitle")} titleKey={k("area.title")} />

    <TopicCallout
      icon={<AiOutlineBulb className="text-sm" />}
      textKey={k("priorityTip")}
      tone="primary"
    />
  </div>
);

ResourceProfileTopic.displayName = "ResourceProfileTopic";

export default ResourceProfileTopic;
