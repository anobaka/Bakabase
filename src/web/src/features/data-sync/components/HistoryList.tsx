import type { IconType } from "react-icons";
import type { DataSyncHistoryDetail, DataSyncHistoryEntry, DataSyncLinkView } from "../api";

import { forwardRef, useCallback, useEffect, useMemo, useRef, useState } from "react";
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
import { findTask, listedTasks } from "../hooks/useDataSyncTask";
import { localDateTime, timeAgo } from "../times";

import HistoryDrawing from "./HistoryDrawing";
import UndoDialog from "./UndoDialog";
import {
  DataSyncErrorNotice,
  fieldClass,
  linkButtonClass,
  panelClass,
  SectionHeading,
  smallButtonClass,
  syncText,
  taskFailureText,
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
 * its details, and Undo while it can still be undone. It can show one device's entries alone,
 * which is where a link's details send the reader for its history (spec §11.2).
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
  /** The device whose entries alone are shown (its node id), or every device's. */
  peer?: string;
  onPeerChange?: (peer?: string) => void;
  onChanged: () => void;
  now?: number;
}

/** An undo that was started: its task, and an earlier run under that id still listed then. */
interface RunningUndo {
  taskId: string;
  /** That earlier run's creation time (`listedTasks`): not this undo's task. */
  earlier?: string;
}

const HistoryList = forwardRef<HTMLHeadingElement, HistoryListProps>(function HistoryList(
  { version, links, selfName, peer, onPeerChange, onChanged, now },
  heading,
) {
  const { t } = useTranslation();
  const [all, setEntries] = useState<DataSyncHistoryEntry[]>();
  const [error, setError] = useState<Error>();
  const [open, setOpen] = useState<number>();
  const [details, setDetails] = useState<Record<number, DataSyncHistoryDetail | Error>>({});
  const [undoing, setUndoing] = useState<DataSyncHistoryEntry>();
  // What the task list showed as the undo dialog opened: a retried undo reuses its task id.
  const listedAtOpen = useRef<ReadonlyMap<string, string>>(new Map());
  const [running, setRunning] = useState<Map<number, RunningUndo>>(new Map());
  const [runErrors, setRunErrors] = useState<Map<number, string>>(new Map());
  // Undos whose task completed: the next read says whether they undid anything.
  const completed = useRef(new Set<number>());
  const tasks = useBTasksStore((state) => state.tasks);
  const latestT = useRef(t);

  latestT.current = t;

  const load = useCallback(async () => {
    // Taken before the read: a task that completed before it began has written what it undid.
    const settled = new Set(completed.current);

    try {
      const next = await dataSyncApi.history();
      const available = (id: number) =>
        next.find((entry) => entry.id === id)?.undoState === DataSyncUndoState.Available;
      // A completed undo whose entry can still be undone undid nothing: every step was refused.
      const nothingUndone = [...settled].filter(available);

      for (const id of settled) completed.current.delete(id);
      setEntries(next);
      setError(undefined);
      // An undo is over once its entry says it is undone — or once it completed without that.
      setRunning((current) => {
        const kept = new Map(current);

        for (const id of current.keys()) if (!available(id) || settled.has(id)) kept.delete(id);

        return kept;
      });
      if (nothingUndone.length > 0)
        setRunErrors((current) => {
          const errors = new Map(current);

          for (const id of nothingUndone)
            errors.set(id, latestT.current("dataSync.history.undoNothing"));

          return errors;
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

  // An undo whose task failed says so on its row; one that completed is read again at once.
  useEffect(() => {
    for (const [id, { taskId, earlier }] of running) {
      const task = findTask(tasks, taskId, earlier);

      if (task?.status === BTaskStatus.Error || task?.status === BTaskStatus.Cancelled) {
        setRunning((current) => {
          const next = new Map(current);

          next.delete(id);

          return next;
        });
        setRunErrors((current) =>
          new Map(current).set(id, taskFailureText(t, task, t("dataSync.history.undoFailed"))),
        );
      } else if (task?.status === BTaskStatus.Completed && !completed.current.has(id)) {
        completed.current.add(id);
        void load();
      }
    }
  }, [tasks, running, t, load]);

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

  // The devices the history names, for its filter: each once, by name.
  const devices = useMemo(() => {
    const byId = new Map<string, string>();

    for (const entry of all ?? [])
      if (entry.peerNodeId) byId.set(entry.peerNodeId, entry.peerName ?? entry.peerNodeId);

    return Array.from(byId, ([nodeId, name]) => ({ nodeId, name })).sort((a, b) =>
      a.name.localeCompare(b.name),
    );
  }, [all]);
  const entries = peer ? all?.filter((entry) => entry.peerNodeId === peer) : all;
  const peerName =
    devices.find((device) => device.nodeId === peer)?.name ??
    links?.find((link) => link.peerNodeId === peer)?.peerName;

  return (
    <section
      aria-labelledby="data-sync-history-title"
      className={`${panelClass} space-y-3`}
      data-testid="data-sync-history"
    >
      <SectionHeading
        headingRef={heading}
        id="data-sync-history-title"
        title={t("dataSync.history.title")}
      >
        {onPeerChange && (devices.length > 1 || peer) ? (
          <select
            aria-label={t("dataSync.history.filter.device")}
            className={`${fieldClass} w-auto py-1 text-xs`}
            data-testid="data-sync-history-filter-device"
            value={peer ?? ""}
            onChange={(event) => onPeerChange(event.target.value || undefined)}
          >
            <option value="">{t("dataSync.history.filter.allDevices")}</option>
            {devices.map((device) => (
              <option key={device.nodeId} value={device.nodeId}>
                {device.name}
              </option>
            ))}
            {peer && !devices.some((device) => device.nodeId === peer) && (
              <option value={peer}>{peerName ?? t("dataSync.otherDevice")}</option>
            )}
          </select>
        ) : null}
      </SectionHeading>
      <HistoryDrawing entries={entries ?? []} now={now} selfName={selfName} />
      <DataSyncErrorNotice error={error} onRetry={() => void load()} />
      {!entries && !error && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {entries && entries.length === 0 && (
        <p className="text-sm text-default-500" data-testid="data-sync-history-empty">
          {peer
            ? t("dataSync.history.emptyWith", { name: peerName ?? t("dataSync.otherDevice") })
            : t("dataSync.history.empty")}
        </p>
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
                      <span className="text-xs text-primary-700" role="status">
                        {t("dataSync.history.undoing")}
                      </span>
                    )}
                    {isUndoable(entry) && !busy && (
                      <button
                        className={smallButtonClass}
                        data-testid="data-sync-history-undo"
                        type="button"
                        onClick={() => {
                          listedAtOpen.current = listedTasks();
                          setUndoing(entry);
                        }}
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
                  <p className="text-xs text-danger-700" role="alert">
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
            const started = taskId ?? "";

            setUndoing(undefined);
            setRunErrors((current) => {
              const next = new Map(current);

              next.delete(id);

              return next;
            });
            completed.current.delete(id);
            setRunning((current) =>
              new Map(current).set(id, {
                taskId: started,
                earlier: listedAtOpen.current.get(started),
              }),
            );
            onChanged();
            void load();
          }}
        />
      )}
    </section>
  );
});

export default HistoryList;
