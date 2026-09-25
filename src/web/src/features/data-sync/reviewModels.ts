import type {
  DataSyncChangePage,
  DataSyncFieldChange,
  DataSyncPlan,
  DataSyncPlanCandidate,
  DataSyncPlanDecision,
  DataSyncPlanItem,
  DataSyncPlanWarning,
} from "./api";

import { DataSyncPlanItemType, DataSyncPlanResolution, DataSyncWarningCode } from "@/sdk/constants";

/*
 * The first-sync review's rules (v3.1 §7.6–§7.8, §10.5), pure: what each item is decided as
 * before anyone touches it, what the bulk bar does, which changes an untick takes with it, what
 * Apply sends and what its button says. The server checks the same rules again; these only make
 * sure the screen never offers what it would refuse.
 */

/** What the reader chose for one item. */
export interface ReviewDecision {
  resolution: DataSyncPlanResolution;
  /** Link and Update only: the definition here it goes to. */
  targetLocalKey?: string;
  /** Create separate only. */
  newName?: string;
  /** Concrete change ids, or a whole group as `"<prefix>:*"` (v3.1 §7.4). */
  excludedChangeIds: string[];
}

export type ReviewDecisions = Record<string, ReviewDecision>;

export const planItems = (plan?: DataSyncPlan): DataSyncPlanItem[] =>
  plan ? plan.kinds.flatMap((section) => section.items) : [];

/** Held items, and items that allow nothing, take no decision: they never block Apply. */
export const takesDecision = (item: DataSyncPlanItem) =>
  item.type !== DataSyncPlanItemType.Held && item.allowedResolutions.length > 0;

const targetsLocal = (resolution: DataSyncPlanResolution) =>
  resolution === DataSyncPlanResolution.Link || resolution === DataSyncPlanResolution.Update;

/**
 * The item's default made explicit: its default resolution (an Unchanged item is an Update
 * that records keys), with the default target and nothing excluded. None where it has none.
 */
export const defaultDecision = (item: DataSyncPlanItem): ReviewDecision | undefined => {
  if (!takesDecision(item)) return undefined;
  const resolution =
    item.type === DataSyncPlanItemType.Unchanged
      ? DataSyncPlanResolution.Update
      : item.defaultResolution;

  if (resolution == null) return undefined;

  return {
    resolution,
    targetLocalKey: targetsLocal(resolution)
      ? (item.defaultTargetLocalKey ?? item.local?.localKey ?? undefined)
      : undefined,
    excludedChangeIds: [],
  };
};

/**
 * What the review starts with: every item that needs no confirmation gets its default, made
 * explicit. Items that need one — every Link included — stay pending until the reader decides.
 */
export const initialDecisions = (plan?: DataSyncPlan): ReviewDecisions => {
  const decisions: ReviewDecisions = {};

  for (const item of planItems(plan)) {
    if (!takesDecision(item) || item.requiresConfirmation) continue;
    const decision = defaultDecision(item);

    if (decision) decisions[item.itemId] = decision;
  }

  return decisions;
};

/** The decision an item goes with: the reader's, else its default where it needs no confirmation. */
export const effectiveDecision = (
  item: DataSyncPlanItem,
  decisions: ReviewDecisions,
): ReviewDecision | undefined =>
  decisions[item.itemId] ?? (item.requiresConfirmation ? undefined : defaultDecision(item));

/** Waits for the reader: takes a decision and has none it can go with. */
export const isPending = (item: DataSyncPlanItem, decisions: ReviewDecisions) =>
  takesDecision(item) && !effectiveDecision(item, decisions);

export const pendingItems = (plan: DataSyncPlan | undefined, decisions: ReviewDecisions) =>
  planItems(plan).filter((item) => isPending(item, decisions));

// ---- the bulk bar --------------------------------------------------------------------------------

/** "Link all N exact matches": every eligible item linked to its one candidate. */
export const bulkLinkExact = (plan: DataSyncPlan | undefined, decisions: ReviewDecisions) => {
  const next = { ...decisions };

  for (const item of planItems(plan)) {
    if (!item.bulkLinkEligible || !item.defaultTargetLocalKey) continue;
    if (!item.allowedResolutions.includes(DataSyncPlanResolution.Link)) continue;
    next[item.itemId] = {
      resolution: DataSyncPlanResolution.Link,
      targetLocalKey: item.defaultTargetLocalKey,
      excludedChangeIds: [],
    };
  }

  return next;
};

/** "Create remaining as separate": every pending item that allows it, with the proposed name. */
export const bulkCreateSeparate = (
  plan: DataSyncPlan | undefined,
  decisions: ReviewDecisions,
  nameFor: (item: DataSyncPlanItem) => string,
) => {
  const next = { ...decisions };

  for (const item of pendingItems(plan, decisions)) {
    if (!item.allowedResolutions.includes(DataSyncPlanResolution.CreateSeparate)) continue;
    next[item.itemId] = {
      resolution: DataSyncPlanResolution.CreateSeparate,
      newName: nameFor(item),
      excludedChangeIds: [],
    };
  }

  return next;
};

/** "Skip all pending". */
export const bulkSkipPending = (plan: DataSyncPlan | undefined, decisions: ReviewDecisions) => {
  const next = { ...decisions };

  for (const item of pendingItems(plan, decisions)) {
    if (!item.allowedResolutions.includes(DataSyncPlanResolution.Skip)) continue;
    next[item.itemId] = { resolution: DataSyncPlanResolution.Skip, excludedChangeIds: [] };
  }

  return next;
};

// ---- field changes -------------------------------------------------------------------------------

/** The group a change id belongs to (`tag:add` for `tag:add:{uuid}`); none for scalar changes. */
export const changeGroup = (changeId: string) => {
  const parts = changeId.split(":");

  return parts.length >= 3 ? `${parts[0]}:${parts[1]}` : undefined;
};

/** The id that excludes a whole group, as the server expands it. */
export const groupExclusion = (group: string) => `${group}:*`;

/**
 * The inline changes an exclusion list takes out: those named, those of an excluded group, and
 * — closed over `DependsOnChangeId` — every change whose parent is taken out (a node's add under
 * a parent that is not added).
 */
export const closeExclusions = (
  changes: readonly DataSyncFieldChange[],
  excluded: readonly string[],
): Set<string> => {
  const named = new Set(excluded);
  const byId = new Map(changes.map((change) => [change.changeId, change]));
  const memo = new Map<string, boolean>();
  const excludedId = (changeId: string, seen: Set<string>): boolean => {
    const known = memo.get(changeId);

    if (known !== undefined) return known;
    if (seen.has(changeId)) return false;
    seen.add(changeId);
    const group = changeGroup(changeId);
    let out = named.has(changeId) || (!!group && named.has(groupExclusion(group)));

    if (!out) {
      const parent = byId.get(changeId)?.dependsOnChangeId;

      out = !!parent && excludedId(parent, seen);
    }
    memo.set(changeId, out);

    return out;
  };

  return new Set(
    changes.filter((change) => excludedId(change.changeId, new Set())).map((c) => c.changeId),
  );
};

/** Ticks or unticks one change. */
export const toggleChange = (decision: ReviewDecision, changeId: string): ReviewDecision => {
  const excluded = decision.excludedChangeIds.includes(changeId)
    ? decision.excludedChangeIds.filter((id) => id !== changeId)
    : [...decision.excludedChangeIds, changeId];

  return { ...decision, excludedChangeIds: excluded };
};

/**
 * Ticks or unticks a whole group. Unticking it replaces the group's single exclusions by the
 * group id, which also covers changes not sent inline.
 */
export const toggleGroup = (decision: ReviewDecision, group: string): ReviewDecision => {
  const id = groupExclusion(group);

  if (decision.excludedChangeIds.includes(id))
    return {
      ...decision,
      excludedChangeIds: decision.excludedChangeIds.filter((excluded) => excluded !== id),
    };

  return {
    ...decision,
    excludedChangeIds: [
      ...decision.excludedChangeIds.filter((excluded) => changeGroup(excluded) !== group),
      id,
    ],
  };
};

/** The candidate a decision goes to, if its target is one of the item's candidates. */
export const chosenCandidate = (
  item: DataSyncPlanItem,
  decision?: ReviewDecision,
): DataSyncPlanCandidate | undefined => {
  const target = decision?.targetLocalKey ?? (decision ? undefined : item.defaultTargetLocalKey);

  return target ? item.candidates.find((candidate) => candidate.localKey === target) : undefined;
};

/** What a decision would change: the item's own changes, or the chosen candidate's. */
export interface DecisionChanges {
  changes: DataSyncFieldChange[];
  counts: DataSyncPlanItem["changeCounts"];
  truncated: boolean;
  warnings: DataSyncPlanWarning[];
  recordsNewKeys: boolean;
  /** Set when the changes are a candidate's: the changes endpoint needs it. */
  candidate?: DataSyncPlanCandidate;
}

export const decisionChanges = (
  item: DataSyncPlanItem,
  decision?: ReviewDecision,
): DecisionChanges | undefined => {
  const resolution = decision?.resolution;

  if (resolution !== undefined && !targetsLocal(resolution)) return undefined;
  const candidate = chosenCandidate(item, decision);

  if (candidate)
    return {
      changes: candidate.changes,
      counts: candidate.changeCounts,
      truncated: candidate.changesTruncated,
      warnings: candidate.warnings,
      recordsNewKeys: candidate.recordsNewKeys,
      candidate,
    };
  if (!item.local) return undefined;

  return {
    changes: item.changes,
    counts: item.changeCounts,
    truncated: item.changesTruncated,
    warnings: item.warnings,
    recordsNewKeys: item.recordsNewKeys,
  };
};

/** How many changes a group stands for, counting those not sent inline too. */
const groupCount = (group: string, target: DecisionChanges) => {
  if (!target.truncated)
    return target.changes.filter((change) => changeGroup(change.changeId) === group).length;
  const op = group.split(":")[1];

  if (op === "add") return target.counts.add;
  if (op === "rename") return target.counts.rename;
  if (op === "recolor") return target.counts.recolor;

  return target.changes.filter((change) => changeGroup(change.changeId) === group).length;
};

/** The changes a decision accepts: all of them, less what it excludes. */
export const acceptedChanges = (target: DecisionChanges, excluded: readonly string[]) => {
  const groups = new Set(excluded.filter((id) => id.endsWith(":*")).map((id) => id.slice(0, -2)));
  let accepted = target.counts.total;

  for (const group of groups) accepted -= groupCount(group, target);
  for (const changeId of closeExclusions(target.changes, excluded)) {
    const group = changeGroup(changeId);

    if (!group || !groups.has(group)) accepted -= 1;
  }

  return Math.max(0, accepted);
};

/** What the footer says: how many changes Apply writes, or that it has nothing to write (N9). */
export interface FooterCounts {
  created: number;
  linked: number;
  /** Accepted field changes. */
  changes: number;
  /** Updates that change nothing but record keys: they still write. */
  keyOnly: number;
  pending: number;
  total: number;
  nothingToApply: boolean;
}

export const footerCounts = (
  plan: DataSyncPlan | undefined,
  decisions: ReviewDecisions,
): FooterCounts => {
  const counts = { created: 0, linked: 0, changes: 0, keyOnly: 0, pending: 0 };

  for (const item of planItems(plan)) {
    if (!takesDecision(item)) continue;
    const decision = effectiveDecision(item, decisions);

    if (!decision) {
      counts.pending += 1;
      continue;
    }
    switch (decision.resolution) {
      case DataSyncPlanResolution.Create:
      case DataSyncPlanResolution.CreateSeparate:
        counts.created += 1;
        break;
      case DataSyncPlanResolution.Link: {
        const target = decisionChanges(item, decision);

        counts.linked += 1;
        if (target) counts.changes += acceptedChanges(target, decision.excludedChangeIds);
        break;
      }
      case DataSyncPlanResolution.Update: {
        const target = decisionChanges(item, decision);
        const accepted = target ? acceptedChanges(target, decision.excludedChangeIds) : 0;

        counts.changes += accepted;
        if (accepted === 0 && target?.recordsNewKeys) counts.keyOnly += 1;
        break;
      }
      default:
        break;
    }
  }
  const total = counts.created + counts.linked + counts.changes + counts.keyOnly;

  return { ...counts, total, nothingToApply: total === 0 };
};

// ---- what Apply sends ----------------------------------------------------------------------------

/**
 * One decision for every item that is not Held (v3.1 B1): the reader's, or the default made
 * explicit, each echoing the token of what was shown — the chosen candidate's when it goes to a
 * candidate, the item's otherwise. Held items get none; an item still pending gets none either
 * (Apply is not offered then, and the server would say so).
 */
export const toApplyInput = (
  plan: DataSyncPlan | undefined,
  decisions: ReviewDecisions,
): DataSyncPlanDecision[] =>
  planItems(plan).flatMap((item) => {
    if (!takesDecision(item)) return [];
    const decision = effectiveDecision(item, decisions);

    if (!decision) return [];
    const needsTarget = targetsLocal(decision.resolution);
    const target = needsTarget
      ? (decision.targetLocalKey ?? item.defaultTargetLocalKey ?? item.local?.localKey)
      : undefined;
    const candidate = target
      ? item.candidates.find((option) => option.localKey === target)
      : undefined;

    return [
      {
        itemId: item.itemId,
        resolution: decision.resolution,
        targetLocalKey: target ?? undefined,
        newName:
          decision.resolution === DataSyncPlanResolution.CreateSeparate
            ? decision.newName
            : undefined,
        excludedChangeIds: needsTarget ? decision.excludedChangeIds : [],
        reviewToken: candidate?.reviewToken ?? item.reviewToken,
      },
    ];
  });

/**
 * After `DecisionsInvalid` the server's fresh plan replaces the one on screen. Decisions are
 * kept where what they were made on still reads the same (the same token); the rest are dropped
 * and named, so the screen can say what changed.
 */
export const rebaseDecisions = (
  previous: DataSyncPlan | undefined,
  next: DataSyncPlan | undefined,
  decisions: ReviewDecisions,
): { decisions: ReviewDecisions; dropped: string[] } => {
  const before = new Map(planItems(previous).map((item) => [item.itemId, item]));
  const kept: ReviewDecisions = initialDecisions(next);
  const dropped: string[] = [];

  for (const item of planItems(next)) {
    const decision = decisions[item.itemId];
    const old = before.get(item.itemId);

    if (!decision || !old) continue;
    const tokenOf = (of: DataSyncPlanItem) =>
      chosenCandidate(of, decision)?.reviewToken ?? of.reviewToken;
    const sameTarget =
      !decision.targetLocalKey ||
      item.candidates.some((candidate) => candidate.localKey === decision.targetLocalKey) ||
      item.local?.localKey === decision.targetLocalKey;

    if (
      sameTarget &&
      tokenOf(old) === tokenOf(item) &&
      item.allowedResolutions.includes(decision.resolution)
    )
      kept[item.itemId] = decision;
    else dropped.push(item.itemId);
  }

  return { decisions: kept, dropped };
};

// ---- rows ----------------------------------------------------------------------------------------

/** The order the type filter lists types in: what needs the reader first. */
export const planTypeOrder: DataSyncPlanItemType[] = [
  DataSyncPlanItemType.NeedsDecision,
  DataSyncPlanItemType.Link,
  DataSyncPlanItemType.Create,
  DataSyncPlanItemType.Update,
  DataSyncPlanItemType.Held,
  DataSyncPlanItemType.Unchanged,
];

/** Items by type, in {@link planTypeOrder}; types with no item are left out. */
export const groupByType = (items: readonly DataSyncPlanItem[]) => {
  const groups = new Map<DataSyncPlanItemType, DataSyncPlanItem[]>();

  for (const type of planTypeOrder) {
    const ofType = items.filter((item) => item.type === type);

    if (ofType.length) groups.set(type, ofType);
  }

  return groups;
};

/**
 * Whether a folding warning applies under the tick state of the item's `ignoreCase` change: an
 * incoming option that merges into one here only when the comparer that folds it is the one
 * that will be in force (v3.1 §3.3.2).
 */
export const foldApplies = (warning: DataSyncPlanWarning, ignoreCaseTicked: boolean) => {
  if (warning.code !== DataSyncWarningCode.OptionLabelConflict) return false;
  const when = warning.args?.when;

  if (when === "withIgnoreCaseChange") return ignoreCaseTicked;
  if (when === "withoutIgnoreCaseChange") return !ignoreCaseTicked;

  return true;
};

/** Merges a page of changes into what the row holds, by change id, in the server's order. */
export const mergeChangePage = (
  held: readonly DataSyncFieldChange[],
  heldWarnings: readonly DataSyncPlanWarning[],
  page: DataSyncChangePage,
) => {
  const seen = new Set(held.map((change) => change.changeId));
  const warningKey = (warning: DataSyncPlanWarning) => `${warning.code}/${warning.changeId ?? ""}`;
  const seenWarnings = new Set(heldWarnings.map(warningKey));

  return {
    changes: [...held, ...page.changes.filter((change) => !seen.has(change.changeId))],
    warnings: [
      ...heldWarnings,
      ...page.warnings.filter((warning) => !seenWarnings.has(warningKey(warning))),
    ],
  };
};
