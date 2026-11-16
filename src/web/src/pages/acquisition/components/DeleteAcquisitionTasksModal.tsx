import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { v4 as uuidv4 } from "uuid";
import { Button, Checkbox, Modal, Pagination, Spinner } from "@/components/bakaui";
import { bytesToSize, extractErrorMessage } from "@/components/utils";
import { deletionReasonCodes, deletionReasonLabel } from "./deletionReasonLabel";
import type { DestroyableProps } from "@/components/bakaui/types";
import {
  acquisitionDeletionApi,
  type AcquisitionDeletionOptions,
  type AcquisitionDeletionProgress,
  type AcquisitionDeletionResult,
} from "./acquisitionDeletionApi";

const pageSize = 20;
type Confirmation = {
  operationId: string;
  options: AcquisitionDeletionOptions;
  taskIds: number[];
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
  onChanged,
  onDestroyed,
}: DestroyableProps & { taskIds: number[]; taskNames?: Record<number, string>; scopeLabel?: string; retryRetainedFiles?: boolean; onChanged?: () => void }) {
  const { t } = useTranslation();
  const [selection, setSelection] = useState(() => [...new Set(taskIds.filter(id => Number.isSafeInteger(id) && id > 0))]);
  const [providedNames] = useState(() => Object.fromEntries(Object.entries(taskNames)
    .filter(([, name]) => name.trim()).map(([id, name]) => [id, name.trim()])));
  const [names, setNames] = useState(providedNames);
  const [retainedReview, setRetainedReview] = useState(retryRetainedFiles);
  const [visible, setVisible] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [confirmation, setConfirmation] = useState<Confirmation>();
  const [result, setResult] = useState<AcquisitionDeletionResult>();
  const [unconfirmed, setUnconfirmed] = useState<(Confirmation & { reason?: string })>();
  const [receiptExpired, setReceiptExpired] = useState(false);
  const [page, setPage] = useState(1);
  const cleanupAllowed = selection.length > 0 && selection.length <= 100;
  const [cleanupFiles, setCleanupFiles] = useState(cleanupAllowed);
  const [deleteDeliveredFiles, setDeleteDeliveredFiles] = useState(false);
  const [removeTaskIfCleanupFails, setRemoveTaskIfCleanupFails] = useState(true);
  const [progress, setProgress] = useState<AcquisitionDeletionProgress>();
  const [progressUnavailable, setProgressUnavailable] = useState(false);
  const backButton = useRef<HTMLButtonElement>(null);
  const mounted = useRef(false);
  const busy = useRef(false);
  const changedCallback = useRef(onChanged);
  changedCallback.current = onChanged;

  const options: AcquisitionDeletionOptions = {
    cleanupFiles,
    deleteDeliveredFiles,
    retryRetainedFiles: retainedReview,
    removeTaskIfCleanupFails: !retainedReview && (cleanupFiles || deleteDeliveredFiles) && removeTaskIfCleanupFails,
  };
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; };
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
            const saved = await acquisitionDeletionApi.remove(unconfirmed.operationId, unconfirmed.taskIds, unconfirmed.options);
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
    setVisible(false);
  };
  const selectionReady = selection.length > 0 && selection.length <= 10000 &&
    (!retainedReview || cleanupFiles || deleteDeliveredFiles);
  const review = () => {
    if (!selectionReady || busy.current) return;
    setConfirmation({ operationId: uuidv4().replace(/-/g, ""), taskIds: [...selection], options });
  };
  const remove = async () => {
    if (!confirmation || busy.current) return;
    busy.current = true;
    setSubmitting(true);
    setProgress(undefined);
    setProgressUnavailable(false);
    setReceiptExpired(false);
    try {
      const next = await acquisitionDeletionApi.remove(confirmation.operationId, confirmation.taskIds, confirmation.options);
      if (mounted.current) setResult(next);
    } catch (cause) {
      const reason = extractErrorMessage(cause);
      if (mounted.current) setUnconfirmed({
        ...confirmation,
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
    setResult(undefined);
    setUnconfirmed(undefined);
    setProgress(undefined);
    setReceiptExpired(false);
    setPage(1);
  };
  const retainedIds = (result?.cleanupResults ?? [])
    .filter(row => !row.completed && (retainedReview || result!.deletedTaskIds.includes(row.taskId)))
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
          <Button color="danger" className="h-auto min-h-10 max-w-full whitespace-normal py-2" onPress={() => void remove()}>
            {t(retainedReview ? "acquisition.delete.retained.confirm" : "acquisition.delete.final.submit")}
          </Button>
          <Button ref={backButton} variant="flat" onPress={() => setConfirmation(undefined)}>
            {t("acquisition.delete.final.back")}
          </Button>
        </div>
      ) : <>
        <Button variant="light" isDisabled={submitting} onPress={close}>{t("common.action.close")}</Button>
        {!result && !submitting && !unconfirmed && <Button color="primary" isDisabled={!selectionReady} onPress={review}>
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
          {taskList(confirmation.taskIds)}
          <p>{t(confirmation.options.cleanupFiles || confirmation.options.deleteDeliveredFiles
            ? "acquisition.delete.final.directCleanup" : "acquisition.delete.final.taskOnly")}</p>
          {confirmation.options.cleanupFiles && <p>{t("acquisition.delete.cleanup.option")}</p>}
          {confirmation.options.deleteDeliveredFiles && <p role="alert" className="font-medium text-danger">
            {t("acquisition.delete.delivered.directConfirmation")}
          </p>}
          <p className="text-xs text-default-500">{t(retainedReview ? "acquisition.delete.retained.preserveHistory" :
            confirmation.options.removeTaskIfCleanupFails ? "acquisition.delete.final.removeOnFailure" : "acquisition.delete.final.keepOnFailure")}</p>
        </div>
      ) : result ? (
        <>
          <p role="status">{t(retainedReview ? "acquisition.delete.retained.result" : "acquisition.delete.result", {
            count: retainedReview ? result.cleanupResults?.filter(row => row.completed).length ?? 0 : result.deletedTaskIds.length,
            skipped: result.skipped.length,
          })}</p>
          {!!result.cleanupResults?.length && <div className="space-y-2">
            {result.fileStatisticsIncomplete && <p role="status">{t("acquisition.delete.cleanup.directResult", {
              count: result.removedDirectories?.length ?? 0,
            })}</p>}
            {result.fileStatisticsIncomplete && <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.directoryStatistics")}</p>}
            {!result.fileStatisticsIncomplete && <p role="status">{t("acquisition.delete.cleanup.result", {
              count: result.removedFiles ?? 0, size: bytesToSize(result.removedBytes ?? 0),
            })}</p>}
            {result.cleanupResults.filter(row => !!row.closedRunIds?.length).map(row => <p key={row.taskId} className="text-xs text-default-600">
              {t("acquisition.delete.cleanup.closedRuns", { name: taskName(row.taskId), runs: row.closedRunIds!.map(run => `#${run}`).join(", ") })}
            </p>)}
            {result.cleanupResults.filter(row => !!row.removedDirectories?.length).map(row => <section key={row.taskId} className="min-w-0 space-y-1 text-sm">
              <p className="font-medium [overflow-wrap:anywhere]">{taskName(row.taskId)} · {t("acquisition.delete.cleanup.removedDirectories")}</p>
              <ul aria-label={t("acquisition.delete.cleanup.removedDirectories")} className="min-w-0 list-disc pl-5 text-xs">
                {row.removedDirectories!.map(path => <li key={path} className="select-text [overflow-wrap:anywhere]">{path}</li>)}
              </ul>
            </section>)}
            {result.cleanupResults.filter(row => !row.completed).map(row => <div key={row.taskId} className="rounded-lg bg-warning-50 p-2 text-sm">
              <p className="[overflow-wrap:anywhere]">{t(row.fileStatisticsIncomplete ? "acquisition.delete.cleanup.directPartial" :
                retainedReview || result.deletedTaskIds.includes(row.taskId) ? "acquisition.delete.cleanup.partialRemoved" : "acquisition.delete.cleanup.partial", {
                name: taskName(row.taskId), count: row.removedFiles, size: bytesToSize(row.removedBytes),
              })}</p>
              {row.reasonCodes.map(code => <p key={code} className="text-xs">{reason(code)}</p>)}
              {!!row.retainedDirectories?.length && <ul aria-label={t("acquisition.delete.cleanup.retainedDirectories")} className="list-disc pl-5 text-xs">
                {row.retainedDirectories.map(path => <li key={path} className="select-text [overflow-wrap:anywhere]">{path}</li>)}
              </ul>}
            </div>)}
            {!result.fileStatisticsIncomplete && <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.physicalSpace")}</p>}
            {retainedIds.length > 0 && <Button size="sm" variant="flat" color="primary" onPress={() => resetReview(retainedIds, true)}>
              {t("acquisition.delete.retained.title")}
            </Button>}
            {!retainedReview && result.cleanupResults.some(row => !row.completed && !result.deletedTaskIds.includes(row.taskId)) &&
              <Button size="sm" variant="flat" onPress={() => resetReview(result.cleanupResults!
                .filter(row => !row.completed && !result.deletedTaskIds.includes(row.taskId)).map(row => row.taskId))}>
                {t("acquisition.delete.cleanup.retry")}
              </Button>}
          </div>}
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
            <Checkbox size="sm" isSelected={cleanupFiles} isDisabled={!cleanupAllowed}
              onValueChange={setCleanupFiles}>
              {t("acquisition.delete.cleanup.option")}
            </Checkbox>
            <div>
              <Checkbox size="sm" color="danger" isSelected={deleteDeliveredFiles} isDisabled={!cleanupAllowed}
                onValueChange={setDeleteDeliveredFiles}>
                {t("acquisition.delete.delivered.option")}
              </Checkbox>
              <p className="text-xs text-default-500">{t("acquisition.delete.delivered.hint")}</p>
            </div>
            {!cleanupAllowed && selection.length > 100 && <p className="text-xs text-default-500">{t("acquisition.delete.cleanup.limit")}</p>}
            {retainedReview && <p className="text-xs text-default-500">{t("acquisition.delete.retained.scopeRequired")}</p>}
            {(cleanupFiles || deleteDeliveredFiles) && <>
              {!retainedReview && <Checkbox size="sm" color="warning" isSelected={removeTaskIfCleanupFails} onValueChange={setRemoveTaskIfCleanupFails}>
                {t("acquisition.delete.cleanup.removeOnFailure")}
              </Checkbox>}
            </>}
          </div>
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
