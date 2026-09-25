import type { DataSyncInboxItemView, DataSyncResolveBatchInput, DataSyncResolveInput } from "./api";

import { millisecondsSince } from "./times";

import {
  DataSyncInboxAction,
  DataSyncInboxActionLabel,
  DataSyncInboxClosure,
  DataSyncInboxItemType,
  DataSyncInboxItemTypeLabel,
  DataSyncNaturalMatch,
} from "@/sdk/constants";

/*
 * "Needs you" (spec §9, §11.3), pure: which items make one card, what each card offers, and the
 * resolve batches its buttons and the bulk bar send. The service validates every batch again;
 * these rules make sure the page never sends one it would refuse — above all, every open
 * conflict of a definition, from every device, in one batch (`ResolveTogether`).
 */

type Item = DataSyncInboxItemView;

/** Items that must be decided together, one card per definition (§9.2). */
const conflictTypes = new Set<DataSyncInboxItemType>([
  DataSyncInboxItemType.FieldConflict,
  DataSyncInboxItemType.ChildRenameConflict,
]);

export const isConflict = (item: Item) => conflictTypes.has(item.type);

const allows = (item: Item, action: DataSyncInboxAction) => item.allowedActions.includes(action);

/** One card: a single item, or every conflict of one definition with every device. */
export interface InboxCardModel {
  key: string;
  kind: string;
  localKey?: string;
  entityName: string;
  type: DataSyncInboxItemType;
  items: Item[];
  /** The devices its items came from, each once. */
  peers: { nodeId: string; name: string }[];
}

const peersOf = (items: Item[]) => {
  const peers = new Map<string, string>();

  for (const item of items)
    if (item.peerNodeId) peers.set(item.peerNodeId, item.peerName ?? item.peerNodeId);

  return Array.from(peers, ([nodeId, name]) => ({ nodeId, name }));
};

const cardOrder = (card: InboxCardModel) => [
  // Link-level cards (a large change) come first: they hold back everything under them.
  card.type === DataSyncInboxItemType.LargeChange ? 0 : 1,
  card.entityName.toLocaleLowerCase(),
  card.peers[0]?.name.toLocaleLowerCase() ?? "",
  card.type,
];

const compare = (a: (string | number)[], b: (string | number)[]) => {
  for (let index = 0; index < a.length; index++) {
    if (a[index] === b[index]) continue;

    return typeof a[index] === "number"
      ? (a[index] as number) - (b[index] as number)
      : String(a[index]).localeCompare(String(b[index]));
  }

  return 0;
};

/**
 * The cards "Needs you" shows, grouped by definition and device: every open conflict of one
 * definition, whichever devices they came from, is one card with one Apply; every other item is
 * a card of its own.
 */
export const groupInbox = (items: readonly Item[]): InboxCardModel[] => {
  const cards = new Map<string, InboxCardModel>();

  for (const item of items) {
    const together = isConflict(item) && item.localKey;
    const key = together ? `conflict:${item.kind}/${item.localKey}` : `item:${item.id}`;
    const card = cards.get(key);

    if (card) card.items.push(item);
    else
      cards.set(key, {
        key,
        kind: item.kind,
        localKey: item.localKey ?? undefined,
        entityName: item.payload.entityName,
        type: item.type,
        items: [item],
        peers: [],
      });
  }

  const list = Array.from(cards.values());

  for (const card of list) {
    card.items.sort((a, b) => a.subjectPath.localeCompare(b.subjectPath) || a.id - b.id);
    card.peers = peersOf(card.items);
  }

  return list.sort((a, b) => compare(cardOrder(a), cardOrder(b)) || a.key.localeCompare(b.key));
};

/** The filters above the cards: one device, one type of definition. */
export interface InboxFilter {
  peer?: string;
  kind?: string;
}

/**
 * The cards a filter keeps. A card is kept whole: a conflict card with the device filtered on
 * still shows the other devices' conflicts, because they are decided together.
 */
export const filterCards = (cards: readonly InboxCardModel[], filter: InboxFilter) =>
  cards.filter(
    (card) =>
      (!filter.peer || card.items.some((item) => item.peerNodeId === filter.peer)) &&
      (!filter.kind || card.kind === filter.kind),
  );

// ---- what a card offers --------------------------------------------------------------------------

/** One button of a card: an action, with the target it names when it names one. */
export interface InboxChoice {
  action: DataSyncInboxAction;
  /** Link / KeepWithEntity: the definition here. */
  targetLocalKey?: string;
  /** KeepRecordLinked: the peer's record kept linked. */
  targetRecordKey?: string;
  /** What the button asks for before it can be applied. */
  input?: "custom" | "newName";
  /** Deletes values or converts them: the backup checkbox applies. */
  destructive: boolean;
}

/** Deleting here with values, or converting a type: what a backup is taken for (§8.10.4). */
export const isDestructive = (item: Item, action: DataSyncInboxAction) => {
  if (action === DataSyncInboxAction.Convert) return true;
  if (action !== DataSyncInboxAction.DeleteHere) return false;
  if (item.type === DataSyncInboxItemType.ChildDeletedInUse)
    return (item.payload.usageCount ?? 1) > 0;

  return (item.payload.valueCount ?? 1) > 0;
};

/**
 * The buttons an item offers, from the actions the service allows it (§9.1): one per candidate
 * that can be linked or kept, one per record that can stay linked. A custom value is never
 * offered for a node's parent, which has no name to type.
 */
export const inboxChoices = (item: Item): InboxChoice[] =>
  item.allowedActions.flatMap((action): InboxChoice[] => {
    const destructive = isDestructive(item, action);

    switch (action) {
      case DataSyncInboxAction.Link:
      case DataSyncInboxAction.KeepWithEntity:
        return (item.payload.candidates ?? [])
          .filter((candidate) => candidate.updatable)
          .map((candidate) => ({ action, targetLocalKey: candidate.localKey, destructive }));
      case DataSyncInboxAction.KeepRecordLinked:
        return (item.payload.records ?? []).map((record) => ({
          action,
          targetRecordKey: record.primaryKey,
          destructive,
        }));
      case DataSyncInboxAction.UseCustom:
        return item.subjectPath.endsWith(":parent")
          ? []
          : [{ action, input: "custom", destructive }];
      case DataSyncInboxAction.KeepBoth:
        return [{ action, input: "newName", destructive }];
      default:
        return [{ action, destructive }];
    }
  });

/** Whether a card has a choice the backup checkbox is for. */
export const cardIsDestructive = (card: InboxCardModel) =>
  card.items.some((item) => inboxChoices(item).some((choice) => choice.destructive));

/** The input sent for one item and one of its choices. */
export const resolveInput = (
  item: Item,
  choice: InboxChoice,
  values: { customValue?: string; newName?: string } = {},
): DataSyncResolveInput => ({
  itemId: item.id,
  action: choice.action,
  token: item.token,
  customValue: choice.input === "custom" ? values.customValue : undefined,
  newName: choice.input === "newName" ? values.newName : undefined,
  targetLocalKey: choice.targetLocalKey,
  targetRecordKey: choice.targetRecordKey,
});

export const batchOf = (
  items: DataSyncResolveInput[],
  backupBeforeDestructive: boolean,
): DataSyncResolveBatchInput => ({ items, backupBeforeDestructive });

// ---- decisions sent from here ----------------------------------------------------------------------

/**
 * A decision sent from here, followed until it is over: its task, and the token each item was sent
 * with — an item that comes back with another one changed while the decision was on its way, and
 * the task updated it without applying anything (§9.2 step 2).
 */
export interface InboxApplying {
  /** Empty when the server started no task to follow. */
  taskId: string;
  tokens: ReadonlyMap<number, string>;
  /** When it was sent (ms): a task never seen in the task list is not waited for forever. */
  sentAt: number;
  /** Its task has been seen in the task list. */
  seen?: boolean;
  /**
   * Over at the first read numbered this or later: its task finished, or there is none to
   * follow — what that read shows is where the decision ended.
   */
  settleAfter?: number;
}

/** How long a decision's task may stay out of the task list before the next read settles it. */
export const UNSEEN_TASK_MS = 30_000;

/**
 * How a decision still followed stands after a read of the open items: `applied` once its card is
 * gone; `changed` once one of its items comes back with another token; `settled` once a read after
 * its task finished — or with no task to follow — still shows the card as it was. Decisions not
 * named are still under way.
 */
export type InboxApplyingOutcome = "applied" | "changed" | "settled";

export const settleApplying = (
  applying: ReadonlyMap<string, InboxApplying>,
  cards: readonly InboxCardModel[],
  read: number,
  now: number,
): Map<string, InboxApplyingOutcome> => {
  const byKey = new Map(cards.map((card) => [card.key, card]));
  const outcomes = new Map<string, InboxApplyingOutcome>();

  for (const [key, entry] of applying) {
    const card = byKey.get(key);

    if (!card) outcomes.set(key, "applied");
    else if (
      card.items.some(
        (item) => entry.tokens.has(item.id) && entry.tokens.get(item.id) !== item.token,
      )
    )
      outcomes.set(key, "changed");
    else if (
      (entry.settleAfter !== undefined && read >= entry.settleAfter) ||
      (!entry.seen && now - entry.sentAt >= UNSEEN_TASK_MS)
    )
      outcomes.set(key, "settled");
  }

  return outcomes;
};

/** The token each item of a batch is sent with. */
export const batchTokens = (batch: DataSyncResolveBatchInput): ReadonlyMap<number, string> =>
  new Map(batch.items.map((input) => [input.itemId, input.token]));

// ---- conflict cards --------------------------------------------------------------------------------

/** One field of a conflict card, and the devices that changed it differently. */
export interface ConflictField {
  path: string;
  items: Item[];
  /** Whether a value of the reader's own can be typed for it. */
  custom: boolean;
}

export const conflictFields = (card: InboxCardModel): ConflictField[] => {
  const fields = new Map<string, Item[]>();

  for (const item of card.items) {
    const items = fields.get(item.subjectPath) ?? [];

    items.push(item);
    fields.set(item.subjectPath, items);
  }

  return Array.from(fields, ([path, items]) => ({
    path,
    items,
    custom: items.some((item) =>
      inboxChoices(item).some((choice) => choice.action === DataSyncInboxAction.UseCustom),
    ),
  }));
};

/** What the reader chose for one field: this device's value, one device's, or one typed. */
export type FieldChoice =
  | { take: "local" }
  | { take: "remote"; itemId: number }
  | { take: "custom"; value: string };

export type ConflictChoices = Record<string, FieldChoice>;

/** Every field has a choice, and a typed value is not empty. */
export const conflictDecided = (card: InboxCardModel, choices: ConflictChoices) =>
  conflictFields(card).every((field) => {
    const choice = choices[field.path];

    return !!choice && (choice.take !== "custom" || choice.value.trim().length > 0);
  });

/**
 * The whole card as one batch. For each field, the device whose value was chosen gets
 * `UseRemote`, a typed value goes on the field's first item as `UseCustom`, and every other item
 * of the field keeps this device's — which by then is the value chosen. `detach` stops syncing
 * the definition, and closes every item of the card.
 */
export const conflictBatch = (
  card: InboxCardModel,
  choices: ConflictChoices | "detach",
  backupBeforeDestructive: boolean,
): DataSyncResolveBatchInput =>
  batchOf(
    conflictFields(card).flatMap((field) => {
      const typedOn = field.items.find((item) => allows(item, DataSyncInboxAction.UseCustom));

      return field.items.map((item): DataSyncResolveInput => {
        const input = (action: DataSyncInboxAction, customValue?: string) => ({
          itemId: item.id,
          action,
          token: item.token,
          customValue,
        });

        if (choices === "detach") return input(DataSyncInboxAction.Detach);
        const choice = choices[field.path];

        if (choice?.take === "remote" && choice.itemId === item.id)
          return input(DataSyncInboxAction.UseRemote);
        if (choice?.take === "custom" && item === typedOn)
          return input(DataSyncInboxAction.UseCustom, choice.value.trim());

        return input(DataSyncInboxAction.KeepLocal);
      });
    }),
    backupBeforeDestructive,
  );

// ---- the bulk bar ----------------------------------------------------------------------------------

export type InboxBulkId =
  | "linkExact"
  | "skipAll"
  | "deleteAll"
  | "keepAll"
  | "keepLocalAll"
  | "useRemoteAll";

/** One bulk action: a whole batch, sent with one backup. */
export interface InboxBulk {
  id: InboxBulkId;
  /** "Use X's for all": the device. */
  peer?: { nodeId: string; name: string };
  count: number;
  destructive: boolean;
  batch: DataSyncResolveBatchInput;
}

const exactMatches = new Set<DataSyncNaturalMatch>([
  DataSyncNaturalMatch.Exact,
  DataSyncNaturalMatch.Identical,
]);

/** The one candidate of a link suggestion with the same name and type, if exactly one. */
export const exactCandidate = (item: Item) => {
  const exact = (item.payload.candidates ?? []).filter(
    (candidate) => candidate.updatable && exactMatches.has(candidate.match),
  );

  return exact.length === 1 ? exact[0] : undefined;
};

/** A bulk action is offered once there are several of its kind to decide. */
export const BULK_MINIMUM = 2;

/**
 * The bulk actions the open cards allow (§9.1): link all exact matches and skip all link
 * suggestions; delete all here and keep all on this device for deletions; and, for conflicts,
 * keep this device's for all, or use one device's for all — each conflict card sent whole, the
 * other devices' conflicts of the same definition keeping this device's.
 */
export const inboxBulks = (
  cards: readonly InboxCardModel[],
  backupBeforeDestructive: boolean,
  {
    wholeCards = true,
  }: {
    /**
     * Every open item is here, so each card holds all of its definition's conflicts. When not —
     * more is open than was read — the conflict bulks are left out: they send every card whole.
     */
    wholeCards?: boolean;
  } = {},
): InboxBulk[] => {
  const items = cards.flatMap((card) => card.items);
  const bulks: InboxBulk[] = [];
  const add = (
    id: InboxBulkId,
    inputs: DataSyncResolveInput[],
    count: number,
    extra: Partial<InboxBulk> = {},
  ) => {
    if (count >= BULK_MINIMUM)
      bulks.push({
        id,
        count,
        destructive: false,
        batch: batchOf(inputs, backupBeforeDestructive),
        ...extra,
      });
  };

  const suggestions = items.filter((item) => item.type === DataSyncInboxItemType.LinkSuggestion);
  const linkable = suggestions.filter(
    (item) => allows(item, DataSyncInboxAction.Link) && exactCandidate(item),
  );

  add(
    "linkExact",
    linkable.map((item) => ({
      itemId: item.id,
      action: DataSyncInboxAction.Link,
      token: item.token,
      targetLocalKey: exactCandidate(item)!.localKey,
    })),
    linkable.length,
  );
  const skippable = suggestions.filter((item) => allows(item, DataSyncInboxAction.Skip));

  add(
    "skipAll",
    skippable.map((item) => ({
      itemId: item.id,
      action: DataSyncInboxAction.Skip,
      token: item.token,
    })),
    skippable.length,
  );

  const deletions = items.filter(
    (item) =>
      item.type === DataSyncInboxItemType.DeletedThere ||
      item.type === DataSyncInboxItemType.ChildDeletedInUse,
  );
  const deletable = deletions.filter((item) => allows(item, DataSyncInboxAction.DeleteHere));

  add(
    "deleteAll",
    deletable.map((item) => ({
      itemId: item.id,
      action: DataSyncInboxAction.DeleteHere,
      token: item.token,
    })),
    deletable.length,
    {
      destructive: deletable.some((item) => isDestructive(item, DataSyncInboxAction.DeleteHere)),
    },
  );
  const keepable = deletions.filter((item) => allows(item, DataSyncInboxAction.KeepHereOnly));

  add(
    "keepAll",
    keepable.map((item) => ({
      itemId: item.id,
      action: DataSyncInboxAction.KeepHereOnly,
      token: item.token,
    })),
    keepable.length,
  );

  if (!wholeCards) return bulks;
  const conflicts = cards.filter((card) => card.items.some(isConflict));
  const whole = (card: InboxCardModel, remote?: string) =>
    card.items.map(
      (item): DataSyncResolveInput => ({
        itemId: item.id,
        action:
          remote && item.peerNodeId === remote
            ? DataSyncInboxAction.UseRemote
            : DataSyncInboxAction.KeepLocal,
        token: item.token,
      }),
    );

  add(
    "keepLocalAll",
    conflicts.flatMap((card) => whole(card)),
    conflicts.length,
  );
  for (const peer of peersOf(conflicts.flatMap((card) => card.items))) {
    const ofPeer = conflicts.filter((card) =>
      card.items.some((item) => item.peerNodeId === peer.nodeId),
    );

    add(
      "useRemoteAll",
      ofPeer.flatMap((card) => whole(card, peer.nodeId)),
      ofPeer.length,
      { peer },
    );
  }

  return bulks;
};

// ---- words ---------------------------------------------------------------------------------------

/**
 * The words of a button (`dataSync.inbox.action.*`): the action's own, or a variant where one
 * action means something else on another type — removing an option rather than a definition,
 * keeping a type rather than a value.
 */
export const actionKey = (type: DataSyncInboxItemType, action: DataSyncInboxAction) => {
  const child = type === DataSyncInboxItemType.ChildDeletedInUse;

  switch (action) {
    case DataSyncInboxAction.KeepLocal:
      return type === DataSyncInboxItemType.TypeChange ? "KeepLocalType" : "KeepLocal";
    case DataSyncInboxAction.DeleteHere:
      return child ? "DeleteHereChild" : "DeleteHere";
    case DataSyncInboxAction.KeepHereOnly:
      return child ? "KeepHereOnlyChild" : "KeepHereOnly";
    case DataSyncInboxAction.RestoreEverywhere:
      return child
        ? "RestoreEverywhereChild"
        : type === DataSyncInboxItemType.MassChildDeletion
          ? "RestoreEverywhereAll"
          : "RestoreEverywhere";
    case DataSyncInboxAction.ApplyAll:
      return type === DataSyncInboxItemType.LargeChange ? "ApplyAllLarge" : "ApplyAll";
    default:
      return DataSyncInboxActionLabel[action] ?? "Skip";
  }
};

/** Every variant {@link actionKey} can name beyond the actions' own labels. */
export const actionKeyVariants = [
  "KeepLocalType",
  "DeleteHereChild",
  "KeepHereOnlyChild",
  "RestoreEverywhereChild",
  "RestoreEverywhereAll",
  "ApplyAllLarge",
] as const;

/**
 * The top line of a card (`dataSync.inbox.type.*`): what happened and where. A rename of the
 * definition itself, and an identity conflict between records, read in their own words.
 */
export const headlineKey = (card: InboxCardModel) => {
  const first = card.items[0];

  if (isConflict(first)) {
    if (card.items.length === 1 && first.subjectPath === "name") return "FieldConflictName";

    return card.items.every((item) => item.type === DataSyncInboxItemType.ChildRenameConflict)
      ? "ChildRenameConflict"
      : "FieldConflict";
  }
  if (first.type === DataSyncInboxItemType.IdentityConflict && first.payload.records?.length)
    return "IdentityConflictRecords";

  return DataSyncInboxItemTypeLabel[first.type] ?? "FieldConflict";
};

export const headlineVariants = ["FieldConflictName", "IdentityConflictRecords"] as const;

// ---- closed items --------------------------------------------------------------------------------

/** How long closed items stay under "Recently resolved". */
export const RECENTLY_RESOLVED_DAYS = 7;

/** Items closed within the last seven days, newest first. */
export const recentlyResolved = (items: readonly Item[], now: number = Date.now()) =>
  items
    .filter((item) => {
      const since = millisecondsSince(item.closedAt, now);

      return since !== null && since <= RECENTLY_RESOLVED_DAYS * 24 * 60 * 60 * 1000;
    })
    .sort(
      (a, b) =>
        (millisecondsSince(a.closedAt, now) ?? 0) - (millisecondsSince(b.closedAt, now) ?? 0),
    );

/** How a closed item reads: "Resolved on X", "Resolved here", or why it no longer applies. */
export const closureKey = (item: Item) => {
  switch (item.closure) {
    case DataSyncInboxClosure.ResolvedElsewhere:
      return item.closedByName ? "resolvedOn" : "resolvedElsewhere";
    case DataSyncInboxClosure.ResolvedHere:
      return "resolvedHere";
    case DataSyncInboxClosure.LinkRemoved:
      return "linkRemoved";
    case DataSyncInboxClosure.LinkStopped:
      return "linkStopped";
    default:
      return "superseded";
  }
};
