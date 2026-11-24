import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Checkbox,
  Chip,
  Modal,
  Pagination,
  Select,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { bytesToSize } from "@/components/utils";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import { localBatchReasonLabel } from "./localBatchReasonLabel";
import AcquisitionQueueStatusChip from "./AcquisitionQueueStatusChip";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import AcquisitionContinueScopeSelect, {
  type AcquisitionContinuationNavigation,
} from "./AcquisitionContinueScopeSelect";
import {
  acquisitionLocalBatchApi,
  localBatchSelectionLimit,
  type LocalBatch,
  type LocalBatchPreview,
  type LocalBatchCapacity,
} from "./acquisitionLocalBatchApi";

import StorageReserveCheckbox, {
  emergencyReserveBytes,
  type StorageReserveMode,
} from "@/components/Acquire/StorageReserveCheckbox";

const knownBatchStates = [
  "queued",
  "starting",
  "running",
  "paused",
  "spaceBlocked",
  "completed",
  "failed",
  "cancelled",
];
const knownRowStates = [
  ...knownBatchStates,
  "checking",
  "submitted",
  "attention",
  "skipped",
  "superseded",
];
const preferredActionOrder = ["continue", "retryTransfer", "reanalyze"];
export type LocalBatchSelection = Partial<Record<number, string>>;
type PreviewRow = LocalBatchPreview["rows"][number];
const supportsAction = (row: PreviewRow, action: string) =>
  row.availableActions?.includes(action) === true;
const usesCompletedDownload = (row: PreviewRow) =>
  row.reasonCode === "RECOVERY_COMPLETED_DOWNLOAD_READY";
const needsSinglePausedCheck = (row: PreviewRow) =>
  row.reasonCode === "RECOVERY_PAUSED_ORIGINAL_CHECK_TIMED_OUT";
const resumesOriginalDownload = (row: PreviewRow) =>
  row.actionCategory === "workflow" &&
  row.currentStepKind === "download.awaitCompletion" &&
  !usesCompletedDownload(row);
const actionLabel = (row: PreviewRow) => {
  if (!supportsAction(row, "continue")) return "acquisition.continueActions.action.unavailable";
  if (row.recoveryAction === "start") return "acquisition.continueActions.action.start";
  if (row.recoveryAction === "retryTransfer")
    return "acquisition.continueActions.action.retryTransfer";
  if (usesCompletedDownload(row)) return "acquisition.continueActions.action.completedDownload";
  if (row.recoveryAction === "verifyAndResume")
    return resumesOriginalDownload(row)
      ? "acquisition.continueActions.action.verifyDownload"
      : "acquisition.continueActions.action.verifyStep";
  if (resumesOriginalDownload(row)) return "acquisition.continueActions.action.resumeDownload";
  if (row.actionCategory === "localProcessing" && row.recoveryScope === "workflow")
    return "acquisition.continueActions.action.resumeLocal";
  return row.actionCategory === "localProcessing"
    ? "acquisition.continueActions.action.localProcessing"
    : "acquisition.continueActions.action.workflow";
};
const previewPageSize = 20;
const allPreviewRows = "all";
type QueueReference = NonNullable<LocalBatchPreview["rows"][number]["queue"]>;
const measured = (value: number | null | undefined) =>
  typeof value === "number" && Number.isFinite(value) && value >= 0;
const isFinished = (batch?: LocalBatch) =>
  batch?.status === "completed" || batch?.status === "cancelled";
const validCapacity = (
  value: Pick<LocalBatchPreview, "volumes" | "requiresSpaceConfirmation" | "estimateIncomplete">,
) =>
  typeof value.requiresSpaceConfirmation === "boolean" &&
  typeof value.estimateIncomplete === "boolean" &&
  Array.isArray(value.volumes) &&
  value.volumes.every(
    (volume) =>
      typeof volume.volumeId === "string" &&
      Array.isArray(volume.paths) &&
      volume.paths.every((path) => typeof path === "string") &&
      [volume.availableBytes, volume.totalBytes, volume.minimumFreeBytes, volume.usableBytes].every(
        measured,
      ) &&
      (volume.estimatedAdditionalBytes == null || measured(volume.estimatedAdditionalBytes)) &&
      typeof volume.exceedsUsable === "boolean" &&
      typeof volume.estimateIncomplete === "boolean",
  );
const validPreview = (value: LocalBatchPreview, ids: number[]) =>
  !!value.previewToken &&
  !!parseServerTime(value.expiresAt) &&
  typeof value.requiresSpaceConfirmation === "boolean" &&
  typeof value.estimateIncomplete === "boolean" &&
  Array.isArray(value.rows) &&
  value.rows.length === ids.length &&
  new Set(value.rows.map((row) => row.taskId)).size === ids.length &&
  value.rows.every(
    (row) =>
      ids.includes(row.taskId) &&
      typeof row.canQueue === "boolean" &&
      typeof row.reasonCode === "string" &&
      typeof row.estimatePartial === "boolean" &&
      (row.estimatedBytes == null || measured(row.estimatedBytes)) &&
      (!row.canQueue ||
        ((row.recoveryScope === "workflow" || row.recoveryScope === "localFiles") &&
          (row.workflowRunId == null
            ? row.recoveryScope === "localFiles" || row.recoveryAction === "start"
            : Number.isSafeInteger(row.workflowRunId) && row.workflowRunId > 0))),
  ) &&
  Array.isArray(value.volumes) &&
  value.volumes.every(
    (volume) =>
      typeof volume.volumeId === "string" &&
      Array.isArray(volume.paths) &&
      volume.paths.every((path) => typeof path === "string") &&
      [volume.availableBytes, volume.totalBytes, volume.minimumFreeBytes, volume.usableBytes].every(
        measured,
      ) &&
      (volume.estimatedAdditionalBytes == null || measured(volume.estimatedAdditionalBytes)) &&
      typeof volume.exceedsUsable === "boolean" &&
      typeof volume.estimateIncomplete === "boolean",
  );

/** Selection and acknowledgement are fixed by a server preview; opening never starts a task. */
export default function AcquisitionLocalBatchModal({
  taskIds,
  scopeLabel,
  onChanged,
  onDestroyed,
  continuationScope,
  onContinuationScopeChange,
  onShowContinuationHistory,
  initialBatchId,
  onBackToSelection,
  automatic = false,
  onOpenQueue,
  onShowDownloads,
  onOpenTask,
  onInspectPausedOriginal,
  initialSelection,
  onSelectionChange,
}: DestroyableProps &
  AcquisitionContinuationNavigation & {
    taskIds?: number[];
    scopeLabel?: string;
    onChanged?: () => void;
    onShowContinuationHistory?: () => void;
    initialBatchId?: string;
    onBackToSelection?: () => void;
    automatic?: boolean;
    onOpenQueue?: (queue: QueueReference) => void;
    onShowDownloads?: () => void;
    onOpenTask?: (id: number) => void;
    onInspectPausedOriginal?: (id: number) => void;
    initialSelection?: LocalBatchSelection;
    onSelectionChange?: (selection: LocalBatchSelection) => void;
  }) {
  const { t } = useTranslation();
  const captured = useRef([
    ...new Set((taskIds ?? []).filter((id) => Number.isSafeInteger(id) && id > 0)),
  ]);
  const [visible, setVisible] = useState(true);
  const [mode, setMode] = useState<"preview" | "queue" | "list">(
    initialBatchId ? "queue" : captured.current.length ? "preview" : "list",
  );
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<LocalBatchPreview>();
  const [batch, setBatch] = useState<LocalBatch>();
  const [batches, setBatches] = useState<LocalBatch[]>([]);
  const [reserveChoice, setReserveChoice] = useState<boolean>();
  const [cancelRequested, setCancelRequested] = useState(false);
  const [error, setError] = useState<"preview" | "list" | "refresh" | "create" | "command">();
  const [errorCode, setErrorCode] = useState<string>();
  const [now, setNow] = useState(Date.now());
  const [selection, setSelection] = useState<LocalBatchSelection>({});
  const [maxIterations, setMaxIterations] = useState(3);
  const [previewPage, setPreviewPage] = useState(1);
  const [reasonFilter, setReasonFilter] = useState(allPreviewRows);
  const [capacityResult, setCapacityResult] = useState<{
    key: string;
    value: LocalBatchCapacity;
  }>();
  const [capacityError, setCapacityError] = useState<string>();
  const [capacityRetry, setCapacityRetry] = useState(0);
  const retainedSelection = useRef(initialSelection);
  const alive = useRef(false);
  const mutation = useRef(false);
  const requestSequence = useRef(0);
  const queueGeneration = useRef(0);
  const currentBatch = useRef<LocalBatch>();
  currentBatch.current = batch;
  const attempted = useRef(new Set<string>());
  const initialPreview = useRef<Promise<LocalBatchPreview>>();
  const initialQueue = useRef<Promise<LocalBatch>>();
  const previewController = useRef<AbortController>();
  const changed = useRef(onChanged);
  changed.current = onChanged;
  const labelBytes = (value?: number | null) =>
    measured(value) ? bytesToSize(value) : t("acquisition.size.unknown");
  const reason = (code?: string | null) => localBatchReasonLabel(t, code);
  const rowHint = (row: PreviewRow) => {
    if (row.reasonCode === "LOCAL_BATCH_READY") {
      if (row.recoveryAction === "verifyAndResume")
        return t(resumesOriginalDownload(row) ? "acquisition.continueActions.verifyDownloadHint" : "acquisition.continueActions.verifyStepHint");
      if (row.actionCategory === "workflow")
        return t(resumesOriginalDownload(row) ? "acquisition.continueActions.originalDownloadHint" : "acquisition.recovery.reason.CONTINUATION_READY");
    }
    return reason(row.reasonCode);
  };
  const reportError = (kind: typeof error, failure: unknown) => {
    setError(kind);
    setErrorCode(
      failure instanceof Error && /^[A-Z][A-Z0-9_]{2,100}$/.test(failure.message)
        ? failure.message
        : undefined,
    );
  };
  const loadPreview = useCallback(
    async (refresh = false) => {
      if (mutation.current) return;
      const sequence = ++requestSequence.current;
      setLoading(true);
      setPreview(undefined);
      setReserveChoice(undefined);
      setSelection({});
      setPreviewPage(1);
      setReasonFilter(allPreviewRows);
      setError(undefined);
      setErrorCode(undefined);
      try {
        if (captured.current.length > localBatchSelectionLimit) return;
        if (refresh || !initialPreview.current) {
          previewController.current?.abort();
          previewController.current = new AbortController();
          initialPreview.current = automatic
            ? acquisitionLocalBatchApi.preview(
                captured.current,
                previewController.current.signal,
                "automatic",
                maxIterations,
              )
            : acquisitionLocalBatchApi.preview(captured.current, previewController.current.signal);
        }
        const next = await initialPreview.current;
        if (!alive.current || requestSequence.current !== sequence) return;
        if (!validPreview(next, captured.current)) throw new Error("LOCAL_BATCH_PREVIEW_INVALID");
        if (
          automatic &&
          next.rows.some(
            (row) =>
              typeof row.canOverrideStorage !== "boolean" ||
              !Array.isArray(row.availableActions) ||
              row.availableActions.some((action) => typeof action !== "string" || !action.trim()) ||
              (row.queue != null &&
                (typeof row.queue.batchId !== "string" ||
                  !row.queue.batchId ||
                  !["localQueue", "continuation"].includes(row.queue.kind) ||
                  typeof row.queue.canRequeue !== "boolean")),
          )
        )
          throw new Error("LOCAL_BATCH_PREVIEW_INVALID");
        setPreview(next);
        if (automatic) {
          const nextSelection: LocalBatchSelection = {};
          for (const row of next.rows) {
            const retained = retainedSelection.current;
            const previous = retained?.[row.taskId];
            if (previous && supportsAction(row, previous) && (!refresh || previous === "continue"))
              nextSelection[row.taskId] = previous;
            else if (
              (!retained || previous) &&
              row.defaultAction === "continue" &&
              supportsAction(row, "continue")
            )
              nextSelection[row.taskId] = "continue";
          }
          setSelection(nextSelection);
        }
        setNow(Date.now());
      } catch (failure) {
        if (alive.current && requestSequence.current === sequence) reportError("preview", failure);
      } finally {
        if (alive.current && requestSequence.current === sequence) setLoading(false);
      }
    },
    [automatic, maxIterations],
  );
  const loadList = useCallback(async () => {
    if (mutation.current) return;
    const sequence = ++requestSequence.current;
    setLoading(true);
    setError(undefined);
    try {
      const next = await acquisitionLocalBatchApi.list();
      if (alive.current && requestSequence.current === sequence) setBatches(next);
    } catch (failure) {
      if (alive.current && requestSequence.current === sequence) reportError("list", failure);
    } finally {
      if (alive.current && requestSequence.current === sequence) setLoading(false);
    }
  }, []);
  const loadInitialQueue = useCallback(async () => {
    if (!initialBatchId || mutation.current) return;
    const sequence = ++requestSequence.current;
    setLoading(true);
    try {
      initialQueue.current ??= acquisitionLocalBatchApi.get(initialBatchId);
      const next = await initialQueue.current;
      if (!alive.current || sequence !== requestSequence.current) return;
      if (next.batchId !== initialBatchId) throw new Error("LOCAL_BATCH_NOT_FOUND");
      currentBatch.current = next;
      setBatch(next);
      setError(undefined);
    } catch (failure) {
      if (alive.current && sequence === requestSequence.current) reportError("refresh", failure);
    } finally {
      if (alive.current && sequence === requestSequence.current) setLoading(false);
    }
  }, [initialBatchId]);
  useEffect(() => {
    alive.current = true;
    if (mode === "preview") void loadPreview();
    else if (mode === "list") void loadList();
    else if (initialBatchId && !currentBatch.current) void loadInitialQueue();
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      alive.current = false;
      ++requestSequence.current;
      clearInterval(timer);
      queueMicrotask(() => {
        if (!alive.current) previewController.current?.abort();
      });
    };
  }, [mode, loadPreview, loadList, initialBatchId, loadInitialQueue]);
  useEffect(() => {
    if (mode !== "queue" || !batch || !visible || isFinished(batch)) return;
    let cancelled = false;
    let checking = false;
    const id = batch.batchId;
    const check = async () => {
      if (mutation.current || checking) return;
      checking = true;
      const generation = queueGeneration.current;
      try {
        const next = await acquisitionLocalBatchApi.get(id);
        if (
          !cancelled &&
          alive.current &&
          generation === queueGeneration.current &&
          currentBatch.current?.batchId === id &&
          !mutation.current
        ) {
          const changedState = JSON.stringify(currentBatch.current) !== JSON.stringify(next);
          currentBatch.current = next;
          setBatch(next);
          setError(undefined);
          if (changedState) changed.current?.();
        }
      } catch (failure) {
        if (!cancelled && alive.current && generation === queueGeneration.current)
          reportError("refresh", failure);
      } finally {
        checking = false;
      }
    };
    const timer = setInterval(() => void check(), 4000);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [mode, batch?.batchId, batch?.status, visible]);
  useEffect(() => {
    if (!automatic || !preview) return;
    retainedSelection.current = selection;
    onSelectionChange?.(selection);
  }, [automatic, preview, selection, onSelectionChange]);
  const rows = preview?.rows ?? [];
  const blockedReasons = new Map<string, number>();
  if (automatic) {
    for (const row of rows) {
      if (!supportsAction(row, "continue"))
        blockedReasons.set(row.reasonCode, (blockedReasons.get(row.reasonCode) ?? 0) + 1);
    }
  }
  const visibleRows =
    reasonFilter === allPreviewRows
      ? rows
      : rows.filter((row) => !supportsAction(row, "continue") && row.reasonCode === reasonFilter);
  const offered = new Set(rows.flatMap((row) => row.availableActions ?? []));
  const columns = [
    ...preferredActionOrder.filter((action) => action === "continue" || offered.has(action)),
    ...[...offered].filter((action) => !preferredActionOrder.includes(action)).sort(),
  ];
  const actionableCount = rows.filter((row) => row.availableActions?.length).length;
  const operationLabel = (action: string) => {
    const key = `acquisition.continueActions.operation.${action}`;
    const translated = t(key);
    if (translated !== key) return translated;
    const words = action
      .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
      .replace(/[._:-]+/g, " ")
      .trim();
    return words.charAt(0).toUpperCase() + words.slice(1);
  };
  const eligible = rows.filter((row) => {
    if (!automatic) return row.canQueue;
    const action = selection[row.taskId];
    return action && supportsAction(row, action);
  });
  const overrideRows = eligible.filter(
    (row) => !automatic || (selection[row.taskId] === "continue" && row.canOverrideStorage),
  );
  const requestKey = JSON.stringify(
    eligible.map((row) => ({
      taskId: row.taskId,
      action: automatic ? selection[row.taskId] : undefined,
    })),
  );
  const supportsReserveMode = typeof preview?.useEmergencySpaceReserve === "boolean";
  const globalLowered = preview?.useEmergencySpaceReserve === true;
  const configuredMinimum =
    preview?.configuredMinimumFreeBytes ?? preview?.volumes[0]?.minimumFreeBytes;
  const lowered = reserveChoice ?? globalLowered;
  const reserveMode: StorageReserveMode =
    lowered === globalLowered || (lowered && !overrideRows.length)
      ? "inherit"
      : lowered
        ? "emergency"
        : "configured";
  const capacityMode = supportsReserveMode ? reserveMode : undefined;
  const measureSelection = automatic || (supportsReserveMode && reserveMode !== "inherit");
  const capacityKey = JSON.stringify([preview?.previewToken, requestKey, capacityMode]);
  const capacity = measureSelection
    ? capacityResult?.key === capacityKey
      ? capacityResult.value
      : undefined
    : preview;
  const waitingForCapacity = measureSelection && !!preview && !!eligible.length && !capacity;
  useEffect(() => {
    if (!measureSelection || mode !== "preview" || !preview || !eligible.length) return;
    let cancelled = false;
    const controller = new AbortController();
    const choices: { taskId: number; action: string }[] = JSON.parse(requestKey);
    const ids = choices.map((choice) => choice.taskId);
    setCapacityError(undefined);
    const timer = setTimeout(() => {
      void acquisitionLocalBatchApi
        .capacity(
          preview.previewToken,
          ids,
          controller.signal,
          capacityMode,
          automatic ? choices : undefined,
        )
        .then((value) => {
          if (cancelled || !alive.current) return;
          if (
            value.previewToken !== preview.previewToken ||
            !Array.isArray(value.taskIds) ||
            value.taskIds.length !== ids.length ||
            new Set(value.taskIds).size !== ids.length ||
            value.taskIds.some((id) => !ids.includes(id)) ||
            (automatic &&
              (!Array.isArray(value.selections) ||
                value.selections.length !== choices.length ||
                choices.some(
                  (choice) =>
                    !value.selections?.some(
                      (item) => item.taskId === choice.taskId && item.action === choice.action,
                    ),
                ))) ||
            !validCapacity(value)
          )
            throw new Error("LOCAL_BATCH_CAPACITY_INVALID");
          setCapacityResult({ key: capacityKey, value });
          setCapacityError(undefined);
        })
        .catch(() => {
          if (!cancelled && alive.current) setCapacityError(capacityKey);
        });
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
      controller.abort();
    };
  }, [
    measureSelection,
    mode,
    preview?.previewToken,
    requestKey,
    capacityRetry,
    capacityMode,
    automatic,
  ]);
  const showOverride =
    configuredMinimum != null &&
    configuredMinimum > emergencyReserveBytes &&
    (overrideRows.length > 0 || (globalLowered && eligible.length > 0)) &&
    (globalLowered ||
      reserveChoice != null ||
      capacity?.requiresSpaceConfirmation ||
      capacity?.estimateIncomplete ||
      overrideRows.some((row) => row.actionCategory === "workflow"));
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const consumed = !!preview && attempted.current.has(preview.previewToken);
  const selectionLocked = loading || busy || !preview || expired || consumed;
  const locked = selectionLocked || !eligible.length || waitingForCapacity;
  const selectAction = (action: string, targets: PreviewRow[], checked: boolean) => {
    setSelection((previous) => {
      const next = { ...previous };
      for (const row of targets) {
        if (!supportsAction(row, action)) continue;
        if (checked) next[row.taskId] = action;
        else if (next[row.taskId] === action) delete next[row.taskId];
      }
      return next;
    });
    setReserveChoice(undefined);
  };
  const submit = async () => {
    if (locked || !preview || mutation.current) return;
    mutation.current = true;
    attempted.current.add(preview.previewToken);
    setBusy(true);
    setError(undefined);
    try {
      const createArgs = [
        preview.previewToken,
        eligible.map((row) => row.taskId),
        reserveMode === "emergency",
        capacityMode,
      ] as const;
      const next = automatic
        ? await acquisitionLocalBatchApi.create(
            ...createArgs,
            eligible.map((row) => ({
              taskId: row.taskId,
              action: selection[row.taskId]!,
            })),
          )
        : await acquisitionLocalBatchApi.create(...createArgs);
      if (alive.current) {
        ++queueGeneration.current;
        currentBatch.current = next;
        setBatch(next);
        setMode("queue");
        setCancelRequested(false);
      }
      changed.current?.();
    } catch (failure) {
      if (alive.current) reportError("create", failure);
      changed.current?.();
    } finally {
      mutation.current = false;
      if (alive.current) setBusy(false);
    }
  };
  const command = async (action: "pause" | "resume" | "cancel") => {
    if (!batch || mutation.current) return;
    mutation.current = true;
    ++queueGeneration.current;
    setBusy(true);
    setError(undefined);
    try {
      const next = await acquisitionLocalBatchApi[action](batch.batchId);
      if (alive.current) {
        currentBatch.current = next;
        setBatch(next);
        setCancelRequested(false);
      }
      changed.current?.();
    } catch (failure) {
      if (alive.current) reportError("command", failure);
      changed.current?.();
    } finally {
      mutation.current = false;
      if (alive.current) setBusy(false);
    }
  };
  const close = () => {
    if (mutation.current) return;
    alive.current = false;
    ++requestSequence.current;
    ++queueGeneration.current;
    previewController.current?.abort();
    setVisible(false);
  };
  const statusLabel = (status: string, row = false) =>
    !row && status === "completed"
      ? t("acquisition.localBatch.ended")
      : t(
          `acquisition.localBatch.status.${(row ? knownRowStates : knownBatchStates).includes(status) ? status : "unknown"}`,
        );
  const hasUnstarted = batch?.rows.some((row) =>
    ["queued", "paused", "spaceBlocked"].includes(row.status),
  );

  return (
    <Modal
      visible={visible}
      size="5xl"
      scrollBehavior="inside"
      title={t(
        automatic
          ? mode === "preview"
            ? "acquisition.continue.title"
            : "acquisition.continueMode.queueTitle"
          : continuationScope
            ? mode === "preview"
              ? "acquisition.continueMode.title"
              : "acquisition.continueMode.queueTitle"
            : mode === "preview"
              ? "acquisition.localBatch.title"
              : "acquisition.localBatch.openQueue",
      )}
      onClose={close}
      onDestroyed={onDestroyed}
      isDismissable={!busy}
      isKeyboardDismissDisabled={busy}
      hideCloseButton={busy}
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button variant="light" isDisabled={busy} onPress={close}>
            {t("common.action.close")}
          </Button>
          {mode === "preview" && (
            <Button
              color={reserveMode === "emergency" ? "warning" : "primary"}
              isDisabled={locked}
              isLoading={busy}
              onPress={() => void submit()}
            >
              {t(
                reserveMode === "emergency" && showOverride
                  ? "acquisition.localBatch.confirmOverride"
                  : automatic
                    ? "acquisition.continueActions.confirm"
                    : "acquisition.localBatch.confirm",
                { count: eligible.length },
              )}
            </Button>
          )}
          {mode === "queue" && batch && !isFinished(batch) && hasUnstarted && (
            <>
              {(batch.status === "paused" || batch.status === "spaceBlocked") && (
                <Button
                  color="primary"
                  isLoading={busy}
                  isDisabled={busy || !!error}
                  onPress={() => void command("resume")}
                >
                  {t("acquisition.localBatch.resume")}
                </Button>
              )}
              {(batch.status === "running" || batch.status === "queued") && (
                <Button
                  variant="flat"
                  isDisabled={busy || !!error}
                  onPress={() => void command("pause")}
                >
                  {t("acquisition.localBatch.pause")}
                </Button>
              )}
              {!cancelRequested ? (
                <Button
                  color="danger"
                  variant="light"
                  isDisabled={busy || !!error}
                  onPress={() => setCancelRequested(true)}
                >
                  {t("acquisition.localBatch.cancel")}
                </Button>
              ) : (
                <>
                  <Button
                    variant="light"
                    isDisabled={busy}
                    onPress={() => setCancelRequested(false)}
                  >
                    {t("acquisition.localBatch.keepQueue")}
                  </Button>
                  <Button
                    color="danger"
                    isDisabled={busy || !!error}
                    onPress={() => void command("cancel")}
                  >
                    {t("acquisition.localBatch.confirmCancel")}
                  </Button>
                </>
              )}
            </>
          )}
        </div>
      }
    >
      {onBackToSelection && (
        <Button
          size="sm"
          variant="light"
          className="self-start"
          isDisabled={busy}
          onPress={onBackToSelection}
        >
          {t("acquisition.continueActions.back")}
        </Button>
      )}
      {mode === "preview" && !automatic && continuationScope && onContinuationScopeChange && (
        <AcquisitionContinueScopeSelect
          value={continuationScope}
          disabled={busy}
          onChange={(scope) => {
            if (!mutation.current) onContinuationScopeChange(scope);
          }}
        />
      )}
      {scopeLabel && mode === "preview" && <p className="text-xs text-default-500">{scopeLabel}</p>}
      {automatic && mode === "preview" && (
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
          <p className="text-default-500">{t("acquisition.continueActions.hint")}</p>
          {onShowDownloads && (
            <Button size="sm" variant="light" isDisabled={busy} onPress={onShowDownloads}>
              {t("acquisition.continueActions.downloadsOnly")}
            </Button>
          )}
        </div>
      )}
      {onShowContinuationHistory && (
        <div className="flex justify-end">
          <Button
            size="sm"
            variant="flat"
            isDisabled={busy}
            onPress={() => {
              if (!mutation.current) onShowContinuationHistory();
            }}
          >
            {t("acquisition.continueMode.otherHistory")}
          </Button>
        </div>
      )}
      {mode === "preview" && captured.current.length > localBatchSelectionLimit && (
        <p role="alert" className="rounded-lg bg-warning-50 p-3 text-sm text-warning-700">
          {t(
            automatic ? "acquisition.continueActions.limit" : "acquisition.continueMode.localLimit",
            { count: captured.current.length, limit: localBatchSelectionLimit },
          )}
        </p>
      )}
      {!automatic && (!continuationScope || mode !== "preview") && (
        <p className="text-xs text-default-500">{t("acquisition.localBatch.explanation")}</p>
      )}
      {error && (
        <div role="alert" className="space-y-2 text-sm text-danger">
          <p>{t(`acquisition.localBatch.error.${error}`)}</p>
          {errorCode && <p className="text-xs">{reason(errorCode)}</p>}
          <div className="flex gap-2">
            {mode === "preview" && error !== "create" && (
              <Button size="sm" variant="flat" onPress={() => void loadPreview(true)}>
                {t("acquisition.localBatch.refreshPreview")}
              </Button>
            )}
            <Button
              size="sm"
              variant="flat"
              onPress={() => {
                ++queueGeneration.current;
                if (mode === "list") void loadList();
                else setMode("list");
              }}
            >
              {t("acquisition.localBatch.openQueue")}
            </Button>
          </div>
        </div>
      )}
      {loading && (mode !== "queue" || !batch) ? (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3">
          <Spinner aria-label={t("acquisition.localBatch.loading")} />
          <p className="text-center text-xs text-default-500">
            {automatic
              ? t("acquisition.continueActions.previewing", { count: captured.current.length })
              : t("acquisition.localBatch.previewOnly")}
          </p>
        </div>
      ) : mode === "preview" && preview ? (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
            <div className="flex gap-2">
              <Chip size="sm" variant="flat">
                {t("acquisition.localBatch.selected", { count: captured.current.length })}
              </Chip>
              <Chip size="sm" variant="flat" color="success">
                {t(
                  automatic
                    ? "acquisition.continueActions.matrix.selected"
                    : "acquisition.localBatch.eligible",
                  { count: eligible.length },
                )}
              </Chip>
              <Chip size="sm" variant="flat">
                {t(
                  automatic
                    ? "acquisition.continueActions.matrix.unavailable"
                    : "acquisition.localBatch.skipped",
                  { count: rows.length - (automatic ? actionableCount : eligible.length) },
                )}
              </Chip>
            </div>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() => void loadPreview(true)}
            >
              {t("acquisition.localBatch.refreshPreview")}
            </Button>
          </div>
          {automatic && !eligible.length && (
            <p role="status" className="rounded-lg bg-default-50 p-3 text-sm text-default-600">
              {t(
                actionableCount > 0
                  ? "acquisition.continueActions.noSelectionHint"
                  : "acquisition.continueActions.noEligibleHint",
              )}
            </p>
          )}
          {automatic && blockedReasons.size > 0 && (
            <Select
              size="sm"
              className="max-w-lg"
              aria-label={t("acquisition.continueActions.reasonFilter.label")}
              label={t("acquisition.continueActions.reasonFilter.label")}
              disallowEmptySelection
              isDisabled={busy}
              selectedKeys={[reasonFilter]}
              dataSource={[
                {
                  value: allPreviewRows,
                  label: t("acquisition.continueActions.reasonFilter.all", { count: rows.length }),
                },
                ...[...blockedReasons.entries()]
                  .sort((left, right) => right[1] - left[1])
                  .map(([code, count]) => ({
                    value: code,
                    label: t("acquisition.continueActions.reasonFilter.reason", {
                      count,
                      reason: reason(code),
                    }),
                  })),
              ]}
              onSelectionChange={(keys) => {
                const next = Array.from(keys)[0];
                if (
                  typeof next !== "string" ||
                  (next !== allPreviewRows && !blockedReasons.has(next))
                )
                  return;
                setReasonFilter(next);
                setPreviewPage(1);
              }}
            />
          )}
          {expired && (
            <p role="alert" className="text-sm text-warning-600">
              {t("acquisition.localBatch.expired")}
            </p>
          )}
          {automatic && columns.includes("reanalyze") && (
            <Select
              size="sm"
              className="max-w-xs"
              label={t("acquisition.adaptive.iterations")}
              aria-label={t("acquisition.adaptive.iterations")}
              disallowEmptySelection
              selectedKeys={[String(maxIterations)]}
              isDisabled={selectionLocked}
              dataSource={[1, 2, 3, 4, 5].map((count) => ({
                value: String(count),
                label: String(count),
              }))}
              onSelectionChange={(keys) => {
                const count = Number(Array.from(keys)[0]);
                if (!Number.isInteger(count) || count < 1 || count > 5 || count === maxIterations)
                  return;
                initialPreview.current = undefined;
                setMaxIterations(count);
              }}
            />
          )}
          <div className="overflow-x-auto rounded-lg border border-default-200">
            <table className="w-full border-collapse text-left text-xs">
              {automatic && (
                <thead className="border-b border-default-200 bg-default-100 text-default-700">
                  <tr>
                    <th scope="col" className="p-2.5">
                      {t("acquisition.continueActions.matrix.task")}
                    </th>
                    <th scope="col" className="p-2.5 text-right">
                      {t("acquisition.continueActions.matrix.size")}
                    </th>
                    {columns.map((action) => {
                      const targets = visibleRows.filter((row) => supportsAction(row, action));
                      const count = targets.filter(
                        (row) => selection[row.taskId] === action,
                      ).length;
                      return (
                        <th
                          key={action}
                          scope="col"
                          className="w-px whitespace-nowrap px-3 py-2.5 text-center"
                        >
                          <Checkbox
                            size="sm"
                            aria-label={operationLabel(action)}
                            isSelected={targets.length > 0 && count === targets.length}
                            isIndeterminate={count > 0 && count < targets.length}
                            isDisabled={selectionLocked || !targets.length}
                            onValueChange={(checked) => selectAction(action, targets, checked)}
                          >
                            {operationLabel(action)}
                          </Checkbox>
                        </th>
                      );
                    })}
                  </tr>
                </thead>
              )}
              <tbody className="divide-y divide-default-200">
                {(automatic
                  ? visibleRows.slice(
                      (previewPage - 1) * previewPageSize,
                      previewPage * previewPageSize,
                    )
                  : visibleRows
                ).map((row, index) => (
                  <tr
                    key={row.taskId}
                    data-testid={`continuation-task-${row.taskId}`}
                    className={automatic && !selection[row.taskId] ? "bg-default-50" : ""}
                  >
                    <td className="min-w-48 space-y-1 p-2.5 align-top">
                      <p className="break-words text-sm">
                        {!automatic && <span className="mr-3 text-default-400">{index + 1}</span>}
                        <span className="mr-2 text-default-500">#{row.taskId}</span>
                        {row.title}
                      </p>
                      {automatic && (
                        <p
                          className={
                            selection[row.taskId]
                              ? "font-medium text-foreground"
                              : "text-default-500"
                          }
                        >
                          {selection[row.taskId] && selection[row.taskId] !== "continue"
                            ? operationLabel(selection[row.taskId]!)
                            : t(actionLabel(row))}
                          {row.currentStepKind && (
                            <span className="ml-2 font-normal">
                              {activityDisplayName(t, row.currentStepKind)}
                            </span>
                          )}
                          {row.defaultAction && !selection[row.taskId] && (
                            <span className="ml-2">
                              {t("acquisition.continueActions.excluded")}
                            </span>
                          )}
                        </p>
                      )}
                      <p className={row.canQueue ? "text-default-500" : "text-warning-600"}>
                        {rowHint(row)}
                      </p>
                      {automatic && selection[row.taskId] === "retryTransfer" && (
                        <p className="text-default-600">
                          {t("acquisition.continueActions.transferRetry.hint")}
                        </p>
                      )}
                      {automatic && captured.current.length > 1 && onInspectPausedOriginal &&
                        needsSinglePausedCheck(row) && (
                          <Button
                            size="sm"
                            variant="light"
                            isDisabled={busy}
                            onPress={() => {
                              if (!mutation.current) onInspectPausedOriginal(row.taskId);
                            }}
                          >
                            {t("acquisition.continueActions.inspectPausedOriginal")}
                          </Button>
                        )}
                      {automatic &&
                        !supportsAction(row, "continue") &&
                        !row.queue &&
                        onOpenTask && (
                          <Button
                            size="sm"
                            variant="light"
                            isDisabled={busy}
                            onPress={() => {
                              if (mutation.current) return;
                              close();
                              onOpenTask(row.taskId);
                            }}
                          >
                            {t("acquisition.continueActions.openTask")}
                          </Button>
                        )}
                      {automatic && row.queue && (
                        <div className="flex flex-wrap items-center gap-2 rounded-md bg-default-100 p-2">
                          <span className="break-all">
                            {t("acquisition.continueActions.existingQueue", {
                              id: row.queue.batchId.slice(0, 8),
                            })}
                          </span>
                          <AcquisitionQueueStatusChip
                            status={
                              row.queue.kind === "localQueue"
                                ? row.queue.status
                                : row.queue.rowStatus
                            }
                          >
                            {row.queue.kind === "localQueue"
                              ? statusLabel(row.queue.status)
                              : t(
                                  `acquisition.continue.status.${["queued", "checking", "submitted", "attention", "skipped", "cancelled", "failed"].includes(row.queue.rowStatus) ? row.queue.rowStatus : "unknown"}`,
                                )}
                          </AcquisitionQueueStatusChip>
                          {onOpenQueue && (
                            <Button
                              size="sm"
                              variant="light"
                              isDisabled={busy}
                              onPress={() => onOpenQueue(row.queue!)}
                            >
                              {t("acquisition.continueActions.viewQueue")}
                            </Button>
                          )}
                          <p className="w-full text-xs text-default-500">
                            {t(
                              row.queue.canRequeue
                                ? "acquisition.continueActions.requeueHint"
                                : ["paused", "spaceBlocked"].includes(row.queue.status)
                                  ? "acquisition.continueActions.queueNotRunning"
                                  : "acquisition.continueActions.queueOwned",
                            )}
                          </p>
                        </div>
                      )}
                      {row.canQueue && (
                        <Tooltip
                          delay={1000}
                          placement="bottom"
                          content={
                            <div className="max-w-lg break-all text-xs">
                              <p>
                                {t("acquisition.localBatch.workingDirectory")}:{" "}
                                {row.workingDirectory ||
                                  t("acquisition.localBatch.noProcessingDirectory")}
                              </p>
                              <p>
                                {t("acquisition.localBatch.deliveryDirectory")}:{" "}
                                {row.deliveryDirectory}
                              </p>
                            </div>
                          }
                        >
                          <span className="cursor-help text-default-500 underline decoration-dotted">
                            {t("acquisition.localBatch.paths")}
                          </span>
                        </Tooltip>
                      )}
                    </td>
                    <td className="whitespace-nowrap p-2.5 text-right align-top text-default-500">
                      {labelBytes(row.estimatedBytes)}
                      {row.estimatePartial && (
                        <span className="block">{t("acquisition.size.partial.label")}</span>
                      )}
                    </td>
                    {automatic &&
                      columns.map((action) => (
                        <td key={action} className="px-3 py-2.5 text-center align-top">
                          {supportsAction(row, action) ? (
                            <Checkbox
                              size="sm"
                              aria-label={t("acquisition.continueActions.matrix.taskAction", {
                                id: row.taskId,
                                action: operationLabel(action),
                              })}
                              isSelected={selection[row.taskId] === action}
                              isDisabled={selectionLocked}
                              onValueChange={(checked) => selectAction(action, [row], checked)}
                            />
                          ) : (
                            <span className="text-default-400">—</span>
                          )}
                        </td>
                      ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {automatic && visibleRows.length > previewPageSize && (
            <Pagination
              page={previewPage}
              total={Math.ceil(visibleRows.length / previewPageSize)}
              onChange={setPreviewPage}
              isDisabled={busy}
            />
          )}
          <div className="space-y-2">
            {waitingForCapacity && capacityError !== capacityKey && (
              <div role="status" className="flex items-center gap-2 text-xs text-default-500">
                <Spinner size="sm" />
                {t("acquisition.continueActions.capacityLoading")}
              </div>
            )}
            {waitingForCapacity && capacityError === capacityKey && (
              <div role="alert" className="flex flex-wrap items-center gap-2 text-xs text-danger">
                {t("acquisition.continueActions.capacityFailed")}
                <Button
                  size="sm"
                  variant="light"
                  onPress={() => setCapacityRetry((value) => value + 1)}
                >
                  {t("common.action.retry")}
                </Button>
              </div>
            )}
            {(automatic && !eligible.length ? [] : (capacity?.volumes ?? [])).map((volume) => (
              <div
                key={volume.volumeId}
                className="rounded-lg border border-default-200 p-3 text-xs"
              >
                <div className="flex flex-wrap gap-x-5 gap-y-2">
                  <span>
                    {t("acquisition.localBatch.space.available", {
                      size: labelBytes(volume.availableBytes),
                    })}
                  </span>
                  <span>
                    {t("acquisition.localBatch.space.reserve", {
                      size: labelBytes(volume.minimumFreeBytes),
                    })}
                  </span>
                  <span className="font-medium">
                    {t("acquisition.localBatch.space.usable", {
                      size: labelBytes(volume.usableBytes),
                    })}
                  </span>
                  <span className={volume.exceedsUsable ? "font-medium text-warning-600" : ""}>
                    {t("acquisition.localBatch.space.estimated", {
                      size: labelBytes(volume.estimatedAdditionalBytes),
                    })}
                  </span>
                </div>
                {!!volume.paths.length && (
                  <Tooltip
                    delay={1000}
                    placement="bottom"
                    content={
                      <div className="max-h-56 max-w-lg space-y-1 overflow-y-auto break-all text-xs">
                        {volume.paths.map((path) => (
                          <p key={path}>{path}</p>
                        ))}
                      </div>
                    }
                  >
                    <p
                      tabIndex={0}
                      aria-label={t("acquisition.localBatch.space.paths", {
                        count: volume.paths.length,
                      })}
                      className="mt-2 flex min-w-0 cursor-help items-center gap-2 text-default-500"
                    >
                      <span className="min-w-0 truncate">{volume.paths[0]}</span>
                      {volume.paths.length > 1 && (
                        <span className="shrink-0 underline decoration-dotted">
                          {t("acquisition.localBatch.space.morePaths", {
                            count: volume.paths.length - 1,
                          })}
                        </span>
                      )}
                    </p>
                  </Tooltip>
                )}
                {typeof volume.currentWritesExceedUsable === "boolean" && (
                  <p className="mt-2 text-xs">
                    {t(volume.currentWritesExceedUsable
                      ? "acquisition.localBatch.space.checkpointInsufficient"
                      : "acquisition.localBatch.space.checkpointGuard")}
                  </p>
                )}
                {(volume.exceedsUsable || volume.estimateIncomplete) && (
                  <p className="mt-2 text-warning-600">
                    {t(
                      volume.exceedsUsable
                        ? "acquisition.localBatch.space.exceeds"
                        : "acquisition.localBatch.space.unknown",
                    )}
                  </p>
                )}
              </div>
            ))}
          </div>
          <p className="text-xs text-default-500">
            {t(
              automatic
                ? "acquisition.continueActions.capacityHint"
                : "acquisition.localBatch.space.estimationHint",
            )}
          </p>
          {capacity?.estimateIncomplete && (
            <p role="status" className="text-xs text-warning-600">
              {t("acquisition.localBatch.space.unknown")}
            </p>
          )}
          {showOverride && (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <StorageReserveCheckbox
                scope="operation"
                minimumBytes={configuredMinimum!}
                selected={lowered}
                disabled={busy || expired || consumed}
                onChange={setReserveChoice}
              />
              {automatic && !globalLowered && overrideRows.length !== eligible.length && (
                <span className="text-xs text-default-500">
                  {t("acquisition.continueActions.overrideScope", { count: overrideRows.length })}
                </span>
              )}
            </div>
          )}
        </>
      ) : mode === "list" ? (
        <>
          <div className="flex justify-end">
            <Button size="sm" variant="light" isDisabled={busy} onPress={() => void loadList()}>
              {t("acquisition.localBatch.refresh")}
            </Button>
          </div>
          {!batches.length && !error && (
            <p className="py-8 text-center text-sm text-default-500">
              {t("acquisition.localBatch.empty")}
            </p>
          )}
          <div className="space-y-2">
            {batches.map((item) => (
              <Button
                key={item.batchId}
                variant="flat"
                className="h-auto w-full justify-between gap-3 py-3 text-left"
                onPress={() => {
                  ++queueGeneration.current;
                  currentBatch.current = item;
                  setBatch(item);
                  setError(undefined);
                  setCancelRequested(false);
                  setMode("queue");
                }}
              >
                <span>
                  {parseServerTime(item.createdAt)?.toLocaleString()} ·{" "}
                  {t("acquisition.localBatch.selected", { count: item.rows.length })}
                </span>
                <AcquisitionQueueStatusChip status={item.status}>
                  {statusLabel(item.status)}
                </AcquisitionQueueStatusChip>
              </Button>
            ))}
          </div>
        </>
      ) : mode === "queue" && batch ? (
        <>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <AcquisitionQueueStatusChip status={batch.status}>
              {statusLabel(batch.status)}
            </AcquisitionQueueStatusChip>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() => {
                ++queueGeneration.current;
                setMode("list");
                setCancelRequested(false);
              }}
            >
              {t("acquisition.localBatch.allQueues")}
            </Button>
          </div>
          <p className="text-xs text-default-500">
            {t(
              batch.status === "spaceBlocked" || batch.status === "paused"
                ? "acquisition.continueActions.queueNotRunning"
                : "acquisition.localBatch.backgroundHint",
            )}
          </p>
          <p className="text-xs text-default-500" aria-live="polite">
            {t("acquisition.localBatch.progress", {
              completed: batch.rows.filter((row) => row.status === "completed").length,
              attention: batch.rows.filter(
                (row) => row.status === "attention" || row.status === "spaceBlocked",
              ).length,
              skipped: batch.rows.filter(
                (row) => row.status === "skipped" || row.status === "cancelled",
              ).length,
              remaining: batch.rows.filter((row) =>
                ["queued", "paused", "starting", "running"].includes(row.status),
              ).length,
            })}
          </p>
          {batch.status === "running" && batch.rows.some((row) => row.status === "paused") && (
            <p className="text-xs text-warning-600">
              {t("acquisition.localBatch.remainingPaused")}
            </p>
          )}
          {batch.allowStorageOverride && (
            <p className="text-xs text-warning-600">
              {t(
                batch.overrideExpiresAt && millisecondsUntil(batch.overrideExpiresAt, now) > 0
                  ? "acquisition.localBatch.overrideActive"
                  : "acquisition.localBatch.overrideExpired",
                { time: parseServerTime(batch.overrideExpiresAt)?.toLocaleString() ?? "—" },
              )}
            </p>
          )}
          {batch.status === "spaceBlocked" && (
            <p role="status" className="text-sm text-warning-600">
              {t("acquisition.localBatch.spaceBlockedHint")}
            </p>
          )}
          {cancelRequested && (
            <p role="alert" className="text-sm text-danger">
              {t("acquisition.localBatch.cancelHint")}
            </p>
          )}
          <div className="divide-y divide-default-200 rounded-lg border border-default-200">
            {batch.rows.map((row) => (
              <div key={row.taskId} className="flex items-start gap-3 p-2.5 text-xs">
                <span className="w-6 shrink-0 text-default-400">{row.order + 1}</span>
                <div className="min-w-0 flex-1 space-y-1">
                  <p className="break-words text-sm">
                    <span className="mr-2 text-default-500">#{row.taskId}</span>
                    {row.title}
                  </p>
                  {row.reasonCode && <p className="text-default-500">{reason(row.reasonCode)}</p>}
                </div>
                <span className="shrink-0 text-right">
                  <AcquisitionQueueStatusChip status={row.status}>
                    {statusLabel(row.status, true)}
                  </AcquisitionQueueStatusChip>
                  <span className="text-default-500">{labelBytes(row.estimatedBytes)}</span>
                </span>
              </div>
            ))}
          </div>
        </>
      ) : null}
    </Modal>
  );
}
