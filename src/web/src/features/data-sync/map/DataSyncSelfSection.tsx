import type { DataSyncMapView } from "../api";
import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";

import { useEffect, useId, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { DATA_SYNC_ROUTE, dataSyncInboxRoute } from "../routes";
import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { useDataSyncStore } from "../stores/dataSync";
import InvitationDialog from "../components/InvitationDialog";
import DataSyncHelp from "../components/DataSyncHelp";
import { buttonClass, StatusDot, syncText, toneText } from "../components/common";
import SharingControls, { openCodeFirst } from "../components/SharingControls";
import { overallStatus, readLane, receiveLane, syncPeersOf } from "../viewModels";

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
  const remoteAccessMode =
    view?.remoteAccessMode ?? overview?.remoteAccessMode ?? RemoteAccessMode.Disabled;
  const own = { sharingEnabled, remoteAccessMode };
  const peers = syncPeersOf(view?.peers);
  const counts = [
    ["receivesFrom", peers.filter((peer) => receiveLane(peer) === "active").length],
    ["readBy", peers.filter((peer) => readLane(peer) === "active").length],
  ] as const;
  const line = overallStatus(t, status ?? overview?.status, now);
  const openItems = overview?.openInboxItems ?? status?.openItems ?? 0;

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
      <SharingControls actions={actions} canManage={canManage} {...own} />
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
            disabled={actions.busy}
            type="button"
            onClick={() => openCodeFirst(t, actions, own, () => setCode(true))}
          >
            {t("dataSync.invitation.button")}
          </button>
        )}
        <Link className={buttonClass} to={DATA_SYNC_ROUTE}>
          {t("dataSync.link.openPage")}
        </Link>
      </div>
      {code && <InvitationDialog actions={actions} now={now} onClose={() => setCode(false)} />}
    </section>
  );
}
