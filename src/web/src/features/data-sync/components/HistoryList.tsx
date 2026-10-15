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
import { historyCounts, historyKindName } from "../historyModels";
import { localDateTime, timeAgo } from "../times";

import {
  DataSyncErrorNotice,
  linkButtonClass,
  panelClass,
  SectionHeading,
  smallButtonClass,
  syncText,
} from "./common";

import {
  DataSyncHistoryKind,
  DataSyncItemActionLabel,
  DataSyncItemOutcome,
  DataSyncItemOutcomeLabel,
} from "@/sdk/constants";
import { Button, Select } from "@/components/bakaui";

/*
 * The history (spec §11.3): every entry — a first sync, a copy, an automatic sync, the reader's
 * own decisions, a restore, or a historical undo — with what it did and its details. It can show
 * one device's entries alone, which is where a link's details send the reader (spec §11.2).
 */

/** Keep audit entries current without following write tasks. */
export const HISTORY_POLL_MS = 30_000;

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
  /** Known devices, for the history filter. */
  peers?: SyncPeer[];
  /** The device whose entries alone are shown (its node id), or every device's. */
  peer?: string;
  onPeerChange?: (peer?: string) => void;
  now?: number;
}

const HistoryList = forwardRef<HTMLHeadingElement, HistoryListProps>(function HistoryList(
  { version, peers, peer, onPeerChange, now },
  heading,
) {
  const { t } = useTranslation();
  const [beforeId, setBeforeId] = useState<number>();
  const [hasMore, setHasMore] = useState(false);
  const [all, setEntries] = useState<DataSyncHistoryEntry[]>();
  const [error, setError] = useState<Error>();
  const [open, setOpen] = useState<number>();
  const [details, setDetails] = useState<Record<number, DataSyncHistoryDetail | Error>>({});
  const requestVersion = useRef(0);
  const load = useCallback(async () => {
    const attempt = ++requestVersion.current;

    try {
      const next = await dataSyncApi.history({ beforeId, peerNodeId: peer, take: 50 });

      if (attempt !== requestVersion.current) return;
      setEntries(next);
      setHasMore(next.length === 50);
      setError(undefined);

      return next;
    } catch (cause) {
      if (attempt !== requestVersion.current) return;
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [beforeId, peer]);

  useEffect(() => {
    setBeforeId(undefined);
    setOpen(undefined);
    setDetails({});
  }, [peer]);

  useEffect(() => {
    void load();
  }, [load, version]);

  useEffect(() => {
    const timer = setInterval(() => {
      if (typeof document === "undefined" || !document.hidden) void load();
    }, HISTORY_POLL_MS);

    return () => clearInterval(timer);
  }, [load]);

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

  // The devices the history names, for its filter: each once, by name.
  const devices = useMemo(() => {
    const byId = new Map<string, string>();

    for (const device of peers ?? []) byId.set(device.nodeId, device.name);
    for (const entry of all ?? [])
      if (entry.peerNodeId) byId.set(entry.peerNodeId, entry.peerName ?? entry.peerNodeId);

    return Array.from(byId, ([nodeId, name]) => ({ nodeId, name })).sort((a, b) =>
      a.name.localeCompare(b.name),
    );
  }, [all, peers]);
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
          <Select
            disallowEmptySelection
            aria-label={t("dataSync.history.filter.device")}
            className="w-full sm:w-48"
            data-testid="data-sync-history-filter-device"
            dataSource={[
              { value: "", label: t("dataSync.history.filter.allDevices") },
              ...devices.map((device) => ({ value: device.nodeId, label: device.name })),
              ...(peer && !devices.some((device) => device.nodeId === peer)
                ? [{ value: peer, label: peerName ?? t("dataSync.otherDevice") }]
                : []),
            ]}
            selectedKeys={[peer ?? ""]}
            size="sm"
            variant="bordered"
            onChange={(event) => onPeerChange(event.target.value || undefined)}
          />
        ) : null}
      </SectionHeading>
      <p className="text-xs text-default-500">{t("dataSync.history.auditOnly")}</p>
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
                    {entry.undoneAt && (
                      <span
                        className="text-xs text-default-500"
                        data-testid="data-sync-history-undone"
                      >
                        {t("dataSync.history.undone", { time: localDateTime(entry.undoneAt) })}
                      </span>
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
      {(beforeId !== undefined || hasMore) && (
        <div className="flex justify-end gap-2">
          {beforeId !== undefined && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => {
                setBeforeId(undefined);
                setOpen(undefined);
                setDetails({});
              }}
            >
              {t("dataSync.activity.newest")}
            </Button>
          )}
          {hasMore && (
            <Button
              className={smallButtonClass}
              size="sm"
              type="button"
              variant="bordered"
              onClick={() => {
                setBeforeId(all?.at(-1)?.id);
                setOpen(undefined);
                setDetails({});
              }}
            >
              {t("dataSync.activity.older")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
});

export default HistoryList;
