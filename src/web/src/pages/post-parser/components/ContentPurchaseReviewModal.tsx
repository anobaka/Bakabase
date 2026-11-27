import type { PostContentSnapshot } from "@/core/models/PostParserTask";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { TaskFilterScope } from "./TaskFilterScopeSummary";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineInfoCircle, AiOutlineQuestionCircle } from "react-icons/ai";
import { Button, Checkbox, Chip, Modal, Radio, RadioGroup, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import TaskFilterScopeSummary from "./TaskFilterScopeSummary";
import LockedContentDetails, {
  formatSourceAmount,
  sourceCurrencyLabel,
} from "./LockedContentDetails";
import { attentionReasonLabel, purchaseSkipReasonLabel } from "../attentionReasons";
import Tooltip from "./PostParserTooltip";

export interface ContentReviewScope {
  keyword?: string;
  status?: string;
  reasonCode?: string;
  taskIds?: number[];
}
export type ContentReviewAction = "buy" | "notRequired" | "defer";
export interface ContentReviewTarget {
  targetId: string;
  floor?: string | null;
  blockIds: string[];
  blocks: PostContentSnapshot["locks"];
  price?: number | null;
  priceText?: string | null;
  currencyCode?: string | null;
  currencyName?: string | null;
  canBuy: boolean;
  canMarkNotRequired: boolean;
  buyBlockReason?: string | null;
  defaultAction: string;
}
export interface ContentReviewTask {
  taskId: number;
  source?: number;
  title?: string | null;
  link?: string | null;
  revision: number;
  workflowRunId?: number | null;
  decisionVersion: number;
  contentFingerprint?: string | null;
  quoteHash: string;
  canSubmit: boolean;
  blockReason?: string | null;
  alreadyUnlocked: boolean;
  targets: ContentReviewTarget[];
}
export interface ContentReviewPreview {
  scope: ContentReviewScope;
  matchedCount: number;
  autoBuyThreshold: number;
  minimumRemainingCoins: number;
  tasks: ContentReviewTask[];
}
export interface ContentReviewTaskSelection {
  taskId: number;
  revision: number;
  workflowRunId: number;
  decisionVersion: number;
  contentFingerprint: string;
  quoteHash: string;
  targets: { targetId: string; action: ContentReviewAction }[];
}
export interface ContentReviewResult {
  requestId?: string;
  requestedCount: number;
  queuedCount: number;
  unchangedCount: number;
  skippedCount: number;
  failedCount: number;
  items: {
    taskId: number;
    status: string;
    workflowRunId?: number | null;
    reviewId?: string | null;
    reason?: string | null;
  }[];
}
interface Props extends DestroyableProps {
  snapshot?: ContentReviewPreview;
  /** A new key is required for every successfully accepted preview, even if its content is unchanged. */
  snapshotKey: string;
  loading?: boolean;
  error?: string;
  onRefresh: () => Promise<void>;
  onSubmit: (input: { tasks: ContentReviewTaskSelection[] }) => Promise<ContentReviewResult>;
  onClose: () => void;
  onInspectTask?: (task: ContentReviewTask) => void;
}

type Choices = {
  key: string;
  selected: Set<number>;
  actions: Record<number, Record<string, ContentReviewAction>>;
};
type Decimal = { integer: bigint; scale: number };

/** Quote text is authoritative; never convert it through a floating-point number. */
function decimalQuote(target: ContentReviewTarget): Decimal | undefined {
  let text = target.priceText?.trim();
  if (!text && typeof target.price === "number" && Number.isFinite(target.price)) {
    text = String(target.price);
    const scientific = /^(\d+)(?:\.(\d+))?e([+-]?\d+)$/i.exec(text);
    if (scientific) {
      const digits = scientific[1] + (scientific[2] ?? "");
      const point = scientific[1].length + Number(scientific[3]);
      text =
        point <= 0
          ? `0.${"0".repeat(-point)}${digits}`
          : point >= digits.length
            ? digits + "0".repeat(point - digits.length)
            : `${digits.slice(0, point)}.${digits.slice(point)}`;
    }
  }
  const match = text && /^\+?(\d+)(?:\.(\d+))?$/.exec(text);
  if (!match) return undefined;
  return { integer: BigInt(match[1] + (match[2] ?? "")), scale: (match[2] ?? "").length };
}
function decimalText({ integer, scale }: Decimal): string {
  if (!scale) return String(integer);
  const digits = String(integer).padStart(scale + 1, "0");
  return `${digits.slice(0, -scale)}.${digits.slice(-scale)}`;
}
function paidTarget(target: ContentReviewTarget): boolean {
  return target.blocks.length > 0 && target.blocks.every((block) => block.isBought);
}
function sourceDomain(link?: string | null): string | undefined {
  if (!link) return undefined;
  try {
    return new URL(link).hostname;
  } catch {
    return undefined;
  }
}
function canBuy(target: ContentReviewTarget): boolean {
  const amount = decimalQuote(target);
  return (
    target.canBuy &&
    !paidTarget(target) &&
    !!amount &&
    (amount.integer === 0n || !!(target.currencyName?.trim() || target.currencyCode?.trim()))
  );
}
function canMarkNotRequired(target: ContentReviewTarget): boolean {
  return target.canMarkNotRequired && !paidTarget(target);
}
const compactStatusCodes = new Set([
  "overPurchaseLimit",
  "priceUnknown",
  "balanceUnknown",
  "insufficientBalance",
  "purchaseUncertain",
]);
function initialChoices(key: string, snapshot?: ContentReviewPreview): Choices {
  const actions: Choices["actions"] = Object.fromEntries(
    (snapshot?.tasks ?? []).map((task) => [
      task.taskId,
      Object.fromEntries(
        task.targets.map((target) => [
          target.targetId,
          target.defaultAction === "buy" && canBuy(target)
            ? "buy"
            : target.defaultAction === "notRequired" && canMarkNotRequired(target)
              ? "notRequired"
              : "defer",
        ]),
      ),
    ]),
  );
  return {
    key,
    selected: new Set(
      (snapshot?.tasks ?? [])
        .filter(
          (task) =>
            task.canSubmit &&
            hasIdentity(task) &&
            (task.alreadyUnlocked ||
              Object.values(actions[task.taskId]).some((action) => action !== "defer")),
        )
        .map((task) => task.taskId),
    ),
    actions,
  };
}
function hasIdentity(task: ContentReviewTask): boolean {
  return (
    Number.isSafeInteger(task.taskId) &&
    task.taskId > 0 &&
    Number.isSafeInteger(task.revision) &&
    task.revision >= 0 &&
    Number.isSafeInteger(task.workflowRunId) &&
    task.workflowRunId! > 0 &&
    Number.isSafeInteger(task.decisionVersion) &&
    task.decisionVersion >= 0 &&
    !!task.contentFingerprint?.trim() &&
    !!task.quoteHash.trim() &&
    new Set(task.targets.map((target) => target.targetId)).size === task.targets.length &&
    task.targets.every((target) => !!target.targetId.trim())
  );
}

/** Pure review UI: the controller owns previews, API calls and durable request identity. */
export default function ContentPurchaseReviewModal({
  snapshot,
  snapshotKey,
  loading = false,
  error,
  onRefresh,
  onSubmit,
  onClose,
  onInspectTask,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const label = (key: string, values?: Record<string, unknown>) =>
    t(`postParser.contentPurchaseReview.${key}`, values);
  const [visible, setVisible] = useState(true);
  const [choices, setChoices] = useState(() => initialChoices(snapshotKey, snapshot));
  const latestChoices = useRef(choices);
  const [working, setWorking] = useState<"refresh" | "submit">();
  const [blockedKey, setBlockedKey] = useState<string>();
  const [localError, setLocalError] = useState<string>();
  const [result, setResult] = useState<{ key: string; value: ContentReviewResult }>();
  const [bulkResult, setBulkResult] = useState<{ key: string; applied: number; skipped: number }>();
  const [expandedTargets, setExpandedTargets] = useState<Set<string>>(() => new Set());
  const detailId = useId();
  const mounted = useRef(true);
  const operation = useRef(false);
  const currentKey = useRef(snapshotKey);
  const closeButton = useRef<HTMLButtonElement>(null);
  currentKey.current = snapshotKey;

  useEffect(() => {
    mounted.current = true;
    const frame = window.requestAnimationFrame(() => closeButton.current?.focus());
    return () => {
      mounted.current = false;
      window.cancelAnimationFrame(frame);
    };
  }, []);
  useEffect(() => {
    const next = initialChoices(snapshotKey, snapshot);
    latestChoices.current = next;
    setChoices(next);
    setLocalError(undefined);
    setResult(undefined);
    setBulkResult(undefined);
    setExpandedTargets(new Set());
  }, [snapshotKey]);

  const currentChoices =
    choices.key === snapshotKey ? choices : initialChoices(snapshotKey, snapshot);
  latestChoices.current = currentChoices;
  const editChoices = (update: (previous: Choices) => Choices) => {
    const next = update(latestChoices.current);
    latestChoices.current = next;
    setChoices(next);
  };
  const busy = loading || !!working;
  const ready =
    !!snapshot &&
    !!snapshotKey &&
    !busy &&
    !error &&
    blockedKey !== snapshotKey &&
    choices.key === snapshotKey;
  const tasks = snapshot?.tasks ?? [];
  const selectedTasks = tasks.filter(
    (task) => currentChoices.selected.has(task.taskId) && task.canSubmit && hasIdentity(task),
  );
  const actionFor = (
    task: ContentReviewTask,
    target: ContentReviewTarget,
    draft = currentChoices,
  ): ContentReviewAction => {
    const action = draft.actions[task.taskId]?.[target.targetId] ?? "defer";
    return action === "buy" && canBuy(target)
      ? "buy"
      : action === "notRequired" && canMarkNotRequired(target)
        ? "notRequired"
        : "defer";
  };
  const hasWork = selectedTasks.some(
    (task) =>
      task.alreadyUnlocked || task.targets.some((target) => actionFor(task, target) !== "defer"),
  );
  const actionCounts = { buy: 0, notRequired: 0, defer: 0 };
  const totals = new Map<string, { amount: Decimal; target: ContentReviewTarget }>();
  for (const task of selectedTasks)
    for (const target of task.targets) {
      if (paidTarget(target)) continue;
      actionCounts[actionFor(task, target)]++;
      if (actionFor(task, target) !== "buy") continue;
      const amount = decimalQuote(target)!;
      const key = target.currencyCode?.trim()
        ? `code:${target.currencyCode.trim()}`
        : target.currencyName?.trim()
          ? `name:${target.currencyName.trim()}`
          : "unknown:free";
      const previous = totals.get(key);
      if (!previous) totals.set(key, { amount, target });
      else {
        const scale = Math.max(amount.scale, previous.amount.scale);
        previous.amount = {
          integer:
            previous.amount.integer * 10n ** BigInt(scale - previous.amount.scale) +
            amount.integer * 10n ** BigInt(scale - amount.scale),
          scale,
        };
      }
    }
  const close = () => {
    if (operation.current) return;
    setVisible(false);
    onClose();
  };
  const refresh = async () => {
    if (operation.current || loading || !mounted.current) return;
    operation.current = true;
    const key = snapshotKey;
    setWorking("refresh");
    setBlockedKey(key);
    setLocalError(undefined);
    try {
      await onRefresh();
    } catch (failure) {
      if (mounted.current && currentKey.current === key)
        setLocalError(failure instanceof Error ? failure.message : label("reviewFailed"));
    } finally {
      operation.current = false;
      if (mounted.current) setWorking(undefined);
    }
  };
  const submit = async () => {
    if (operation.current || !mounted.current || !ready) return;
    const draft = latestChoices.current;
    const selection = tasks.filter(
      (task) => draft.selected.has(task.taskId) && task.canSubmit && hasIdentity(task),
    );
    if (
      draft.key !== snapshotKey ||
      !selection.some(
        (task) =>
          task.alreadyUnlocked ||
          task.targets.some((target) => actionFor(task, target, draft) !== "defer"),
      )
    )
      return;
    operation.current = true;
    const key = snapshotKey;
    const input = {
      tasks: selection.map(
        (task): ContentReviewTaskSelection => ({
          taskId: task.taskId,
          revision: task.revision,
          workflowRunId: task.workflowRunId!,
          decisionVersion: task.decisionVersion,
          contentFingerprint: task.contentFingerprint!,
          quoteHash: task.quoteHash,
          targets: task.targets.map((target) => ({
            targetId: target.targetId,
            action: actionFor(task, target, draft),
          })),
        }),
      ),
    };
    setWorking("submit");
    setBlockedKey(key);
    setLocalError(undefined);
    try {
      const value = await onSubmit(input);
      if (mounted.current && currentKey.current === key) setResult({ key, value });
    } catch (failure) {
      if (mounted.current && currentKey.current === key)
        setLocalError(failure instanceof Error ? failure.message : label("reviewFailed"));
    } finally {
      operation.current = false;
      if (mounted.current) setWorking(undefined);
    }
  };
  const currentResult = result?.key === snapshotKey ? result.value : undefined;
  const applyBulkAction = (action: ContentReviewAction) => {
    if (!ready || operation.current) return;
    const draft = latestChoices.current;
    const selection = tasks.filter(
      (task) => draft.selected.has(task.taskId) && task.canSubmit && hasIdentity(task),
    );
    if (draft.key !== snapshotKey || !selection.length) return;
    const actions = { ...draft.actions };
    let applied = 0;
    let skipped = 0;
    for (const task of selection) {
      const taskActions = { ...actions[task.taskId] };
      for (const target of task.targets) {
        if (paidTarget(target)) continue;
        if (
          (action === "buy" && !canBuy(target)) ||
          (action === "notRequired" && !canMarkNotRequired(target))
        ) {
          skipped++;
          continue;
        }
        taskActions[target.targetId] = action;
        applied++;
      }
      actions[task.taskId] = taskActions;
    }
    editChoices(() => ({ ...draft, actions }));
    setBulkResult({ key: snapshotKey, applied, skipped });
  };

  return (
    <Modal
      visible={visible}
      title={label("title")}
      size="7xl"
      classNames={{
        base: "max-h-[calc(100dvh-2rem)]",
        header: "px-4 py-3",
        body: "min-h-0 gap-0 overflow-y-auto px-4 py-2",
        footer: "flex-wrap items-center gap-2 border-t border-default-200 px-4 py-3",
      }}
      hideCloseButton={!!working}
      isDismissable={!working}
      isKeyboardDismissDisabled={!!working}
      onClose={close}
      onDestroyed={onDestroyed}
      footer={
        <>
          <div
            aria-label={label("selectedTotals")}
            className="mr-auto min-w-0 flex-[1_1_22rem] space-y-1 text-xs"
          >
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span className="font-medium">
                {label("selected", { count: selectedTasks.length })}
              </span>
              <span className="text-default-500">{label("selectionActions", actionCounts)}</span>
            </div>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              {totals.size ? (
                [...totals.entries()].map(([key, total]) => (
                  <span key={key} className="break-all font-semibold tabular-nums">
                    {total.amount.integer === 0n &&
                    !total.target.currencyName &&
                    !total.target.currencyCode ? (
                      label("free")
                    ) : (
                      <>
                        {decimalText(total.amount)}{" "}
                        {sourceCurrencyLabel(
                          total.target.currencyName,
                          total.target.currencyCode,
                          undefined,
                          label("currencyUnknown"),
                        )}
                      </>
                    )}
                  </span>
                ))
              ) : (
                <span className="text-default-500">{label("noPurchaseSelected")}</span>
              )}
            </div>
          </div>
          <Button ref={closeButton} variant="flat" isDisabled={!!working} onPress={close}>
            {label("close")}
          </Button>
          <Button
            variant="flat"
            isLoading={working === "refresh"}
            isDisabled={busy}
            onPress={() => void refresh()}
          >
            {label("refresh")}
          </Button>
          <Button
            color="primary"
            className="h-auto min-h-10 min-w-0 max-w-full self-center whitespace-normal py-2"
            isLoading={working === "submit"}
            isDisabled={!ready || !hasWork}
            onPress={() => void submit()}
          >
            {label("submit", { count: selectedTasks.length })}
          </Button>
        </>
      }
    >
      <div className="space-y-2 text-sm leading-relaxed">
        {busy && (
          <div role="status" className="flex items-center gap-2 text-xs text-default-500">
            <Spinner size="sm" />
            {label(
              working === "submit"
                ? "submitting"
                : working === "refresh"
                  ? "refreshing"
                  : "loading",
            )}
          </div>
        )}
        {(localError || error) && (
          <p role="alert" className="whitespace-pre-wrap break-words text-danger">
            {localError || error}
          </p>
        )}
        {snapshot && (
          <>
            <div
              data-content-review-summary
              className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs text-default-500"
            >
              <span className="shrink-0 font-medium text-foreground">
                {label("matched", { count: snapshot.matchedCount })}
              </span>
              <Tooltip
                content={
                  snapshot.scope.taskIds?.length ? (
                    label("scopeTasks", { count: snapshot.scope.taskIds.length })
                  ) : (
                    <TaskFilterScopeSummary
                      keyword={snapshot.scope.keyword}
                      status={(snapshot.scope.status ?? "all") as TaskFilterScope["status"]}
                      reasonCode={snapshot.scope.reasonCode}
                    />
                  )
                }
              >
                <Button
                  aria-label={label("scopeDetails")}
                  className="h-6 min-w-0 max-w-full px-1.5 text-xs"
                  size="sm"
                  variant="light"
                >
                  <span className="truncate">
                    {snapshot.scope.taskIds?.length
                      ? label("scopeTasks", { count: snapshot.scope.taskIds.length })
                      : [
                          snapshot.scope.keyword?.trim(),
                          snapshot.scope.status && snapshot.scope.status !== "all"
                            ? t(`postParser.summary.${snapshot.scope.status}`)
                            : t("postParser.filters.anyStatus"),
                          snapshot.scope.reasonCode
                            ? attentionReasonLabel(t, snapshot.scope.reasonCode)
                            : "",
                        ]
                          .filter(Boolean)
                          .join(" · ")}
                  </span>
                </Button>
              </Tooltip>
              <Tooltip
                content={label("limits", {
                  limit: formatSourceAmount(snapshot.autoBuyThreshold),
                  reserve: formatSourceAmount(snapshot.minimumRemainingCoins),
                })}
              >
                <Button
                  className="h-6 min-w-0 gap-1 px-1.5 text-xs"
                  size="sm"
                  variant="light"
                  startContent={<AiOutlineInfoCircle aria-hidden />}
                >
                  {label("limitsShort", {
                    limit: formatSourceAmount(snapshot.autoBuyThreshold),
                    reserve: formatSourceAmount(snapshot.minimumRemainingCoins),
                  })}
                </Button>
              </Tooltip>
              <Tooltip
                content={
                  <div className="max-w-lg space-y-2 text-xs">
                    <p>{label("description")}</p>
                    <p>{label("snapshot")}</p>
                    <p>{label("notRequiredHint")}</p>
                    <p>{label("deferHint")}</p>
                  </div>
                }
              >
                <Button
                  isIconOnly
                  aria-label={label("help")}
                  size="sm"
                  variant="light"
                  className="h-6 min-w-6"
                >
                  <AiOutlineQuestionCircle aria-hidden />
                </Button>
              </Tooltip>
            </div>
            <div
              aria-label={label("bulk.label")}
              role="group"
              className="flex flex-wrap items-center gap-1 rounded-lg bg-default-50 px-2 py-1.5"
            >
              <Button
                size="sm"
                variant="light"
                className="h-7 min-w-0 px-2 text-xs"
                isDisabled={!ready}
                onPress={() => {
                  if (operation.current || !ready) return;
                  editChoices((previous) => ({
                    ...previous,
                    selected: new Set(
                      tasks
                        .filter((task) => task.canSubmit && hasIdentity(task))
                        .map((task) => task.taskId),
                    ),
                  }));
                  setBulkResult(undefined);
                }}
              >
                {label("selectAll")}
              </Button>
              <Button
                size="sm"
                variant="light"
                className="h-7 min-w-0 px-2 text-xs"
                isDisabled={!ready || !selectedTasks.length}
                onPress={() => {
                  if (operation.current || !ready) return;
                  editChoices((previous) => ({ ...previous, selected: new Set() }));
                  setBulkResult(undefined);
                }}
              >
                {label("clearSelection")}
              </Button>
              <span aria-hidden className="mx-1 h-4 border-l border-default-300" />
              <span className="text-xs text-default-500">
                {label("bulk.scope", { count: selectedTasks.length })}
              </span>
              {(["buy", "notRequired", "defer"] as const).map((action) => (
                <Button
                  key={action}
                  size="sm"
                  variant="flat"
                  className="h-7 min-w-0 px-2 text-xs"
                  isDisabled={!ready || !selectedTasks.length}
                  onPress={() => applyBulkAction(action)}
                >
                  {label(`bulk.${action}`)}
                </Button>
              ))}
              <Tooltip content={label("bulk.hint")}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  className="h-6 min-w-6"
                  aria-label={label("bulk.help")}
                >
                  <AiOutlineInfoCircle aria-hidden />
                </Button>
              </Tooltip>
              {bulkResult?.key === snapshotKey && (
                <span role="status" className="text-xs text-default-500">
                  {label("bulk.result", bulkResult)}
                </span>
              )}
            </div>
            {!tasks.length && <p role="status">{label("noTasks")}</p>}
            <div className="space-y-2">
              {tasks.map((task) => {
                const title = task.title?.trim() || label("taskTitle", { id: task.taskId });
                const available = task.canSubmit && hasIdentity(task);
                return (
                  <section
                    key={task.taskId}
                    aria-label={title}
                    className="min-w-0 rounded-lg border border-default-200 px-2.5 py-2"
                  >
                    <div className="flex min-w-0 items-start gap-2">
                      <Checkbox
                        aria-label={label("selectTask", { id: task.taskId, title })}
                        isSelected={currentChoices.selected.has(task.taskId)}
                        isDisabled={!ready || !available}
                        className="mt-0.5"
                        onValueChange={(selected) => {
                          if (operation.current || !ready) return;
                          editChoices((previous) => {
                            const next = new Set(previous.selected);
                            if (selected) next.add(task.taskId);
                            else next.delete(task.taskId);
                            return { ...previous, selected: next };
                          });
                          setBulkResult(undefined);
                        }}
                      />
                      <div className="min-w-0 flex-1">
                        <div className="flex min-w-0 items-baseline gap-2">
                          {task.link ? (
                            <ExternalLink
                              href={task.link}
                              className="min-w-0 truncate font-medium"
                              title={task.link}
                            >
                              #{task.taskId} {title}
                            </ExternalLink>
                          ) : (
                            <p className="truncate font-medium" title={title}>
                              #{task.taskId} {title}
                            </p>
                          )}
                          {sourceDomain(task.link) && (
                            <span className="hidden max-w-[12rem] shrink-0 truncate text-xs text-default-400 sm:inline">
                              {sourceDomain(task.link)}
                            </span>
                          )}
                        </div>
                        {task.alreadyUnlocked && (
                          <p className="text-xs text-success-600">{label("alreadyUnlocked")}</p>
                        )}
                        {!available && (
                          <p className="text-xs text-warning-600">
                            {task.blockReason
                              ? purchaseSkipReasonLabel(t, task.blockReason)
                              : label("taskUnavailable")}
                          </p>
                        )}
                      </div>
                      {onInspectTask && (
                        <Button
                          size="sm"
                          variant="light"
                          className="h-7 shrink-0 px-2 text-xs"
                          isDisabled={busy}
                          onPress={() => onInspectTask(task)}
                        >
                          {label("inspectTask")}
                        </Button>
                      )}
                    </div>
                    {!!task.targets.length && (
                      <div className="mt-1 divide-y divide-default-100">
                        {task.targets.map((target, index) => {
                          const targetLabel = target.floor
                            ? label("targetFloor", { floor: target.floor })
                            : label("targetLabel", { index: index + 1 });
                          const targetKey = `${task.taskId}:${target.targetId}`;
                          const targetId = `${detailId}-${task.taskId}-${index}`;
                          const expanded = expandedTargets.has(targetKey);
                          const targetAction = actionFor(task, target);
                          const unavailableReason =
                            !canBuy(target) && !paidTarget(target)
                              ? target.buyBlockReason
                                ? purchaseSkipReasonLabel(t, target.buyBlockReason)
                                : label("cannotBuy")
                              : undefined;
                          return (
                            <div key={target.targetId} className="py-1.5">
                              <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
                                <div className="flex min-w-0 flex-wrap items-center gap-1.5">
                                  <span className="text-xs font-medium">{targetLabel}</span>
                                  <Chip
                                    size="sm"
                                    variant="flat"
                                    classNames={{ base: "h-5", content: "px-1 text-[10px]" }}
                                  >
                                    {label("blocks", { count: target.blockIds.length })}
                                  </Chip>
                                  {!!target.blocks.length && (
                                    <Button
                                      size="sm"
                                      variant="light"
                                      className="h-6 min-w-0 px-1.5 text-xs"
                                      aria-label={label("details")}
                                      aria-expanded={expanded}
                                      aria-controls={targetId}
                                      onPress={() =>
                                        setExpandedTargets((previous) => {
                                          const next = new Set(previous);
                                          if (next.has(targetKey)) next.delete(targetKey);
                                          else next.add(targetKey);
                                          return next;
                                        })
                                      }
                                    >
                                      {label("detailsShort")}
                                    </Button>
                                  )}
                                </div>
                                <p className="min-w-0 break-all text-xs" title={label("quote")}>
                                  <span className="text-default-500">{label("quoteShort")}：</span>
                                  <span className="font-semibold tabular-nums">
                                    {formatSourceAmount(
                                      target.priceText,
                                      target.price,
                                      label("priceUnknown"),
                                    )}{" "}
                                    {sourceCurrencyLabel(
                                      target.currencyName,
                                      target.currencyCode,
                                      undefined,
                                      label("currencyUnknown"),
                                    )}
                                  </span>
                                </p>
                                {paidTarget(target) ? (
                                  <p className="text-xs text-success-600">
                                    {label("alreadyUnlocked")}
                                  </p>
                                ) : (
                                  <>
                                    <RadioGroup
                                      aria-label={label("actionLabel", { target: targetLabel })}
                                      orientation="horizontal"
                                      className="sm:ml-auto"
                                      classNames={{ wrapper: "flex-wrap gap-x-3 gap-y-1" }}
                                      value={targetAction}
                                      isDisabled={!ready || !available}
                                      onValueChange={(action) => {
                                        if (
                                          operation.current ||
                                          !ready ||
                                          !available ||
                                          (action !== "buy" &&
                                            action !== "notRequired" &&
                                            action !== "defer")
                                        )
                                          return;
                                        if (
                                          (action === "buy" && !canBuy(target)) ||
                                          (action === "notRequired" && !canMarkNotRequired(target))
                                        )
                                          return;
                                        editChoices((previous) => ({
                                          ...previous,
                                          actions: {
                                            ...previous.actions,
                                            [task.taskId]: {
                                              ...previous.actions[task.taskId],
                                              [target.targetId]: action,
                                            },
                                          },
                                        }));
                                        setBulkResult(undefined);
                                      }}
                                    >
                                      <Radio value="buy" size="sm" isDisabled={!canBuy(target)}>
                                        {label("action.buy")}
                                      </Radio>
                                      <Radio
                                        value="notRequired"
                                        size="sm"
                                        isDisabled={!canMarkNotRequired(target)}
                                      >
                                        {label("action.notRequired")}
                                      </Radio>
                                      <Radio value="defer" size="sm">
                                        {label("action.defer")}
                                      </Radio>
                                    </RadioGroup>
                                    <Tooltip
                                      content={
                                        <div className="max-w-md space-y-1 text-xs">
                                          {unavailableReason && <p>{unavailableReason}</p>}
                                          <p>
                                            {label(
                                              targetAction === "notRequired"
                                                ? "notRequiredHint"
                                                : targetAction === "defer"
                                                  ? "deferHint"
                                                  : "quoteHint",
                                            )}
                                          </p>
                                        </div>
                                      }
                                    >
                                      <Button
                                        isIconOnly
                                        size="sm"
                                        variant="light"
                                        className="h-6 min-w-6 text-default-400"
                                        aria-label={label("actionHint", { target: targetLabel })}
                                      >
                                        <AiOutlineInfoCircle aria-hidden />
                                      </Button>
                                    </Tooltip>
                                  </>
                                )}
                                {unavailableReason && (
                                  <Tooltip focusableTrigger content={unavailableReason}>
                                    <span className="inline-flex shrink-0">
                                      <Chip
                                        size="sm"
                                        color="warning"
                                        variant="flat"
                                        classNames={{ base: "h-5", content: "px-1 text-[10px]" }}
                                      >
                                        {label(
                                          `status.${compactStatusCodes.has(target.buyBlockReason ?? "") ? target.buyBlockReason : "other"}`,
                                        )}
                                      </Chip>
                                    </span>
                                  </Tooltip>
                                )}
                              </div>
                              {expanded && (
                                <div id={targetId} className="mt-2 rounded-lg bg-default-50 p-2">
                                  <LockedContentDetails
                                    content={{
                                      locks: target.blocks,
                                      currencyCode: target.currencyCode,
                                      currencyName: target.currencyName,
                                    }}
                                    compact
                                  />
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </section>
                );
              })}
            </div>
          </>
        )}
        {!busy && blockedKey === snapshotKey && (
          <p role="status" className="text-xs text-warning-600">
            {label("refreshRequired")}
          </p>
        )}
        {currentResult && (
          <section
            aria-label={label("result.title")}
            className="space-y-2 rounded-lg bg-default-50 p-3"
          >
            <h3 className="font-medium">{label("result.title")}</h3>
            <div role="status" className="flex flex-wrap gap-x-4 gap-y-1">
              <span>{label("result.queued", { count: currentResult.queuedCount })}</span>
              <span>{label("result.unchanged", { count: currentResult.unchangedCount })}</span>
              <span>{label("result.skipped", { count: currentResult.skippedCount })}</span>
              <span>{label("result.failed", { count: currentResult.failedCount })}</span>
            </div>
            <p className="text-xs text-default-500">{label("result.pendingNotice")}</p>
            <ul className="space-y-1 text-xs">
              {currentResult.items.map((item, index) => (
                <li key={`${item.taskId}-${index}`} className="break-words">
                  {label("result.item", {
                    id: item.taskId,
                    status: label(`result.status.${item.status}`),
                  })}
                  {item.reason ? ` — ${purchaseSkipReasonLabel(t, item.reason)}` : ""}
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </Modal>
  );
}
