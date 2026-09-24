"use client";

import type { HelpTopicContentProps, MultiDeviceHelpSectionId } from "../../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import BrowseSection from "./BrowseSection";
import { mdk } from "./devices";
import OverviewSection from "./OverviewSection";
import SetupSection from "./SetupSection";
import SwitchSection from "./SwitchSection";

import { Tab, Tabs } from "@/components/bakaui";

export const multiDeviceSections: MultiDeviceHelpSectionId[] = [
  "whatIs",
  "browse",
  "switch",
  "setup",
];

const isSection = (value?: string): value is MultiDeviceHelpSectionId =>
  multiDeviceSections.includes(value as MultiDeviceHelpSectionId);

/**
 * Using Bakabase on several devices of one network: every device is its own server with
 * its own storage, and a computer can reach the others in two ways — the merged
 * read-only library, and switching its window to a device it manages.
 */
const MultiDeviceTopic = ({ section, onNavigate }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const [activeSection, setActiveSection] = useState<MultiDeviceHelpSectionId>(
    isSection(section) ? section : "whatIs",
  );

  useEffect(() => {
    if (isSection(section)) setActiveSection(section);
  }, [section]);

  return (
    <div className="flex flex-col gap-3">
      <Tabs
        aria-label={t("helpCenter.topic.multiDevice")}
        selectedKey={activeSection}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setActiveSection(key as MultiDeviceHelpSectionId)}
      >
        {multiDeviceSections.map((id) => (
          <Tab key={id} title={t(mdk(`section.${id}`))} />
        ))}
      </Tabs>

      {activeSection === "whatIs" && (
        <OverviewSection onNavigate={onNavigate} onShowSection={setActiveSection} />
      )}
      {activeSection === "browse" && <BrowseSection onNavigate={onNavigate} />}
      {activeSection === "switch" && <SwitchSection onNavigate={onNavigate} />}
      {activeSection === "setup" && <SetupSection onNavigate={onNavigate} />}
    </div>
  );
};

MultiDeviceTopic.displayName = "MultiDeviceTopic";

export default MultiDeviceTopic;
