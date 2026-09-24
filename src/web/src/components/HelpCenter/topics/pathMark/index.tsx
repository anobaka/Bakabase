"use client";

import type { HelpTopicContentProps, PathMarkHelpSectionId } from "../../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import WhatIsSection from "./WhatIsSection";
import ExamplesSection from "./ExamplesSection";
import ComparisonSection from "./ComparisonSection";

import { Tab, Tabs } from "@/components/bakaui";

const sectionIds: PathMarkHelpSectionId[] = ["whatIs", "examples", "comparison"];

/** The help center passes any topic's section id; only this topic's own ids select a tab. */
const isSection = (value?: string): value is PathMarkHelpSectionId =>
  sectionIds.includes(value as PathMarkHelpSectionId);

const PathMarkTopic = ({ section }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const [activeSection, setActiveSection] = useState<PathMarkHelpSectionId>(
    isSection(section) ? section : "whatIs",
  );

  useEffect(() => {
    if (isSection(section)) {
      setActiveSection(section);
    }
  }, [section]);

  return (
    <div className="flex flex-col gap-3">
      <Tabs
        aria-label={t("helpCenter.topic.pathMark")}
        selectedKey={activeSection}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setActiveSection(key as PathMarkHelpSectionId)}
      >
        {sectionIds.map((id) => (
          <Tab key={id} title={t(`helpCenter.pathMark.section.${id}`)} />
        ))}
      </Tabs>

      {activeSection === "whatIs" && <WhatIsSection />}
      {activeSection === "examples" && <ExamplesSection />}
      {activeSection === "comparison" && <ComparisonSection />}
    </div>
  );
};

PathMarkTopic.displayName = "PathMarkTopic";

export default PathMarkTopic;
