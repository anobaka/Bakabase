import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Pagination, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { parseServerTime } from "@/core/serverTime";
import { acquisitionContinuationApi as api, type AcquisitionContinuation } from "./acquisitionContinuationApi";
import { localBatchReasonLabel } from "./localBatchReasonLabel";
import AcquisitionQueueStatusChip from "./AcquisitionQueueStatusChip";

const active = (job: AcquisitionContinuation) => job.status === "queued" || job.status === "checking";
const rowStatuses = new Set(["queued", "checking", "submitted", "attention", "skipped", "cancelled", "failed"]);

/** Submission receipts are separate from resource completion and survive closing the dialog. */
export function AcquisitionContinuationStatus({
  initialJob, onChanged,
}: { initialJob: AcquisitionContinuation; onChanged?: () => void }) {
  const { t } = useTranslation();
  const [job, setJob] = useState(initialJob);
  const [page, setPage] = useState(1);
  const [error, setError] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const alive = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const changed = useRef(onChanged);
  changed.current = onChanged;
  useEffect(() => {
    alive.current = true;
    let checking = false;
    const timer = setInterval(() => {
      if (busy.current || checking || !active(job)) return;
      checking = true;
      const request = ++sequence.current;
      void api.get(job.batchId).then((next) => {
        if (!alive.current || request !== sequence.current) return;
        setJob(next);
        setError(false);
        if (!active(next)) changed.current?.();
      }).catch(() => {
        if (alive.current && request === sequence.current) setError(true);
      }).finally(() => { checking = false; });
    }, 4000);
    return () => { alive.current = false; sequence.current++; clearInterval(timer); };
  }, [job.batchId, job.status]);
  const cancel = async () => {
    if (busy.current) return;
    busy.current = true;
    sequence.current++;
    setCancelling(true);
    try {
      const next = await api.cancel(job.batchId);
      if (alive.current) { setJob(next); setError(false); }
      changed.current?.();
    } catch { if (alive.current) setError(true); }
    finally { busy.current = false; if (alive.current) setCancelling(false); }
  };
  return <div className="min-w-0 space-y-3">
    <p role="status" className="text-sm">{t("acquisition.continue.submissionHint")}</p>
    <div className="flex flex-wrap items-center gap-2">
      {["queued", "checking", "submitted", "attention", "skipped", "cancelled"].map((status) => {
        const count = job.rows.filter((row) => row.status === status).length;
        return count ? <AcquisitionQueueStatusChip key={status} status={status}>{t(`acquisition.continue.status.${status}`)} {count}</AcquisitionQueueStatusChip> : null;
      })}
      {active(job) && <Button size="sm" color="danger" variant="flat" isLoading={cancelling} onPress={() => void cancel()}>
        {t("acquisition.continue.cancelRemaining")}
      </Button>}
    </div>
    <p className="text-xs text-default-500">{t("acquisition.continue.cancelHint")}</p>
    {error && <p role="alert" className="text-sm text-danger">{t("acquisition.continue.refreshFailed")}</p>}
    <div className="divide-y divide-default-200 rounded-lg border border-default-200">
      {job.rows.slice((page - 1) * 20, page * 20).map((row) => <div key={row.taskId} className="flex flex-wrap gap-x-3 gap-y-1 p-2 text-sm">
        <span className="min-w-0 flex-1 break-words">#{row.taskId} {row.title}</span>
        <AcquisitionQueueStatusChip status={row.status}>
          {t(`acquisition.continue.status.${rowStatuses.has(row.status) ? row.status : "unknown"}`)}
        </AcquisitionQueueStatusChip>
        {row.reasonCode && <p className="w-full break-words text-xs text-default-500">{localBatchReasonLabel(t, row.reasonCode)}</p>}
      </div>)}
    </div>
    {job.rows.length > 20 && <Pagination page={page} total={Math.ceil(job.rows.length / 20)} onChange={setPage} />}
  </div>;
}

export default function AcquisitionContinuationJobsModal({
  onChanged, onDestroyed, onShowLocalQueues, initialBatchId, onBackToSelection,
}: DestroyableProps & { onChanged?: () => void; onShowLocalQueues?: () => void;
  initialBatchId?: string; onBackToSelection?: () => void }) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [jobs, setJobs] = useState<AcquisitionContinuation[]>();
  const [selected, setSelected] = useState<AcquisitionContinuation>();
  const [error, setError] = useState(false);
  const [page, setPage] = useState(1);
  const alive = useRef(false);
  const sequence = useRef(0);
  const initialJob = useRef<Promise<AcquisitionContinuation>>();
  const load = useCallback(async () => {
    const request = ++sequence.current;
    try {
      const next = await api.list();
      if (alive.current && request === sequence.current) {
        setJobs(next); setPage(1); setError(false);
      }
    } catch { if (alive.current && request === sequence.current) setError(true); }
  }, []);
  const loadInitial = useCallback(async () => {
    if (!initialBatchId) return;
    const request = ++sequence.current;
    try {
      initialJob.current ??= api.get(initialBatchId);
      const next = await initialJob.current;
      if (!alive.current || request !== sequence.current) return;
      if (next.batchId !== initialBatchId) throw new Error("CONTINUATION_NOT_FOUND");
      setSelected(next); setError(false);
    } catch { if (alive.current && request === sequence.current) setError(true); }
  }, [initialBatchId]);
  useEffect(() => {
    alive.current = true;
    if (initialBatchId) void loadInitial(); else void load();
    return () => { alive.current = false; sequence.current++; };
  }, [load, loadInitial, initialBatchId]);
  return <Modal visible={visible} title={t("acquisition.continue.records")} size="5xl" scrollBehavior="inside"
    onClose={() => setVisible(false)} onDestroyed={onDestroyed}
    footer={<Button variant="light" onPress={() => setVisible(false)}>{t("common.action.close")}</Button>}>
    <div className="flex flex-wrap gap-2">
      {onBackToSelection && <Button size="sm" variant="light" onPress={onBackToSelection}>{t("acquisition.continueActions.back")}</Button>}
      {onShowLocalQueues && <Button size="sm" variant="flat" onPress={onShowLocalQueues}>{t("acquisition.continue.localQueues")}</Button>}
      {selected ? <Button size="sm" variant="light" onPress={() => { setSelected(undefined); void load(); }}>{t("acquisition.continue.backToRecords")}</Button>
        : <Button size="sm" variant="light" onPress={() => void load()}>{t("common.action.refresh")}</Button>}
    </div>
    {selected ? <AcquisitionContinuationStatus key={selected.batchId} initialJob={selected} onChanged={onChanged} /> : <>
      <p className="text-xs text-default-500">{t("acquisition.continue.submissionHint")}</p>
      {error && <p role="alert" className="text-danger">{t("acquisition.continue.refreshFailed")}</p>}
      {!jobs && !error ? <Spinner /> : jobs?.length ? <>
        {jobs.slice((page - 1) * 20, page * 20).map((job) => <Button key={job.batchId} variant="flat" className="h-auto w-full justify-start whitespace-normal p-3 text-left" onPress={() => setSelected(job)}>
          <span>{parseServerTime(job.createdAt)?.toLocaleString() ?? job.createdAt} · {t(`acquisition.recovery.scope.${job.scope}`)} · {t("acquisition.recovery.fixedScope", { count: job.rows.length })}</span>
        </Button>)}
        {jobs.length > 20 && <Pagination page={page} total={Math.ceil(jobs.length / 20)} onChange={setPage} />}
      </> : !error && <p className="text-default-500">{t("acquisition.continue.noRecords")}</p>}
    </>}
  </Modal>;
}
