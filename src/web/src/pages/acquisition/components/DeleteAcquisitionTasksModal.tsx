import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Chip, Modal, Pagination, Spinner } from "@/components/bakaui";
import { bytesToSize, extractErrorMessage } from "@/components/utils";
import AcquisitionDeletionCleanupSummary from "./AcquisitionDeletionCleanupSummary";
import { deletionReasonCodes, deletionReasonLabel } from "./deletionReasonLabel";
import type { DestroyableProps } from "@/components/bakaui/types";
import { millisecondsUntil } from "@/core/serverTime";
import {
  acquisitionDeletionApi,
  type AcquisitionDeletionOptions,
  type AcquisitionDeletionProgress,
  type AcquisitionDeletionPreview,
  type AcquisitionDeletionResult,
} from "./acquisitionDeletionApi";

const pageSize = 20;
type Confirmation = {
  preview: AcquisitionDeletionPreview;
  options: AcquisitionDeletionOptions;
  taskIds: number[];
};

function matchesScope(
  preview: AcquisitionDeletionPreview,
  ids: number[],
  options: AcquisitionDeletionOptions,
) {
  const returnedIds = new Set(preview.rows.map((row) => row.taskId));
  if (!preview.previewId || preview.rows.length !== ids.length || returnedIds.size !== ids.length ||
      !ids.every(id => returnedIds.has(id))) return false;
  return (
    !!preview.retryRetainedFiles === !!options.retryRetainedFiles &&
    !!preview.cleanupFiles === !!options.cleanupFiles &&
    !!preview.skipFileVerification === !!options.skipFileVerification &&
    !!preview.deleteDeliveredFiles === !!options.deleteDeliveredFiles
  );
}

function recordEligible(
  row: AcquisitionDeletionPreview["rows"][number],
  removeTaskIfCleanupFails: boolean,
) {
  return row.canDelete || (removeTaskIfCleanupFails && row.canRemoveTaskIfCleanupFails === true);
}

function deletionStatusLabel(progress: AcquisitionDeletionProgress | undefined, receivingReceipt: boolean,
  unavailable: boolean, expired: boolean) {
  if (receivingReceipt) {
    if (unavailable) return expired ? "acquisition.delete.receipt.expired" : "acquisition.delete.receipt.unavailable";
    if (!progress) return "acquisition.delete.receipt.checking";
    if (progress.state !== "running") {
      return ["completed", "interrupted", "preview"].includes(progress.state)
        ? `acquisition.delete.receipt.${progress.state}` : "acquisition.delete.receipt.unavailable";
    }
  }
  if (!progress || progress.state === "preview") return "acquisition.delete.progress.starting";
  const phase = ["rechecking", "cleaning", "deletingRecord", "completed", "interrupted"].includes(progress.phase)
    ? progress.phase : "rechecking";
  return `acquisition.delete.progress.phase.${phase}`;
}

export default function DeleteAcquisitionTasksModal({
  taskIds,
  scopeLabel,
  retryRetainedFiles = false,
  onChanged,
  onDestroyed,
}: DestroyableProps & { taskIds: number[]; scopeLabel?: string; retryRetainedFiles?: boolean; onChanged?: () => void }) {
  const { t } = useTranslation();
  const captured = useRef([...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0))]);
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [scopeRevision, setScopeRevision] = useState(0);
  const [scope, setScope] = useState<AcquisitionDeletionPreview>();
  const [inventory, setInventory] = useState<AcquisitionDeletionPreview>();
  const [inventoryLoading, setInventoryLoading] = useState(false);
  const [inventoryFailed, setInventoryFailed] = useState(false);
  const [inventoryError, setInventoryError] = useState<string>();
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [result, setResult] = useState<AcquisitionDeletionResult>();
  const [retainedReview, setRetainedReview] = useState<{ taskIds: number[]; options: AcquisitionDeletionOptions } | undefined>(
    () => retryRetainedFiles ? { taskIds: captured.current, options: {
      cleanupFiles: true, retryRetainedFiles: true, skipFileVerification: false,
    } } : undefined,
  );

  const [error, setError] = useState<"preview">();
  const [unconfirmed, setUnconfirmed] = useState<{ previewId: string; reason?: string }>();
  const [receiptExpired, setReceiptExpired] = useState(false);
  const [page, setPage] = useState(1);
  const [now, setNow] = useState(Date.now());
  const cleanupAllowed = captured.current.length > 0 && captured.current.length <= 100;
  const [cleanupFiles, setCleanupFiles] = useState(cleanupAllowed);
  const [deleteDeliveredFiles, setDeleteDeliveredFiles] = useState(false);
  const [skipFileVerification, setSkipFileVerification] = useState(true);
  const [removeTaskIfCleanupFails, setRemoveTaskIfCleanupFails] = useState(true);
  const fileOptions: AcquisitionDeletionOptions = { cleanupFiles, deleteDeliveredFiles, skipFileVerification };
  const [progress, setProgress] = useState<AcquisitionDeletionProgress>();
  const [progressUnavailable, setProgressUnavailable] = useState(false);
  const backButton = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  const scopeRequest = useRef<AbortController>();
  const inventoryRequest = useRef<AbortController>();
  const busy = useRef(false);
  const reviewGeneration = useRef(0);
  const reviewPending = useRef(false);
  const attempted = useRef(new Set<string>());
  const inventoryCache = useRef(new Map<string, AcquisitionDeletionPreview>());
  const retainedScopeKey = retainedReview?.taskIds.join(",");
  const retainedReviewRef = useRef(retainedReview);
  retainedReviewRef.current = retainedReview;
  const changedCallback = useRef(onChanged);
  changedCallback.current = onChanged;

  const cancelPreparation = useCallback(() => {
    reviewGeneration.current++;
    reviewPending.current = false;
    inventoryRequest.current?.abort();
    setPreparing(false);
  }, []);
  const resetInventory = useCallback(() => {
    cancelPreparation();
    setInventory(undefined);
    setInventoryLoading(false);
    setInventoryFailed(false);
    setInventoryError(undefined);
  }, [cancelPreparation]);

  const load = useCallback(async () => {
    if (busy.current) return;
    scopeRequest.current?.abort();
    const controller = new AbortController();
    scopeRequest.current = controller;
    inventoryCache.current.clear();
    resetInventory();
    setScope(undefined);
    setConfirmation(undefined);
    setLoading(true);
    setError(undefined);
    setUnconfirmed(undefined);
    setResult(undefined);
    setPage(1);
    try {
      const retained = retainedReviewRef.current;
      const ids = retained?.taskIds ?? captured.current;
      const options = retained?.options ?? {};
      const next = await acquisitionDeletionApi.preview(ids, options, controller.signal);
      if (controller.signal.aborted) return;
      if (!matchesScope(next, ids, options)) throw new Error("DELETE_SCOPE_CHANGED");
      setScope(next);
      if (retained) setInventory(next);
      setNow(Date.now());
    } catch {
      if (!controller.signal.aborted) setError("preview");
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [resetInventory, retainedScopeKey, scopeRevision]);
  useEffect(() => {
    mounted.current = true;
    void load();
    return () => {
      mounted.current = false;
      reviewGeneration.current++;
      reviewPending.current = false;
      scopeRequest.current?.abort();
      inventoryRequest.current?.abort();
    };
  }, [load]);

  const inspectFiles = async (allowCached = false) => {
    const ids = retainedReview?.taskIds ?? captured.current;
    const options: AcquisitionDeletionOptions = retainedReview?.options ?? fileOptions;
    if (!scope || (!options.cleanupFiles && !options.deleteDeliveredFiles) || busy.current || confirmation) return;
    // Keep a previously reviewed file scope while refreshing it (including if refresh fails).
    inventoryRequest.current?.abort();
    setInventoryFailed(false);
    setInventoryError(undefined);
    const controller = new AbortController();
    inventoryRequest.current = controller;
    const cacheKey = JSON.stringify([ids, !!options.cleanupFiles, !!options.deleteDeliveredFiles,
      !!options.skipFileVerification, !!options.retryRetainedFiles]);
    const existing = inventory ?? scope;
    const cached = allowCached ? (matchesScope(existing, ids, options) ? existing : inventoryCache.current.get(cacheKey)) : undefined;
    if (cached && !attempted.current.has(cached.previewId) && millisecondsUntil(cached.expiresAt) > 0 &&
      matchesScope(cached, ids, options)) {
      setInventory(cached);
      setInventoryLoading(false);
      setNow(Date.now());
      setPage(1);
      return cached;
    }
    setInventoryLoading(true);
    try {
      const next = await acquisitionDeletionApi.preview(
        ids,
        options,
        controller.signal,
      );
      if (controller.signal.aborted) return;
      if (!matchesScope(next, ids, options))
        throw new Error("CLEANUP_PREVIEW_CHANGED");
      if (inventoryCache.current.size >= 8) inventoryCache.current.clear();
      inventoryCache.current.set(cacheKey, next);
      setInventory(next);
      setNow(Date.now());
      setPage(1);
      return next;
    } catch (cause) {
      if (!controller.signal.aborted) {
        setInventoryFailed(true);
        const code = extractErrorMessage(cause);
        setInventoryError(deletionReasonCodes.has(code) ? code : undefined);
      }
    } finally {
      if (!controller.signal.aborted) setInventoryLoading(false);
    }
  };
  // Checkboxes only change local choices. A final-file inventory is prepared once
  // when advancing to confirmation, not on every option change.
  const changeFileOptions = () => {
    resetInventory();
  };
  const changeRetainedOptions = (options: AcquisitionDeletionOptions) => {
    if (!retainedReview) return;
    changeFileOptions();
    setRetainedReview({ ...retainedReview, options: { ...retainedReview.options, ...options } });
  };
  const preview = confirmation?.preview ?? inventory ?? scope;
  const retainedOptionsChanged = !!retainedReview && !!preview &&
    !matchesScope(preview, retainedReview.taskIds, retainedReview.options);
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const rows = preview?.rows ?? [];
  const eligible = (row: AcquisitionDeletionPreview["rows"][number]) =>
    recordEligible(row, !retainedReview && (cleanupFiles || deleteDeliveredFiles) && removeTaskIfCleanupFails);
  const eligibleRows = rows.filter(eligible);
  const stoppingIds = confirmation?.preview.rows
    .filter(row => row.willStopExecution && confirmation.taskIds.includes(row.taskId)).map(row => row.taskId) ?? [];

  const selectionReady =
    !!preview &&
    !preparing &&
    !inventoryLoading &&
    !loading &&
    !error &&
    !expired &&
    !attempted.current.has(preview.previewId) &&
    (eligibleRows.length > 0 || retainedOptionsChanged);
  useEffect(() => {
    if (!preview || !visible) return;
    const remaining = millisecondsUntil(preview.expiresAt);
    if (!remaining) return;
    const timer = setTimeout(() => setNow(Date.now()), remaining + 1);
    return () => clearTimeout(timer);
  }, [preview, visible]);
  useEffect(() => {
    if (confirmation) backButton.current?.focus();
  }, [confirmation]);
  const trackedId = submitting ? confirmation?.preview.previewId : unconfirmed?.previewId;
  useEffect(() => {
    if (!trackedId || !visible) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const next = await acquisitionDeletionApi.progress(
          trackedId,
          controller.signal,
        );
        if (controller.signal.aborted) return;
        if (next.previewId !== trackedId)
          throw new Error("DELETION_PROGRESS_CHANGED");
        setProgress(next);
        setProgressUnavailable(false);
        if (unconfirmed && next.state !== "running") {
          if (next.state === "completed" || next.state === "interrupted") changedCallback.current?.();
          return;
        }
      } catch (cause) {
        if (controller.signal.aborted) return;
        setProgressUnavailable(true);
        if (unconfirmed) {
          setReceiptExpired(extractErrorMessage(cause) === "ACQUISITION_DELETE_PREVIEW_EXPIRED");
          return;
        }
      }
      timer = setTimeout(() => void poll(), 1000);
    };
    void poll();
    return () => {
      controller.abort();
      clearTimeout(timer);
    };
  }, [trackedId, unconfirmed, visible]);
  const close = () => {
    if (busy.current) return;
    scopeRequest.current?.abort();
    cancelPreparation();
    setVisible(false);
  };
  const review = async () => {
    if (!selectionReady || !preview || millisecondsUntil(preview.expiresAt) <= 0 || busy.current || reviewPending.current)
      return;
    const generation = ++reviewGeneration.current;
    reviewPending.current = true;
    const options = retainedReview?.options ?? (cleanupFiles || deleteDeliveredFiles ? { ...fileOptions, removeTaskIfCleanupFails } : {});
    try {
      let selected = preview;
      if (options.cleanupFiles || options.deleteDeliveredFiles) {
        setPreparing(true);
        const prepared = await inspectFiles(true);
        if (!prepared) return;
        selected = prepared;
      }
      if (!mounted.current || generation !== reviewGeneration.current) return;
      if (millisecondsUntil(selected.expiresAt) <= 0) { setNow(Date.now()); return; }
      const ready = selected.rows.filter(row => recordEligible(row, !retainedReview && !!options.removeTaskIfCleanupFails));
      if (ready.length === 0) return;
      inventoryRequest.current?.abort();
      setInventoryLoading(false);
      setConfirmation({
        preview: selected,
        taskIds: ready.map((row) => row.taskId),
        options,
      });
    } finally {
      if (generation === reviewGeneration.current) {
        reviewPending.current = false;
        setPreparing(false);
      }
    }
  };
  const remove = async () => {
    if (
      !confirmation ||
      busy.current ||
      millisecondsUntil(confirmation.preview.expiresAt) <= 0 ||
      attempted.current.has(confirmation.preview.previewId)
    )
      return;
    busy.current = true;
    attempted.current.add(confirmation.preview.previewId);
    setSubmitting(true);
    setProgress(undefined);
    setProgressUnavailable(false);
    setReceiptExpired(false);
    setError(undefined);
    try {
      const next = await acquisitionDeletionApi.remove(
        confirmation.preview.previewId,
        confirmation.options.removeTaskIfCleanupFails === undefined ? {} : { removeTaskIfCleanupFails: confirmation.options.removeTaskIfCleanupFails },
      );
      if (mounted.current) setResult(next);
    } catch (cause) {
      const reason = extractErrorMessage(cause);
      if (mounted.current) {
        setProgress(undefined);
        setUnconfirmed({ previewId: confirmation.preview.previewId, reason: deletionReasonCodes.has(reason) ? reason : undefined });
      }
    } finally {
      busy.current = false;
      if (mounted.current) {
        setSubmitting(false);
        setConfirmation(undefined);
        setScope(undefined);
        resetInventory();
      }
      onChanged?.();
    }
  };
  const retainedIds = (result?.cleanupResults ?? [])
    .filter(row => !row.completed && (retainedReview ? retainedReview.taskIds.includes(row.taskId) : result!.deletedTaskIds.includes(row.taskId)))
    .map(row => row.taskId);
  const recheckRetained = () => {
    if (!retainedIds.length || busy.current) return;
    setResult(undefined);
    setRetainedReview({ taskIds: retainedIds, options: retainedReview?.options ?? { ...fileOptions, retryRetainedFiles: true } });
    setScopeRevision(value => value + 1);
  };
  const reason = (code?: string | null) => deletionReasonLabel(t, code);
  const closeButton = (
    <Button variant="light" isDisabled={submitting} onPress={close}>
      {t("common.action.close")}
    </Button>
  );

  return (
    <Modal
      visible={visible}
      title={t(
        retainedReview ? "acquisition.delete.retained.title" : confirmation && !submitting ? "acquisition.delete.final.title" : "acquisition.delete.title",
      )}
      size="4xl"
      classNames={{ body: "min-w-0" }}
      onClose={close}
      onDestroyed={onDestroyed}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      hideCloseButton={submitting}
      footer={
        confirmation && !submitting ? (
          <div className="flex w-full flex-wrap items-center justify-end gap-2">
            <Button
              key="delete"
              color="danger"
              className="h-auto min-h-10 max-w-full whitespace-normal py-2"
              isDisabled={expired}
              onPress={() => void remove()}
            >
              {t(retainedReview ? "acquisition.delete.retained.confirm" : "acquisition.delete.final.submit")}
            </Button>
            <Button
              key="back"
              ref={backButton}
              variant="flat"
              onPress={() => setConfirmation(undefined)}
            >
              {t("acquisition.delete.final.back")}
            </Button>
          </div>
        ) : (
          <>
            {closeButton}
            {!result && !submitting && !unconfirmed && (
              <Button key="review" color="primary" isDisabled={!selectionReady} isLoading={preparing} onPress={() => void review()}>
                {t("acquisition.delete.next")}
              </Button>
            )}
          </>
        )
      }
    >
      {submitting || unconfirmed ? (
        <div role="status" className="space-y-2 py-3 text-sm" aria-live="polite">
          <div className="flex items-center gap-2">
            {(submitting || (!progressUnavailable && (!progress || progress.state === "running"))) && <Spinner size="sm" />}
            <span>
              {t(deletionStatusLabel(progress, !!unconfirmed, progressUnavailable, receiptExpired))}
            </span>
            {progress?.currentTaskId != null && <span>#{progress.currentTaskId}</span>}
          </div>
          {progress && (
            <p>
              {t("acquisition.delete.progress.tasks", {
                completed: progress.finishedTasks,
                total: progress.totalTasks,
              })}
            </p>
          )}
          {progress && (cleanupFiles || deleteDeliveredFiles) && (
            <p>
              {t("acquisition.delete.progress.files", {
                count: progress.completedFiles,
                size: bytesToSize(progress.completedBytes),
              })}
            </p>
          )}
          {unconfirmed ? <>
            {unconfirmed.reason && <p className="text-sm text-danger">{reason(unconfirmed.reason)}</p>}
            {progressUnavailable && !receiptExpired ? (
              <Button size="sm" variant="flat" onPress={() => {
                setProgressUnavailable(false);
                setUnconfirmed({ ...unconfirmed });
              }}>{t("acquisition.delete.receipt.checkAgain")}</Button>
            ) : (receiptExpired || (progress && progress.state !== "running")) && (
              <Button size="sm" variant="flat" onPress={() => void load()}>{t("acquisition.delete.recheck")}</Button>
            )}
          </> : <p className="text-xs text-default-600">
            {t(
              progressUnavailable
                ? "acquisition.delete.progress.unavailable"
                : "acquisition.delete.progress.safety",
            )}
          </p>}
        </div>
      ) : confirmation ? (
        <div className="space-y-3 text-sm">
          <p className="font-medium">
            {t(retainedReview ? "acquisition.delete.retained.scope" : "acquisition.delete.final.scope", { count: confirmation.taskIds.length })}
          </p>
          <p className="max-h-16 overflow-auto break-words text-xs text-default-500">
            {confirmation.taskIds.map((id) => `#${id}`).join(", ")}
          </p>
          <p>{t(confirmation.options.cleanupFiles || confirmation.options.deleteDeliveredFiles
            ? "acquisition.delete.final.boundInventory" : "acquisition.delete.final.taskOnly")}</p>
          {stoppingIds.length > 0 &&
            <p role="alert" className="text-warning-700">{t("acquisition.delete.stopOnConfirm", {
              ids: stoppingIds.map(id => `#${id}`).join(", "),
            })}</p>}
          {(confirmation.options.cleanupFiles || confirmation.options.deleteDeliveredFiles) && <>
            {(!stoppingIds.length || !!confirmation.preview.cleanupFileCount) && <p className="text-sm">{t("acquisition.delete.cleanup.total", {
              count: confirmation.preview.cleanupFileCount ?? 0, size: bytesToSize(confirmation.preview.cleanupBytes ?? 0),
            })}</p>}
            {confirmation.preview.cleanupSizeIncomplete &&
              <p className="text-xs text-warning-600">{t("acquisition.delete.cleanup.incompleteSize")}</p>}
            {confirmation.options.deleteDeliveredFiles && <p role="alert" className="font-medium text-danger">
              {t(stoppingIds.length ? "acquisition.delete.delivered.afterStop" : "acquisition.delete.delivered.confirmation", {
                count: confirmation.preview.rows.filter(row => confirmation.taskIds.includes(row.taskId))
                  .flatMap(row => row.cleanup?.groups ?? []).filter(group => group.kind === "publishedFiles" && group.eligible)
                  .reduce((sum, group) => sum + group.fileCount, 0),
                size: bytesToSize(confirmation.preview.rows.filter(row => confirmation.taskIds.includes(row.taskId))
                  .flatMap(row => row.cleanup?.groups ?? []).filter(group => group.kind === "publishedFiles" && group.eligible)
                  .reduce((sum, group) => sum + group.bytes, 0)),
              })}
            </p>}
            <div className="max-h-80 min-w-0 overflow-y-auto space-y-2">
              {confirmation.preview.rows.map(row => <section key={row.taskId} className="min-w-0 rounded-lg border border-default-200 p-2">
                <p className="font-medium [overflow-wrap:anywhere]">#{row.taskId} {row.title}</p>
                {row.reasonCode && <p className="text-xs text-warning-600">{reason(row.reasonCode)}</p>}
                {row.cleanup && <AcquisitionDeletionCleanupSummary cleanup={row.cleanup} reason={reason} />}
              </section>)}
            </div>
            <p className="text-xs text-default-500">{t(retainedReview ? "acquisition.delete.retained.preserveHistory" :
              confirmation.options.removeTaskIfCleanupFails ? "acquisition.delete.final.removeOnFailure" :
                "acquisition.delete.final.keepOnFailure")}</p>
          </>}
          {expired && (
            <p role="alert" className="text-warning-600">
              {t("acquisition.delete.expired")}
            </p>
          )}
        </div>
      ) : result ? (
        <>
          <p role="status">
            {t(retainedReview ? "acquisition.delete.retained.result" : "acquisition.delete.result", {
              count: retainedReview ? result.cleanupResults?.filter(row => row.completed).length ?? 0 : result.deletedTaskIds.length,
              skipped: result.skipped.length,
            })}
          </p>
          {!!result.cleanupResults?.length && (
            <div className="space-y-2">
              <p role="status">
                {t("acquisition.delete.cleanup.result", {
                  count: result.removedFiles ?? 0,
                  size: bytesToSize(result.removedBytes ?? 0),
                })}
              </p>
              {result.cleanupResults
                .filter((row) => !!row.closedRunIds?.length)
                .map((row) => (
                  <p key={row.taskId} className="text-xs text-default-600">
                    {t("acquisition.delete.cleanup.closedRuns", {
                      id: row.taskId,
                      runs: row.closedRunIds!.map((run) => `#${run}`).join(", "),
                    })}
                  </p>
                ))}
              {result.cleanupResults
                .filter((row) => !row.completed)
                .map((row) => (
                  <div key={row.taskId} className="rounded-lg bg-warning-50 p-2 text-sm">
                    <p>
                      {t(
                        retainedReview || result.deletedTaskIds.includes(row.taskId)
                          ? "acquisition.delete.cleanup.partialRemoved"
                          : "acquisition.delete.cleanup.partial",
                        {
                          id: row.taskId,
                          count: row.removedFiles,
                          size: bytesToSize(row.removedBytes),
                        },
                      )}
                    </p>
                    {row.reasonCodes.map((code) => (
                      <p key={code} className="text-xs">
                        {reason(code)}
                      </p>
                    ))}
                    {!!row.retainedDirectories?.length && (
                      <ul
                        aria-label={t("acquisition.delete.cleanup.retainedDirectories")}
                        className="list-disc pl-5 text-xs"
                      >
                        {row.retainedDirectories.map((path) => (
                          <li key={path} className="select-text [overflow-wrap:anywhere]">
                            {path}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                ))}
              <p className="text-xs text-default-500">
                {t("acquisition.delete.cleanup.physicalSpace")}
              </p>
              {retainedIds.length > 0 && (
                <Button size="sm" variant="flat" color="primary" onPress={recheckRetained}>
                  {t("acquisition.delete.retained.title")}
                </Button>
              )}
              {!retainedReview && result.cleanupResults.some(
                (row) => !row.completed && !result.deletedTaskIds.includes(row.taskId),
              ) && (
                <Button size="sm" variant="flat" onPress={() => void load()}>
                  {t("acquisition.delete.cleanup.retry")}
                </Button>
              )}
            </div>
          )}
          {result.skipped.length > 0 && (
            <div className="max-h-64 overflow-y-auto divide-y divide-default-200 rounded-lg border border-default-200">
              {result.skipped.map((row) => (
                <div key={row.taskId} className="p-2 text-sm">
                  <span className="mr-2 font-medium">#{row.taskId}</span>
                  {reason(row.reasonCode)}
                </div>
              ))}
            </div>
          )}
        </>
      ) : (
        <>
          <p className="text-sm text-default-500">{t(retainedReview
            ? (retainedReview.options.deleteDeliveredFiles ? "acquisition.delete.retained.preserveHistory" : "acquisition.delete.retained.preserve")
            : deleteDeliveredFiles ? "acquisition.delete.delivered.preserveHistory" : "acquisition.delete.preserve")}</p>
          {retainedReview && (
            <div className="flex flex-col items-start gap-2">
              <Checkbox size="sm" isSelected={!!retainedReview.options.cleanupFiles}
                isDisabled={!!retainedReview.options.cleanupFiles && !retainedReview.options.deleteDeliveredFiles}
                onValueChange={cleanupFiles => changeRetainedOptions({ cleanupFiles })}>
                {t("acquisition.delete.cleanup.option")}
              </Checkbox>
              <div>
                <Checkbox size="sm" color="danger" isSelected={!!retainedReview.options.deleteDeliveredFiles}
                  isDisabled={!!retainedReview.options.deleteDeliveredFiles && !retainedReview.options.cleanupFiles}
                  onValueChange={deleteDeliveredFiles => changeRetainedOptions({ deleteDeliveredFiles })}>
                  {t("acquisition.delete.delivered.option")}
                </Checkbox>
                <p className="text-xs text-default-500">{t("acquisition.delete.delivered.hint")}</p>
              </div>
              <p className="text-xs text-default-500">{t("acquisition.delete.retained.scopeRequired")}</p>
              <Checkbox size="sm" color="warning" isSelected={!!retainedReview.options.skipFileVerification}
                onValueChange={skipFileVerification => changeRetainedOptions({skipFileVerification})}>
                {t("acquisition.delete.cleanup.skipVerification")}
              </Checkbox>
            </div>
          )}
          {!retainedReview && <div className="space-y-2">
            <Checkbox
              size="sm"
              isSelected={cleanupFiles}
              isDisabled={!cleanupAllowed}
              onValueChange={(checked) => {
                changeFileOptions();
                setPage(1);
                setCleanupFiles(checked);
              }}
            >
              {t("acquisition.delete.cleanup.option")}
            </Checkbox>
            <div>
              <Checkbox size="sm" color="danger" isSelected={deleteDeliveredFiles} isDisabled={!cleanupAllowed}
                onValueChange={(checked) => {
                  changeFileOptions(); setPage(1); setDeleteDeliveredFiles(checked);
                }}>
                {t("acquisition.delete.delivered.option")}
              </Checkbox>
              <p className="text-xs text-default-500">{t("acquisition.delete.delivered.hint")}</p>
            </div>
            {captured.current.length > 100 && (
              <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.limit")}</p>
            )}
            {(cleanupFiles || deleteDeliveredFiles) && (
              <div className="flex flex-col items-start gap-2 border-l-2 border-default-200 pl-3">
                <div>
                  <Checkbox
                    size="sm"
                    color="warning"
                    isSelected={skipFileVerification}
                    onValueChange={(checked) => {
                      changeFileOptions();
                      setSkipFileVerification(checked);
                    }}
                  >
                    {t("acquisition.delete.cleanup.skipVerification")}
                  </Checkbox>
                  <p className="text-xs text-default-500">
                    {t("acquisition.delete.cleanup.skipVerificationHint")}
                  </p>
                </div>
                <Checkbox
                  size="sm"
                  color="warning"
                  isSelected={removeTaskIfCleanupFails}
                  onValueChange={value => { cancelPreparation(); setInventoryLoading(false); setRemoveTaskIfCleanupFails(value); }}
                >
                  {t("acquisition.delete.cleanup.removeOnFailure")}
                </Checkbox>
                <Button
                  size="sm"
                  variant="flat"
                  isDisabled={!scope || inventoryLoading || !!inventory || expired}
                  isLoading={inventoryLoading}
                  onPress={() => void inspectFiles()}
                >
                  {t("acquisition.delete.cleanup.inspect")}
                </Button>
                {inventoryLoading && <p role="status" className="text-xs text-default-500">
                  {t(skipFileVerification ? "acquisition.delete.cleanup.listingSkip" : "acquisition.delete.cleanup.listing")}
                </p>}
                {inventoryFailed && (
                  <p className="text-xs text-warning-600" role="alert">
                    {inventoryError ? reason(inventoryError) : t("acquisition.delete.cleanup.inventoryFailed")}
                  </p>
                )}
              </div>
            )}
          </div>}
          {(retainedOptionsChanged || (!inventory && deleteDeliveredFiles)) && !preparing && <p className="text-xs text-default-500">
            {t("acquisition.delete.cleanup.prepareNext")}
          </p>}
          {retainedReview && inventoryLoading && <p role="status" className="text-xs text-default-500">
            {t(retainedReview.options.skipFileVerification ? "acquisition.delete.cleanup.listingSkip" : "acquisition.delete.cleanup.listing")}
          </p>}
          {retainedReview && inventoryFailed && <p role="alert" className="text-xs text-warning-600">
            {inventoryError ? reason(inventoryError) : t("acquisition.delete.cleanup.inventoryFailed")}
          </p>}
          {scopeLabel && <p className="break-words text-xs text-default-500">{scopeLabel}</p>}
          {error && (
            <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
              {t(`acquisition.delete.error.${error}`)}
              <Button size="sm" variant="flat" onPress={() => void load()}>
                {t("acquisition.delete.recheck")}
              </Button>
            </div>
          )}
          {loading && (
            <div className="flex justify-center py-8">
              <Spinner aria-label={t("acquisition.delete.loading")} />
            </div>
          )}
          {preview && (
            <>
              <div className="flex flex-wrap items-center gap-2 text-xs" aria-live="polite">
                <Chip size="sm" variant="flat">
                  {t("acquisition.delete.total", { count: preview.totalCount })}
                </Chip>
                {!retainedOptionsChanged && <Chip size="sm" color="danger" variant="flat">
                  {t("acquisition.delete.eligible", { count: eligibleRows.length })}
                </Chip>}
                {!retainedOptionsChanged && <Chip size="sm" variant="flat">
                  {t("acquisition.delete.blocked", { count: rows.length - eligibleRows.length })}
                </Chip>}
              </div>
              {inventory && !retainedOptionsChanged && (
                <div className="space-y-1 rounded-lg border border-default-200 p-3 text-sm">
                  <p>
                    {t("acquisition.delete.cleanup.identifiedTotal", {
                      count: inventory.identifiedFileCount ?? 0,
                      size: bytesToSize(inventory.identifiedBytes ?? 0),
                    })}
                  </p>
                  <p>
                    {t("acquisition.delete.cleanup.total", {
                      count: inventory.cleanupFileCount ?? 0,
                      size: bytesToSize(inventory.cleanupBytes ?? 0),
                    })}
                  </p>
                  {inventory.cleanupSizeIncomplete && (
                    <p className="text-xs text-warning-600">
                      {t("acquisition.delete.cleanup.incompleteSize")}
                    </p>
                  )}
                  <p className="text-xs text-default-500">
                    {t("acquisition.delete.cleanup.physicalSpace")}
                  </p>
                </div>
              )}
              {expired && !error && (
                <div
                  role="alert"
                  className="flex flex-wrap items-center gap-2 text-sm text-warning"
                >
                  {t("acquisition.delete.expired")}
                  <Button
                    size="sm"
                    variant="flat"
                    onPress={() => (inventory && !retainedReview ? void inspectFiles() : void load())}
                  >
                    {t("acquisition.delete.recheck")}
                  </Button>
                </div>
              )}
              <div className="min-w-0 divide-y divide-default-200 rounded-lg border border-default-200">
                {rows.slice((page - 1) * pageSize, page * pageSize).map((row) => (
                  <div
                    key={row.taskId}
                    className="flex min-w-0 flex-wrap items-start justify-between gap-1 p-2 text-sm"
                  >
                    <span className="min-w-0 flex-1 [overflow-wrap:anywhere]">
                      <span className="mr-2 text-default-500">#{row.taskId}</span>
                      {row.title}
                    </span>
                    <span
                      className={`max-w-full text-xs [overflow-wrap:anywhere] ${eligible(row) ? "text-danger" : "text-default-500"}`}
                    >
                      {!retainedOptionsChanged && (eligible(row) ? t(retainedReview ? "acquisition.delete.retained.ready" : "acquisition.delete.rowEligible") : reason(row.reasonCode))}
                    </span>
                    {row.willStopExecution && <p className="w-full text-xs text-warning-700">
                      {t("acquisition.delete.stopOnConfirm", { ids: `#${row.taskId}` })}
                    </p>}
                    {row.cleanup && !retainedOptionsChanged && (
                      <AcquisitionDeletionCleanupSummary cleanup={row.cleanup} reason={reason} />
                    )}
                  </div>
                ))}
              </div>
              {rows.length > pageSize && (
                <Pagination
                  aria-label={t("acquisition.delete.pagination")}
                  page={page}
                  total={Math.ceil(rows.length / pageSize)}
                  onChange={setPage}
                />
              )}
            </>
          )}
        </>
      )}
    </Modal>
  );
}
