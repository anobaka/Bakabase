import type { IconType } from "react-icons";
import type { DataSyncHistoryDetail, DataSyncHistoryEntry, DataSyncLinkView } from "../api";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineCheckCircle,
  AiOutlineCopy,
  AiOutlineHistory,
  AiOutlineLink,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineUndo,
} from "react-icons/ai";

import { dataSyncApi } from "../api";
import { historyCounts, historyKindName, isUndoable } from "../historyModels";
import { localDateTime, timeAgo } from "../times";

import HistoryDrawing from "./HistoryDrawing";
import UndoDialog from "./UndoDialog";
import {
  DataSyncErrorNotice,
  linkButtonClass,
  panelClass,
  SectionHeading,
  smallButtonClass,
  syncText,
} from "./common";

import {
  BTaskStatus,
  DataSyncHistoryKind,
  DataSyncItemActionLabel,
  DataSyncItemOutcome,
  DataSyncItemOutcomeLabel,
  DataSyncUndoState,
} from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

/*
 * The history (spec §11.3): who sent definitions here, drawn, then every entry — a first sync,
 * a copy, an automatic sync, the reader's own decisions, an undo, a restore — with what it did,
 * its details, and Undo while it can still be undone.
 */

/** How often the history is read again, and how often while an undo runs. */
export const HISTORY_POLL_MS = 30_000;
export const HISTORY_LIVE_POLL_MS = 3_000;

const kindIcons: Record<DataSyncHistoryKind, IconType> = {
  [DataSyncHistoryKind.FirstLink]: AiOutlineLink,
  [DataSyncHistoryKind.CopyOnce]: AiOutlineCopy,
  [DataSyncHistoryKind.AutoSync]: AiOutlineSync,
  [DataSyncHistoryKind.Resolution]: AiOutlineCheckCircle,
  [DataSyncHistoryKind.Undo]: AiOutlineUndo,
  [DataSyncHistoryKind.Restore]: AiOutlineHistory,
  [DataSyncHistoryKind.EntitySetting]: AiOutlineSetting,
};

export interface HistoryListProps {
  version: number;
  /** The links, for the mode an undone change syncs out through. */
  links?: DataSyncLinkView[];
  selfName: string;
  onChanged: () => void;
  now?: number;
}

export default function HistoryList({
  version,
  links,
  selfName,
  onChanged,
  now,
}: HistoryListProps) {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<DataSyncHistoryEntry[]>();
  const [error, setError] = useState<Error>();
  const [open, setOpen] = useState<number>();
  const [details, setDetails] = useState<Record<number, DataSyncHistoryDetail | Error>>({});
  const [undoing, setUndoing] = useState<DataSyncHistoryEntry>();
  const [running, setRunning] = useState<Map<number, string>>(new Map());
  const [runErrors, setRunErrors] = useState<Map<number, string>>(new Map());
  const tasks = useBTasksStore((state) => state.tasks);

  const load = useCallback(async () => {
    try {
      const next = await dataSyncApi.history();

      setEntries(next);
      setError(undefined);
      // An undo is over once its entry says it is undone.
      setRunning((current) => {
        const kept = new Map(current);

        for (const id of current.keys())
          if (next.find((entry) => entry.id === id)?.undoState !== DataSyncUndoState.Available)
            kept.delete(id);

        return kept;
      });
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, version]);

  const live = running.size > 0;

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (typeof document === "undefined" || !document.hidden) void load();
      },
      live ? HISTORY_LIVE_POLL_MS : HISTORY_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

  // An undo whose task failed says so on its row.
  useEffect(() => {
    for (const [id, taskId] of running) {
      const task = tasks.find((one) => one.id === taskId);

      if (task?.status === BTaskStatus.Error || task?.status === BTaskStatus.Cancelled) {
        setRunning((current) => {
          const next = new Map(current);

          next.delete(id);

          return next;
        });
        setRunErrors((current) =>
          new Map(current).set(
            id,
            task.briefError || task.error || t("dataSync.history.undoFailed"),
          ),
        );
      }
    }
  }, [tasks, running, t]);

  const toggle = async (entry: DataSyncHistoryEntry) => {
    if (open === entry.id) {
      setOpen(undefined);

      return;
    }
    setOpen(entry.id);
    if (details[entry.id] && !(details[entry.id] instanceof Error)) return;
    try {
      const detail = await dataSyncApi.historyEntry(entry.id);

      if (detail) setDetails((current) => ({ ...current, [entry.id]: detail }));
    } catch (cause) {
      setDetails((current) => ({
        ...current,
        [entry.id]: cause instanceof Error ? cause : new Error(String(cause)),
      }));
    }
  };

  const modeOf = (entry: DataSyncHistoryEntry) =>
    entry.linkId != null ? links?.find((link) => link.id === entry.linkId)?.mode : undefined;

  return (
    <section
      aria-labelledby="data-sync-history-title"
      className={`${panelClass} space-y-3`}
      data-testid="data-sync-history"
    >
      <SectionHeading id="data-sync-history-title" title={t("dataSync.history.title")} />
      <HistoryDrawing entries={entries ?? []} now={now} selfName={selfName} />
      <DataSyncErrorNotice error={error} onRetry={() => void load()} />
      {!entries && !error && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {entries && entries.length === 0 && (
        <p className="text-sm text-default-500">{t("dataSync.history.empty")}</p>
      )}
      {entries && entries.length > 0 && (
        <ul className="divide-y divide-default-100" data-testid="data-sync-history-list">
          {entries.map((entry) => {
            const Icon = kindIcons[entry.kind] ?? AiOutlineSync;
            const kind = historyKindName(entry.kind);
            const counts = historyCounts(entry.counts);
            const detail = details[entry.id];
            const busy = running.has(entry.id);

            return (
              <li
                key={entry.id}
                className="space-y-1.5 py-2"
                data-entry={entry.id}
                data-kind={kind}
                data-testid="data-sync-history-entry"
              >
                <div className="flex flex-wrap items-center gap-2 text-sm">
                  <Icon aria-hidden className="shrink-0 text-default-500" />
                  <span className="font-medium">{t(`dataSync.history.kind.${kind}`)}</span>
                  {entry.peerName && <span className={syncText}>{entry.peerName}</span>}
                  <span
                    className="text-xs text-default-500"
                    data-testid="data-sync-history-time"
                    title={localDateTime(entry.appliedAt)}
                  >
                    {timeAgo(t, entry.appliedAt, now)}
                  </span>
                  <span className="ml-auto flex flex-wrap items-center gap-2">
                    {entry.undoState === DataSyncUndoState.Undone && (
                      <span
                        className="text-xs text-default-500"
                        data-testid="data-sync-history-undone"
                      >
                        {t("dataSync.history.undone", { time: localDateTime(entry.undoneAt) })}
                      </span>
                    )}
                    {entry.undoState === DataSyncUndoState.Expired &&
                      entry.kind !== DataSyncHistoryKind.Undo && (
                        <span className="text-xs text-default-500">
                          {t("dataSync.history.expired")}
                        </span>
                      )}
                    {busy && (
                      <span className="text-xs text-primary" role="status">
                        {t("dataSync.history.undoing")}
                      </span>
                    )}
                    {isUndoable(entry) && !busy && (
                      <button
                        className={smallButtonClass}
                        data-testid="data-sync-history-undo"
                        type="button"
                        onClick={() => setUndoing(entry)}
                      >
                        {t("dataSync.undo.button")}
                      </button>
                    )}
                    <button
                      aria-expanded={open === entry.id}
                      className={linkButtonClass}
                      data-testid="data-sync-history-details"
                      type="button"
                      onClick={() => void toggle(entry)}
                    >
                      {t(
                        open === entry.id
                          ? "dataSync.history.hideDetails"
                          : "dataSync.history.details",
                      )}
                    </button>
                  </span>
                </div>
                {counts.length > 0 && (
                  <p className="text-xs text-default-500" data-testid="data-sync-history-counts">
                    {counts
                      .map(({ key, count }) => t(`dataSync.history.count.${key}`, { count }))
                      .join(" · ")}
                  </p>
                )}
                {runErrors.get(entry.id) && (
                  <p className="text-xs text-danger" role="alert">
                    {runErrors.get(entry.id)}
                  </p>
                )}
                {open === entry.id && (
                  <div
                    className="rounded-lg bg-default-50 p-2 text-xs"
                    data-testid="data-sync-history-items"
                  >
                    {detail instanceof Error ? (
                      <DataSyncErrorNotice error={detail} onRetry={() => void toggle(entry)} />
                    ) : !detail ? (
                      <p role="status">{t("dataSync.loading")}</p>
                    ) : detail.items.length === 0 ? (
                      <p>{t("dataSync.history.noItems")}</p>
                    ) : (
                      <ul className="space-y-0.5">
                        {detail.items.map((item) => (
                          <li key={item.itemId} className="flex flex-wrap gap-x-2">
                            <span className="font-medium">{item.name}</span>
                            <span className="text-default-500">
                              {item.outcome === DataSyncItemOutcome.Applied
                                ? t(
                                    `dataSync.history.action.${DataSyncItemActionLabel[item.action] ?? "None"}`,
                                  )
                                : t(
                                    `dataSync.history.outcome.${DataSyncItemOutcomeLabel[item.outcome] ?? "NoChange"}`,
                                  )}
                            </span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}
      {undoing && (
        <UndoDialog
          entry={undoing}
          linkMode={modeOf(undoing)}
          onClose={() => setUndoing(undefined)}
          onStarted={(taskId) => {
            const id = undoing.id;

            setUndoing(undefined);
            setRunErrors((current) => {
              const next = new Map(current);

              next.delete(id);

              return next;
            });
            setRunning((current) => new Map(current).set(id, taskId ?? ""));
            onChanged();
            void load();
          }}
        />
      )}
    </section>
  );
}
