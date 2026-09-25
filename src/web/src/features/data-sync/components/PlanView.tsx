import type { DataSyncPlan, DataSyncPlanItem } from "../api";
import type { ReviewDecision, ReviewDecisions } from "../reviewModels";
import type { DataSyncDecisionErrorCode } from "@/sdk/constants";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  bulkCreateSeparate,
  bulkLinkExact,
  bulkSkipPending,
  effectiveDecision,
  groupByType,
  isPending,
  pendingItems,
  planTypeOrder,
} from "../reviewModels";
import { orderKinds } from "../viewModels";

import PlanItemRow from "./PlanItemRow";
import { smallButtonClass } from "./common";

import {
  DataSyncPlanItemType,
  DataSyncPlanItemTypeLabel,
  DataSyncPlanResolution,
} from "@/sdk/constants";

/*
 * The plan of a first sync (v3.1 §10.5): the summary, the bulk bar, a tab per type of
 * definition, a filter chip per item type, and the rows — the ones that need the reader first.
 * Long plans are shown a page of rows at a time.
 */

/** Rows shown at first, and added by each "Show more". */
export const PLAN_PAGE = 100;

export interface PlanViewProps {
  plan: DataSyncPlan;
  decisions: ReviewDecisions;
  onChange: (decisions: ReviewDecisions) => void;
  sourceName: string;
  reviewId: string;
  disabled: boolean;
  /** Items the server refused a decision for, with why. */
  errors?: Map<string, DataSyncDecisionErrorCode>;
  onPlanChanged: () => void;
}

export default function PlanView({
  plan,
  decisions,
  onChange,
  sourceName,
  reviewId,
  disabled,
  errors,
  onPlanChanged,
}: PlanViewProps) {
  const { t } = useTranslation();
  const sections = useMemo(() => {
    const order = orderKinds(plan.kinds.map((section) => section.kind));

    return order.map((kind) => plan.kinds.find((section) => section.kind === kind)!);
  }, [plan]);
  const [kind, setKind] = useState(sections[0]?.kind);
  const [types, setTypes] = useState<DataSyncPlanItemType[]>(
    planTypeOrder.filter((type) => type !== DataSyncPlanItemType.Unchanged),
  );
  const [shown, setShown] = useState(PLAN_PAGE);
  const section = sections.find((item) => item.kind === kind) ?? sections[0];
  const groups = useMemo(() => groupByType(section?.items ?? []), [section]);
  // The plan's own order, which stays put as the reader decides.
  const rows = (section?.items ?? []).filter((item) => types.includes(item.type));
  const pending = pendingItems(plan, decisions);
  const separateFor = (item: DataSyncPlanItem) =>
    t("dataSync.plan.separateName", { name: item.incoming.name, source: sourceName });
  const canCreateSeparate = pending.some((item) =>
    item.allowedResolutions.includes(DataSyncPlanResolution.CreateSeparate),
  );
  const typeTotal = (type: DataSyncPlanItemType) =>
    plan.summary.counts
      .filter((count) => count.type === type)
      .reduce((sum, count) => sum + count.count, 0);

  const set = (item: DataSyncPlanItem, decision: ReviewDecision) =>
    onChange({ ...decisions, [item.itemId]: decision });

  return (
    <div className="space-y-3" data-testid="data-sync-plan">
      <p className="flex flex-wrap gap-x-3 gap-y-1 text-xs" data-testid="data-sync-plan-summary">
        {planTypeOrder
          .filter((type) => typeTotal(type) > 0)
          .map((type) => (
            <span key={type} data-type={DataSyncPlanItemTypeLabel[type]}>
              {t(`dataSync.plan.summary.${DataSyncPlanItemTypeLabel[type]}`, {
                count: typeTotal(type),
              })}
            </span>
          ))}
      </p>

      {(plan.summary.bulkLinkEligibleCount > 0 || pending.length > 0) && (
        <div className="flex flex-wrap gap-2" data-testid="data-sync-plan-bulk">
          {plan.summary.bulkLinkEligibleCount > 0 && (
            <button
              className={smallButtonClass}
              data-testid="data-sync-plan-link-exact"
              disabled={disabled}
              type="button"
              onClick={() => onChange(bulkLinkExact(plan, decisions))}
            >
              {t("dataSync.plan.bulk.linkExact", { count: plan.summary.bulkLinkEligibleCount })}
            </button>
          )}
          {canCreateSeparate && (
            <button
              className={smallButtonClass}
              data-testid="data-sync-plan-create-separate"
              disabled={disabled}
              type="button"
              onClick={() => onChange(bulkCreateSeparate(plan, decisions, separateFor))}
            >
              {t("dataSync.plan.bulk.createSeparate")}
            </button>
          )}
          {pending.length > 0 && (
            <button
              className={smallButtonClass}
              data-testid="data-sync-plan-skip-pending"
              disabled={disabled}
              type="button"
              onClick={() => onChange(bulkSkipPending(plan, decisions))}
            >
              {t("dataSync.plan.bulk.skipPending", { count: pending.length })}
            </button>
          )}
        </div>
      )}

      {sections.length > 1 && (
        <div aria-label={t("dataSync.entity.kinds")} className="flex gap-1" role="tablist">
          {sections.map((item) => (
            <button
              key={item.kind}
              aria-selected={item.kind === section?.kind}
              className={`rounded-md px-3 py-1.5 text-sm ${
                item.kind === section?.kind
                  ? "bg-primary/10 font-medium text-primary-700"
                  : "hover:bg-default-100"
              }`}
              role="tab"
              type="button"
              onClick={() => {
                setKind(item.kind);
                setShown(PLAN_PAGE);
              }}
            >
              {t(`dataSync.kind.${item.kind}`, { defaultValue: item.kind })} ({item.items.length})
            </button>
          ))}
        </div>
      )}

      {groups.size > 0 && (
        <div
          aria-label={t("dataSync.plan.typeFilter")}
          className="flex flex-wrap gap-1.5"
          role="group"
        >
          {Array.from(groups, ([type, items]) => {
            const on = types.includes(type);

            return (
              <button
                key={type}
                aria-pressed={on}
                className={`rounded-full border px-2.5 py-0.5 text-xs ${
                  on ? "border-primary bg-primary/10 text-primary-700" : "border-default-200"
                }`}
                data-testid="data-sync-plan-type-chip"
                data-type={DataSyncPlanItemTypeLabel[type]}
                type="button"
                onClick={() =>
                  setTypes((current) =>
                    on ? current.filter((item) => item !== type) : [...current, type],
                  )
                }
              >
                {t(`dataSync.plan.summary.${DataSyncPlanItemTypeLabel[type]}`, {
                  count: items.length,
                })}
              </button>
            );
          })}
        </div>
      )}

      {section && !section.supported && (
        <p className="text-xs text-default-500">{t("dataSync.plan.kindUnsupported")}</p>
      )}
      {section && section.localOnlyCount > 0 && (
        <p className="text-xs text-default-500">
          {t("dataSync.plan.localOnly", { count: section.localOnlyCount })}
        </p>
      )}

      {rows.length === 0 ? (
        <p className="text-sm text-default-500">{t("dataSync.plan.noRows")}</p>
      ) : (
        <ul className="divide-y divide-default-100">
          {rows.slice(0, shown).map((item) => (
            <PlanItemRow
              key={item.itemId}
              decision={effectiveDecision(item, decisions)}
              disabled={disabled}
              error={errors?.get(item.itemId)}
              item={item}
              pending={isPending(item, decisions)}
              planId={plan.planId}
              reviewId={reviewId}
              separateName={separateFor(item)}
              sourceName={sourceName}
              onChange={(decision) => set(item, decision)}
              onPlanChanged={onPlanChanged}
            />
          ))}
        </ul>
      )}
      {rows.length > shown && (
        <button
          className={smallButtonClass}
          type="button"
          onClick={() => setShown((current) => current + PLAN_PAGE)}
        >
          {t("dataSync.plan.showMoreRows", { count: Math.min(PLAN_PAGE, rows.length - shown) })}
        </button>
      )}
    </div>
  );
}
