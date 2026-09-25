import type {
  DataSyncHistoryDetail,
  DataSyncPlan,
  DataSyncReviewResult,
  DataSyncReviewSource,
} from "../api";
import type { ReviewDecisions } from "../reviewModels";
import type { DataSyncConfirmation } from "../hooks/useDataSyncActions";
import type { DataSyncDecisionErrorCode } from "@/sdk/constants";

import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { dataSyncApi, throwIfProblem } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";
import { isTaskOver, useDataSyncTask } from "../hooks/useDataSyncTask";
import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { footerCounts, initialDecisions, rebaseDecisions, toApplyInput } from "../reviewModels";
import { useDataSyncStore } from "../stores/dataSync";
import { timeAgo } from "../times";
import { sharingNeeded, twoWayConfirmation } from "../viewModels";

import DataSyncDialog from "./DataSyncDialog";
import PlanView from "./PlanView";
import { buttonClass, DataSyncErrorNotice, primaryClass, smallButtonClass } from "./common";

import { edgeStyles } from "@/features/federation/map/DeviceMapCanvas";
import {
  DataSyncItemOutcome,
  DataSyncLinkMode,
  DataSyncPlanItemType,
  DataSyncPlanItemTypeLabel,
  DataSyncProblemCode,
  DataSyncProblemCodeLabel,
  DataSyncReviewState,
  RemoteAccessMode,
} from "@/sdk/constants";

/*
 * The first sync review (spec §11.3; v3.1's import review): what the other device's definitions
 * would do here, decided item by item, then applied as one task — which can be cancelled while
 * it waits or runs, never through the task list's stop button. After a copy once, the reader
 * chooses whether to keep receiving.
 */

/** How often the review is read again while its apply runs. */
export const REVIEW_POLL_MS = 1_500;

export interface ReviewViewProps {
  /** The review to show; none while the link's first review is still being fetched. */
  reviewId?: string;
  /** The device the review reads. */
  peerName: string;
  peerNodeId?: string;
  /**
   * The link still waits for the other device to let this one read it: no review is coming
   * until it is approved there, which may take hours.
   */
  awaitingAccess?: boolean;
  selfName: string;
  onClose: () => void;
  /** Something changed here: the page reads data sync again. */
  onChanged: () => void;
  now?: number;
}

type Phase = "loading" | "preparing" | "gone" | "review" | "applying" | "done" | "failed";

const phaseOf = (result: DataSyncReviewResult | undefined, retrying: boolean): Phase => {
  if (!result) return "loading";
  if (result.problem) return "gone";
  switch (result.state) {
    case DataSyncReviewState.Applying:
      return "applying";
    case DataSyncReviewState.Applied:
      return "done";
    case DataSyncReviewState.Failed:
      return retrying && result.plan ? "review" : "failed";
    default:
      return result.plan ? "review" : "gone";
  }
};

export default function ReviewView({
  reviewId: initialReviewId,
  peerName,
  peerNodeId,
  awaitingAccess = false,
  selfName,
  onClose,
  onChanged,
  now,
}: ReviewViewProps) {
  const { t } = useTranslation();
  const [reviewId, setReviewId] = useState(initialReviewId);
  const [result, setResult] = useState<DataSyncReviewResult>();
  const [loadError, setLoadError] = useState<Error>();
  const [decisions, setDecisions] = useState<ReviewDecisions>({});
  const [errors, setErrors] = useState<Map<string, DataSyncDecisionErrorCode>>();
  const [banner, setBanner] = useState<"decisionsInvalid" | "planChanged">();
  const [retrying, setRetrying] = useState(false);
  const [detail, setDetail] = useState<DataSyncHistoryDetail>();
  const planId = useRef<string>();
  const told = useRef(false);
  const actions = useDataSyncActions(() => undefined);
  const phase = initialReviewId || reviewId ? phaseOf(result, retrying) : "preparing";
  const task = useDataSyncTask(result?.taskId);
  const name = result?.source?.name || peerName;

  useEffect(() => setReviewId(initialReviewId), [initialReviewId]);

  /** Takes a review as the server answered it; decisions start over on another plan. */
  const take = useCallback((next: DataSyncReviewResult | undefined) => {
    if (!next) return;
    if (next.reviewId) setReviewId(next.reviewId);
    if (next.plan && next.plan.planId !== planId.current) {
      planId.current = next.plan.planId;
      setDecisions(initialDecisions(next.plan));
      setErrors(undefined);
    }
    setResult(next);
  }, []);

  const load = useCallback(async () => {
    if (!reviewId) return;
    try {
      take(await dataSyncApi.review(reviewId));
      setLoadError(undefined);
    } catch (cause) {
      setLoadError(cause instanceof Error ? cause : new Error(String(cause)));
    }
  }, [reviewId, take]);

  useEffect(() => {
    void load();
  }, [load]);

  // While it applies, the review says how it went: read it until it stops applying — and at
  // once when the task list says the task is over.
  useEffect(() => {
    if (phase !== "applying") return;
    const timer = setInterval(() => void load(), REVIEW_POLL_MS);

    return () => clearInterval(timer);
  }, [phase, load]);
  useEffect(() => {
    if (phase === "applying" && task && isTaskOver(task.phase)) void load();
  }, [phase, task?.phase, load]);

  // Done: what it did, and what changed while the reader reviewed.
  const applyLogId = result?.applyLogId;

  useEffect(() => {
    if (phase !== "done") return;
    if (!told.current) {
      told.current = true;
      onChanged();
    }
    if (applyLogId == null) return;
    let live = true;

    dataSyncApi.historyEntry(applyLogId).then(
      (entry) => live && setDetail(entry ?? undefined),
      () => undefined,
    );

    return () => {
      live = false;
    };
  }, [phase, applyLogId]);

  const plan = result?.plan ?? undefined;
  const footer = footerCounts(plan, decisions);
  const twoWay = result?.linkMode === DataSyncLinkMode.TwoWay && !result.copyOnce;

  const planChanged = () => {
    setBanner("planChanged");
    void load();
  };

  const apply = () =>
    reviewId &&
    void actions.run(async () => {
      const start = await dataSyncApi.applyReview(reviewId, {
        decisions: toApplyInput(plan, decisions),
        // A first sync creates, updates and links; it never deletes values.
        backupBeforeDestructive: false,
      });

      if (start?.problem?.code === DataSyncProblemCode.DecisionsInvalid && start.plan) {
        const rebased = rebaseDecisions(plan, start.plan, decisions);

        planId.current = start.plan.planId;
        setDecisions(rebased.decisions);
        setErrors(new Map(start.decisionErrors.map((error) => [error.itemId, error.code])));
        setBanner("decisionsInvalid");
        setResult((current) => current && { ...current, plan: start.plan as DataSyncPlan });

        return;
      }
      throwIfProblem(start);
      setBanner(undefined);
      setRetrying(false);
      setResult(
        (current) =>
          current && {
            ...current,
            state: DataSyncReviewState.Applying,
            taskId: start.taskId ?? current.taskId,
          },
      );
    }, []);

  const cancel = () =>
    reviewId &&
    void actions.run(async () => {
      const answer = throwIfProblem(await dataSyncApi.cancelReviewApply(reviewId));

      // Staged: back to the review. Applied: it finished first. Still applying: it stops at
      // its next step, and the review says so when read again.
      if (answer.state !== DataSyncReviewState.Applying) await load();
    }, []);

  const refetch = () =>
    reviewId &&
    void actions.run(async () => {
      const next = throwIfProblem(await dataSyncApi.refetchReview(reviewId));

      planId.current = undefined;
      setBanner(undefined);
      take(next);
    }, []);

  const title = result?.copyOnce
    ? t("dataSync.review.copyTitle", { name })
    : t("dataSync.review.title", { name });

  return (
    <DataSyncDialog
      wide
      busy={actions.busy}
      footer={
        phase === "review" && plan ? (
          <ReviewFooter busy={actions.busy} footer={footer} onApply={apply} onClose={onClose} />
        ) : (
          <button className={buttonClass} type="button" onClick={onClose}>
            {phase === "done" ? t("dataSync.done") : t("dataSync.close")}
          </button>
        )
      }
      testId="data-sync-review"
      title={title}
      onClose={onClose}
    >
      {result?.source && (
        <ReviewDrawing plan={plan} selfName={selfName} source={result.source} twoWay={twoWay} />
      )}
      {result?.source && (phase === "review" || phase === "failed") && (
        <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
          <span data-testid="data-sync-review-fetched">
            {t("dataSync.review.fetched", { time: timeAgo(t, result.source.fetchedAt, now) })}
          </span>
          <button
            className={smallButtonClass}
            data-testid="data-sync-review-refetch"
            disabled={actions.busy}
            type="button"
            onClick={refetch}
          >
            {t("dataSync.review.fetchAgain")}
          </button>
        </div>
      )}
      {twoWay && phase === "review" && (
        <p
          className="rounded-lg border border-secondary/30 bg-secondary/5 p-2 text-xs"
          data-testid="data-sync-review-two-way"
        >
          {t("dataSync.review.twoWayNote", { name })}
        </p>
      )}
      <DataSyncErrorNotice error={loadError} onRetry={() => void load()} />
      <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      {banner && (
        <p
          className="rounded-lg border border-warning/40 bg-warning/10 p-2 text-xs"
          data-testid="data-sync-review-banner"
          role="status"
        >
          {t(`dataSync.review.${banner}`)}
        </p>
      )}

      {phase === "loading" && !loadError && (
        <p className="text-sm text-default-500" role="status">
          {t("dataSync.loading")}
        </p>
      )}
      {phase === "preparing" &&
        (awaitingAccess ? (
          <div className="space-y-1 text-sm" data-testid="data-sync-review-awaiting-access">
            <p role="status">{t("dataSync.status.AwaitingAccess", { name })}</p>
            <p className="text-xs text-default-500">{t("dataSync.link.approveThere", { name })}</p>
          </div>
        ) : (
          <p className="text-sm" data-testid="data-sync-review-preparing" role="status">
            {t("dataSync.wizard.fetching", { name })}
          </p>
        ))}
      {phase === "gone" && (
        <p className="text-sm" data-testid="data-sync-review-gone">
          {t(
            `dataSync.problem.${
              (result?.problem && DataSyncProblemCodeLabel[result.problem.code]) ||
              "NothingToReview"
            }`,
          )}
        </p>
      )}
      {phase === "review" && plan && reviewId && (
        <PlanView
          decisions={decisions}
          disabled={actions.busy}
          errors={errors}
          plan={plan}
          reviewId={reviewId}
          sourceName={name}
          onChange={setDecisions}
          onPlanChanged={planChanged}
        />
      )}
      {phase === "applying" && (
        <ApplyProgress
          busy={actions.busy}
          percentage={task?.percentage}
          running={task?.phase === "running"}
          waitingReason={task?.waitingReason}
          onCancel={cancel}
        />
      )}
      {phase === "failed" && (
        <div className="space-y-2" data-testid="data-sync-review-failed">
          <p className="text-sm text-danger-700" role="alert">
            {t("dataSync.review.failed", { reason: result?.lastError || task?.error || "" })}
          </p>
          <button
            className={buttonClass}
            type="button"
            onClick={() => {
              setRetrying(true);
              void load();
            }}
          >
            {t("dataSync.review.tryAgain")}
          </button>
        </div>
      )}
      {phase === "done" && result && (
        <ReviewDone
          detail={detail}
          name={name}
          peerNodeId={result.source?.nodeId ?? peerNodeId}
          result={result}
          onChanged={onChanged}
          onClose={onClose}
        />
      )}
    </DataSyncDialog>
  );
}

/** Apply, or why not yet: what is still undecided, or that there is nothing to write. */
function ReviewFooter({
  footer,
  busy,
  onApply,
  onClose,
}: {
  footer: ReturnType<typeof footerCounts>;
  busy: boolean;
  onApply: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const blocked = footer.pending > 0;

  return (
    <div className="flex w-full flex-wrap items-center justify-between gap-2">
      <p className="text-xs text-default-500">{t("dataSync.review.safe")}</p>
      <div className="flex gap-2">
        <button className={buttonClass} disabled={busy} type="button" onClick={onClose}>
          {t("dataSync.review.later")}
        </button>
        <button
          className={primaryClass}
          data-testid="data-sync-review-apply"
          disabled={busy || blocked || footer.nothingToApply}
          title={blocked ? t("dataSync.review.decideFirst", { count: footer.pending }) : undefined}
          type="button"
          onClick={onApply}
        >
          {footer.nothingToApply && !blocked
            ? t("dataSync.review.nothingToApply")
            : t("dataSync.review.apply", { count: footer.total })}
        </button>
      </div>
      {blocked && (
        <p className="w-full text-right text-xs text-warning-700 dark:text-warning">
          {t("dataSync.review.decideFirst", { count: footer.pending })}
        </p>
      )}
    </div>
  );
}

/** The apply at work: waiting for another task, or running, with Cancel either way. */
function ApplyProgress({
  running,
  percentage,
  waitingReason,
  busy,
  onCancel,
}: {
  running: boolean;
  percentage?: number;
  waitingReason?: string;
  busy: boolean;
  onCancel: () => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="space-y-2" data-testid="data-sync-review-applying" role="status">
      {running ? (
        <>
          <p className="text-sm">{t("dataSync.review.applying")}</p>
          <div className="h-2 overflow-hidden rounded-full bg-default-100">
            <div
              className="h-full bg-primary transition-all"
              style={{ width: `${Math.max(2, Math.min(100, percentage ?? 0))}%` }}
            />
          </div>
        </>
      ) : (
        <p className="text-sm" data-testid="data-sync-review-waiting">
          {waitingReason
            ? t("dataSync.review.waitingFor", { reason: waitingReason })
            : t("dataSync.review.waiting")}
        </p>
      )}
      <p className="text-xs text-default-500">{t("dataSync.review.closeWhileApplying")}</p>
      <button
        className={buttonClass}
        data-testid="data-sync-review-cancel"
        disabled={busy}
        type="button"
        onClick={onCancel}
      >
        {t("dataSync.cancel")}
      </button>
    </div>
  );
}

/** What the apply did; after a copy once, whether to keep receiving. */
function ReviewDone({
  result,
  detail,
  name,
  peerNodeId,
  onChanged,
  onClose,
}: {
  result: DataSyncReviewResult;
  detail?: DataSyncHistoryDetail;
  name: string;
  peerNodeId?: string;
  onChanged: () => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const actions = useDataSyncActions(() => undefined);
  const overview = useDataSyncStore((state) => state.overview);
  // The server's word on who may create access counts too: the window's own guess keeps its
  // defaults — this device's own window — when who is looking could not be read.
  const canManage = useCanManageDefinitionSharing() && (overview?.canManageSharing ?? true);
  const keepInStepButton = useRef<HTMLButtonElement>(null);
  const counts = detail?.entry.counts;
  const changed = (detail?.items ?? []).filter(
    (item) =>
      item.outcome === DataSyncItemOutcome.ChangedSinceReview ||
      item.outcome === DataSyncItemOutcome.ChangedDuringApply,
  );
  const linkId = result.linkId;
  const own = {
    sharingEnabled: overview?.sharingEnabled ?? false,
    remoteAccessMode: overview?.remoteAccessMode ?? RemoteAccessMode.Disabled,
  };
  const mustTurnOn = sharingNeeded(own);

  const then = (operation: () => Promise<unknown>) =>
    void actions.run(async () => {
      await operation();
      onChanged();
      onClose();
    }, []);

  /**
   * Keeping in step both ways lets the other device read this one: asked first, in the words
   * every other place that offers it uses — and saying what it turns on here, only if it is off.
   */
  const keepInStep = (id: number) =>
    actions.confirm({
      ...twoWayConfirmation(t, name, own, mustTurnOn),
      action: async () => {
        if (mustTurnOn)
          await dataSyncApi.setSharing({
            enabled: true,
            enablePairedRemoteAccess: own.remoteAccessMode === RemoteAccessMode.Disabled,
          });
        await dataSyncApi.updateLink(id, { mode: DataSyncLinkMode.TwoWay });
        onChanged();
        onClose();
      },
      refresh: [],
    });

  return (
    <div className="space-y-3" data-testid="data-sync-review-done">
      <p className="text-sm font-medium">
        {result.copyOnce
          ? t("dataSync.review.copied", { name })
          : t("dataSync.review.inStep", { name })}
      </p>
      {counts && (
        <p className="text-xs text-default-500" data-testid="data-sync-review-counts">
          {t("dataSync.review.counts", {
            applied: counts.created + counts.updated + counts.linked,
            skipped: counts.skipped,
            changed: counts.changedSinceReview + counts.changedDuringApply,
          })}
        </p>
      )}
      {changed.length > 0 && (
        <div className="space-y-1" data-testid="data-sync-review-changed">
          <p className="text-xs font-medium">{t("dataSync.review.changedWhileReviewing")}</p>
          <ul className="list-disc pl-5 text-xs text-default-500">
            {changed.map((item) => (
              <li key={item.itemId}>{item.name}</li>
            ))}
          </ul>
        </div>
      )}
      <DataSyncErrorNotice error={actions.error} onDismiss={() => actions.setError(undefined)} />
      {result.copyOnce && linkId != null && (
        <div className="space-y-2" data-testid="data-sync-review-follow-up">
          <div className="flex flex-wrap gap-2">
            <button
              className={buttonClass}
              data-testid="data-sync-review-keep-receiving"
              disabled={actions.busy}
              type="button"
              onClick={() =>
                then(() => dataSyncApi.updateLink(linkId, { mode: DataSyncLinkMode.Follow }))
              }
            >
              {t("dataSync.review.keepReceiving", { name })}
            </button>
            {canManage && (
              <button
                ref={keepInStepButton}
                aria-expanded={!!actions.confirmation}
                className={buttonClass}
                data-testid="data-sync-review-keep-in-step"
                disabled={actions.busy}
                type="button"
                onClick={() => keepInStep(linkId)}
              >
                {t("dataSync.mode.twoWay")}
              </button>
            )}
            {peerNodeId && (
              <button
                className={buttonClass}
                data-testid="data-sync-review-stop-reading"
                disabled={actions.busy}
                type="button"
                onClick={() => then(() => dataSyncApi.forgetAccess(peerNodeId))}
              >
                {t("dataSync.link.stopReading.button", { name })}
              </button>
            )}
          </div>
          {actions.confirmation && (
            <InlineConfirmation
              busy={actions.busy}
              confirmation={actions.confirmation}
              error={actions.confirmationError}
              onCancel={() => {
                actions.cancelConfirmation();
                keepInStepButton.current?.focus();
              }}
              onConfirm={actions.confirmCurrent}
            />
          )}
        </div>
      )}
    </div>
  );
}

/**
 * A question asked inside the review rather than in a dialog over it: the review is a dialog
 * already, and a second one on top would fight it for the keyboard. Takes the keyboard when it
 * appears; Cancel gives it back to where the review's own buttons are.
 */
function InlineConfirmation({
  confirmation,
  busy,
  error,
  onConfirm,
  onCancel,
}: {
  confirmation: DataSyncConfirmation;
  busy: boolean;
  error?: Error;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  const { t } = useTranslation();
  const root = useRef<HTMLElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const titleId = useId();
  const latest = useRef({ busy, onCancel });

  latest.current = { busy, onCancel };

  useEffect(() => heading.current?.focus(), []);

  // Escape answers the question, and only the question: it never reaches the review's dialog.
  useEffect(() => {
    const element = root.current;

    if (!element) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      event.stopPropagation();
      if (!latest.current.busy) latest.current.onCancel();
    };

    element.addEventListener("keydown", onKeyDown);

    return () => element.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <section
      ref={root}
      aria-labelledby={titleId}
      className="space-y-2 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm"
      data-testid="data-sync-review-confirm"
      role="group"
    >
      <h3 ref={heading} className="font-medium outline-none" id={titleId} tabIndex={-1}>
        {confirmation.title}
      </h3>
      <p className="text-xs">{confirmation.description}</p>
      {confirmation.warning && (
        <p className="text-xs font-medium" data-testid="data-sync-review-confirm-warning">
          {confirmation.warning}
        </p>
      )}
      <DataSyncErrorNotice error={error} />
      <div className="flex flex-wrap justify-end gap-2">
        <button className={buttonClass} disabled={busy} type="button" onClick={onCancel}>
          {t("dataSync.cancel")}
        </button>
        <button
          className={primaryClass}
          data-testid="data-sync-review-confirm-yes"
          disabled={busy}
          type="button"
          onClick={onConfirm}
        >
          {t("federation.confirm")}
        </button>
      </div>
    </section>
  );
}

/** Where the definitions come from: the other device → this device, both ways when in step. */
function ReviewDrawing({
  source,
  selfName,
  twoWay,
  plan,
}: {
  source: DataSyncReviewSource;
  selfName: string;
  twoWay: boolean;
  plan?: DataSyncPlan;
}) {
  const { t } = useTranslation();
  const style = edgeStyles.sync;
  const counts = (plan?.summary.counts ?? []).reduce<Record<number, number>>((sum, count) => {
    sum[count.type] = (sum[count.type] ?? 0) + count.count;

    return sum;
  }, {});
  const shown = [
    DataSyncPlanItemType.Create,
    DataSyncPlanItemType.Link,
    DataSyncPlanItemType.Update,
    DataSyncPlanItemType.NeedsDecision,
    DataSyncPlanItemType.Held,
  ].filter((type) => counts[type] > 0);
  const label = t(twoWay ? "dataSync.review.drawingBoth" : "dataSync.review.drawing", {
    name: source.name,
    self: selfName,
  });

  return (
    <figure className="space-y-1" data-testid="data-sync-review-drawing">
      <svg aria-label={label} className="h-16 w-full max-w-md" role="img" viewBox="0 0 360 64">
        <defs>
          <marker
            id="data-sync-review-arrow"
            markerHeight="8"
            markerWidth="8"
            orient="auto-start-reverse"
            refX="7"
            refY="4"
          >
            <path className={style.fill} d="M0 0 L8 4 L0 8 Z" />
          </marker>
        </defs>
        <rect
          className="fill-content2 stroke-default-300"
          height="40"
          rx="8"
          width="120"
          x="4"
          y="12"
        />
        <text className="fill-foreground text-[12px]" textAnchor="middle" x="64" y="36">
          {truncate(source.name)}
        </text>
        <rect
          className="fill-primary/10 stroke-primary"
          height="40"
          rx="8"
          width="120"
          x="236"
          y="12"
        />
        <text className="fill-foreground text-[12px]" textAnchor="middle" x="296" y="36">
          {truncate(selfName)}
        </text>
        <line
          className={style.stroke}
          markerEnd="url(#data-sync-review-arrow)"
          markerStart={twoWay ? "url(#data-sync-review-arrow)" : undefined}
          strokeWidth={style.width}
          x1="130"
          x2="230"
          y1="32"
          y2="32"
        />
      </svg>
      <figcaption className="flex flex-wrap gap-x-3 text-xs text-default-500">
        <span className="sr-only">{label}</span>
        {shown.map((type) => (
          <span key={type}>
            {t(`dataSync.plan.summary.${DataSyncPlanItemTypeLabel[type]}`, { count: counts[type] })}
          </span>
        ))}
      </figcaption>
    </figure>
  );
}

const truncate = (text: string) => (text.length > 16 ? `${text.slice(0, 15)}…` : text);
