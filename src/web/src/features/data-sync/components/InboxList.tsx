import type { DataSyncInboxItemView, DataSyncResolveBatchInput } from "../api";
import type { InboxBulk, InboxCardModel } from "../inboxModels";
import type { SyncPeer } from "../viewModels";
import type { InboxConfirmation } from "./InboxCard";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { motion, useReducedMotion } from "framer-motion";

import { dataSyncApi, throwIfProblem } from "../api";
import { useBackupTarget } from "../hooks/useBackupTarget";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import {
  closureKey,
  filterCards,
  groupInbox,
  headlineKey,
  inboxBulks,
  recentlyResolved,
} from "../inboxModels";
import { useDataSyncStore } from "../stores/dataSync";
import { localDateTime } from "../times";
import { dataSyncKinds, elsewhereLines } from "../viewModels";

import ElsewhereLines from "./ElsewhereLines";
import InboxBulkBar from "./InboxBulkBar";
import InboxCard, { cardValues } from "./InboxCard";
import { DataSyncErrorNotice, fieldClass, panelClass, SectionHeading } from "./common";

import ConfirmDialog from "@/features/federation/components/ConfirmDialog";
import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

/*
 * "Needs you" (spec §11.3): the changes that wait for a decision here, one card per definition
 * and device — every conflict of a definition on one card — with filters by device and type of
 * definition, the bulk bar, the lines for devices that hold decisions of their own, and what
 * was decided in the last seven days.
 */

/** How often "Needs you" is read again, and how often while a decision it sent is applied. */
export const INBOX_POLL_MS = 15_000;
export const INBOX_LIVE_POLL_MS = 3_000;
/** How long a card closed elsewhere stays, saying so, before it leaves. */
export const CLOSED_CARD_MS = 4_000;

const OPEN_TAKE = 500;
const CLOSED_TAKE = 200;

export interface InboxListProps {
  /** Moves on whenever the page read data sync again after an action. */
  version: number;
  peers: SyncPeer[];
  /** `?tab=inbox&peer=`: the device to show first. */
  initialPeer?: string;
  /** `?tab=inbox`: shown even when empty, and scrolled to. */
  focus: boolean;
  onChanged: () => void;
  now?: number;
}

type Item = DataSyncInboxItemView;

export default function InboxList({
  version,
  peers,
  initialPeer,
  focus,
  onChanged,
  now,
}: InboxListProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState<Item[]>();
  const [closed, setClosed] = useState<Item[]>([]);
  const [error, setError] = useState<Error>();
  const [peer, setPeer] = useState(initialPeer ?? "");
  const [kind, setKind] = useState("");
  const [applying, setApplying] = useState<Map<string, string>>(new Map());
  const [cardErrors, setCardErrors] = useState<Map<string, Error>>(new Map());
  const [leaving, setLeaving] = useState<Map<string, { card: InboxCardModel; item: Item }>>(
    new Map(),
  );
  const [bulkBackup, setBulkBackup] = useState(true);
  const [bulkError, setBulkError] = useState<Error>();
  const backup = useBackupTarget();
  const section = useRef<HTMLElement>(null);
  const shownCards = useRef<InboxCardModel[]>([]);
  const actions = useDataSyncActions(() => undefined);
  const reducedMotion = useReducedMotion();
  const openItemsHere = useDataSyncStore((state) => state.status?.openItems);
  const tasks = useBTasksStore((state) => state.tasks);

  useEffect(() => setPeer(initialPeer ?? ""), [initialPeer]);

  const load = useCallback(async () => {
    try {
      const [openPage, allPage] = await Promise.all([
        dataSyncApi.inbox({ openOnly: true, take: OPEN_TAKE }),
        dataSyncApi.inbox({ openOnly: false, take: CLOSED_TAKE }),
      ]);
      const nextOpen = openPage?.items ?? [];
      const nextClosed = (allPage?.items ?? []).filter((item) => !!item.closedAt);
      const stillOpen = new Set(nextOpen.map((item) => item.id));
      const closedById = new Map(nextClosed.map((item) => [item.id, item]));

      // Cards that were on screen and closed meanwhile say how, then leave.
      const gone = shownCards.current.filter((card) =>
        card.items.every((item) => !stillOpen.has(item.id)),
      );

      if (gone.length)
        setLeaving((current) => {
          const next = new Map(current);

          for (const card of gone) {
            const item = card.items.map((one) => closedById.get(one.id)).find(Boolean);

            if (item) next.set(card.key, { card, item });
          }

          return next;
        });
      // A decision is applied once its card is gone.
      const openKeys = new Set(groupInbox(nextOpen).map((card) => card.key));

      setApplying((current) => {
        const next = new Map(current);

        for (const key of current.keys()) if (!openKeys.has(key)) next.delete(key);

        return next;
      });
      setOpen(nextOpen);
      setClosed(nextClosed);
      setError(undefined);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, version, openItemsHere]);

  const live = applying.size > 0;

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (typeof document === "undefined" || !document.hidden) void load();
      },
      live ? INBOX_LIVE_POLL_MS : INBOX_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, load]);

  // A card closed elsewhere leaves after it has said so.
  useEffect(() => {
    if (!leaving.size) return;
    const timer = setTimeout(() => setLeaving(new Map()), CLOSED_CARD_MS);

    return () => clearTimeout(timer);
  }, [leaving]);

  // A decision whose task failed: the card says so and can be decided again.
  useEffect(() => {
    if (!applying.size) return;
    for (const [key, taskId] of applying) {
      const task = tasks.find((one) => one.id === taskId);

      if (task?.status === BTaskStatus.Error || task?.status === BTaskStatus.Cancelled) {
        setApplying((current) => {
          const next = new Map(current);

          next.delete(key);

          return next;
        });
        setCardErrors((current) =>
          new Map(current).set(
            key,
            new Error(task.briefError || task.error || t("dataSync.inbox.card.failed")),
          ),
        );
      }
    }
  }, [tasks, applying, t]);

  const cards = useMemo(() => groupInbox(open ?? []), [open]);

  shownCards.current = cards;
  const filtered = filterCards(cards, { peer: peer || undefined, kind: kind || undefined });
  const bulks = inboxBulks(filtered, bulkBackup);
  const recent = recentlyResolved(closed, now);
  const devices = useMemo(() => {
    const byId = new Map<string, string>();

    for (const card of cards) for (const one of card.peers) byId.set(one.nodeId, one.name);

    return Array.from(byId, ([nodeId, name]) => ({ nodeId, name })).sort((a, b) =>
      a.name.localeCompare(b.name),
    );
  }, [cards]);
  const kinds = dataSyncKinds.filter((item) => cards.some((card) => card.kind === item));
  const hasElsewhere = elsewhereLines(t, peers).length > 0;
  const visible =
    focus || cards.length > 0 || recent.length > 0 || hasElsewhere || leaving.size > 0;

  useEffect(() => {
    if (focus && open) section.current?.scrollIntoView?.({ block: "start" });
  }, [focus, !!open]);

  const resolved = () => {
    onChanged();
    void load();
  };

  const resolve = (
    card: InboxCardModel,
    batch: DataSyncResolveBatchInput,
    confirmation?: InboxConfirmation,
  ) => {
    const operation = async () => {
      const start = throwIfProblem(await dataSyncApi.resolve(batch));

      setCardErrors((current) => {
        const next = new Map(current);

        next.delete(card.key);

        return next;
      });
      setApplying((current) => new Map(current).set(card.key, start.taskId ?? ""));
      resolved();
    };

    if (confirmation) actions.confirm({ ...confirmation, action: operation, refresh: [] });
    else
      void actions.run(operation, [], (cause) =>
        setCardErrors((current) => new Map(current).set(card.key, cause)),
      );
  };

  const runBulk = (bulk: InboxBulk) => {
    const operation = async () => {
      setBulkError(undefined);
      const start = throwIfProblem(await dataSyncApi.resolve(bulk.batch));
      const keys = new Set(bulk.batch.items.map((input) => input.itemId));

      setApplying((current) => {
        const next = new Map(current);

        for (const card of cards)
          if (card.items.some((item) => keys.has(item.id))) next.set(card.key, start.taskId ?? "");

        return next;
      });
      resolved();
    };

    if (bulk.destructive)
      actions.confirm({
        title: t(`dataSync.inbox.bulk.${bulk.id}`, {
          count: bulk.count,
          name: bulk.peer?.name ?? "",
        }),
        description: t("dataSync.inbox.confirm.deleteAll", { count: bulk.count }),
        warning: bulkBackup
          ? t("dataSync.inbox.confirm.backedUp")
          : t("dataSync.inbox.confirm.notBackedUp"),
        action: operation,
        refresh: [],
      });
    else void actions.run(operation, [], setBulkError);
  };

  if (!visible) return null;

  return (
    <section
      ref={section}
      aria-labelledby="data-sync-inbox-title"
      className={`${panelClass} space-y-3`}
      data-testid="data-sync-inbox"
    >
      <SectionHeading id="data-sync-inbox-title" title={t("dataSync.inbox.title")}>
        {devices.length > 1 || peer ? (
          <select
            aria-label={t("dataSync.inbox.filter.device")}
            className={`${fieldClass} w-auto py-1 text-xs`}
            data-testid="data-sync-inbox-filter-device"
            value={peer}
            onChange={(event) => setPeer(event.target.value)}
          >
            <option value="">{t("dataSync.inbox.filter.allDevices")}</option>
            {devices.map((device) => (
              <option key={device.nodeId} value={device.nodeId}>
                {device.name}
              </option>
            ))}
            {peer && !devices.some((device) => device.nodeId === peer) && (
              <option value={peer}>
                {peers.find((one) => one.nodeId === peer)?.name ?? t("dataSync.otherDevice")}
              </option>
            )}
          </select>
        ) : null}
        {kinds.length > 1 && (
          <select
            aria-label={t("dataSync.inbox.filter.kind")}
            className={`${fieldClass} w-auto py-1 text-xs`}
            data-testid="data-sync-inbox-filter-kind"
            value={kind}
            onChange={(event) => setKind(event.target.value)}
          >
            <option value="">{t("dataSync.inbox.filter.allKinds")}</option>
            {kinds.map((item) => (
              <option key={item} value={item}>
                {t(`dataSync.kind.${item}`, { defaultValue: item })}
              </option>
            ))}
          </select>
        )}
      </SectionHeading>

      <ElsewhereLines peers={peers} />
      <DataSyncErrorNotice error={error} onRetry={() => void load()} />
      <DataSyncErrorNotice error={bulkError} onDismiss={() => setBulkError(undefined)} />
      <InboxBulkBar
        backup={backup}
        backupFirst={bulkBackup}
        bulks={bulks}
        busy={actions.busy}
        onBackupFirst={setBulkBackup}
        onRun={runBulk}
      />

      {open && filtered.length === 0 && leaving.size === 0 && (
        <p className="text-sm text-default-500" data-testid="data-sync-inbox-empty">
          {t(cards.length ? "dataSync.inbox.noneFiltered" : "dataSync.inbox.empty")}
        </p>
      )}
      {!open && !error && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      <div className="space-y-3">
        {Array.from(leaving.values(), ({ card, item }) => (
          // Says how it closed, then fades as it leaves (at once, where motion is reduced).
          <motion.div
            key={`leaving-${card.key}`}
            animate={reducedMotion ? undefined : { opacity: [1, 1, 0] }}
            data-testid="data-sync-inbox-leaving"
            initial={false}
            transition={{ duration: CLOSED_CARD_MS / 1000, times: [0, 0.75, 1] }}
          >
            <InboxCard
              busy
              backup={backup}
              card={card}
              closedItem={item}
              now={now}
              onResolve={() => undefined}
            />
          </motion.div>
        ))}
        {filtered.map((card) => (
          <InboxCard
            key={card.key}
            applying={applying.has(card.key)}
            backup={backup}
            busy={actions.busy}
            card={card}
            error={cardErrors.get(card.key)}
            now={now}
            onDismissError={() =>
              setCardErrors((current) => {
                const next = new Map(current);

                next.delete(card.key);

                return next;
              })
            }
            onPauseLink={(linkId) =>
              void actions.run(
                async () => {
                  throwIfProblem(await dataSyncApi.pauseLink(linkId));
                  resolved();
                },
                [],
                (cause) => setCardErrors((current) => new Map(current).set(card.key, cause)),
              )
            }
            onResolve={(batch, confirmation) => resolve(card, batch, confirmation)}
          />
        ))}
      </div>

      {recent.length > 0 && (
        <details
          className="rounded-lg border border-default-200 p-2"
          data-testid="data-sync-inbox-recent"
        >
          <summary className="cursor-pointer text-xs font-medium">
            {t("dataSync.inbox.recent", { count: recent.length })}
          </summary>
          <ul className="mt-2 divide-y divide-default-100">
            {recent.map((item) => {
              const card = groupInbox([item])[0];

              return (
                <li key={item.id} className="py-1.5 text-xs" data-closure={closureKey(item)}>
                  <p className="font-medium">
                    {t(`dataSync.inbox.type.${headlineKey(card)}`, cardValues(card))}
                  </p>
                  <p className="text-default-500">
                    {t(`dataSync.inbox.closure.${closureKey(item)}`, {
                      name: item.closedByName ?? "",
                    })}{" "}
                    · {localDateTime(item.closedAt)}
                  </p>
                </li>
              );
            })}
          </ul>
        </details>
      )}

      {actions.confirmation && (
        <ConfirmDialog
          busy={actions.busy}
          description={actions.confirmation.description}
          error={actions.confirmationError}
          title={actions.confirmation.title}
          warning={actions.confirmation.warning}
          onCancel={actions.cancelConfirmation}
          onConfirm={actions.confirmCurrent}
        />
      )}
    </section>
  );
}
