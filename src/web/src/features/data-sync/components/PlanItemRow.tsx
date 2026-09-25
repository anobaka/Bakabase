import type { DataSyncPlanItem } from "../api";
import type { ReviewDecision } from "../reviewModels";

import { useId, useState } from "react";
import { useTranslation } from "react-i18next";

import { chosenCandidate, decisionChanges, toggleChange, toggleGroup } from "../reviewModels";

import ChangeList from "./ChangeList";
import { fieldClass, linkButtonClass, syncText } from "./common";

import {
  DataSyncDecisionErrorCode,
  DataSyncDecisionErrorCodeLabel,
  DataSyncHeldReasonLabel,
  DataSyncNaturalMatchLabel,
  DataSyncPlanItemReason,
  DataSyncPlanItemReasonLabel,
  DataSyncPlanItemType,
  DataSyncPlanItemTypeLabel,
  DataSyncPlanResolution,
  DataSyncPlanResolutionLabel,
  DataSyncWarningCodeLabel,
} from "@/sdk/constants";

/*
 * One definition of the review: what it is, what the plan proposes, and the reader's choice —
 * the resolution, the definition here it goes to, the name of a separate copy — with what it
 * would change underneath, one tick per change.
 */

export interface PlanItemRowProps {
  item: DataSyncPlanItem;
  /** The decision it goes with: the reader's, or its default. */
  decision?: ReviewDecision;
  pending: boolean;
  /** The device the review reads, by name. */
  sourceName: string;
  /** Proposed for a separate copy: "{name} ({source})". */
  separateName: string;
  reviewId: string;
  planId: string;
  disabled: boolean;
  error?: DataSyncDecisionErrorCode;
  onChange: (decision: ReviewDecision) => void;
  onPlanChanged: () => void;
}

const badgeClass: Record<DataSyncPlanItemType, string> = {
  [DataSyncPlanItemType.Create]: "bg-success/10 text-success-700 dark:text-success",
  [DataSyncPlanItemType.Update]: "bg-primary/10 text-primary",
  [DataSyncPlanItemType.Unchanged]: "bg-default-100 text-default-500",
  [DataSyncPlanItemType.Link]: `bg-secondary/10 ${syncText}`,
  [DataSyncPlanItemType.NeedsDecision]: "bg-warning/10 text-warning-700 dark:text-warning",
  [DataSyncPlanItemType.Held]: "bg-default-100 text-default-500",
};

export default function PlanItemRow({
  item,
  decision,
  pending,
  sourceName,
  separateName,
  reviewId,
  planId,
  disabled,
  error,
  onChange,
  onPlanChanged,
}: PlanItemRowProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const pickerId = useId();
  const held = item.type === DataSyncPlanItemType.Held;
  const typeName = DataSyncPlanItemTypeLabel[item.type];
  const candidate = chosenCandidate(item, decision);
  const target = decisionChanges(item, decision);
  const resolution = decision?.resolution;
  const choosesTarget =
    (resolution === DataSyncPlanResolution.Link || resolution === DataSyncPlanResolution.Update) &&
    item.candidates.length > 0;
  const hasDetails =
    !!target &&
    (target.counts.total > 0 || item.unchangedChildren > 0 || item.localOnlyChildren > 0);
  const itemWarnings = item.warnings.filter((warning) => !warning.changeId);

  const choose = (next: DataSyncPlanResolution) => {
    const targets = next === DataSyncPlanResolution.Link || next === DataSyncPlanResolution.Update;

    onChange({
      resolution: next,
      targetLocalKey: targets
        ? (decision?.targetLocalKey ??
          item.defaultTargetLocalKey ??
          item.local?.localKey ??
          item.candidates[0]?.localKey)
        : undefined,
      newName:
        next === DataSyncPlanResolution.CreateSeparate
          ? (decision?.newName ?? separateName)
          : undefined,
      excludedChangeIds: [],
    });
  };

  return (
    <li
      className="space-y-2 py-3"
      data-item-id={item.itemId}
      data-pending={pending || undefined}
      data-testid="data-sync-plan-item"
      data-type={typeName}
    >
      <div className="flex flex-wrap items-start gap-2">
        <div className="min-w-0 flex-1">
          <p className="break-words text-sm font-medium [overflow-wrap:anywhere]">
            {item.incoming.name}
          </p>
          {item.incoming.subtype && (
            <p className="text-xs text-default-500">
              {t(`PropertyType.${item.incoming.subtype}`, { defaultValue: item.incoming.subtype })}
            </p>
          )}
        </div>
        <span
          className={`rounded-md px-2 py-0.5 text-xs font-medium ${badgeClass[item.type] ?? ""}`}
          data-testid="data-sync-plan-badge"
        >
          {item.type === DataSyncPlanItemType.Link
            ? t("dataSync.plan.type.Link", {
                name: candidate?.name ?? item.candidates[0]?.name ?? "",
              })
            : t(`dataSync.plan.type.${typeName}`)}
        </span>
        {item.reason === DataSyncPlanItemReason.LocalIsNewer && (
          <span
            className="rounded-md bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary"
            data-testid="data-sync-plan-newer-here"
          >
            {t("dataSync.plan.newerHere")}
          </span>
        )}
      </div>

      {item.reason != null && item.reason !== DataSyncPlanItemReason.LocalIsNewer && (
        <p className="text-xs text-warning-700 dark:text-warning">
          {t(`dataSync.plan.reason.${DataSyncPlanItemReasonLabel[item.reason]}`)}
        </p>
      )}
      {held && (
        <p className="text-xs text-default-500" data-testid="data-sync-plan-held">
          {t(
            `dataSync.plan.held.${
              item.heldReason != null ? DataSyncHeldReasonLabel[item.heldReason] : "Invalid"
            }`,
          )}
        </p>
      )}

      {!held && item.allowedResolutions.length > 0 && (
        <div className="flex flex-wrap items-center gap-2">
          <label className="sr-only" htmlFor={pickerId}>
            {t("dataSync.plan.resolutionFor", { name: item.incoming.name })}
          </label>
          <select
            className={`${fieldClass} w-auto py-1 text-xs`}
            data-testid="data-sync-plan-resolution"
            disabled={disabled}
            id={pickerId}
            value={resolution ?? ""}
            onChange={(event) => choose(Number(event.target.value) as DataSyncPlanResolution)}
          >
            {resolution === undefined && (
              <option disabled value="">
                {t("dataSync.plan.choose")}
              </option>
            )}
            {item.allowedResolutions.map((option) => (
              <option
                key={option}
                title={
                  option === DataSyncPlanResolution.Skip
                    ? t("dataSync.plan.skipHint", { name: sourceName })
                    : undefined
                }
                value={option}
              >
                {t(`dataSync.plan.resolution.${DataSyncPlanResolutionLabel[option]}`)}
              </option>
            ))}
          </select>
          {choosesTarget && decision && (
            <select
              aria-label={t("dataSync.plan.target", { name: item.incoming.name })}
              className={`${fieldClass} w-auto py-1 text-xs`}
              data-testid="data-sync-plan-target"
              disabled={disabled}
              value={decision.targetLocalKey ?? ""}
              onChange={(event) =>
                onChange({ ...decision, targetLocalKey: event.target.value, excludedChangeIds: [] })
              }
            >
              {item.candidates.map((option) => (
                <option key={option.localKey} value={option.localKey}>
                  {t("dataSync.plan.candidate", {
                    name: option.name,
                    match: t(`dataSync.plan.match.${DataSyncNaturalMatchLabel[option.match]}`),
                  })}
                </option>
              ))}
            </select>
          )}
          {resolution === DataSyncPlanResolution.CreateSeparate && decision && (
            <input
              aria-label={t("dataSync.plan.separateNameLabel")}
              className={`${fieldClass} w-56 py-1 text-xs`}
              data-testid="data-sync-plan-separate-name"
              disabled={disabled}
              maxLength={256}
              value={decision.newName ?? ""}
              onChange={(event) => onChange({ ...decision, newName: event.target.value })}
            />
          )}
          {hasDetails && (
            <button
              aria-expanded={open}
              className={linkButtonClass}
              data-testid="data-sync-plan-expand"
              type="button"
              onClick={() => setOpen((current) => !current)}
            >
              {open
                ? t("dataSync.plan.hideChanges")
                : t("dataSync.plan.showChanges", { count: target!.counts.total })}
            </button>
          )}
        </div>
      )}
      {resolution === DataSyncPlanResolution.Skip && (
        <p className="text-xs text-default-500">
          {t("dataSync.plan.skipHint", { name: sourceName })}
        </p>
      )}
      {error && (
        <p className="text-xs text-danger" data-testid="data-sync-plan-error" role="alert">
          {t(
            `dataSync.decisionError.${
              DataSyncDecisionErrorCodeLabel[error] ??
              DataSyncDecisionErrorCodeLabel[DataSyncDecisionErrorCode.ChangedSinceReview]
            }`,
          )}
        </p>
      )}
      {itemWarnings.length > 0 && (
        <ul className="space-y-0.5 text-xs text-default-500">
          {itemWarnings.map((warning, index) => (
            <li
              key={`${warning.code}-${index}`}
              data-warning={DataSyncWarningCodeLabel[warning.code]}
            >
              {t(`dataSync.plan.warning.${DataSyncWarningCodeLabel[warning.code]}`)}
            </li>
          ))}
        </ul>
      )}
      {open && hasDetails && target && (
        <ChangeList
          decision={decision}
          disabled={disabled || !decision}
          item={item}
          planId={planId}
          reviewId={reviewId}
          target={target}
          onPlanChanged={onPlanChanged}
          onToggle={(changeId) => decision && onChange(toggleChange(decision, changeId))}
          onToggleGroup={(group) => decision && onChange(toggleGroup(decision, group))}
        />
      )}
    </li>
  );
}
