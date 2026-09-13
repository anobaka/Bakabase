"use client";

import type { HelpSectionId, HelpTopicContentProps } from "../../types";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineAppstore, AiOutlineFilter, AiOutlinePieChart } from "react-icons/ai";

import {
  TopicCallout,
  TopicCards,
  TopicFlow,
  TopicHeadline,
  TopicSteps,
} from "../../components/TopicBlocks";

import { Tab, Tabs } from "@/components/bakaui";

const k = (key: string) => `helpCenter.collection.${key}`;
const sections: HelpSectionId[] = ["whatIs", "examples", "comparison"];
const steps = (prefix: string, ids: string[]) =>
  ids.map((id) => ({
    id,
    titleKey: k(`${prefix}.${id}.title`),
    descKey: k(`${prefix}.${id}.desc`),
  }));
const cards = [
  {
    id: "membership",
    icon: <AiOutlineAppstore className="text-lg" />,
    titleKey: k("card.membership.title"),
    descKey: k("card.membership.desc"),
  },
  {
    id: "criteria",
    icon: <AiOutlineFilter className="text-lg" />,
    titleKey: k("card.criteria.title"),
    descKey: k("card.criteria.desc"),
  },
  {
    id: "progress",
    icon: <AiOutlinePieChart className="text-lg" />,
    titleKey: k("card.progress.title"),
    descKey: k("card.progress.desc"),
  },
];

const CollectionTopic = ({ section }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const [selectedSection, setSelectedSection] = useState<HelpSectionId>(
    section && sections.includes(section) ? section : "whatIs",
  );

  useEffect(() => {
    if (section && sections.includes(section)) setSelectedSection(section);
  }, [section]);

  return (
    <div className="flex flex-col gap-4">
      <Tabs
        aria-label={t("helpCenter.topic.collection")}
        selectedKey={selectedSection}
        size="sm"
        variant="underlined"
        onSelectionChange={(key) => setSelectedSection(key as HelpSectionId)}
      >
        {sections.map((id) => (
          <Tab key={id} title={t(k(`section.${id}`))} />
        ))}
      </Tabs>

      {selectedSection === "whatIs" && (
        <>
          <TopicHeadline introKey={k("intro")} titleKey={k("headline")} />
          <TopicFlow
            steps={steps("flow", ["define", "gather", "track", "acquire"])}
            titleKey={k("flow.title")}
          />
          <TopicCards cards={cards} columns={3} titleKey={k("card.title")} />
          <TopicCallout textKey={k("boundary")} tone="primary" />
        </>
      )}

      {selectedSection === "examples" && (
        <>
          <TopicHeadline introKey={k("examples.intro")} titleKey={k("examples.title")} />
          {["series", "author"].map((id) => (
            <section key={id} className="flex flex-col gap-3">
              <div>
                <h4 className="text-sm font-semibold">{t(k(`example.${id}.title`))}</h4>
                <p className="mt-1 text-sm text-default-600">{t(k(`example.${id}.intro`))}</p>
              </div>
              <TopicSteps steps={steps(`example.${id}.step`, ["one", "two", "three"])} />
              <TopicCallout textKey={k(`example.${id}.result`)} />
            </section>
          ))}
        </>
      )}

      {selectedSection === "comparison" && (
        <>
          <TopicHeadline introKey={k("comparison.intro")} titleKey={k("comparison.title")} />
          <div className="overflow-x-auto rounded-lg border border-default-200">
            <table className="w-full min-w-[420px] text-left text-sm">
              <thead className="bg-default-100">
                <tr>
                  {["approach", "limit", "benefit"].map((id) => (
                    <th key={id} className="p-3 font-medium" scope="col">
                      {t(k(`comparison.column.${id}`))}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {["files", "checklist", "downloads"].map((id) => (
                  <tr key={id} className="border-t border-default-200 align-top">
                    <th className="p-3 font-medium" scope="row">
                      {t(k(`comparison.${id}.approach`))}
                    </th>
                    <td className="p-3 text-default-600">{t(k(`comparison.${id}.limit`))}</td>
                    <td className="p-3 text-default-600">{t(k(`comparison.${id}.benefit`))}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <TopicCallout textKey={k("comparison.tip")} />
        </>
      )}
    </div>
  );
};

export default CollectionTopic;
