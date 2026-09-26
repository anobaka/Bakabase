import type { DataSyncInboxItemView, DataSyncInboxQuery, DataSyncResolveBatchInput } from "../api";
import type { InboxApplying, InboxBulk, InboxCardModel } from "../inboxModels";
import type { SyncPeer } from "../viewModels";
import type { InboxConfirmation } from "./InboxCard";
import type { RefObject } from "react";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { motion, useReducedMotion } from "framer-motion";

import { dataSyncApi, DataSyncProblemError, throwIfProblem } from "../api";
import { useBackupTarget } from "../hooks/useBackupTarget";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import {
  batchTokens,
  closureKey,
  filterCards,
  groupInbox,
  headlineKey,
  inboxBulks,
  isConflict,
  recentlyResolved,
  settleApplying,
} from "../inboxModels";
import { useDataSyncStore } from "../stores/dataSync";
import { localDateTime } from "../times";
import { dataSyncKinds, elsewhereLines } from "../viewModels";

import DataSyncHelp from "./DataSyncHelp";
import ElsewhereLines from "./ElsewhereLines";
import InboxBulkBar from "./InboxBulkBar";
import InboxCard, { cardValues } from "./InboxCard";
import { DataSyncErrorNotice, fieldClass, panelClass, SectionHeading } from "./common";

import ConfirmDialog from "@/features/federation/components/ConfirmDialog";
import { BTaskStatus, DataSyncProblemCode } from "@/sdk/constants";
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

/** The page asked of `GET /data-sync/inbox`. The server may answer fewer: pages are read on. */
export const INBOX_PAGE = 500;
/**
 * How many open items "Needs you" reads at most, a page at a time. Past it the section says how
 * many it shows of how many, and leaves out what needs every item of a definition at once.
 */
export const OPEN_LIMIT = 5_000;
/** How many of the items decided lately are read for "Recently resolved". */
export const CLOSED_TAKE = 200;
/** Read again less often while there is more than one page to read. */
export const INBOX_LARGE_POLL_MS = 60_000;

type Item = DataSyncInboxItemView;

/** Refusals that say the page no longer shows what there is to decide: read it again. */
const staleProblems = new Set<DataSyncProblemCode>([
  DataSyncProblemCode.InboxItemChanged,
  DataSyncProblemCode.InboxItemClosed,
  DataSyncProblemCode.ResolveTogether,
]);

/**
 * Open items, a page at a time, up to {@link OPEN_LIMIT}: until a page comes back empty or the
 * total is reached, whatever page size the server keeps to. Pages can shift while they are read
 * — an item opened or closed meanwhile — so an item met twice is kept once, and one missed is
 * there at the next read.
 */
async function readOpen(
  query: Pick<DataSyncInboxQuery, "kind" | "localKey"> = {},
): Promise<{ items: Item[]; total: number }> {
  const byId = new Map<number, Item>();
  let total = 0;
  let skip = 0;

  for (;;) {
    const page = await dataSyncApi.inbox({ ...query, openOnly: true, skip, take: INBOX_PAGE });
    const items = page?.items ?? [];
    const known = byId.size;

    total = page?.total ?? skip + items.length;
    for (const item of items) byId.set(item.id, item);
    skip += items.length;
    // A page that brings nothing new: a server that does not page — never read forever.
    if (!items.length || byId.size === known || skip >= total || byId.size >= OPEN_LIMIT) break;
  }

  return { items: Array.from(byId.values()), total: Math.max(total, byId.size) };
}

/** Every open item, up to {@link OPEN_LIMIT}, and how many are open in all. */
export const readOpenItems = () => readOpen();

/**
 * One definition's open items whole, however many others are open (`GET /data-sync/inbox` with
 * its kind and local key): every conflict of it, which must be decided together (§9.2).
 */
export const readEntityItems = async (kind: string, localKey: string) =>
  (await readOpen({ kind, localKey })).items;

/** The cards still to decide, in their order: not those that say how they closed. */
const liveCards = (section: HTMLElement) =>
  Array.from(
    section.querySelectorAll<HTMLElement>(
      '[data-testid="data-sync-inbox-card"]:not([data-closed])',
    ),
  );

/** Focus that is nowhere: on the page's body, or on an element no longer in the page. */
const nowhere = (element: Element | null) =>
  !element || element === document.body || !element.isConnected;

/**
 * Keeps the keyboard on the cards when a card takes away what had it: its control disabled while
 * its decision is sent (Chromium moves focus to the page's body at once), or the card itself
 * gone once decided. Focus goes to that card's heading while it is there, else to the next
 * card's, else to the section's heading. Only focus the reader left on a card is kept: a pointer
 * pressed elsewhere, or focus moved outside the cards, lets go.
 */
function useCardFocus(section: RefObject<HTMLElement>, heading: RefObject<HTMLHeadingElement>) {
  // The card that has the keyboard, and where it stood among the cards.
  const holder = useRef<{ key: string; index: number }>();
  const watched = useRef<{ element: HTMLElement; observer: MutationObserver }>();

  const check = useCallback(() => {
    const held = holder.current;
    const element = section.current;

    if (!held || !element || !nowhere(document.activeElement)) return;
    // A confirmation still asks: the keyboard is its until it closes.
    if (document.querySelector('[role="alertdialog"]')) return;
    const cards = liveCards(element);
    const card = cards.find((item) => item.dataset.card === held.key) ?? cards[held.index];

    holder.current = card ? { key: card.dataset.card!, index: cards.indexOf(card) } : undefined;
    (card?.querySelector<HTMLElement>("h3") ?? heading.current)?.focus();
  }, [section, heading]);

  useEffect(() => {
    const onFocusIn = (event: FocusEvent) => {
      const target = event.target instanceof Element ? event.target : null;

      // A confirmation a card opened: the card keeps the keyboard's place.
      if (target?.closest('[role="alertdialog"]')) return;
      const card =
        target && section.current?.contains(target)
          ? target.closest<HTMLElement>('[data-testid="data-sync-inbox-card"]:not([data-closed])')
          : null;

      holder.current =
        card && section.current
          ? { key: card.dataset.card!, index: liveCards(section.current).indexOf(card) }
          : undefined;
    };
    const onPointerDown = () => {
      holder.current = undefined;
    };

    document.addEventListener("focusin", onFocusIn, true);
    document.addEventListener("pointerdown", onPointerDown, true);

    return () => {
      document.removeEventListener("focusin", onFocusIn, true);
      document.removeEventListener("pointerdown", onPointerDown, true);
      watched.current?.observer.disconnect();
      watched.current = undefined;
    };
  }, [section]);

  // Every change inside the section is checked — the section itself comes and goes with what
  // there is to show — and every render too: a confirmation closing changes nothing inside it.
  useEffect(() => {
    const element = section.current;

    if (watched.current?.element !== element) {
      watched.current?.observer.disconnect();
      watched.current = undefined;
      if (element && typeof MutationObserver === "function") {
        const observer = new MutationObserver(check);

        observer.observe(element, {
          childList: true,
          subtree: true,
          attributes: true,
          attributeFilter: ["disabled"],
        });
        watched.current = { element, observer };
      }
    }
    check();
  });
}

/** Where a definition read whole is remembered: by kind and local key. */
const entityKey = (kind: string, localKey: string) => `${kind}/${localKey}`;

/** `items` with `more` among them, each item once — as `more` has it, the later read. */
const withItems = (items: readonly Item[], more: readonly Item[]) => {
  const byId = new Map(items.map((item) => [item.id, item]));

  for (const item of more) byId.set(item.id, item);

  return Array.from(byId.values());
};

/**
 * The items decided lately. The server lists open items first, then newest first, so the closed
 * ones start where the open ones end; an open item met here anyway is left out.
 */
export async function readClosedItems(openCount: number): Promise<Item[]> {
  const page = await dataSyncApi.inbox({ openOnly: false, skip: openCount, take: CLOSED_TAKE });

  return (page?.items ?? []).filter((item) => !!item.closedAt);
}

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
  /** How many items are open in all: more than are shown, past {@link OPEN_LIMIT}. */
  const [openTotal, setOpenTotal] = useState(0);
  /** Whether the first pages held every open item; past {@link OPEN_LIMIT} they do not. */
  const [complete, setComplete] = useState(true);
  const [closed, setClosed] = useState<Item[]>([]);
  const [error, setError] = useState<Error>();
  const [peer, setPeer] = useState(initialPeer ?? "");
  const [kind, setKind] = useState("");
  const [applying, setApplying] = useState<Map<string, InboxApplying>>(new Map());
  const [cardErrors, setCardErrors] = useState<Map<string, Error>>(new Map());
  const [leaving, setLeaving] = useState<Map<string, { card: InboxCardModel; item: Item }>>(
    new Map(),
  );
  const [bulkBackup, setBulkBackup] = useState(true);
  const [bulkError, setBulkError] = useState<Error>();
  const backup = useBackupTarget();
  const section = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const shownCards = useRef<InboxCardModel[]>([]);
  const actions = useDataSyncActions(() => undefined);
  const reducedMotion = useReducedMotion();
  const openItemsHere = useDataSyncStore((state) => state.status?.openItems);
  const tasks = useBTasksStore((state) => state.tasks);
  // The decisions followed, kept here as well, so a read answered before the next render judges
  // what was just sent; and how many reads have started, so only a read begun after a task
  // finished settles its decision.
  const applyingNow = useRef(applying);
  const reads = useRef(0);
  // Definitions read whole because the first pages could not hold them all: read again with every
  // read while there is more than those pages, and kept while they have open items.
  const entities = useRef(new Map<string, { kind: string; localKey: string }>());

  useEffect(() => setPeer(initialPeer ?? ""), [initialPeer]);
  useCardFocus(section, heading);

  const updateApplying = useCallback(
    (change: (current: Map<string, InboxApplying>) => Map<string, InboxApplying>) => {
      const next = change(applyingNow.current);

      applyingNow.current = next;
      setApplying(next);
    },
    [],
  );
  const setCardError = useCallback(
    (key: string, cause?: Error) =>
      setCardErrors((current) => {
        const next = new Map(current);

        if (cause) next.set(key, cause);
        else next.delete(key);

        return next;
      }),
    [],
  );

  const load = useCallback(async () => {
    const read = ++reads.current;

    try {
      const { items: firstPages, total } = await readOpenItems();
      const nextClosed = await readClosedItems(total);
      const whole = firstPages.length >= total;
      let nextOpen = firstPages;

      if (whole) entities.current.clear();
      else
        for (const [key, { kind, localKey }] of entities.current) {
          const items = await readEntityItems(kind, localKey);

          if (items.length) nextOpen = withItems(nextOpen, items);
          else entities.current.delete(key);
        }
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
      // A decision is over once its card is gone; once an item of it changed meanwhile, which the
      // card then says, showing what it is now; or once its task finished before this read.
      const outcomes = settleApplying(applyingNow.current, groupInbox(nextOpen), read, Date.now());

      if (outcomes.size) {
        updateApplying((current) => {
          const next = new Map(current);

          for (const key of outcomes.keys()) next.delete(key);

          return next;
        });
        for (const [key, outcome] of outcomes)
          if (outcome === "changed")
            setCardError(
              key,
              new DataSyncProblemError({ code: DataSyncProblemCode.InboxItemChanged }),
            );
      }
      setOpen(nextOpen);
      setOpenTotal(total);
      setComplete(whole);
      setClosed(nextClosed);
      setError(undefined);
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [updateApplying, setCardError]);

  useEffect(() => {
    void load();
  }, [load, version, openItemsHere]);

  const live = applying.size > 0;
  // Many pages to read: read them less often, except while a decision sent from here applies.
  const large = openTotal > INBOX_PAGE;

  useEffect(() => {
    const timer = setInterval(
      () => {
        if (typeof document === "undefined" || !document.hidden) void load();
      },
      live ? INBOX_LIVE_POLL_MS : large ? INBOX_LARGE_POLL_MS : INBOX_POLL_MS,
    );

    return () => clearInterval(timer);
  }, [live, large, load]);

  // A card closed elsewhere leaves after it has said so.
  useEffect(() => {
    if (!leaving.size) return;
    const timer = setTimeout(() => setLeaving(new Map()), CLOSED_CARD_MS);

    return () => clearTimeout(timer);
  }, [leaving]);

  // Each decision's task, as the task list tells it. One that failed: the card says why and can
  // be decided again. One that finished — or left the list after it was seen there: over at the
  // next read, which shows where the decision ended (applied, or an item changed meanwhile).
  useEffect(() => {
    const changes = new Map<string, { taskId: string; change: "failed" | "finished" | "seen" }>();
    const failures = new Map<string, Error>();

    for (const [key, entry] of applying) {
      if (!entry.taskId || entry.settleAfter !== undefined) continue;
      const task = tasks.find((one) => one.id === entry.taskId);

      if (task?.status === BTaskStatus.Error || task?.status === BTaskStatus.Cancelled) {
        changes.set(key, { taskId: entry.taskId, change: "failed" });
        failures.set(
          key,
          new Error(task.briefError || task.error || t("dataSync.inbox.card.failed")),
        );
      } else if (task?.status === BTaskStatus.Completed || (!task && entry.seen))
        changes.set(key, { taskId: entry.taskId, change: "finished" });
      else if (task && !entry.seen) changes.set(key, { taskId: entry.taskId, change: "seen" });
    }
    if (!changes.size) return;
    const settleAfter = reads.current + 1;

    updateApplying((current) => {
      const next = new Map(current);

      for (const [key, { taskId, change }] of changes) {
        const entry = current.get(key);

        // Sent again meanwhile: the new decision is followed on its own.
        if (!entry || entry.taskId !== taskId) continue;
        if (change === "failed") next.delete(key);
        else
          next.set(key, {
            ...entry,
            seen: true,
            settleAfter: change === "finished" ? settleAfter : entry.settleAfter,
          });
      }

      return next;
    });
    for (const [key, cause] of failures) setCardError(key, cause);
    if (Array.from(changes.values()).some(({ change }) => change === "finished")) void load();
  }, [tasks, applying, t, load, updateApplying, setCardError]);

  const cards = useMemo(() => groupInbox(open ?? []), [open]);
  // Past what is read, a definition's conflicts may be only partly here: nothing that needs all
  // of them at once is offered for many until the rest is read, and a card's own Apply reads its
  // definition whole first (`completeCard`).

  shownCards.current = cards;
  const filtered = filterCards(cards, { peer: peer || undefined, kind: kind || undefined });
  const bulks = inboxBulks(filtered, bulkBackup, { wholeCards: complete });
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

  /** Follows the cards a batch decides, until the decision is over. */
  const follow = (keys: string[], batch: DataSyncResolveBatchInput, taskId?: string | null) => {
    const entry: InboxApplying = {
      taskId: taskId ?? "",
      tokens: batchTokens(batch),
      sentAt: Date.now(),
      // No task to follow: the read that comes next shows where it ended.
      settleAfter: taskId ? undefined : reads.current + 1,
    };

    updateApplying((current) => {
      const next = new Map(current);

      for (const key of keys) next.set(key, entry);

      return next;
    });
    for (const key of keys) setCardError(key);
  };

  /**
   * Reads a conflict card's definition whole and shows every open item of it from then on, with
   * every read. Answers whether the card lacked a conflict: it must then be decided again.
   */
  const completeCard = async (card: InboxCardModel) => {
    if (!card.localKey) return false;
    const items = await readEntityItems(card.kind, card.localKey);
    const shown = new Set(card.items.map((item) => item.id));

    entities.current.set(entityKey(card.kind, card.localKey), {
      kind: card.kind,
      localKey: card.localKey,
    });
    setOpen((current) => withItems(current ?? [], items));

    return items.some((item) => isConflict(item) && !shown.has(item.id));
  };

  /**
   * Sends a batch. Refused because it no longer matches what there is to decide — an item changed
   * or closed meanwhile, or a conflict of the definition missing from the card — "Needs you" is
   * read again, so the card shows what there is to decide now: the missing conflicts read with
   * their definition, wherever they are among the open items.
   */
  const send = async (batch: DataSyncResolveBatchInput, card?: InboxCardModel) => {
    try {
      return throwIfProblem(await dataSyncApi.resolve(batch));
    } catch (cause) {
      if (cause instanceof DataSyncProblemError && staleProblems.has(cause.problem.code)) {
        if (card && cause.problem.code === DataSyncProblemCode.ResolveTogether)
          await completeCard(card).catch(() => false);
        void load();
      }
      throw cause;
    }
  };

  const resolve = (
    card: InboxCardModel,
    batch: DataSyncResolveBatchInput,
    confirmation?: InboxConfirmation,
  ) => {
    const operation = async () => {
      // Not every open item was read: the card may lack a conflict of its definition, decided
      // together with it. Read the definition whole first; one it lacked shows, to decide again.
      if (!complete && card.items.some(isConflict) && (await completeCard(card)))
        throw new DataSyncProblemError({ code: DataSyncProblemCode.ResolveTogether });
      const start = await send(batch, card);

      follow([card.key], batch, start.taskId);
      resolved();
    };

    if (confirmation) actions.confirm({ ...confirmation, action: operation, refresh: [] });
    else void actions.run(operation, [], (cause) => setCardError(card.key, cause));
  };

  const runBulk = (bulk: InboxBulk) => {
    const operation = async () => {
      setBulkError(undefined);
      const start = await send(bulk.batch);
      const ids = new Set(bulk.batch.items.map((input) => input.itemId));

      follow(
        cards.filter((card) => card.items.some((item) => ids.has(item.id))).map((card) => card.key),
        bulk.batch,
        start.taskId,
      );
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
      <SectionHeading
        headingRef={heading}
        id="data-sync-inbox-title"
        title={t("dataSync.inbox.title")}
      >
        <DataSyncHelp />
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
      {open && !complete && (
        <p
          className="rounded-lg bg-default-100 p-2 text-xs"
          data-testid="data-sync-inbox-partial"
          role="status"
        >
          {t("dataSync.inbox.partial", { shown: open.length, total: openTotal })}
        </p>
      )}
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
            onDismissError={() => setCardError(card.key)}
            onPauseLink={(linkId) =>
              void actions.run(
                async () => {
                  throwIfProblem(await dataSyncApi.pauseLink(linkId));
                  resolved();
                },
                [],
                (cause) => setCardError(card.key, cause),
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
