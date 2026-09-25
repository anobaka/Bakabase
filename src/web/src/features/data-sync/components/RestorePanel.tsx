import type { DataSyncRestoreView } from "../api";
import type { DataSyncPanelActions } from "../hooks/useDataSyncActions";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi, throwIfProblem } from "../api";
import { localDateTime } from "../times";

import { buttonClass, DataSyncErrorNotice, primaryClass } from "./common";
import DataSyncHelp from "./DataSyncHelp";

import { DataSyncPauseReason, DataSyncRestoreChoice } from "@/sdk/constants";

/*
 * "My configuration wins" (spec §9.5): this device's data looks older than what the other
 * devices have seen — restored from a backup, or copied from another computer — and nothing
 * syncs on the paused links until the reader says what wins. The panel names the evidence and,
 * when only one device suggested it, that device.
 */

/** What the panel says it saw: this device's own records, another device's, or both. */
export type RestoreEvidence = "own" | "peer" | "both";

export const restoreEvidence = (view: DataSyncRestoreView): RestoreEvidence => {
  const suspected = view.reason === DataSyncPauseReason.LocalRestoreSuspected;

  if (suspected) return "peer";

  return view.evidenceFromName ? "both" : "own";
};

export default function RestorePanel({
  actions,
  version,
  /** `?restore=1`: shown even when nothing waits, to say so. */
  asked,
}: {
  actions: DataSyncPanelActions;
  version: number;
  asked: boolean;
}) {
  const { t } = useTranslation();
  const [view, setView] = useState<DataSyncRestoreView>();
  const [error, setError] = useState<Error>();
  const [later, setLater] = useState(false);

  const load = useCallback(async () => {
    try {
      setView((await dataSyncApi.restore()) ?? undefined);
      setError(undefined);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, version]);

  if (!view?.pending) {
    if (!asked) return null;

    return (
      <section
        className="rounded-xl border border-default-200 p-3 text-sm"
        data-testid="data-sync-restore-panel"
      >
        <DataSyncErrorNotice error={error} onRetry={() => void load()} />
        {view && <p>{t("dataSync.restore.nothing")}</p>}
      </section>
    );
  }

  const evidence = restoreEvidence(view);
  const name = view.evidenceFromName ?? "";
  const choose = (choice: DataSyncRestoreChoice) =>
    actions.confirm({
      title:
        choice === DataSyncRestoreChoice.ThisDeviceWins
          ? t("dataSync.restore.confirm.thisDevice")
          : t("dataSync.restore.confirm.others"),
      description: t(
        choice === DataSyncRestoreChoice.ThisDeviceWins
          ? "dataSync.restore.thisDeviceWins.description"
          : "dataSync.restore.othersWin.description",
      ),
      action: async () => {
        throwIfProblem(await dataSyncApi.chooseRestore(choice, view.linkId ?? undefined));
        await load();
      },
      refresh: ["dataSync"],
    });

  if (later)
    return (
      <p
        className="flex flex-wrap items-center gap-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm"
        data-testid="data-sync-restore-pending"
      >
        <span>{t("dataSync.restore.pending")}</span>
        <button className={buttonClass} type="button" onClick={() => setLater(false)}>
          {t("dataSync.restore.decide")}
        </button>
      </p>
    );

  return (
    <section
      aria-labelledby="data-sync-restore-title"
      className="space-y-3 rounded-xl border border-warning/40 bg-warning/5 p-4"
      data-evidence={evidence}
      data-testid="data-sync-restore-pending"
    >
      <div className="flex flex-wrap items-start gap-4">
        <RestoreDrawing />
        <div className="min-w-0 flex-1 space-y-1.5">
          <div className="flex items-center gap-1">
            <h2 className="font-semibold" id="data-sync-restore-title">
              {t("dataSync.restore.title")}
            </h2>
            <DataSyncHelp />
          </div>
          <p className="text-sm">{t("dataSync.restore.intro")}</p>
          <p className="text-sm text-default-600" data-testid="data-sync-restore-evidence">
            {t(`dataSync.restore.evidence.${evidence}`, { name })}
          </p>
          <p className="text-xs text-default-500">
            {[
              view.detectedAt
                ? t("dataSync.restore.detectedAt", { time: localDateTime(view.detectedAt) })
                : "",
              t("dataSync.restore.pausedLinks", { count: view.pausedLinks }),
            ]
              .filter(Boolean)
              .join(" · ")}
          </p>
        </div>
      </div>
      <DataSyncErrorNotice error={error} onRetry={() => void load()} />
      <div className="grid gap-2 sm:grid-cols-3">
        <RestoreChoice
          primary
          busy={actions.busy}
          description={t("dataSync.restore.thisDeviceWins.short")}
          testId="data-sync-restore-this-device"
          title={t("dataSync.restore.thisDeviceWins.title")}
          onChoose={() => choose(DataSyncRestoreChoice.ThisDeviceWins)}
        />
        <RestoreChoice
          busy={actions.busy}
          description={t("dataSync.restore.othersWin.short")}
          testId="data-sync-restore-others"
          title={t("dataSync.restore.othersWin.title")}
          onChoose={() => choose(DataSyncRestoreChoice.OthersWin)}
        />
        <RestoreChoice
          busy={actions.busy}
          description={t("dataSync.restore.later.short")}
          testId="data-sync-restore-later"
          title={t("dataSync.restore.later.title")}
          onChoose={() => setLater(true)}
        />
      </div>
    </section>
  );
}

function RestoreChoice({
  title,
  description,
  primary = false,
  busy,
  testId,
  onChoose,
}: {
  title: string;
  description: string;
  primary?: boolean;
  busy: boolean;
  testId: string;
  onChoose: () => void;
}) {
  return (
    <div className="flex flex-col gap-2 rounded-lg border border-default-200 bg-content1 p-3">
      <p className="text-xs text-default-500">{description}</p>
      <button
        className={`${primary ? primaryClass : buttonClass} mt-auto`}
        data-testid={testId}
        disabled={busy}
        type="button"
        onClick={onChoose}
      >
        {title}
      </button>
    </div>
  );
}

/** This device, with a clock arrow pointing back: its data went back in time. */
function RestoreDrawing() {
  return (
    <svg aria-hidden className="h-16 w-24 shrink-0" viewBox="0 0 96 64">
      <rect
        className="fill-primary/10 stroke-primary"
        height="34"
        rx="4"
        strokeWidth="2"
        width="52"
        x="8"
        y="10"
      />
      <path
        className="fill-none stroke-primary"
        d="M34 44 V52 M24 54 H44"
        strokeLinecap="round"
        strokeWidth="2"
      />
      <circle className="fill-content1 stroke-warning" cx="70" cy="26" r="16" strokeWidth="2" />
      <path
        className="fill-none stroke-warning"
        d="M70 17 V26 L76 30"
        strokeLinecap="round"
        strokeWidth="2"
      />
      <path
        className="fill-none stroke-warning"
        d="M56 14 A18 18 0 0 1 86 18"
        markerStart="url(#data-sync-restore-back)"
        strokeLinecap="round"
        strokeWidth="2"
      />
      <defs>
        <marker
          id="data-sync-restore-back"
          markerHeight="6"
          markerWidth="6"
          orient="auto-start-reverse"
          refX="1"
          refY="3"
        >
          <path className="fill-warning" d="M0 0 L6 3 L0 6 Z" />
        </marker>
      </defs>
    </svg>
  );
}
