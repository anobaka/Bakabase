import { describe, expect, it } from "vitest";

import { dataSyncKinds, failureCodesWithWords } from "../viewModels";
import { changeOps, scalarPaths } from "../components/ChangeList";
import { historyCountKeys, undoGroupOrder } from "../historyModels";
import { actionKeyVariants, headlineVariants } from "../inboxModels";

import {
  DataSyncDecisionErrorCode,
  DataSyncDecisionErrorCodeLabel,
  DataSyncHeldReason,
  DataSyncHeldReasonLabel,
  DataSyncHistoryKind,
  DataSyncHistoryKindLabel,
  DataSyncInboxAction,
  DataSyncInboxActionLabel,
  DataSyncInboxItemType,
  DataSyncInboxItemTypeLabel,
  DataSyncItemAction,
  DataSyncItemActionLabel,
  DataSyncItemOutcome,
  DataSyncItemOutcomeLabel,
  DataSyncNaturalMatch,
  DataSyncNaturalMatchLabel,
  DataSyncPauseReason,
  DataSyncPauseReasonLabel,
  DataSyncPeerErrorCode,
  DataSyncPeerErrorCodeLabel,
  DataSyncPlanItemReason,
  DataSyncPlanItemReasonLabel,
  DataSyncPlanItemType,
  DataSyncPlanItemTypeLabel,
  DataSyncPlanResolution,
  DataSyncPlanResolutionLabel,
  DataSyncProblemCode,
  DataSyncProblemCodeLabel,
  DataSyncUndoBlock,
  DataSyncUndoBlockLabel,
  DataSyncWarningCode,
  DataSyncWarningCodeLabel,
} from "@/sdk/constants";

/*
 * Every key the data sync page, its drawings and its status indicator can show exists in both
 * languages, with the same placeholders — and no copy quotes a device name.
 *
 * Keys are read out of the components' own source, so a string added without a translation
 * fails here instead of rendering as its key. Keys built at runtime are enumerated below from
 * the same enums and lists the components use.
 */

type Resources = Record<string, string>;

const merge = (modules: Record<string, Resources>): Resources =>
  Object.assign({}, ...Object.values(modules));

// The same files `i18n.ts` merges into one flat namespace per language.
const en = merge(
  import.meta.glob<Resources>("../../../locales/en/**/*.json", { eager: true, import: "default" }),
);
const cn = merge(
  import.meta.glob<Resources>("../../../locales/cn/**/*.json", { eager: true, import: "default" }),
);
const pageEn = import.meta.glob<Resources>("../../../locales/en/pages/dataSync.json", {
  eager: true,
  import: "default",
});
const pageCn = import.meta.glob<Resources>("../../../locales/cn/pages/dataSync.json", {
  eager: true,
  import: "default",
});

const sources = import.meta.glob<string>(["../**/*.ts", "../**/*.tsx", "!../__tests__/**"], {
  eager: true,
  query: "?raw",
  import: "default",
});

/** Namespaces data sync draws from; a quoted string under one of them is a key. */
const keyPattern =
  /["'`](dataSync(?:\.[A-Za-z0-9_]+)+|federation\.map(?:\.[A-Za-z0-9_]+)+|federation\.(?:mode|confirm|cancel|dismiss))["'`]/g;

const staticKeys = (source: string) => Array.from(source.matchAll(keyPattern), (match) => match[1]);

const labels = <T extends number>(
  enumeration: Record<string, string | number>,
  label: Record<T, string>,
) =>
  Object.values(enumeration)
    .filter((value): value is T => typeof value === "number")
    .map((value) => label[value]);

const dynamicKeys = [
  ...dataSyncKinds.map((kind) => `dataSync.kind.${kind}`),
  ...dataSyncKinds.map((kind) => `dataSync.diagram.count.${kind}`),
  ...(
    [
      "inStep",
      "syncing",
      "offline",
      "needsYou",
      "awaitingAccess",
      "awaitingReview",
      "waitingForPeerReview",
      "paused",
      "off",
      "readsHere",
      "notApproved",
      "updateNeeded",
      "accessLost",
      "restorePending",
      "failed",
    ] as const
  ).map((card) => `dataSync.diagram.card.${card}`),
  ...["off", "follow", "twoWay"].map((mode) => `dataSync.mode.${mode}`),
  ...["follow", "twoWay"].map((mode) => `dataSync.mode.short.${mode}`),
  ...["follow", "twoWay"].map((mode) => `dataSync.readers.mode.${mode}`),
  ...[
    "inStep",
    "needsYou",
    "awaitingReview",
    "waitingForPeerReview",
    "awaitingAccess",
    "paused",
    "offline",
    "failed",
  ].map((state) => `dataSync.readers.state.${state}`),
  ...labels(DataSyncProblemCode, DataSyncProblemCodeLabel).map(
    (code) => `dataSync.problem.${code}`,
  ),
  ...[...labels(DataSyncPeerErrorCode, DataSyncPeerErrorCodeLabel), ...failureCodesWithWords].map(
    (code) => `dataSync.peerError.${code}`,
  ),
  ...labels(DataSyncPauseReason, DataSyncPauseReasonLabel).map(
    (reason) => `dataSync.status.paused.${reason}`,
  ),
  "dataSync.status.paused.PeerResetRestored",
  ...[
    "needsYou",
    "localOnly",
    "detached",
    "heldAtSource",
    "tooLarge",
    "unreadable",
    "differs",
    "definitionOnly",
    "synced",
    "syncedFrom",
  ].map((badge) => `dataSync.entity.badge.${badge}`),
  ...["keepLocal", "detach", "rejoin", "definitionOnlyOn", "definitionOnlyOff"].map(
    (action) => `dataSync.entity.action.${action}`,
  ),
  ...["linked", "tooOld", "notSharing", "readable", "asks", "manageElsewhere"].map(
    (status) => `dataSync.wizard.candidate.${status}`,
  ),
  ...["follow", "twoWay", "copyOnce"].map((how) => `dataSync.wizard.explain.${how}`),

  // The first sync review.
  ...labels(DataSyncPlanItemType, DataSyncPlanItemTypeLabel).flatMap((type) => [
    `dataSync.plan.type.${type}`,
    `dataSync.plan.summary.${type}`,
  ]),
  ...labels(DataSyncPlanItemReason, DataSyncPlanItemReasonLabel).map(
    (reason) => `dataSync.plan.reason.${reason}`,
  ),
  ...labels(DataSyncHeldReason, DataSyncHeldReasonLabel).map(
    (held) => `dataSync.plan.held.${held}`,
  ),
  ...labels(DataSyncPlanResolution, DataSyncPlanResolutionLabel).map(
    (resolution) => `dataSync.plan.resolution.${resolution}`,
  ),
  ...labels(DataSyncNaturalMatch, DataSyncNaturalMatchLabel).flatMap((match) => [
    `dataSync.plan.match.${match}`,
    `dataSync.inbox.match.${match}`,
  ]),
  ...labels(DataSyncWarningCode, DataSyncWarningCodeLabel).map(
    (code) => `dataSync.plan.warning.${code}`,
  ),
  ...labels(DataSyncDecisionErrorCode, DataSyncDecisionErrorCodeLabel).map(
    (code) => `dataSync.decisionError.${code}`,
  ),
  ...scalarPaths.map((path) => `dataSync.plan.path.${path}`),
  ...[...changeOps, "other"].map((op) => `dataSync.plan.change.group.${op}`),
  ...["decisionsInvalid", "planChanged"].map((banner) => `dataSync.review.${banner}`),

  // Needs you.
  ...[...labels(DataSyncInboxItemType, DataSyncInboxItemTypeLabel), ...headlineVariants].map(
    (type) => `dataSync.inbox.type.${type}`,
  ),
  ...[
    ...labels(DataSyncInboxAction, DataSyncInboxActionLabel),
    ...actionKeyVariants,
    "PauseLink",
  ].map((action) => `dataSync.inbox.action.${action}`),
  ...["linkExact", "skipAll", "deleteAll", "keepAll", "keepLocalAll", "useRemoteAll"].map(
    (bulk) => `dataSync.inbox.bulk.${bulk}`,
  ),
  ...[
    "resolvedOn",
    "resolvedElsewhere",
    "resolvedHere",
    "superseded",
    "linkRemoved",
    "linkStopped",
  ].map((closure) => `dataSync.inbox.closure.${closure}`),

  // The history, undo, restore.
  ...labels(DataSyncHistoryKind, DataSyncHistoryKindLabel).map(
    (kind) => `dataSync.history.kind.${kind}`,
  ),
  ...historyCountKeys.map((count) => `dataSync.history.count.${count}`),
  ...labels(DataSyncItemAction, DataSyncItemActionLabel).map(
    (action) => `dataSync.history.action.${action}`,
  ),
  ...labels(DataSyncItemOutcome, DataSyncItemOutcomeLabel).map(
    (outcome) => `dataSync.history.outcome.${outcome}`,
  ),
  ...undoGroupOrder.map((group) => `dataSync.undo.group.${group}`),
  ...labels(DataSyncUndoBlock, DataSyncUndoBlockLabel).map(
    (block) => `dataSync.undo.blocked.${block}`,
  ),
  ...["own", "peer", "both"].map((evidence) => `dataSync.restore.evidence.${evidence}`),
];

/** The device map's words for data sync (spec §11.1), which live with data sync's own. */
const mapKeys = [
  ...["in", "out"].flatMap((direction) =>
    ["active", "pending"].map((status) => `federation.map.direction.sync.${direction}.${status}`),
  ),
  "federation.map.sync.mode.twoWay",
  "federation.map.sync.mode.follow",
  "federation.map.attention.sync.in",
  "federation.map.source.dataSync",
  // This device's counts in the map's details (map/DataSyncSelfSection).
  ...["receivesFrom", "readBy"].map((count) => `dataSync.map.self.${count}`),
  ...[
    "syncPaused",
    "syncFailed",
    "syncUpdateNeeded",
    "syncAccessLost",
    "syncNeedsYou",
    "syncNeedsYouThere",
    "syncRequestEnded",
  ].map((issue) => `federation.map.issue.${issue}`),
];

/** The status catalogue of spec §11.6, entry by entry. */
const catalogue = [
  "InStep",
  "Syncing",
  "FullReconciliation",
  "ReadBackFailed",
  "NeedsYou",
  "NeedsYouThere",
  "Offline",
  "AwaitingAccess",
  "AccessRejected",
  "AwaitingReview",
  "WaitingForPeerReview",
  "ReadBackDeclined",
  "MutualFollow",
  "PeerTooOld",
  "ThisTooOld",
  "AccessRevoked",
  "PeerRemoteAccessOff",
  "PeerRestorePending",
  "TooLarge",
  "Failed",
].map((entry) => `dataSync.status.${entry}`);

const placeholders = (text: string) =>
  Array.from(text.matchAll(/{{\s*([\w.]+)\s*}}/g), (match) => match[1]).sort();

describe("locales for data sync", () => {
  const keys = Array.from(
    new Set([
      ...Object.values(sources).flatMap(staticKeys),
      ...dynamicKeys,
      ...mapKeys,
      ...catalogue,
    ]),
  )
    // A prefix a key is built on is not a key of its own.
    .filter((key) => !key.endsWith("."))
    .sort();

  it("reads keys out of the components", () => {
    expect(Object.keys(sources).length).toBeGreaterThan(10);
    expect(keys).toEqual(
      expect.arrayContaining([
        "dataSync.request.claim",
        "dataSync.sharing.label",
        "dataSync.twoWay.consent",
        "dataSync.off.stillReads",
        "dataSync.manageElsewhere",
        "dataSync.notAvailable",
      ]),
    );
  });

  it.each(keys)("%s exists in English and Chinese with the same placeholders", (key) => {
    expect(en[key], `en: ${key}`).toEqual(expect.any(String));
    expect(cn[key], `cn: ${key}`).toEqual(expect.any(String));
    expect(en[key].trim(), `en: ${key}`).not.toBe("");
    expect(cn[key].trim(), `cn: ${key}`).not.toBe("");
    expect(placeholders(cn[key]), key).toEqual(placeholders(en[key]));
  });

  it("has the same keys in both languages", () => {
    const english = Object.values(pageEn)[0];
    const chinese = Object.values(pageCn)[0];

    expect(Object.keys(chinese).sort()).toEqual(Object.keys(english).sort());
  });

  it.each([
    ["English", Object.values(pageEn)[0]],
    ["Chinese", Object.values(pageCn)[0]],
  ])("never quotes a device name in %s", (_, resources: Resources) => {
    for (const [key, text] of Object.entries(resources)) {
      expect(text, key).not.toMatch(/[«“「『"']\s*{{\s*name\s*}}/);
      // An apostrophe after a name is its possessive ("{{name}}'s"), not a quote.
      expect(text, key).not.toMatch(/{{\s*name\s*}}\s*(?:[»”」』"]|'(?!s\b))/);
    }
  });

  it("says the feature by its name, and never by the retired ones", () => {
    const chinese = Object.values(pageCn)[0];

    expect(chinese["dataSync.title"]).toBe("数据同步");
    for (const [key, text] of Object.entries(chinese)) {
      expect(text, key).not.toContain("配置同步");
      expect(text, key).not.toContain("配置包");
      expect(text, key).not.toContain("分享给他人");
    }
  });

  it("gives the pages that list definitions their words, and says removing a device ends sync", () => {
    for (const key of ["customProperty.empty.syncHint", "customProperty.action.syncWithDevice"]) {
      expect(en[key], key).toEqual(expect.any(String));
      expect(cn[key], key).toEqual(expect.any(String));
    }
    // Hook H-dev-remove: removing a device ends definitions sync with it, both ways.
    expect(en["federation.devices.removeConfirm"]).toContain(
      "definitions sync with it ends in both directions",
    );
    expect(cn["federation.devices.removeConfirm"]).toContain("双向定义同步也会结束");
    expect(placeholders(cn["federation.devices.removeConfirm"])).toEqual(
      placeholders(en["federation.devices.removeConfirm"]),
    );
  });

  it("uses the spec's terms for the review, Needs you and the history", () => {
    expect(cn["dataSync.inbox.title"]).toBe("待你决定");
    expect(cn["dataSync.history.title"]).toBe("同步记录");
    expect(cn["dataSync.inbox.action.Detach"]).toBe("不再同步此项");
    expect(cn["dataSync.inbox.action.KeepHereOnlyChild"]).toBe("仅保留在本机");
    expect(cn["dataSync.plan.newerHere"]).toBe("本机较新");
    expect(en["dataSync.review.twoWayNote"]).toBe(
      "What you keep here, and any name you choose, is also sent to {{name}}.",
    );
    expect(en["dataSync.plan.skipHint"]).toBe(
      "Not synced with {{name}}. You can include it later.",
    );
  });

  it("uses the spec's exact words for approval and consent", () => {
    expect(en["dataSync.request.from"]).toBe("From {{address}}. Only approve your own devices.");
    expect(cn["dataSync.request.from"]).toBe("来自 {{address}}。只批准你自己的设备。");
    expect(en["dataSync.sharing.label"]).toBe(
      "Let devices I approve read this device's definitions",
    );
    expect(cn["dataSync.status.MutualFollow"]).toBe("你们互相接收，因此等同于双向保持一致");
  });
});
