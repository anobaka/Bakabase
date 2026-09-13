"use client";

import type { HelpSectionId, HelpTopicContentProps } from "../../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineApartment,
  AiOutlineBulb,
  AiOutlineCheckSquare,
  AiOutlineFileText,
  AiOutlineFolderOpen,
  AiOutlineInbox,
  AiOutlineLink,
  AiOutlinePlayCircle,
} from "react-icons/ai";

import {
  TopicCallout,
  TopicCards,
  TopicFlow,
  TopicHeadline,
  TopicSteps,
} from "../../components/TopicBlocks";

import { Tab, Tabs } from "@/components/bakaui";

const k = (key: string) => `helpCenter.acquisition.${key}`;
const sections: HelpSectionId[] = ["whatIs", "examples", "comparison"];

const flow = ["lead", "workflow", "receive", "library"].map((id) => ({
  id,
  titleKey: k(`flow.${id}.title`),
  descKey: k(`flow.${id}.desc`),
}));

const concepts = [
  { id: "lead", icon: <AiOutlineLink className="text-lg" /> },
  { id: "workflow", icon: <AiOutlineApartment className="text-lg" /> },
  { id: "task", icon: <AiOutlinePlayCircle className="text-lg" /> },
  { id: "inbox", icon: <AiOutlineInbox className="text-lg" /> },
].map(({ id, icon }) => ({
  id,
  icon,
  titleKey: k(`concept.${id}.name`),
  descKey: k(`concept.${id}.intro`),
}));

const exampleSteps = ["setup", "start", "download", "claim", "finish"].map((id) => ({
  id,
  titleKey: k(`example.${id}.title`),
  descKey: k(`example.${id}.desc`),
}));

const listCards = [
  { id: "preview", icon: <AiOutlineFileText className="text-lg" /> },
  { id: "import", icon: <AiOutlineCheckSquare className="text-lg" /> },
].map(({ id, icon }) => ({
  id,
  icon,
  titleKey: k(`list.${id}.title`),
  descKey: k(`list.${id}.desc`),
}));

const uses = [
  { id: "identity", icon: <AiOutlineLink className="text-lg" /> },
  { id: "handoff", icon: <AiOutlineInbox className="text-lg" /> },
  { id: "organize", icon: <AiOutlineFolderOpen className="text-lg" /> },
].map(({ id, icon }) => ({
  id,
  icon,
  titleKey: k(`use.${id}.title`),
  descKey: k(`use.${id}.desc`),
}));

const AcquisitionTopic = ({ section }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const [activeSection, setActiveSection] = useState<HelpSectionId>(section ?? "whatIs");

  useEffect(() => {
    if (section && sections.includes(section)) setActiveSection(section);
  }, [section]);

  return (
    <div className="flex flex-col gap-4">
      <Tabs
        aria-label={t("helpCenter.topic.acquisition")}
        selectedKey={activeSection}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setActiveSection(key as HelpSectionId)}
      >
        {sections.map((id) => (
          <Tab key={id} title={t(k(`section.${id}`))} />
        ))}
      </Tabs>

      {activeSection === "whatIs" && (
        <div className="flex flex-col gap-4">
          <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />
          <TopicFlow steps={flow} titleKey={k("flow.title")} />
          <TopicCards cards={concepts} columns={2} />
          <TopicCallout icon={<AiOutlineBulb />} textKey={k("boundary")} tone="primary" />
        </div>
      )}

      {activeSection === "examples" && (
        <div className="flex flex-col gap-4">
          <TopicHeadline introKey={k("example.intro")} titleKey={k("example.title")} />
          <TopicSteps steps={exampleSteps} />
          <TopicCallout textKey={k("example.defaultNote")} />
          <TopicCards cards={listCards} subtitleKey={k("list.intro")} titleKey={k("list.title")} />
        </div>
      )}

      {activeSection === "comparison" && (
        <div className="flex flex-col gap-4">
          <TopicHeadline introKey={k("use.intro")} titleKey={k("use.title")} />
          <TopicCards cards={uses} columns={1} />
          <TopicCallout icon={<AiOutlineBulb />} textKey={k("use.saveNote")} tone="primary" />
        </div>
      )}
    </div>
  );
};

AcquisitionTopic.displayName = "AcquisitionTopic";

export default AcquisitionTopic;
