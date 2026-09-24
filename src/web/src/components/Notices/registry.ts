import type { IconType } from "react-icons";
import type { HelpSectionId, HelpTopicId } from "@/components/HelpCenter/types";

import { AiOutlineCluster, AiOutlineSwap } from "react-icons/ai";

/**
 * Where a notice may be shown, by who is looking at this install's UI.
 *
 * - `local` — someone at the machine that runs this install: the desktop app's own window,
 *   or a browser on that machine. The default.
 * - `lanAdmin` — a browser on another device that may administer this install, because the
 *   install lets any browser in (`Unrestricted`, the container default). For a headless
 *   server this is the only way anyone sees its own UI, so a notice about the server itself
 *   belongs here; a notice about the desktop app does not.
 *
 * Never, whatever a notice says: a window showing another server through this device's
 * console relay, or the retired thin client. That page is the other server's UI, served by
 * its bundle and talking to its API, so a notice read there would be recorded as read on
 * *that* server. Nor a browser that may only read this install: it could not record anything.
 */
export type NoticeAudience = "local" | "lanAdmin";

/** What a notice's button opens. Taking it also marks the notice read. */
export type NoticeAction =
  | {
      kind: "help";
      labelKey: string;
      topic: HelpTopicId;
      section?: HelpSectionId;
    }
  | {
      kind: "route";
      labelKey: string;
      /** An SPA route of this install, e.g. `/federation/devices?section=servers`. */
      route: string;
    };

/**
 * Something a fresh install can have done before its UI first loaded that makes an
 * upgrade-only notice its business after all (`showOnFreshInstallWhen`).
 *
 * - `thinClientPairingsImported` — the desktop app's first start brought over the pairings
 *   of the removed thin client on this machine: whoever installed it did use the thin client.
 */
export type FreshInstallFact = "thinClientPairingsImported";

export interface NoticeDefinition {
  /**
   * Stable forever: it is what an install records once the notice is read. Never rename or
   * reuse one — a renamed notice is shown again to everyone who read it.
   */
  id: string;
  /**
   * The app version that introduced it, as shown to the reader. Display only: nothing
   * compares versions here (`order` sorts, and the fresh-install rule needs no version).
   */
  introducedIn: string;
  /**
   * Reading order among unread notices, lowest first; the help center lists them highest
   * first. A new notice takes a number above every existing one.
   */
  order: number;
  icon: IconType;
  titleKey: string;
  /** The lead paragraph. */
  bodyKey: string;
  /** Optional bullet points under the lead paragraph. */
  pointKeys?: string[];
  action?: NoticeAction;
  /** Defaults to `["local"]`. */
  audience?: NoticeAudience[];
  /**
   * Only for installs that existed before this notice shipped: it describes a change from
   * something a fresh install never had. A fresh install records every upgrade-only notice it
   * ships with as read the first time its UI loads (`CaptureNoticeBaseline`), so it is never
   * greeted with them, while notices added by later releases still reach it after it updates.
   */
  upgradeOnly?: boolean;
  /**
   * For an upgrade-only notice: a fresh install where this holds is shown it anyway, like an
   * upgraded one. Asked once, when the fresh install records its baseline, and only where
   * the answer can be had (`learnFreshInstallFacts`); anywhere else it stays upgrade-only.
   */
  showOnFreshInstallWhen?: FreshInstallFact;
}

const k = (key: string) => `notices.item.${key}`;

/**
 * Every notice shipped with the app. Adding one: give it a new id and an `order` above
 * the rest, add its text to `locales/{en,cn}/components/notices.json`, and decide its
 * audience and whether it is upgrade-only. The registry test checks the rest.
 */
export const notices: NoticeDefinition[] = [
  {
    id: "multi-device",
    introducedIn: "2.4.0",
    order: 10,
    icon: AiOutlineCluster,
    titleKey: k("multiDevice.title"),
    bodyKey: k("multiDevice.body"),
    pointKeys: [k("multiDevice.point.library"), k("multiDevice.point.switch")],
    action: {
      kind: "help",
      labelKey: k("multiDevice.action"),
      topic: "multiDevice",
    },
    // Both features live in the desktop app's own window: a browser on another device can
    // neither open the merged library (its routes are loopback-only) nor switch servers.
    audience: ["local"],
    // News to someone who used an earlier version. A fresh install's welcome and help center
    // introduce these features as part of the app instead.
    upgradeOnly: true,
  },
  {
    id: "thin-client-discontinued",
    introducedIn: "2.4.0",
    order: 20,
    icon: AiOutlineSwap,
    titleKey: k("thinClient.title"),
    bodyKey: k("thinClient.body"),
    pointKeys: [
      k("thinClient.point.imported"),
      k("thinClient.point.otherComputers"),
      k("thinClient.point.uninstall"),
    ],
    action: {
      kind: "route",
      labelKey: k("thinClient.action"),
      // devicesRoute("servers"), spelled out so the registry does not depend on the
      // federation feature's modules (the registry test checks the two agree).
      route: "/federation/devices?section=servers",
    },
    // About the desktop app replacing another desktop program: meaningless to a browser on
    // another device, and to a headless server, which never manages anything.
    audience: ["local"],
    upgradeOnly: true,
    // A fresh install of the desktop app that found the thin client's pairings on this
    // machine and brought them over was installed by someone who used the thin client.
    showOnFreshInstallWhen: "thinClientPairingsImported",
  },
];

export const audienceOf = (notice: NoticeDefinition): NoticeAudience[] =>
  notice.audience ?? ["local"];
