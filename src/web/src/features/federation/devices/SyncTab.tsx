import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { buttonClass, ErrorNotice, panelClass } from "../components/common";

import { useDevicesPage } from "./context";
import TabHeading from "./TabHeading";

import { StatusDot, toneText } from "@/features/data-sync/components/common";
import { DATA_SYNC_ROUTE, dataSyncInboxRoute } from "@/features/data-sync/routes";
import { useDataSyncStore } from "@/features/data-sync/stores/dataSync";
import { overallStatus, readLane, receiveLane, syncPeersOf } from "@/features/data-sync/viewModels";

/**
 * Data sync, summed up: how it is, whether this device shares its definitions, how many
 * devices it receives from and how many read it, and the way to its own page, where
 * everything about it is decided. Read from the page's data (`/data-sync/map`) and the status
 * the hub keeps current, like the device map's.
 */
export default function SyncTab() {
  const { t } = useTranslation();
  const { data, now } = useDevicesPage();
  const overview = useDataSyncStore((state) => state.overview);
  const status = data.dataSyncStatus ?? overview?.status;
  const view = data.dataSync;
  const peers = syncPeersOf(view?.peers);
  const sharingEnabled = view?.sharingEnabled ?? overview?.sharingEnabled;
  const line = overallStatus(t, status, now);
  const openItems = status?.openItems ?? overview?.openInboxItems ?? 0;
  const counts = [
    ["receivesFrom", peers.filter((peer) => receiveLane(peer) === "active").length],
    ["readBy", peers.filter((peer) => readLane(peer) === "active").length],
  ] as const;

  return (
    <>
      <TabHeading introKey="dataSync.description" />
      <section className={`${panelClass} space-y-3`} data-testid="devices-sync">
        <ErrorNotice error={data.dataSyncError} onRetry={() => void data.reload(["dataSync"])} />
        {line && (
          <p className={`flex items-start gap-2 text-sm ${toneText[line.tone]}`}>
            <StatusDot tone={line.tone} />
            <span>{line.text}</span>
          </p>
        )}
        {sharingEnabled !== undefined && (
          <p className="text-sm">
            {t(sharingEnabled ? "dataSync.sharing.isOn" : "dataSync.sharing.isOff")}
          </p>
        )}
        {view && (
          <dl className="grid gap-2 @xl:grid-cols-2">
            {counts.map(([key, value]) => (
              <div key={key} className="rounded-lg bg-default-50 p-2">
                <dt className="text-xs text-default-500">{t(`dataSync.map.self.${key}`)}</dt>
                <dd className="text-lg font-semibold">{value}</dd>
              </div>
            ))}
          </dl>
        )}
        <div className="flex flex-wrap gap-2">
          {openItems > 0 && (
            <Link className={buttonClass} to={dataSyncInboxRoute()}>
              {t("dataSync.status.NeedsYou", { count: openItems })}
            </Link>
          )}
          <Link className={buttonClass} to={DATA_SYNC_ROUTE}>
            {t("dataSync.link.openPage")}
          </Link>
        </div>
      </section>
    </>
  );
}
