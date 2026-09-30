import type { IconType } from "react-icons";
import type { DataSyncHistoryDetail, DataSyncHistoryEntry } from "../api";
import type { SyncPeer } from "../viewModels";

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
import { listedTasks } from "../hooks/useDataSyncTask";
import { useTaskFollower } from "../hooks/useTaskFollower";
import { localDateTime, timeAgo } from "../times";

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
  DataSyncHistoryKind,
  DataSyncItemActionLabel,
  DataSyncItemOutcome,
  DataSyncItemOutcomeLabel,
  DataSyncUndoState,
} from "@/sdk/constants";

/*
 * The history (spec §11.3): every entry — a first sync, a copy, an automatic sync, the reader's
 * own decisions, an undo, a restore — with what it did, its details, and Undo while it can still
 * be undone. It can show one device's entries alone, which is where a link's details send the
 * reader for its history (spec §11.2).
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
  /** The devices, for the mode an undone change syncs out through. */
  peers?: SyncPeer[];
  /** The device whose entries alone are shown (its node id), or every device's. */
  peer?: string;
  onPeerChange?: (peer?: string) => void;
  onChanged: () => void;
  now?: number;
}

const HistoryList = forwardRef<HTMLHeadingElement, HistoryListProps>(function HistoryList(
  { version, peers, peer, onPeerChange, onChanged, now },
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
  const [runErrors, setRunErrors] = useState<Map<number, string>>(new Map());
  const say = (ids: number[], text?: string) =>
    setRunErrors((current) => {
      const errors = new Map(current);

      for (const id of ids)
        if (text) errors.set(id, text);
        else errors.delete(id);

      return errors;
    });

  const load = useCallback(async () => {
    try {
      const next = await dataSyncApi.history();

      setEntries(next);
      setError(undefined);

      return next;
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, []);

  // An undo whose task failed says so on its row. One that is over is read again: an entry that
  // can still be undone then undid nothing — every step was refused.
  const follower = useTaskFollower<number>({
    onOver: async (ids) => {
      const next = await load();

      say(
        ids.filter((id) => next?.some((entry) => entry.id === id && isUndoable(entry))),
        t("dataSync.history.undoNothing"),
      );
    },
    onFailed: (ids, task) => say(ids, taskFailureText(t, task, t("dataSync.history.undoFailed"))),
  });

  useEffect(() => {
    void load();
  }, [load, version]);

  const live = follower.running.size > 0;

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (typeof document === "undefined" || !document.hidden) void load();
      },
      live ? HISTORY_LIVE_POLL_MS : HISTORY_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

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
    entry.linkId != null ? peers?.find((item) => item.linkId === entry.linkId)?.mode : undefined;

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
    peers?.find((item) => item.nodeId === peer)?.name;

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
            // Undoing until its task is over, unless the entry already says it is undone.
            const busy = follower.running.has(entry.id) && isUndoable(entry);

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
            setUndoing(undefined);
            say([undoing.id]);
            if (taskId) follower.follow([undoing.id], taskId, listedAtOpen.current.get(taskId));
            onChanged();
            void load();
          }}
        />
      )}
    </section>
  );
});

export default HistoryList;
