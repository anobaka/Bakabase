import type { TFunction } from "i18next";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { OwnSharing } from "../viewModels";

import { useId } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { codeTurnsOnConfirmation, remoteAccessConfirmation, sharingNeeded } from "../viewModels";

import { smallButtonClass } from "./common";

import { RemoteAccessMode } from "@/sdk/constants";

/**
 * Turns on everything another device needs to read this one: sharing, and remote access with
 * pairing required where it is off (spec §7.2.4). The server changes remote access only from
 * Disabled. Run only inside an operation the person has already confirmed.
 */
export const turnOnSharing = (own: OwnSharing) =>
  dataSyncApi.setSharing({
    enabled: true,
    enablePairedRemoteAccess: own.remoteAccessMode === RemoteAccessMode.Disabled,
  });

/**
 * A code only works while this device shares its definitions and remote access is on (spec
 * §7.2.3): where either is off, it asks to turn them on first, then opens the code. For a code
 * meant for one device, `name` names it.
 */
export const openCodeFirst = (
  t: TFunction,
  actions: DataSyncPanelActions,
  own: OwnSharing,
  open: () => void,
  name?: string,
) => {
  if (!sharingNeeded(own)) {
    open();

    return;
  }
  actions.confirm({
    ...codeTurnsOnConfirmation(t, own, name),
    action: async () => {
      await turnOnSharing(own);
      if (actions.mounted.current) open();
    },
    refresh: ["dataSync", "sharing"],
  });
};

/*
 * Whether devices this one approves may read its definitions, on the `/data-sync` page and in
 * the device map's details alike. Turning sharing on is offered only where this window may
 * create access (spec §7.1.5); turning it off stays available everywhere, so access can always
 * be shut. Turning it on also offers to turn remote access on (spec §7.2.4) — and, while sharing
 * is on and remote access off, which is where another device's "Remote access is off" status
 * sends the reader, a button of its own does.
 */
export default function SharingControls({
  actions,
  canManage,
  sharingEnabled,
  remoteAccessMode,
}: {
  actions: DataSyncPanelActions;
  canManage: boolean;
} & OwnSharing) {
  const { t } = useTranslation();
  const hintId = useId();
  const own = { sharingEnabled, remoteAccessMode };
  const remoteOff = remoteAccessMode === RemoteAccessMode.Disabled;

  const setSharing = (enabled: boolean) => {
    if (!enabled)
      actions.confirm({
        title: t("dataSync.sharing.offTitle"),
        description: t("dataSync.sharing.offDescription"),
        action: () => dataSyncApi.setSharing({ enabled: false, enablePairedRemoteAccess: false }),
        refresh: ["dataSync", "sharing"],
      });
    else if (remoteOff)
      actions.confirm({
        title: t("dataSync.sharing.onTitle"),
        description: t("dataSync.sharing.hint"),
        warning: t("dataSync.sharing.remoteAccess"),
        action: () => turnOnSharing(own),
        refresh: ["dataSync", "sharing"],
      });
    else void actions.run(() => turnOnSharing(own), ["dataSync", "sharing"]);
  };

  return (
    <div className="space-y-1">
      {canManage ? (
        <label className="flex cursor-pointer items-start gap-3 text-sm">
          <input
            aria-describedby={hintId}
            checked={sharingEnabled}
            className="mt-1 accent-secondary"
            data-testid="data-sync-sharing-switch"
            disabled={actions.busy}
            role="switch"
            type="checkbox"
            onChange={(event) => setSharing(event.target.checked)}
          />
          <span className="font-medium">{t("dataSync.sharing.label")}</span>
        </label>
      ) : (
        // Not a switch here: it could only ever be turned off from this window.
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="font-medium">
            {t(sharingEnabled ? "dataSync.sharing.isOn" : "dataSync.sharing.isOff")}
          </span>
          {sharingEnabled && (
            <button
              className={smallButtonClass}
              data-testid="data-sync-sharing-off"
              disabled={actions.busy}
              type="button"
              onClick={() => setSharing(false)}
            >
              {t("dataSync.sharing.turnOff")}
            </button>
          )}
        </div>
      )}
      <p className="pl-7 text-xs text-default-500" data-testid="data-sync-sharing-hint" id={hintId}>
        {t("dataSync.sharing.hint")}
        {/* What turning the switch on also does: said only while it is off. */}
        {remoteOff && canManage && !sharingEnabled ? ` ${t("dataSync.sharing.remoteAccess")}` : ""}
      </p>
      {canManage && sharingEnabled && remoteOff && (
        <div
          className="ml-7 flex flex-wrap items-center gap-2 rounded-lg border border-warning/30 bg-warning/5 px-2.5 py-1.5 text-xs"
          data-testid="data-sync-remote-access-off"
        >
          <span className="min-w-0 flex-1 text-warning-700 dark:text-warning">
            {t("dataSync.remoteAccess.offLine")}
          </span>
          <button
            className={smallButtonClass}
            data-testid="data-sync-remote-access-on"
            disabled={actions.busy}
            type="button"
            onClick={() =>
              actions.confirm({
                ...remoteAccessConfirmation(t),
                action: () => turnOnSharing(own),
                refresh: ["dataSync", "sharing"],
              })
            }
          >
            {t("dataSync.remoteAccess.turnOn")}
          </button>
        </div>
      )}
    </div>
  );
}
