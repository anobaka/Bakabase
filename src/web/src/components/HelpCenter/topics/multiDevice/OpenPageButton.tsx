"use client";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight } from "react-icons/ai";

import { Button } from "@/components/bakaui";
import { DEVICES_ROUTE, openLocalView } from "@/features/federation/switching";
import { useIsConsole, useIsPureClient, useRemoteAccessStore } from "@/stores/remoteAccess";

export const LIBRARY_ROUTE = "/federation";
export { DEVICES_ROUTE };
/** The "let other devices manage this one" part of the Devices page. */
export const MANAGEMENT_ROUTE = `${DEVICES_ROUTE}?section=management`;

/**
 * Where a link to the multi-device pages can take this window.
 *
 * Those pages belong to the desktop app's own window (their menu entries are
 * `localNodeOnly`): they talk to this device's loopback-only coordinator.
 * - `here` — this is that window: route to the page.
 * - `local` — the desktop app showing a device it manages: the pages are this
 *   computer's, so switch the window back to this computer at that page.
 * - `none` — a browser on another device, or the retired Bakabase Client: nothing to open.
 */
export const useDevicePagesReach = (): "here" | "local" | "none" => {
  const isPureClient = useIsPureClient();
  const isConsole = useIsConsole();
  const isLocalNode =
    useRemoteAccessStore((state) => state.initialized && state.isLocal) && !isPureClient;

  if (isLocalNode) return "here";
  if (isConsole) return "local";

  return "none";
};

/** A link from the help text to the page it describes; absent where that page cannot open. */
const OpenPageButton = ({
  route,
  labelKey,
  onNavigate,
}: {
  route: string;
  labelKey: string;
  onNavigate?: (path: string) => void;
}) => {
  const { t } = useTranslation();
  const reach = useDevicePagesReach();
  const [failed, setFailed] = useState(false);

  if (reach === "none" || (reach === "here" && !onNavigate)) return null;

  const open = () => {
    if (reach === "here") {
      onNavigate?.(route);

      return;
    }
    setFailed(false);
    // Navigation replaces this page on success; only a failure comes back here.
    openLocalView(route).catch(() => setFailed(true));
  };

  return (
    <span className="inline-flex flex-col items-start gap-1">
      <Button
        color="primary"
        endContent={<AiOutlineArrowRight className="text-sm" />}
        size="sm"
        variant="flat"
        onPress={open}
      >
        {t(labelKey)}
      </Button>
      {failed && (
        <span className="text-xs text-danger" role="alert">
          {t("federation.switcher.openFailed")}
        </span>
      )}
    </span>
  );
};

OpenPageButton.displayName = "OpenPageButton";

export default OpenPageButton;
