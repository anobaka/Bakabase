import type { TFunction } from "i18next";
import type { DataSyncFieldChange, DataSyncPlanItem, DataSyncPlanWarning } from "../api";
import type { DecisionChanges, ReviewDecision } from "../reviewModels";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi } from "../api";
import { changeGroup, closeExclusions, foldApplies, mergeChangePage } from "../reviewModels";

import DisplayValue, { displayText } from "./DisplayValue";
import { DataSyncErrorNotice, linkButtonClass } from "./common";

import { DataSyncFieldChangeKind, DataSyncProblemCode } from "@/sdk/constants";

/*
 * What a review item would change here, one tick per change: every change starts ticked, since
 * the other device's value wins on a first sync; unticking keeps this device's. Option changes
 * come in groups, each with a tick of its own that takes changes not sent inline with it.
 */

/** Paths the change list names in words; anything else is shown as the path itself. */
export const scalarPaths = [
  "name",
  "type",
  "ignoreCase",
  "childrenLocal",
  "defaultValue",
  "orderKey",
  "settings.precision",
  "settings.showProgressBar",
  "settings.maxValue",
  "settings.layout",
  "settings.valueIsSingleton",
] as const;

export const pathLabel = (t: TFunction, path: string) =>
  (scalarPaths as readonly string[]).includes(path) ? t(`dataSync.plan.path.${path}`) : path;

/** The operations option changes are grouped by, as their ids name them. */
export const changeOps = ["add", "rename", "recolor", "remove", "move"] as const;

const opOf = (group: string) => group.split(":")[1] ?? "";

const groupLabel = (t: TFunction, group: string, count: number) => {
  const op = opOf(group);

  return (changeOps as readonly string[]).includes(op)
    ? t(`dataSync.plan.change.group.${op}`, { count })
    : t("dataSync.plan.change.group.other", { count });
};

/** Changes that are about one option or extension, rather than a field of the definition. */
const isChildChange = (change: DataSyncFieldChange) => !!changeGroup(change.changeId);

export interface ChangeListProps {
  item: DataSyncPlanItem;
  decision?: ReviewDecision;
  target: DecisionChanges;
  reviewId: string;
  planId: string;
  disabled: boolean;
  onToggle: (changeId: string) => void;
  onToggleGroup: (group: string) => void;
  /** The plan moved on: the review is read again. */
  onPlanChanged: () => void;
}

export default function ChangeList({
  item,
  decision,
  target,
  reviewId,
  planId,
  disabled,
  onToggle,
  onToggleGroup,
  onPlanChanged,
}: ChangeListProps) {
  const { t } = useTranslation();
  const [held, setHeld] = useState<{
    changes: DataSyncFieldChange[];
    warnings: DataSyncPlanWarning[];
  }>({ changes: target.changes, warnings: target.warnings });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<Error>();
  const candidateKey = target.candidate?.localKey;

  // Another target, or a fresh plan: start again from what came inline.
  useEffect(() => {
    setHeld({ changes: target.changes, warnings: target.warnings });
    setError(undefined);
  }, [planId, item.itemId, candidateKey, target.changes, target.warnings]);

  const excluded = decision?.excludedChangeIds ?? [];
  const closed = useMemo(() => closeExclusions(held.changes, excluded), [held.changes, excluded]);
  const excludedGroups = new Set(
    excluded.filter((id) => id.endsWith(":*")).map((id) => id.slice(0, -2)),
  );
  const ignoreCase = held.changes.find((change) => change.path === "ignoreCase");
  const ignoreCaseTicked = !!ignoreCase && !closed.has(ignoreCase.changeId);
  const scalars = held.changes.filter((change) => !isChildChange(change));
  const groups = new Map<string, DataSyncFieldChange[]>();

  for (const change of held.changes.filter(isChildChange)) {
    const group = changeGroup(change.changeId)!;

    groups.set(group, [...(groups.get(group) ?? []), change]);
  }
  const more = target.truncated && held.changes.length < target.counts.total;

  const showMore = async () => {
    setLoading(true);
    setError(undefined);
    try {
      const page = await dataSyncApi.reviewChanges(reviewId, {
        planId,
        itemId: item.itemId,
        candidate: candidateKey,
        skip: held.changes.length,
        take: 200,
      });

      if (page?.problem?.code === DataSyncProblemCode.PlanChanged) {
        onPlanChanged();

        return;
      }
      if (page) setHeld((current) => mergeChangePage(current.changes, current.warnings, page));
    } catch (cause) {
      setError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      setLoading(false);
    }
  };

  const foldOf = (change: DataSyncFieldChange) =>
    change.kind === DataSyncFieldChangeKind.AddChild
      ? held.warnings.find(
          (warning) =>
            warning.changeId === change.changeId && foldApplies(warning, ignoreCaseTicked),
        )
      : undefined;
  const parentExcluded = (change: DataSyncFieldChange) =>
    !!change.dependsOnChangeId &&
    closed.has(change.changeId) &&
    !excluded.includes(change.changeId);

  const row = (change: DataSyncFieldChange, label?: string) => {
    const ticked = !closed.has(change.changeId);
    const byParent = parentExcluded(change);
    const inGroup = excludedGroups.has(changeGroup(change.changeId) ?? "");
    const fold = foldOf(change);
    const rename = change.kind === DataSyncFieldChangeKind.RenameChild;

    return (
      <li
        key={change.changeId}
        className="flex items-start gap-2 py-1 text-xs"
        data-change={change.changeId}
        data-testid="data-sync-change"
      >
        <input
          aria-label={`${label ? `${label}: ` : ""}${
            change.from ? `${displayText(t, change.from)} → ` : ""
          }${displayText(t, change.to)}`}
          checked={ticked}
          className="mt-0.5"
          disabled={disabled || byParent || inGroup}
          type="checkbox"
          onChange={() => onToggle(change.changeId)}
        />
        <div className="min-w-0 flex-1 space-y-0.5">
          <div className="flex flex-wrap items-center gap-1.5">
            {label && <span className="font-medium">{label}</span>}
            {change.from && (
              <>
                <DisplayValue value={change.from} />
                <span aria-hidden>→</span>
              </>
            )}
            <DisplayValue value={change.to} />
          </div>
          {fold && (
            <p className="text-default-500" data-testid="data-sync-change-fold">
              {t("dataSync.plan.change.mergesInto", {
                into: fold.args?.intoLabel ?? fold.args?.into ?? "",
              })}
            </p>
          )}
          {rename && change.inUseCount != null && (
            <p
              className="text-default-500"
              data-testid="data-sync-change-in-use"
              title={t("dataSync.plan.change.renameAffectsLabelWriters")}
            >
              {t("dataSync.plan.change.inUse", { count: change.inUseCount })}
            </p>
          )}
          {byParent && (
            <p className="text-default-500">{t("dataSync.plan.change.parentUnticked")}</p>
          )}
        </div>
      </li>
    );
  };

  return (
    <div className="space-y-2" data-testid="data-sync-change-list">
      {scalars.length > 0 && (
        <ul className="divide-y divide-default-100">
          {scalars.map((change) => row(change, pathLabel(t, change.path)))}
        </ul>
      )}
      {Array.from(groups, ([group, changes]) => {
        const off = excludedGroups.has(group);
        const count = target.truncated ? undefined : changes.length;

        return (
          <div key={group} className="rounded-lg border border-default-200 p-2" data-group={group}>
            <label className="flex items-center gap-2 text-xs font-medium">
              <input
                checked={!off}
                data-testid="data-sync-change-group"
                disabled={disabled}
                type="checkbox"
                onChange={() => onToggleGroup(group)}
              />
              {groupLabel(t, group, count ?? groupTotal(group, target))}
            </label>
            <ul className="ml-5 mt-1 divide-y divide-default-100">
              {changes.map((change) => row(change))}
            </ul>
          </div>
        );
      })}
      {(target.counts.total > 0 || item.unchangedChildren > 0 || item.localOnlyChildren > 0) && (
        <p className="text-xs text-default-500">
          {[
            item.unchangedChildren > 0
              ? t("dataSync.plan.change.same", { count: item.unchangedChildren })
              : "",
            item.localOnlyChildren > 0
              ? t("dataSync.plan.change.onlyHere", { count: item.localOnlyChildren })
              : "",
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>
      )}
      <DataSyncErrorNotice error={error} />
      {more && (
        <button
          className={linkButtonClass}
          data-testid="data-sync-change-more"
          disabled={loading}
          type="button"
          onClick={() => void showMore()}
        >
          {loading
            ? t("dataSync.loading")
            : t("dataSync.plan.change.showMore", {
                count: target.counts.total - held.changes.length,
              })}
        </button>
      )}
    </div>
  );
}

/** How many changes of a group there are in all, when not all came inline. */
const groupTotal = (group: string, target: DecisionChanges) => {
  const op = opOf(group);

  if (op === "add") return target.counts.add;
  if (op === "rename") return target.counts.rename;
  if (op === "recolor") return target.counts.recolor;

  return target.changes.filter((change) => changeGroup(change.changeId) === group).length;
};
