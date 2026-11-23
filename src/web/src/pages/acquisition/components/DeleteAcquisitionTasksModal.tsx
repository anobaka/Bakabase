import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { v4 as uuidv4 } from "uuid";
import { Button, Checkbox, Modal, Pagination, Spinner } from "@/components/bakaui";
import { bytesToSize, extractErrorMessage } from "@/components/utils";
import CleanupDirectoryPicker, { type CleanupDirectorySelection } from "./CleanupDirectoryPicker";
import { deletionReasonCodes, deletionReasonLabel } from "./deletionReasonLabel";
import type { DestroyableProps } from "@/components/bakaui/types";
import {
  acquisitionDeletionApi,
  type AcquisitionDeletionOptions,
  type AcquisitionDeletionPreview,
  type AcquisitionDeletionProgress,
  type AcquisitionDeletionResult,
} from "./acquisitionDeletionApi";

const pageSize = 20;
type Confirmation = {
  operationId: string;
  options: AcquisitionDeletionOptions;
  taskIds: number[];
  preview?: AcquisitionDeletionPreview;
};

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
  taskNames = {},
  scopeLabel,
  retryRetainedFiles = false,
  reviewWaitingRuns = false,
  initialScopes,
  onChanged,
  onDestroyed,
}: DestroyableProps & { taskIds: number[]; taskNames?: Record<number, string>; scopeLabel?: string; retryRetainedFiles?: boolean; reviewWaitingRuns?: boolean; initialScopes?: { intermediate: boolean; delivery: boolean }; onChanged?: () => void }) {
  const { t } = useTranslation();
  const [selection, setSelection] = useState(() => [...new Set(taskIds.filter(id => Number.isSafeInteger(id) && id > 0))]);
  const [providedNames] = useState(() => Object.fromEntries(Object.entries(taskNames)
    .filter(([, name]) => name.trim()).map(([id, name]) => [id, name.trim()])));
  const [names, setNames] = useState(providedNames);
  const [retainedReview, setRetainedReview] = useState(retryRetainedFiles);
  const [chooseDirectories, setChooseDirectories] = useState(false);
  const [directories, setDirectories] = useState<CleanupDirectorySelection>({});
  const [visible, setVisible] = useState(true);
  const [reviewingRuns, setReviewingRuns] = useState(false);
  const [previewFailed, setPreviewFailed] = useState(false);
  const [selectionInvalid, setSelectionInvalid] = useState(false);
  const reviewRequest = useRef<AbortController | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [result, setResult] = useState<AcquisitionDeletionResult>();
  const [unconfirmed, setUnconfirmed] = useState<(Confirmation & { reason?: string })>();
  const [receiptExpired, setReceiptExpired] = useState(false);
  const [page, setPage] = useState(1);
  const cleanupAllowed = selection.length > 0 && selection.length <= 100;
  const [cleanupFiles, setCleanupFiles] = useState(cleanupAllowed && (initialScopes?.intermediate ?? true));
  const [deleteDeliveredFiles, setDeleteDeliveredFiles] = useState(cleanupAllowed && (initialScopes?.delivery ?? false));
  const [removeTaskIfCleanupFails, setRemoveTaskIfCleanupFails] = useState(false);
  const [progress, setProgress] = useState<AcquisitionDeletionProgress>();
  const [progressUnavailable, setProgressUnavailable] = useState(false);
  const backButton = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  const busy = useRef(false);
  const changedCallback = useRef(onChanged);
  changedCallback.current = onChanged;

  const requestsFileCleanup = cleanupFiles || deleteDeliveredFiles;
  const options: AcquisitionDeletionOptions = {
    cleanupFiles,
    deleteDeliveredFiles,
    retryRetainedFiles: retainedReview,
    removeTaskIfCleanupFails: !retainedReview && requestsFileCleanup && removeTaskIfCleanupFails,
  };
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; reviewRequest.current?.abort(); };
  }, []);

  useEffect(() => {
    const missing = selection.filter(id => !providedNames[id]);
    if (!visible || !missing.length) return;
    const controller = new AbortController();
    // Cross-page selections contain IDs only. Fetch labels without a file preview,
    // and never hold up confirmation if a task was removed or labels are unavailable.
    void acquisitionDeletionApi.names(missing, controller.signal).then(next => {
      if (!controller.signal.aborted && Object.keys(next).length) setNames(previous => ({ ...previous, ...next }));
    }).catch(() => {});
    return () => controller.abort();
  }, [selection, providedNames, visible]);

  const taskName = (id: number) => names[id]?.trim() || t("acquisition.delete.nameUnavailable", { id });
  const taskList = (ids: number[]) => <ul aria-label={t("acquisition.delete.taskNames")}
    className="max-h-64 min-w-0 overflow-y-auto divide-y divide-default-200 rounded-lg border border-default-200">
    {ids.map(id => <li key={id} className="px-3 py-2 text-sm [overflow-wrap:anywhere]">{taskName(id)}</li>)}
  </ul>;

  useEffect(() => {
    if (confirmation) backButton.current?.focus();
  }, [confirmation]);
  const trackedId = submitting ? confirmation?.operationId : unconfirmed?.operationId;
  useEffect(() => {
    if (!trackedId || !visible) return;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const poll = async () => {
      try {
        const next = await acquisitionDeletionApi.progress(trackedId, controller.signal);
        if (controller.signal.aborted) return;
        if (next.previewId !== trackedId) throw new Error("DELETION_PROGRESS_CHANGED");
        setProgress(next);
        setProgressUnavailable(false);
        if (unconfirmed && next.state !== "running") {
          if (next.state === "completed") {
            // A completed operation is replayed with the same ID and scope only
            // to retrieve its saved result after a lost HTTP response.
            const saved = await acquisitionDeletionApi.remove(unconfirmed.operationId, unconfirmed.preview ? undefined : unconfirmed.taskIds, unconfirmed.options);
            if (controller.signal.aborted) return;
            setResult(saved);
            setUnconfirmed(undefined);
          }
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
    reviewRequest.current?.abort();
    setVisible(false);
  };
  const selectionReady = selection.length > 0 && selection.length <= 10000 &&
    (!retainedReview || cleanupFiles || deleteDeliveredFiles);
  const review = async () => {
    if (!selectionReady || busy.current || reviewRequest.current) return;
    setDirectories({});
    setSelectionInvalid(false);
    if (!retainedReview || (!reviewWaitingRuns && !chooseDirectories)) {
      setConfirmation({ operationId: uuidv4().replace(/-/g, ""), taskIds: [...selection], options });
      return;
    }
    const controller = new AbortController();
    reviewRequest.current = controller;
    setReviewingRuns(true);
    setPreviewFailed(false);
    try {
      const preview = await acquisitionDeletionApi.previewRetained(selection, options, controller.signal);
      if (controller.signal.aborted) return;
      const reviewedIds = new Set(preview.rows.map(row => row.taskId));
      if (!preview.previewId || !preview.retryRetainedFiles || preview.cleanupFiles !== cleanupFiles ||
        preview.deleteDeliveredFiles !== deleteDeliveredFiles || preview.rows.length !== selection.length ||
        selection.some(id => !reviewedIds.has(id))) throw new Error("DELETION_PREVIEW_CHANGED");
      setConfirmation({ operationId: preview.previewId, taskIds: [...selection], options, preview });
    } catch {
      if (!controller.signal.aborted) setPreviewFailed(true);
    } finally {
      reviewRequest.current = null;
      if (!controller.signal.aborted) setReviewingRuns(false);
    }
  };
  const remove = async () => {
    if (!confirmation || busy.current || (chooseDirectories && !Object.keys(directories).length)) return;
    const submitted = chooseDirectories ? { ...confirmation, options: { ...confirmation.options, selectedCleanupDirectories: directories } } : confirmation;
    busy.current = true;
    setSubmitting(true);
    setProgress(undefined);
    setProgressUnavailable(false);
    setReceiptExpired(false);
    try {
      const next = await acquisitionDeletionApi.remove(submitted.operationId, submitted.preview ? undefined : submitted.taskIds, submitted.options);
      if (mounted.current) setResult(next);
    } catch (cause) {
      const reason = extractErrorMessage(cause);
      if (mounted.current && reason === "ACQUISITION_DELETE_DIRECTORY_SELECTION_INVALID") {
        setSelectionInvalid(true);
      } else if (mounted.current) setUnconfirmed({
        ...submitted,
        reason: deletionReasonCodes.has(reason) ? reason : undefined,
      });
    } finally {
      busy.current = false;
      if (mounted.current) {
        setSubmitting(false);
        setConfirmation(undefined);
      }
      changedCallback.current?.();
    }
  };
  const resetReview = (ids = selection, retained = retainedReview) => {
    setSelection(ids);
    setRetainedReview(retained);
    setChooseDirectories(false);
    setDirectories({});
    setSelectionInvalid(false);
    setResult(undefined);
    setUnconfirmed(undefined);
    setProgress(undefined);
    setReceiptExpired(false);
    setRemoveTaskIfCleanupFails(false);
    setPreviewFailed(false);
    setPage(1);
  };
  const cleanupResults = result?.cleanupResults ?? [];
  const incompleteCleanup = cleanupResults.filter(row => !row.completed);
  const retainedIds = incompleteCleanup
    .filter(row => retainedReview || result?.deletedTaskIds.includes(row.taskId))
    .map(row => row.taskId);
  const reason = (code?: string | null) => deletionReasonLabel(t, code);

  return (
    <Modal
      visible={visible}
      title={t(retainedReview ? "acquisition.delete.retained.title" : confirmation && !submitting
        ? "acquisition.delete.final.title" : "acquisition.delete.title")}
      size="4xl"
      classNames={{ body: "min-w-0" }}
      onClose={close}
      onDestroyed={onDestroyed}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      hideCloseButton={submitting}
      footer={confirmation && !submitting ? (
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button color="danger" className="h-auto min-h-10 max-w-full whitespace-normal py-2"
            isDisabled={confirmation.preview?.deletableCount === 0 || (chooseDirectories && !Object.keys(directories).length)} onPress={() => void remove()}>
            {t(retainedReview ? "acquisition.delete.retained.confirm" : "acquisition.delete.final.submit")}
          </Button>
          <Button ref={backButton} variant="flat" onPress={() => setConfirmation(undefined)}>
            {t("acquisition.delete.final.back")}
          </Button>
        </div>
      ) : <>
        <Button variant="light" isDisabled={submitting} onPress={close}>{t("common.action.close")}</Button>
        {!result && !submitting && !unconfirmed && <Button color="primary" isDisabled={!selectionReady || reviewingRuns} isLoading={reviewingRuns} onPress={() => void review()}>
          {t("acquisition.delete.next")}
        </Button>}
      </>}
    >
      {submitting || unconfirmed ? (
        <div role="status" className="space-y-2 py-3 text-sm" aria-live="polite">
          <div className="flex items-center gap-2">
            {(submitting || (!progressUnavailable && (!progress || progress.state === "running"))) && <Spinner size="sm" />}
            <span>{t(deletionStatusLabel(progress, !!unconfirmed, progressUnavailable, receiptExpired))}</span>
            {progress?.currentTaskId != null && <span className="min-w-0 [overflow-wrap:anywhere]">{taskName(progress.currentTaskId)}</span>}
          </div>
          {progress && <p>{t("acquisition.delete.progress.tasks", {
            completed: progress.finishedTasks, total: progress.totalTasks,
          })}</p>}
          {unconfirmed ? <>
            {unconfirmed.reason && <p className="text-sm text-danger">{reason(unconfirmed.reason)}</p>}
            {progressUnavailable && !receiptExpired ? (
              <Button size="sm" variant="flat" onPress={() => {
                setProgressUnavailable(false);
                setUnconfirmed({ ...unconfirmed });
              }}>{t("acquisition.delete.receipt.checkAgain")}</Button>
            ) : (receiptExpired || (progress && progress.state !== "running")) && (
              <Button size="sm" variant="flat" onPress={() => resetReview()}>{t("acquisition.delete.cleanup.retry")}</Button>
            )}
          </> : <p className="text-xs text-default-600">{t(progressUnavailable
            ? "acquisition.delete.progress.unavailable" : "acquisition.delete.progress.safety")}</p>}
        </div>
      ) : confirmation ? (
        <div className="space-y-3 text-sm">
          <p className="font-medium">{t(retainedReview ? "acquisition.delete.retained.scope" : "acquisition.delete.final.scope", {
            count: confirmation.taskIds.length,
          })}</p>
          {confirmation.preview ? <>
            {confirmation.preview.rows.some(row => row.canDelete && (!chooseDirectories || directories[row.taskId]?.length) && row.cleanup?.supersededRuns?.length) &&
              <p>{t("acquisition.delete.history.closeWaitingRuns")}</p>}
            {chooseDirectories && <CleanupDirectoryPicker preview={confirmation.preview} selected={directories} onChange={setDirectories} />}
            <ul aria-label={t("acquisition.delete.taskNames")} className="max-h-64 overflow-y-auto divide-y divide-default-200">
              {confirmation.preview.rows.filter(row => !chooseDirectories || !row.canDelete || (directories[row.taskId]?.length && row.cleanup?.supersededRuns?.length)).map(row => <li key={row.taskId} className="py-2 [overflow-wrap:anywhere]">
                <p className="font-medium">#{row.taskId} {row.title || taskName(row.taskId)}</p>
                {row.canDelete ? !!row.cleanup?.supersededRuns?.length &&
                  <ul className="list-disc pl-5">
                    {row.cleanup.supersededRuns.map(run => <li key={run.runId}>{t(
                      run.replacedByRevision > run.revision ? "acquisition.delete.cleanup.supersededRun" : "acquisition.delete.history.waitingRun",
                      { run: run.runId, revision: run.revision, replacedBy: run.replacedByRevision },
                    )}</li>)}
                  </ul> : <p className="text-warning-600">{reason(row.reasonCode)}</p>}
              </li>)}
            </ul>
          </> : taskList(confirmation.taskIds)}
          <p>{t(chooseDirectories ? "acquisition.delete.directories.confirmScope" : confirmation.options.cleanupFiles || confirmation.options.deleteDeliveredFiles
            ? "acquisition.delete.final.directCleanup" : "acquisition.delete.final.taskOnly")}</p>
          {confirmation.options.cleanupFiles && <p>{t("acquisition.delete.cleanup.option")}</p>}
          {confirmation.options.deleteDeliveredFiles && <p role="alert" className="font-medium text-danger">
            {t("acquisition.delete.delivered.directConfirmation")}
          </p>}
          {(confirmation.options.cleanupFiles || confirmation.options.deleteDeliveredFiles) && (
            <p className="text-xs text-default-500">{t(retainedReview ? "acquisition.delete.retained.preserveHistory" :
              confirmation.options.removeTaskIfCleanupFails ? "acquisition.delete.final.removeOnFailure" : "acquisition.delete.final.keepOnFailure")}</p>
          )}
        </div>
      ) : result ? (
        <>
          {retainedIds.length > 0 && <p role="alert" className="rounded-lg bg-warning-50 p-2 text-sm font-medium">{t("acquisition.delete.history.retainedWarning", { count: retainedIds.length })}</p>}
          {cleanupResults.some(row => row.unselectedDirectories?.length) && <p>{t("acquisition.delete.directories.kept", {
            count: cleanupResults.reduce((sum, row) => sum + (row.unselectedDirectories?.length ?? 0), 0),
          })}</p>}
          {requestsFileCleanup && <div className="space-y-2">
            <p role="status" className="font-medium">{t(chooseDirectories ? "acquisition.delete.directories.outcome" : "acquisition.delete.cleanup.outcomeSummary", {
              completed: cleanupResults.length - incompleteCleanup.length,
              incomplete: incompleteCleanup.length,
              skipped: selection.length - cleanupResults.length,
            })}</p>
            {!!result.removedDirectories?.length && <p role="status">{t("acquisition.delete.cleanup.directResult", {
              count: result.removedDirectories.length,
            })}</p>}
            {result.fileStatisticsIncomplete && <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.directoryStatistics")}</p>}
            {!result.fileStatisticsIncomplete && !!result.removedFiles && <>
              <p role="status">{t("acquisition.delete.cleanup.result", {
                count: result.removedFiles, size: bytesToSize(result.removedBytes ?? 0),
              })}</p>
              <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.physicalSpace")}</p>
            </>}
            {cleanupResults.filter(row => !!row.closedRunIds?.length).map(row => <p key={row.taskId} className="text-xs text-default-600">
              {t("acquisition.delete.cleanup.closedRuns", { name: taskName(row.taskId), runs: row.closedRunIds!.map(run => `#${run}`).join(", ") })}
            </p>)}
            {cleanupResults.filter(row => !!row.removedDirectories?.length).map(row => <section key={row.taskId} className="min-w-0 space-y-1 text-sm">
              <p className="font-medium [overflow-wrap:anywhere]">{taskName(row.taskId)} · {t("acquisition.delete.cleanup.removedDirectories")}</p>
              <ul aria-label={t("acquisition.delete.cleanup.removedDirectories")} className="min-w-0 list-disc pl-5 text-xs">
                {row.removedDirectories!.map(path => <li key={path} className="select-text [overflow-wrap:anywhere]">{path}</li>)}
              </ul>
            </section>)}
            {incompleteCleanup.map(row => <div key={row.taskId} className="rounded-lg bg-warning-50 p-2 text-sm">
              <p className="font-medium [overflow-wrap:anywhere]">{t("acquisition.delete.cleanup.incompleteTask", { name: taskName(row.taskId) })}</p>
              {!retainedReview && <p className="text-xs">{t(result.deletedTaskIds.includes(row.taskId)
                ? "acquisition.delete.cleanup.taskRemoved" : "acquisition.delete.cleanup.taskKept")}</p>}
              {row.reasonCodes.map(code => <p key={code} className="text-xs">{reason(code)}</p>)}
              {!!row.retainedDirectories?.length && <div className="mt-1 text-xs">
                <p>{t("acquisition.delete.cleanup.retainedDirectories")}</p>
                <ul aria-label={t("acquisition.delete.cleanup.retainedDirectories")} className="list-disc pl-5">
                  {row.retainedDirectories.map(path => <li key={path} className="select-text [overflow-wrap:anywhere]">{path}</li>)}
                </ul>
              </div>}
            </div>)}
            {retainedIds.length > 0 && <Button size="sm" variant="flat" color="primary" onPress={() => resetReview(retainedIds, true)}>
              {t("acquisition.delete.retained.title")}
            </Button>}
            {!retainedReview && incompleteCleanup.some(row => !result.deletedTaskIds.includes(row.taskId)) &&
              <Button size="sm" variant="flat" onPress={() => resetReview(incompleteCleanup
                .filter(row => !result.deletedTaskIds.includes(row.taskId)).map(row => row.taskId))}>
                {t("acquisition.delete.cleanup.retry")}
              </Button>}
          </div>}
          {!retainedReview && <p role="status">{t("acquisition.delete.result", {
            count: result.deletedTaskIds.length,
            skipped: result.skipped.length,
          })}</p>}
          {result.skipped.length > 0 && <div className="max-h-64 overflow-y-auto divide-y divide-default-200 rounded-lg border border-default-200">
            {result.skipped.map(row => <div key={row.taskId} className="p-2 text-sm">
              <span className="mr-2 font-medium [overflow-wrap:anywhere]">{taskName(row.taskId)}</span>{reason(row.reasonCode)}
            </div>)}
          </div>}
        </>
      ) : (
        <>
          <p className="text-sm text-default-500">{t(retainedReview ? "acquisition.delete.retained.preserveHistory" : "acquisition.delete.preserve")}</p>
          <div className="flex flex-col items-start gap-2">
            <Checkbox size="sm" isSelected={cleanupFiles} isDisabled={!cleanupAllowed || reviewingRuns}
              onValueChange={setCleanupFiles}>
              {t("acquisition.delete.cleanup.option")}
            </Checkbox>
            <div>
              <Checkbox size="sm" color="danger" isSelected={deleteDeliveredFiles} isDisabled={!cleanupAllowed || reviewingRuns}
                onValueChange={setDeleteDeliveredFiles}>
                {t("acquisition.delete.delivered.option")}
              </Checkbox>
              <p className="text-xs text-default-500">{t("acquisition.delete.delivered.hint")}</p>
            </div>
            {!cleanupAllowed && selection.length > 100 && <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.limit")}</p>}
            {retainedReview && <Checkbox size="sm" isSelected={chooseDirectories}
              isDisabled={!requestsFileCleanup || !cleanupAllowed || reviewingRuns} onValueChange={setChooseDirectories}>
              {t("acquisition.delete.directories.choose")}
            </Checkbox>}
            {retainedReview && <p className="text-xs text-default-500">{t("acquisition.delete.retained.scopeRequired")}</p>}
            {requestsFileCleanup && !retainedReview && <Checkbox size="sm" color="warning" isSelected={removeTaskIfCleanupFails} onValueChange={setRemoveTaskIfCleanupFails}>
              {t("acquisition.delete.cleanup.removeOnFailure")}
            </Checkbox>}
          </div>
          {selectionInvalid && <p role="alert" className="text-sm text-danger">{reason("ACQUISITION_DELETE_DIRECTORY_SELECTION_INVALID")}</p>}
          {previewFailed && <p role="alert" className="text-sm text-danger">{t("acquisition.delete.error.preview")}</p>}
          {scopeLabel && <p className="break-words text-xs text-default-500">{scopeLabel}</p>}
          <p className="text-sm">{t("acquisition.delete.total", { count: selection.length })}</p>
          {taskList(selection.slice((page - 1) * pageSize, page * pageSize))}
          {selection.length > pageSize && <Pagination aria-label={t("acquisition.delete.pagination")}
            page={page} total={Math.ceil(selection.length / pageSize)} onChange={setPage} />}
        </>
      )}
    </Modal>
  );
}
