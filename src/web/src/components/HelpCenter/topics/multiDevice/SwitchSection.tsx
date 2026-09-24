"use client";

import {
  AiOutlineDisconnect,
  AiOutlineLayout,
  AiOutlineMenu,
  AiOutlineSafety,
} from "react-icons/ai";

import { TopicCards, TopicHeadline } from "../../components/TopicBlocks";

import { mdk } from "./devices";
import OpenPageButton, { DEVICES_ROUTE } from "./OpenPageButton";
import PlayHereDiagram from "./PlayHereDiagram";
import SwitchMockup from "./SwitchMockup";

const cards = [
  { id: "ownUi", icon: <AiOutlineLayout className="text-lg" /> },
  {
    id: "control",
    icon: <AiOutlineSafety className="text-lg" />,
    tone: "bg-warning/15 text-warning-700 dark:text-warning",
  },
  { id: "tray", icon: <AiOutlineMenu className="text-lg" /> },
  { id: "stop", icon: <AiOutlineDisconnect className="text-lg" /> },
].map(({ id, icon, tone }) => ({
  id,
  icon,
  tone,
  titleKey: mdk(`switch.card.${id}.title`),
  descKey: mdk(`switch.card.${id}.desc`),
}));

/** Showing a managed device's own interface in this window (server switching). */
const SwitchSection = ({ onNavigate }: { onNavigate?: (path: string) => void }) => (
  <div className="flex flex-col gap-4">
    <TopicHeadline introKey={mdk("switch.intro")} titleKey={mdk("switch.headline")} />
    <SwitchMockup />
    <PlayHereDiagram />
    <TopicCards cards={cards} columns={2} />
    <div>
      <OpenPageButton
        labelKey={mdk("open.devices")}
        route={DEVICES_ROUTE}
        onNavigate={onNavigate}
      />
    </div>
  </div>
);

SwitchSection.displayName = "SwitchSection";

export default SwitchSection;
