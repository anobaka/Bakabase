import type { MutableRefObject, RefObject } from "react";
import type { useDevicesData } from "../hooks/useDevicesData";
import type { SharingCandidate } from "../types";
import type { DevicesAnchor, DevicesTabId } from "../switching";
import type { DevicesTab } from "./sections";

import { createContext, useContext } from "react";

export type DevicesData = ReturnType<typeof useDevicesData>;

/** What the library sharing forms hold; kept by the page, so a tab switch loses nothing. */
export interface SharingForm {
  address: string;
  setAddress: (value: string) => void;
  code: string;
  setCode: (value: string) => void;
  shareBack: boolean;
  setShareBack: (value: boolean) => void;
  /** Unset until the user touches it: the default then follows the remote-access mode. */
  configureRemote?: boolean;
  setConfigureRemote: (value: boolean) => void;
  invite?: { code: string; expiresAt: string };
  setInvite: (value?: { code: string; expiresAt: string }) => void;
  discovered?: SharingCandidate[];
  setDiscovered: (value?: SharingCandidate[]) => void;
}

/**
 * The devices page as its tabs see it: the data read once for the whole page, and the
 * page's own way of running an action — one at a time, its outcome in the page's feedback
 * above the tabs, the listings it may have changed read again afterwards.
 */
export interface DevicesPageContextValue {
  data: DevicesData;
  busy: boolean;
  /**
   * Runs a user action. Library sharing's status is read again afterwards (and remote
   * access, which turning sharing on can change); a failure goes to `onError`.
   */
  run: (operation: () => Promise<unknown>, onError?: (cause: Error) => void) => Promise<boolean>;
  /** Asks first, in the page's confirmation dialog. */
  confirm: (
    title: string,
    description: string,
    action: () => Promise<unknown>,
    warning?: string,
  ) => void;
  setNotice: (text?: string) => void;
  /** A clock that ticks every second, for expiries. */
  now: number;
  mounted: MutableRefObject<boolean>;
  /** Outgoing requests whose claim failed lately, left alone for a while. */
  claimBackoff: MutableRefObject<Map<string, number>>;
  sharingForm: SharingForm;
  /** The tab shown and the place inside it a link asked for. */
  tab: DevicesTabId;
  anchor: DevicesAnchor | null;
  /** Changes with every navigation, a link followed again included. */
  locationKey: string;
  /** The place a link has just revealed, marked for a moment. */
  revealed: DevicesAnchor | null;
  /** Every tab, in order: the device tab's chooser reads theirs. */
  tabs: DevicesTab[];
  /** The shown tab's own heading, where focus goes when nothing more precise is left. */
  headingRef: RefObject<HTMLHeadingElement>;
}

export const DevicesPageContext = createContext<DevicesPageContextValue | null>(null);

export function useDevicesPage() {
  const value = useContext(DevicesPageContext);

  if (!value) throw new Error("useDevicesPage outside the devices page");

  return value;
}
