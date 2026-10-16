import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";
import type { OwnSharing } from "../viewModels";

import { useId } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { remoteAccessConfirmation } from "../viewModels";

import { smallButtonClass } from "./common";

import { RemoteAccessMode } from "@/sdk/constants";
import { Button, Switch } from "@/components/bakaui";

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
        refresh: ["dataSync", "identity"],
      });
    else if (remoteOff)
      actions.confirm({
        title: t("dataSync.sharing.onTitle"),
        description: t("dataSync.sharing.hint"),
        warning: t("dataSync.sharing.remoteAccess"),
        action: () => turnOnSharing(own),
        refresh: ["dataSync", "identity"],
      });
    else void actions.run(() => turnOnSharing(own), ["dataSync", "identity"]);
  };

  return (
    <div className="space-y-1">
      {canManage ? (
        <Switch
          aria-describedby={hintId}
          classNames={{ base: "m-0 max-w-full p-0", label: "text-xs" }}
          color="secondary"
          data-testid="data-sync-sharing-switch"
          isDisabled={actions.busy}
          isSelected={sharingEnabled}
          size="sm"
          onChange={(event) => setSharing(event.target.checked)}
        >
          <span className="font-medium">{t("dataSync.sharing.label")}</span>
        </Switch>
      ) : (
        // Not a switch here: it could only ever be turned off from this window.
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="font-medium">
            {t(sharingEnabled ? "dataSync.sharing.isOn" : "dataSync.sharing.isOff")}
          </span>
          {sharingEnabled && (
            <Button
              className={smallButtonClass}
              data-testid="data-sync-sharing-off"
              disabled={actions.busy}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => setSharing(false)}
            >
              {t("dataSync.sharing.turnOff")}
            </Button>
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
          <Button
            className={smallButtonClass}
            data-testid="data-sync-remote-access-on"
            disabled={actions.busy}
            size="sm"
            type="button"
            variant="bordered"
            onClick={() =>
              actions.confirm({
                ...remoteAccessConfirmation(t),
                action: () => turnOnSharing(own),
                refresh: ["dataSync", "identity"],
              })
            }
          >
            {t("dataSync.remoteAccess.turnOn")}
          </Button>
        </div>
      )}
    </div>
  );
}
