"use client";

import {
  AiOutlineCloudServer,
  AiOutlineImport,
  AiOutlineKey,
  AiOutlineWarning,
} from "react-icons/ai";

import { TopicCallout, TopicHeadline, TopicSteps } from "../../components/TopicBlocks";

import { mdk } from "./devices";
import OpenPageButton, { DEVICES_ROUTE, MANAGEMENT_ROUTE } from "./OpenPageButton";

const steps = (track: string, ids: string[]) =>
  ids.map((id) => ({
    id,
    titleKey: mdk(`setup.${track}.${id}.title`),
    descKey: mdk(`setup.${track}.${id}.desc`),
  }));

const tracks = [
  {
    id: "browse",
    steps: steps("browse", ["share", "connect", "both", "enable"]),
    route: DEVICES_ROUTE,
    openKey: mdk("open.devices"),
  },
  {
    id: "manage",
    steps: steps("manage", ["allow", "add", "switch", "map"]),
    route: MANAGEMENT_ROUTE,
    openKey: mdk("open.management"),
  },
];

/** How to get there: the Devices and sharing page, one track per way of using a device. */
const SetupSection = ({ onNavigate }: { onNavigate?: (path: string) => void }) => (
  <div className="flex flex-col gap-4">
    <TopicHeadline introKey={mdk("setup.intro")} titleKey={mdk("setup.headline")} />

    <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
      {tracks.map((track) => (
        <section key={track.id} className="flex flex-col gap-2" data-track={track.id}>
          <TopicSteps steps={track.steps} titleKey={mdk(`setup.${track.id}.title`)} />
          <div>
            <OpenPageButton labelKey={track.openKey} route={track.route} onNavigate={onNavigate} />
          </div>
        </section>
      ))}
    </div>

    <TopicCallout icon={<AiOutlineWarning />} textKey={mdk("setup.unrestricted")} tone="warning" />
    <TopicCallout icon={<AiOutlineCloudServer />} textKey={mdk("setup.nas")} />
    <TopicCallout icon={<AiOutlineKey />} textKey={mdk("setup.codes")} />
    <TopicCallout icon={<AiOutlineImport />} textKey={mdk("setup.thinClient")} tone="primary" />
  </div>
);

SetupSection.displayName = "SetupSection";

export default SetupSection;
