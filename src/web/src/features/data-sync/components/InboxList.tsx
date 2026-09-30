import type { DataSyncInboxItemView, DataSyncResolveBatchInput } from "../api";
import type { InboxBulk, InboxCardModel } from "../inboxModels";
import type { SyncPeer } from "../viewModels";
import type { InboxConfirmation } from "./InboxCard";
import type { RefObject } from "react";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { motion, useReducedMotion } from "framer-motion";

import { dataSyncApi, DataSyncProblemError, throwIfProblem } from "../api";
import { useBackupTarget } from "../hooks/useBackupTarget";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { listedTasks } from "../hooks/useDataSyncTask";
import { useTaskFollower } from "../hooks/useTaskFollower";
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

import DataSyncHelp from "./DataSyncHelp";
import ElsewhereLines from "./ElsewhereLines";
import InboxBulkBar from "./InboxBulkBar";
import InboxCard, { cardValues } from "./InboxCard";
import {
  DataSyncConfirmDialog,
  DataSyncErrorNotice,
  fieldClass,
  panelClass,
  SectionHeading,
  taskFailureText,
} from "./common";

import { DataSyncLinkMode, DataSyncProblemCode } from "@/sdk/constants";

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

/**
 * How many open items "Needs you" reads at most, in one read: whole definitions, as the server
 * never ends a page inside one. Past it the section says how many it shows of how many.
 */
export const OPEN_LIMIT = 5_000;
/** How many of the items decided lately are read for "Recently resolved". */
export const CLOSED_TAKE = 200;
/** Read again less often while this many are open. */
export const INBOX_LARGE = 500;
export const INBOX_LARGE_POLL_MS = 60_000;

type Item = DataSyncInboxItemView;

/** Refusals that say the page no longer shows what there is to decide: read it again. */
const staleProblems = new Set<DataSyncProblemCode>([
  DataSyncProblemCode.InboxItemChanged,
  DataSyncProblemCode.InboxItemClosed,
  DataSyncProblemCode.ResolveTogether,
]);

/** Every open item, up to {@link OPEN_LIMIT}, and how many are open in all. */
export const readOpenItems = async () => {
  const page = await dataSyncApi.inbox({ openOnly: true, take: OPEN_LIMIT });
  const items = page?.items ?? [];

  return { items, total: Math.max(page?.total ?? 0, items.length) };
};

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
  const [closed, setClosed] = useState<Item[]>([]);
  const [error, setError] = useState<Error>();
  const [peer, setPeer] = useState(initialPeer ?? "");
  const [kind, setKind] = useState("");
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
  // The token each item of a decision still followed was sent with, by card.
  const sent = useRef(new Map<string, ReadonlyMap<number, string>>());

  useEffect(() => setPeer(initialPeer ?? ""), [initialPeer]);
  useCardFocus(section, heading);

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

  /** Reads "Needs you" again; answers the open items read, or nothing when the read failed. */
  const load = useCallback(async () => {
    try {
      const { items: nextOpen, total } = await readOpenItems();
      const nextClosed = await readClosedItems(total);
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
      setOpen(nextOpen);
      setOpenTotal(total);
      setClosed(nextClosed);
      setError(undefined);

      return nextOpen;
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, []);

  // Each decision's task: one that failed says why on its card, which can be decided again. Once
  // one is over, the read after it shows where the decision ended — applied, or an item that came
  // back with another token: it changed while the decision was on its way, and the task updated
  // it without applying anything (§9.2 step 2), which its card says.
  const follower = useTaskFollower<string>({
    onOver: async (keys) => {
      const cards = new Map(groupInbox((await load()) ?? []).map((card) => [card.key, card]));

      for (const key of keys) {
        const tokens = sent.current.get(key);

        sent.current.delete(key);
        if (
          cards
            .get(key)
            ?.items.some((item) => tokens?.has(item.id) && tokens.get(item.id) !== item.token)
        )
          setCardError(
            key,
            new DataSyncProblemError({ code: DataSyncProblemCode.InboxItemChanged }),
          );
      }
    },
    onFailed: (keys, task) => {
      for (const key of keys) {
        sent.current.delete(key);
        setCardError(key, new Error(taskFailureText(t, task, t("dataSync.inbox.card.failed"))));
      }
    },
  });

  useEffect(() => {
    void load();
  }, [load, version, openItemsHere]);

  const live = follower.running.size > 0;
  // Many to read: read them less often, except while a decision sent from here applies.
  const large = openTotal > INBOX_LARGE;

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

  /**
   * Follows the cards a batch decides until its task is over; with no task, the read that comes
   * next shows where it ended. `earlier`: the task list as it was before the batch was sent.
   */
  const follow = (
    keys: string[],
    batch: DataSyncResolveBatchInput,
    taskId: string | null | undefined,
    earlier: ReadonlyMap<string, string>,
  ) => {
    for (const key of keys) setCardError(key);
    if (!taskId) return;
    const tokens = new Map(batch.items.map((input) => [input.itemId, input.token]));

    for (const key of keys) sent.current.set(key, tokens);
    follower.follow(keys, taskId, earlier.get(taskId));
  };

  /**
   * Sends a batch. Refused because it no longer matches what there is to decide — an item changed
   * or closed meanwhile, or a conflict of the definition opened since — "Needs you" is read again,
   * so the card shows what there is to decide now.
   */
  const send = async (batch: DataSyncResolveBatchInput) => {
    try {
      return throwIfProblem(await dataSyncApi.resolve(batch));
    } catch (cause) {
      if (cause instanceof DataSyncProblemError && staleProblems.has(cause.problem.code))
        void load();
      throw cause;
    }
  };

  const resolve = (
    card: InboxCardModel,
    batch: DataSyncResolveBatchInput,
    confirmation?: InboxConfirmation,
  ) => {
    const operation = async () => {
      const earlier = listedTasks();
      const start = await send(batch);

      follow([card.key], batch, start.taskId, earlier);
      resolved();
    };

    if (confirmation) actions.confirm({ ...confirmation, action: operation, refresh: [] });
    else void actions.run(operation, [], (cause) => setCardError(card.key, cause));
  };

  const runBulk = (bulk: InboxBulk) => {
    const operation = async () => {
      setBulkError(undefined);
      const earlier = listedTasks();
      const start = await send(bulk.batch);
      const ids = new Set(bulk.batch.items.map((input) => input.itemId));

      follow(
        cards.filter((card) => card.items.some((item) => ids.has(item.id))).map((card) => card.key),
        bulk.batch,
        start.taskId,
        earlier,
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
      {open && open.length < openTotal && (
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
            applying={follower.running.has(card.key)}
            backup={backup}
            busy={actions.busy}
            card={card}
            error={cardErrors.get(card.key)}
            follows={peers.some(
              (peer) =>
                peer.linkId === card.items[0].linkId && peer.mode === DataSyncLinkMode.Follow,
            )}
            now={now}
            onDismissError={() => setCardError(card.key)}
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
        <DataSyncConfirmDialog
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
