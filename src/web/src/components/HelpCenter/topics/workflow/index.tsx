"use client";

import type { HelpTopicContentProps, WorkflowHelpSectionId } from "../../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import WhatIsSection from "./WhatIsSection";
import ExamplesSection from "./ExamplesSection";

import { Tab, Tabs } from "@/components/bakaui";

const sectionIds: WorkflowHelpSectionId[] = ["whatIs", "examples"];

const WorkflowTopic = ({ section }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const [activeSection, setActiveSection] = useState<WorkflowHelpSectionId>(
    section === "examples" ? "examples" : "whatIs",
  );

  useEffect(() => {
    if (section === "whatIs" || section === "examples") {
      setActiveSection(section);
    }
  }, [section]);

  return (
    <div className="flex flex-col gap-3">
      <Tabs
        aria-label={t("helpCenter.topic.workflow")}
        selectedKey={activeSection}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setActiveSection(key as WorkflowHelpSectionId)}
      >
        {sectionIds.map((id) => (
          <Tab key={id} title={t(`helpCenter.workflow.section.${id}`)} />
        ))}
      </Tabs>

      {activeSection === "whatIs" && <WhatIsSection />}
      {activeSection === "examples" && <ExamplesSection />}
    </div>
  );
};

WorkflowTopic.displayName = "WorkflowTopic";

export default WorkflowTopic;
