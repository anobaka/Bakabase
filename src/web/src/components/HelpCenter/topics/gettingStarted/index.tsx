"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineFolderOpen,
  AiOutlineProfile,
  AiOutlineSearch,
  AiOutlineSetting,
  AiOutlineSwap,
  AiOutlineTool,
} from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

const k = (key: string) => `helpCenter.gettingStarted.${key}`;

/**
 * Replaces the old eight-slide onboarding carousel. Same ground covered, but as a
 * page you can come back to and skim, rather than a one-shot sequence that had to be
 * "viewed again" from a settings row.
 */
const capabilityCards = [
  {
    id: "pathMark",
    icon: <AiOutlineFolderOpen className="text-lg" />,
    titleKey: k("capability.pathMark.title"),
    descKey: k("capability.pathMark.desc"),
    tone: "bg-success/10 text-success",
  },
  {
    id: "resourceProfile",
    icon: <AiOutlineProfile className="text-lg" />,
    titleKey: k("capability.resourceProfile.title"),
    descKey: k("capability.resourceProfile.desc"),
    tone: "bg-primary/10 text-primary",
  },
  {
    id: "browsing",
    icon: <AiOutlineSearch className="text-lg" />,
    titleKey: k("capability.browsing.title"),
    descKey: k("capability.browsing.desc"),
    tone: "bg-secondary/10 text-secondary",
  },
  {
    id: "fileProcessor",
    icon: <AiOutlineTool className="text-lg" />,
    titleKey: k("capability.fileProcessor.title"),
    descKey: k("capability.fileProcessor.desc"),
    tone: "bg-warning/10 text-warning",
  },
  {
    id: "downloader",
    icon: <AiOutlineCloudDownload className="text-lg" />,
    titleKey: k("capability.downloader.title"),
    descKey: k("capability.downloader.desc"),
    tone: "bg-primary/10 text-primary",
  },
  {
    id: "fileMover",
    icon: <AiOutlineSwap className="text-lg" />,
    titleKey: k("capability.fileMover.title"),
    descKey: k("capability.fileMover.desc"),
    tone: "bg-success/10 text-success",
  },
];

const firstSteps = [
  { id: "mark", titleKey: k("step.mark.title"), descKey: k("step.mark.desc") },
  { id: "sync", titleKey: k("step.sync.title"), descKey: k("step.sync.desc") },
  { id: "browse", titleKey: k("step.browse.title"), descKey: k("step.browse.desc") },
];

const GettingStartedTopic = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />

      <TopicSteps steps={firstSteps} titleKey={k("step.title")} />

      <TopicCards
        cards={capabilityCards}
        subtitleKey={k("capability.subtitle")}
        titleKey={k("capability.title")}
      />

      <TopicCallout
        icon={<AiOutlineSetting className="text-sm" />}
        textKey={k("footerHint")}
        tone="primary"
      />

      <p className="text-xs text-default-400">{t(k("navHint"))}</p>
    </div>
  );
};

GettingStartedTopic.displayName = "GettingStartedTopic";

export default GettingStartedTopic;
