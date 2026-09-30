import type { TFunction } from "i18next";
import type { DataSyncOverview, DataSyncReaderView } from "../api";
import type { DataSyncActions } from "../hooks/useDataSyncActions";

import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { timeAgo } from "../times";

import { panelClass, SectionHeading, smallButtonClass } from "./common";
import SharingControls, { openCodeFirst } from "./SharingControls";

/*
 * This device's side of data sync: whether devices it approves may read its definitions
 * (`SharingControls`), whether definitions made here are shared by themselves, a code for
 * another device, pausing everything, and who reads this device now. Creating a code is offered
 * only where this window may create access (spec §7.1.5); pausing and stopping a reader stay
 * available everywhere, so access can always be shut.
 */

/**
 * What a reader declared about its side when it last read this device (spec §7.5.6): `ok`,
 * `awaitingReview` (its own first review), `waitingForPeerReview` (this device's review),
 * `paused:{reason}` or `needsYou:{n}` — in the words of the readers list, or none for a word this
 * build does not know (a newer device may say more).
 */
export const readerStateText = (t: TFunction, state?: string | null): string | undefined => {
  const [code, value] = (state ?? "").split(":");

  switch (code) {
    case "ok":
      return t("dataSync.readers.state.inStep");
    case "awaitingReview":
      return t("dataSync.readers.state.awaitingReview");
    case "waitingForPeerReview":
      return t("dataSync.readers.state.waitingForPeerReview");
    case "paused":
      return t("dataSync.readers.state.paused");
    case "needsYou":
      return t("dataSync.readers.state.needsYou", { count: Number(value) || 0 });
    default:
      return undefined;
  }
};

export default function ThisDeviceSection({
  overview,
  readers,
  actions,
  canManage,
  onCreateCode,
  now,
}: {
  overview: DataSyncOverview;
  readers: DataSyncReaderView[];
  actions: DataSyncActions;
  canManage: boolean;
  onCreateCode: () => void;
  now?: number;
}) {
  const { t } = useTranslation();
  const own = {
    sharingEnabled: overview.sharingEnabled,
    remoteAccessMode: overview.remoteAccessMode,
  };

  // Leaves sharing as it is (no `enabled`): a value read before sharing changed elsewhere is
  // never sent back, and the switch is open to every window, like turning sharing off.
  const setNewDefinitionsShared = (shared: boolean) =>
    void actions.run(
      () =>
        dataSyncApi.setSharing({
          enablePairedRemoteAccess: false,
          newDefinitionsStayLocal: !shared,
        }),
      ["dataSync"],
    );

  const stopReader = (reader: DataSyncReaderView) =>
    actions.confirm({
      title: t("dataSync.arrow.read.stopTitle", { name: reader.name }),
      description: t("dataSync.arrow.read.stopDescription", { name: reader.name }),
      action: () => dataSyncApi.revokeReader(reader.nodeId),
      refresh: ["dataSync", "sharing"],
    });

  return (
    <section
      aria-labelledby="data-sync-this-device"
      className={`${panelClass} space-y-4`}
      data-testid="data-sync-this-device"
    >
      <SectionHeading id="data-sync-this-device" title={t("dataSync.self.title")}>
        <button
          className={smallButtonClass}
          data-testid="data-sync-pause-all"
          disabled={actions.busy}
          type="button"
          onClick={() =>
            void actions.run(() => dataSyncApi.setAllPaused(!overview.allPaused), ["dataSync"])
          }
        >
          {t(overview.allPaused ? "dataSync.pause.resumeAll" : "dataSync.pause.pauseAll")}
        </button>
      </SectionHeading>

      <SharingControls actions={actions} canManage={canManage} {...own} />

      <div className="flex flex-wrap items-center gap-3">
        <label className="flex items-center gap-3 text-sm">
          <input
            checked={!overview.newDefinitionsStayLocal}
            className="accent-secondary"
            data-testid="data-sync-new-definitions"
            disabled={actions.busy}
            type="checkbox"
            onChange={(event) => setNewDefinitionsShared(event.target.checked)}
          />
          {t("dataSync.self.newDefinitions")}
        </label>
        {canManage && (
          <button
            className={smallButtonClass}
            data-testid="data-sync-create-code"
            disabled={actions.busy}
            type="button"
            onClick={() => openCodeFirst(t, actions, own, onCreateCode)}
          >
            {t("dataSync.invitation.button")}
          </button>
        )}
      </div>
      {!canManage && (
        <p className="text-xs text-default-500" data-testid="data-sync-manage-elsewhere">
          {t("dataSync.manageElsewhere")}
        </p>
      )}

      <div className="space-y-2" data-testid="data-sync-readers">
        <h3 className="text-sm font-semibold">{t("dataSync.readers.title")}</h3>
        {readers.length === 0 && (
          <p className="text-xs text-default-500">{t("dataSync.readers.none")}</p>
        )}
        {readers.length > 0 && (
          <ul className="divide-y divide-default-100 rounded-lg border border-default-200">
            {readers.map((reader) => {
              return (
                <li
                  key={reader.nodeId}
                  className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm"
                  data-reader={reader.nodeId}
                >
                  <span className="min-w-0 flex-1 font-medium">{reader.name}</span>
                  <span className="text-xs text-default-500">
                    {[
                      reader.mode
                        ? t(
                            `dataSync.readers.mode.${reader.mode === "twoWay" ? "twoWay" : "follow"}`,
                          )
                        : undefined,
                      readerStateText(t, reader.state),
                      t("dataSync.readers.lastRead", { time: timeAgo(t, reader.lastReadAt, now) }),
                      reader.upToDate ? t("dataSync.readers.upToDate") : undefined,
                    ]
                      .filter(Boolean)
                      .join(" · ")}
                  </span>
                  <button
                    className={smallButtonClass}
                    disabled={actions.busy}
                    type="button"
                    onClick={() => stopReader(reader)}
                  >
                    {t("dataSync.readers.stop")}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </div>
    </section>
  );
}
