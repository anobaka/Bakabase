import type { ComponentType } from "react";
import type { IconType } from "react-icons";
import type { DevicesData } from "./context";
import type { DevicesAnchor, DevicesSection, DevicesTabId } from "../switching";

import {
  AiOutlineCloudServer,
  AiOutlineLaptop,
  AiOutlineSetting,
  AiOutlineShareAlt,
} from "react-icons/ai";

import AdvancedTab from "./AdvancedTab";
import ManageTab from "./ManageTab";
import SharingTab from "./SharingTab";
import ThisDeviceTab from "./ThisDeviceTab";
import { loaded, troubledServers, waitingSharingRequests } from "./selectors";

import { RemoteAccessMode } from "@/sdk/constants";

export type { DevicesAnchor, DevicesSection, DevicesTabId };

/** What a tab's link says beside its name: how many requests wait there, and whether to look. */
export interface DevicesBadge {
  count: number;
  /** i18n key of the spoken count, with `{{count}}`. */
  countKey?: string;
  attention: boolean;
}

/** A card of the device tab's "Add another device", leading to the form in this tab. */
export interface DevicesChooser {
  titleKey: string;
  descKey: string;
  section: DevicesAnchor;
  /** Hidden where it cannot apply (a headless server manages nothing). */
  hidden?: (data: DevicesData) => boolean;
}

export interface DevicesTab {
  id: DevicesTabId;
  labelKey: string;
  icon: IconType;
  /** The places inside it a link can land on. */
  anchors: DevicesAnchor[];
  Panel: ComponentType;
  badge?: (data: DevicesData) => DevicesBadge | undefined;
  chooser?: DevicesChooser;
}

/** The element each place is, and when it can be brought into view. */
export const devicesAnchors: Record<
  DevicesAnchor,
  { elementId: string; ready: (data: DevicesData) => boolean }
> = {
  addresses: { elementId: "device-addresses", ready: (data) => loaded(data, "access") },
  servers: { elementId: "managed-servers", ready: (data) => loaded(data, "servers") },
  "add-server": { elementId: "managed-server-add", ready: (data) => loaded(data, "servers") },
  // Under the servers list: arriving before it has filled would let it push this away again.
  management: {
    elementId: "management-access",
    ready: (data) => loaded(data, "servers") && loaded(data, "access"),
  },
  browsing: { elementId: "library-browsing", ready: (data) => loaded(data, "sharing") },
  connect: { elementId: "library-connect", ready: (data) => loaded(data, "sharing") },
  share: { elementId: "library-share", ready: (data) => loaded(data, "sharing") },
  "sharing-requests": { elementId: "sharing-requests", ready: (data) => loaded(data, "sharing") },
  identity: { elementId: "federation-identity", ready: (data) => loaded(data, "sharing") },
};

export const devicesTabs: DevicesTab[] = [
  {
    id: "device",
    labelKey: "federation.devices.tab.device",
    icon: AiOutlineLaptop,
    anchors: ["addresses"],
    Panel: ThisDeviceTab,
  },
  {
    id: "manage",
    labelKey: "federation.devices.tab.manage",
    icon: AiOutlineCloudServer,
    anchors: ["servers", "add-server", "management"],
    Panel: ManageTab,
    badge: (data) => {
      const count = data.access?.pendingRequests?.length ?? 0;
      const attention =
        troubledServers(data).length > 0 || data.access?.mode === RemoteAccessMode.Unrestricted;

      return count || attention
        ? { count, countKey: "federation.devices.nav.pendingManage", attention }
        : undefined;
    },
    chooser: {
      titleKey: "federation.devices.chooser.manage.title",
      descKey: "federation.devices.chooser.manage.desc",
      section: "add-server",
      hidden: (data) => data.servers?.available === false,
    },
  },
  {
    id: "sharing",
    labelKey: "federation.devices.tab.sharing",
    icon: AiOutlineShareAlt,
    anchors: ["browsing", "connect", "share", "sharing-requests"],
    Panel: SharingTab,
    badge: (data) => {
      const count = waitingSharingRequests(data).length;

      return count
        ? { count, countKey: "federation.devices.nav.pendingShare", attention: false }
        : undefined;
    },
    chooser: {
      titleKey: "federation.devices.chooser.browse.title",
      descKey: "federation.devices.chooser.browse.desc",
      section: "connect",
    },
  },
  {
    id: "advanced",
    labelKey: "federation.devices.tab.advanced",
    icon: AiOutlineSetting,
    anchors: ["identity"],
    Panel: AdvancedTab,
  },
];

/**
 * Where `?section=` leads: a tab id is that tab, an anchor is its place inside its tab.
 * Nothing, or a value this page does not know, is the device tab, and nothing is revealed.
 */
export function resolveSection(value: string | null | undefined): {
  tab: DevicesTabId;
  anchor: DevicesAnchor | null;
  /** Whether the link named a tab or place at all: only then does focus move. */
  explicit: boolean;
} {
  const tab = devicesTabs.find((entry) => entry.id === value);

  if (tab) return { tab: tab.id, anchor: null, explicit: true };
  const owner = devicesTabs.find((entry) => entry.anchors.includes(value as DevicesAnchor));

  if (owner) return { tab: owner.id, anchor: value as DevicesAnchor, explicit: true };

  return { tab: "device", anchor: null, explicit: false };
}
