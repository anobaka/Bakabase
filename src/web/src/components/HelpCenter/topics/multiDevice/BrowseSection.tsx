"use client";

import {
  AiOutlineCheckCircle,
  AiOutlineInfoCircle,
  AiOutlinePlayCircle,
  AiOutlineSearch,
} from "react-icons/ai";

import { TopicCallout, TopicCards, TopicHeadline } from "../../components/TopicBlocks";

import { mdk } from "./devices";
import LibraryMockup from "./LibraryMockup";
import OpenPageButton, { LIBRARY_ROUTE } from "./OpenPageButton";

const cards = [
  { id: "search", icon: <AiOutlineSearch className="text-lg" /> },
  { id: "view", icon: <AiOutlinePlayCircle className="text-lg" /> },
  { id: "coverage", icon: <AiOutlineCheckCircle className="text-lg" /> },
].map(({ id, icon }) => ({
  id,
  icon,
  titleKey: mdk(`browse.card.${id}.title`),
  descKey: mdk(`browse.card.${id}.desc`),
}));

/** The merged, read-only multi-device library (`/federation`). */
const BrowseSection = ({ onNavigate }: { onNavigate?: (path: string) => void }) => (
  <div className="flex flex-col gap-4">
    <TopicHeadline introKey={mdk("browse.intro")} titleKey={mdk("browse.headline")} />
    <LibraryMockup />
    <TopicCards cards={cards} columns={3} />
    <TopicCallout icon={<AiOutlineInfoCircle />} textKey={mdk("browse.boundary")} />
    <div>
      <OpenPageButton
        labelKey={mdk("open.library")}
        route={LIBRARY_ROUTE}
        onNavigate={onNavigate}
      />
    </div>
  </div>
);

BrowseSection.displayName = "BrowseSection";

export default BrowseSection;
