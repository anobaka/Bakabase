import type { IconType } from "react-icons";
import type { HelpSectionId, HelpTopicId } from "@/components/HelpCenter/types";

import { AiOutlineCluster } from "react-icons/ai";

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
}

const k = (key: string) => `notices.item.${key}`;

/**
 * Ids of notices that shipped once and were removed. Never reuse one: installs recorded it
 * as read, so a new notice under it would never be shown to them.
 */
export const retiredNoticeIds = ["thin-client-discontinued"] as const;

/**
 * Every notice shipped with the app. Adding one: give it a new id — never one of
 * {@link retiredNoticeIds} — and an `order` above the rest, add its text to
 * `locales/{en,cn}/components/notices.json`, and decide its audience and whether it is
 * upgrade-only. The registry test checks the rest.
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
];

export const audienceOf = (notice: NoticeDefinition): NoticeAudience[] =>
  notice.audience ?? ["local"];
