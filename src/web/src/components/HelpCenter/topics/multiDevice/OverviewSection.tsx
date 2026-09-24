"use client";

import type { MultiDeviceHelpSectionId } from "../../types";

import { useTranslation } from "react-i18next";
import {
  AiOutlineAppstore,
  AiOutlineCloudServer,
  AiOutlineCluster,
  AiOutlineHdd,
  AiOutlineSafety,
  AiOutlineSwap,
} from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline } from "../../components/TopicBlocks";

import { mdk } from "./devices";
import NetworkDiagram from "./NetworkDiagram";
import OpenPageButton, { DEVICES_ROUTE, LIBRARY_ROUTE } from "./OpenPageButton";

import { Button } from "@/components/bakaui";

const pillars = [
  { id: "server", icon: <AiOutlineCloudServer className="text-lg" /> },
  { id: "storage", icon: <AiOutlineHdd className="text-lg" /> },
  { id: "reach", icon: <AiOutlineCluster className="text-lg" /> },
].map(({ id, icon }) => ({
  id,
  icon,
  titleKey: mdk(`pillar.${id}.title`),
  descKey: mdk(`pillar.${id}.desc`),
}));

const ways = [
  {
    id: "browse" as const,
    icon: <AiOutlineAppstore className="text-lg" />,
    route: LIBRARY_ROUTE,
    openKey: mdk("open.library"),
    tone: "border-primary/25 bg-primary/5",
    chip: "bg-primary/10 text-primary",
  },
  {
    id: "switch" as const,
    icon: <AiOutlineSwap className="text-lg" />,
    route: DEVICES_ROUTE,
    openKey: mdk("open.devices"),
    tone: "border-warning/30 bg-warning/5",
    chip: "bg-warning/15 text-warning-700 dark:text-warning",
  },
];

const comparisonRows = ["see", "do", "play", "access"];

const OverviewSection = ({
  onNavigate,
  onShowSection,
}: {
  onNavigate?: (path: string) => void;
  onShowSection: (id: MultiDeviceHelpSectionId) => void;
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-5">
      <TopicHeadline introKey={mdk("intro")} titleKey={mdk("headline")} />

      <NetworkDiagram />

      <TopicCards cards={pillars} columns={3} titleKey={mdk("pillar.title")} />

      <section className="flex flex-col gap-2">
        <h4 className="text-sm font-medium">{t(mdk("ways.title"))}</h4>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          {ways.map((way) => (
            <div
              key={way.id}
              className={`flex flex-col gap-2 rounded-lg border p-3 ${way.tone}`}
              data-way={way.id}
            >
              <div className="flex items-center gap-2">
                <span
                  className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg ${way.chip}`}
                >
                  {way.icon}
                </span>
                <span className="text-sm font-semibold">{t(mdk(`ways.${way.id}.title`))}</span>
              </div>
              <p className="text-xs text-default-600">{t(mdk(`ways.${way.id}.desc`))}</p>
              <p className="text-xs text-default-500">{t(mdk(`ways.${way.id}.where`))}</p>
              <div className="mt-auto flex flex-wrap items-center gap-2 pt-1">
                <Button size="sm" variant="light" onPress={() => onShowSection(way.id)}>
                  {t(mdk("ways.more"))}
                </Button>
                <OpenPageButton labelKey={way.openKey} route={way.route} onNavigate={onNavigate} />
              </div>
            </div>
          ))}
        </div>
      </section>

      <section className="flex flex-col gap-2">
        <h4 className="text-sm font-medium">{t(mdk("compare.title"))}</h4>
        <div className="overflow-x-auto rounded-lg border border-default-200">
          <table className="w-full min-w-[480px] text-left text-xs">
            <thead className="bg-default-100">
              <tr>
                <th className="p-2.5 font-medium" scope="col">
                  <span className="sr-only">{t(mdk("compare.column.aspect"))}</span>
                </th>
                {ways.map((way) => (
                  <th key={way.id} className="p-2.5 font-medium" scope="col">
                    {t(mdk(`ways.${way.id}.title`))}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {comparisonRows.map((row) => (
                <tr key={row} className="border-t border-default-200 align-top">
                  <th className="p-2.5 font-medium text-default-700" scope="row">
                    {t(mdk(`compare.${row}.aspect`))}
                  </th>
                  {ways.map((way) => (
                    <td key={way.id} className="p-2.5 text-default-600">
                      {t(mdk(`compare.${row}.${way.id}`))}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <TopicCallout icon={<AiOutlineSafety />} textKey={mdk("trust")} tone="primary" />
    </div>
  );
};

OverviewSection.displayName = "OverviewSection";

export default OverviewSection;
