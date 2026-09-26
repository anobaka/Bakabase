import type { TFunction } from "i18next";
import type { DataSyncOverview, DataSyncReaderView } from "../api";
import type { DataSyncActions } from "../hooks/useDataSyncActions";

import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { timeAgo } from "../times";
import {
  codeTurnsOnConfirmation,
  remoteAccessConfirmation,
  sharingNeeded,
  turnOnSharingInput,
} from "../viewModels";

import { DataSyncErrorNotice, panelClass, SectionHeading, smallButtonClass } from "./common";

import { RemoteAccessMode } from "@/sdk/constants";

/*
 * This device's side of data sync: whether devices it approves may read its definitions,
 * whether definitions made here are shared by themselves, a code for another device, pausing
 * everything, and who reads this device now. Turning sharing on and creating a code are
 * offered only where this window may create access (spec §7.1.5); turning things off, pausing
 * and stopping a reader stay available everywhere, so access can always be shut.
 *
 * Every way of letting another device read this one also offers to turn remote access on
 * (spec §7.2.4): the switch, a code, and — while sharing is on and remote access off, which is
 * where another device's "Remote access is off" status sends the reader — a button of its own.
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
  readersError,
  actions,
  canManage,
  onCreateCode,
  onRetryReaders,
  now,
}: {
  overview: DataSyncOverview;
  readers?: DataSyncReaderView[];
  readersError?: Error;
  actions: DataSyncActions;
  canManage: boolean;
  onCreateCode: () => void;
  onRetryReaders: () => void;
  now?: number;
}) {
  const { t } = useTranslation();
  const remoteOff = overview.remoteAccessMode === RemoteAccessMode.Disabled;
  const own = {
    sharingEnabled: overview.sharingEnabled,
    remoteAccessMode: overview.remoteAccessMode,
  };

  /** A code only works while both are on: what is off is turned on first, then it is shown. */
  const createCode = () => {
    if (!sharingNeeded(own)) {
      onCreateCode();

      return;
    }
    actions.confirm({
      ...codeTurnsOnConfirmation(t, own),
      action: async () => {
        await dataSyncApi.setSharing(turnOnSharingInput(own));
        if (actions.mounted.current) onCreateCode();
      },
      refresh: ["dataSync", "sharing"],
    });
  };

  const turnOnRemoteAccess = () =>
    actions.confirm({
      ...remoteAccessConfirmation(t),
      action: () => dataSyncApi.setSharing({ enabled: true, enablePairedRemoteAccess: true }),
      refresh: ["dataSync", "sharing"],
    });

  const setSharing = (enabled: boolean) => {
    if (enabled) {
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

      return;
    }
    actions.confirm({
      title: t("dataSync.sharing.offTitle"),
      description: t("dataSync.sharing.offDescription"),
      action: () => dataSyncApi.setSharing({ enabled: false, enablePairedRemoteAccess: false }),
      refresh: ["dataSync", "sharing"],
    });
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

      <div className="space-y-1">
        {canManage ? (
          <label className="flex cursor-pointer items-start gap-3 text-sm">
            <input
              aria-describedby="data-sync-sharing-hint"
              checked={overview.sharingEnabled}
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
              {t(overview.sharingEnabled ? "dataSync.sharing.isOn" : "dataSync.sharing.isOff")}
            </span>
            {overview.sharingEnabled && (
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
        <p className="pl-7 text-xs text-default-500" id="data-sync-sharing-hint">
          {t("dataSync.sharing.hint")}
          {/* What turning the switch on also does: said only while it is off. */}
          {remoteOff && canManage && !overview.sharingEnabled
            ? ` ${t("dataSync.sharing.remoteAccess")}`
            : ""}
        </p>
        {canManage && overview.sharingEnabled && remoteOff && (
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
              onClick={turnOnRemoteAccess}
            >
              {t("dataSync.remoteAccess.turnOn")}
            </button>
          </div>
        )}
      </div>

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
            onClick={createCode}
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
        <DataSyncErrorNotice error={readersError} onRetry={onRetryReaders} />
        {readers && readers.length === 0 && (
          <p className="text-xs text-default-500">{t("dataSync.readers.none")}</p>
        )}
        {readers && readers.length > 0 && (
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
