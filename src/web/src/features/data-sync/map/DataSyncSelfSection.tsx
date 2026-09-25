import type { DataSyncMapView } from "../api";
import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";

import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { dataSyncApi } from "../api";
import { DATA_SYNC_ROUTE, dataSyncInboxRoute } from "../routes";
import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { useDataSyncStore } from "../stores/dataSync";
import InvitationDialog from "../components/InvitationDialog";
import DataSyncHelp from "../components/DataSyncHelp";
import { buttonClass, StatusDot, syncText, toneText } from "../components/common";
import { overallStatus, readLane, receiveLane, syncPeerFromMapPeer } from "../viewModels";

import { useWordedActions } from "./useWordedActions";

import { KindBadge } from "@/features/federation/map/DeviceMapCanvas";
import { RemoteAccessMode } from "@/sdk/constants";

/*
 * This device's side of data sync, in the device map's details when this device is shown
 * (spec §11.1, hook H-map-self): how it is, whether devices it approves may read its
 * definitions, how many it receives from and how many read it, a one-time code, and the way to
 * the page. Everything goes through the map's actions.
 */

export default function DataSyncSelfSection({
  actions: hostActions,
  view,
  now,
}: {
  actions: DataSyncDialogActions;
  /** `GET /data-sync/map`, for the counts; absent until read. */
  view?: DataSyncMapView;
  now?: number;
}) {
  const { t } = useTranslation();
  const headingId = useId();
  const actions = useWordedActions(hostActions);
  const canManageHere = useCanManageDefinitionSharing();
  const overview = useDataSyncStore((state) => state.overview);
  const status = useDataSyncStore((state) => state.status);
  const load = useDataSyncStore((state) => state.load);
  const [code, setCode] = useState(false);

  // The overview is this device's own side; read once when the section is first shown without it.
  useEffect(() => {
    if (!overview) void load();
  }, []);

  const canManage = canManageHere && (overview?.canManageSharing ?? true);
  const sharingEnabled = view?.sharingEnabled ?? overview?.sharingEnabled ?? false;
  const remoteOff =
    (view?.remoteAccessMode ?? overview?.remoteAccessMode ?? RemoteAccessMode.Disabled) ===
    RemoteAccessMode.Disabled;
  const peers = (view?.peers ?? []).map(syncPeerFromMapPeer);
  const counts = [
    ["receivesFrom", peers.filter((peer) => receiveLane(peer) === "active").length],
    ["readBy", peers.filter((peer) => readLane(peer) === "active").length],
  ] as const;
  const line = overallStatus(t, status ?? overview?.status, now);
  const openItems = overview?.openInboxItems ?? status?.openItems ?? 0;
  const codeUnavailable = !sharingEnabled
    ? t("dataSync.invitation.needsSharing")
    : remoteOff
      ? t("dataSync.invitation.needsRemoteAccess")
      : undefined;

  const setSharing = (enabled: boolean) => {
    if (!enabled) {
      actions.confirm({
        title: t("dataSync.sharing.offTitle"),
        description: t("dataSync.sharing.offDescription"),
        action: () => dataSyncApi.setSharing({ enabled: false, enablePairedRemoteAccess: false }),
        refresh: ["dataSync", "sharing"],
      });

      return;
    }
    const operation = () =>
      dataSyncApi.setSharing({ enabled: true, enablePairedRemoteAccess: remoteOff });

    if (remoteOff)
      actions.confirm({
        title: t("dataSync.sharing.onTitle"),
        description: t("dataSync.sharing.hint"),
        warning: t("dataSync.sharing.remoteAccess"),
        action: operation,
        refresh: ["dataSync", "sharing"],
      });
    else void actions.run(operation, ["dataSync", "sharing"]);
  };

  return (
    <section
      aria-labelledby={headingId}
      className="space-y-3 border-t border-default-200 pt-4"
      data-testid="data-sync-self-section"
    >
      <div className="flex items-center justify-between gap-2">
        <h3 className={`flex items-center gap-2 text-sm font-semibold ${syncText}`} id={headingId}>
          <KindBadge kind="sync" />
          {t("federation.map.edge.sync")}
        </h3>
        <DataSyncHelp />
      </div>
      {line && (
        <p
          className={`flex items-start gap-2 text-sm ${toneText[line.tone]}`}
          data-testid="data-sync-self-status"
        >
          <StatusDot tone={line.tone} />
          <span>{line.text}</span>
        </p>
      )}
      {canManage ? (
        <label className="flex cursor-pointer items-start gap-2 text-sm">
          <input
            checked={sharingEnabled}
            className="mt-1 accent-secondary"
            data-testid="data-sync-self-sharing"
            disabled={actions.busy}
            role="switch"
            type="checkbox"
            onChange={(event) => setSharing(event.target.checked)}
          />
          <span>
            {t("dataSync.sharing.label")}
            <span className="mt-0.5 block text-xs text-default-500">
              {t("dataSync.sharing.hint")}
              {remoteOff ? ` ${t("dataSync.sharing.remoteAccess")}` : ""}
            </span>
          </span>
        </label>
      ) : (
        <p className="text-sm">
          {t(sharingEnabled ? "dataSync.sharing.isOn" : "dataSync.sharing.isOff")}
        </p>
      )}
      <dl className="grid grid-cols-2 gap-2">
        {counts.map(([key, value]) => (
          <div key={key} className="rounded-lg bg-default-50 p-2">
            <dt className="text-xs text-default-500">{t(`dataSync.map.self.${key}`)}</dt>
            <dd className="text-lg font-semibold">{value}</dd>
          </div>
        ))}
      </dl>
      <div className="flex flex-wrap gap-2">
        {openItems > 0 && (
          <Link className={buttonClass} to={dataSyncInboxRoute()}>
            {t("dataSync.status.NeedsYou", { count: openItems })}
          </Link>
        )}
        {canManage && (
          <button
            className={buttonClass}
            data-testid="data-sync-self-code"
            disabled={actions.busy || !!codeUnavailable}
            title={codeUnavailable}
            type="button"
            onClick={() => setCode(true)}
          >
            {t("dataSync.invitation.button")}
          </button>
        )}
        <Link className={buttonClass} to={DATA_SYNC_ROUTE}>
          {t("dataSync.link.openPage")}
        </Link>
      </div>
      {canManage && codeUnavailable && (
        <p className="text-xs text-default-500">{codeUnavailable}</p>
      )}
      {code && <InvitationDialog actions={actions} now={now} onClose={() => setCode(false)} />}
    </section>
  );
}
