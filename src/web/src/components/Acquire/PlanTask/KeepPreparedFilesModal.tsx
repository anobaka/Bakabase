import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Textarea } from "@/components/bakaui";
import { planTaskApi } from "./api";
import type { PlanTaskDetail } from "./models";
import useAttemptStorageReserve, { type AttemptRunOptions } from "./useAttemptStorageReserve";

export function canKeepPreparedFiles(detail?: PlanTaskDetail) {
  return detail?.canKeepPreparedFiles === true && detail.task.phase === "ready" &&
    detail.task.workflowRunId == null && !detail.task.isDeleted && !detail.task.deletedAt &&
    !!detail.plan?.parts.length && detail.plan.parts.every(part => part.continueFrom?.taskId === detail.task.id &&
      part.continueFrom.artifactId && part.continueFrom.fingerprint);
}

/** Starting uses the server's saved revision and the exact inputs the user reviewed. */
function matchesConfirmation(before: PlanTaskDetail, after: PlanTaskDetail, reason: string) {
  const parts = after.plan?.parts;
  return after.task.id === before.task.id && after.task.phase === "ready" && after.task.workflowRunId == null &&
    !after.task.isDeleted && !after.task.deletedAt && after.task.planRevision >= before.task.planRevision &&
    after.planHistory?.some(revision => revision.revision === after.task.planRevision && revision.reason === reason) &&
    !after.plan?.adaptiveFileProcessing && !after.plan?.publishRetainedFiles && !!parts?.length && parts.length === before.plan?.parts.length &&
    before.plan.parts.every(part => {
      const next = parts.find(p => p.id === part.id);
      const input = part.continueFrom, saved = next?.continueFrom;
      return !!input && !!saved && saved.taskId === input.taskId && saved.previousRunId === input.previousRunId &&
        saved.artifactId === input.artifactId && saved.operationId === input.operationId && saved.fingerprint === input.fingerprint &&
        !next.useAdaptiveAssessment && next.processing?.requirement === "notRequired" && next.processing.steps.length === 0;
    });
}

export default function KeepPreparedFilesModal({ detail, stale, onClose, onSaved, onRefresh }: {
  detail: PlanTaskDetail;
  stale: boolean;
  onClose: () => void;
  onSaved: (next: PlanTaskDetail, options: AttemptRunOptions) => void;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  const [note, setNote] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [failure, setFailure] = useState(false);
  const submitted = useRef(false);
  const reserve = useAttemptStorageReserve(submitting || stale);
  const submit = async () => {
    if (submitted.current || stale || !reserve.options || !canKeepPreparedFiles(detail)) return;
    submitted.current = true;
    setSubmitting(true);
    try {
      const reason = note.trim();
      const next = await planTaskApi.keepPreparedFiles(detail.task.id, detail.task.planRevision, reason || undefined);
      if (!matchesConfirmation(detail, next, reason || "delivery.keepPreparedFiles")) throw new Error("Unexpected saved revision");
      onSaved(next, reserve.options);
    } catch {
      setFailure(true);
      onRefresh();
    } finally {
      setSubmitting(false);
    }
  };
  const title = t("acquisition.keepPrepared.title");
  return <Modal visible size="xl" title={title} aria-label={title} onClose={onClose}
    isDismissable={!submitting} isKeyboardDismissDisabled={submitting} hideCloseButton={submitting}
    footer={<>
      <Button variant="light" isDisabled={submitting} onPress={onClose}>{t("acquisition.plan.close")}</Button>
      <Button color="primary" isLoading={submitting} isDisabled={stale || failure || !reserve.ready}
        onPress={() => void submit()}>{t("acquisition.keepPrepared.confirm")}</Button>
    </>}>
    <p className="text-sm">{t("acquisition.keepPrepared.hint")}</p>
    <ul className="space-y-1 text-sm">
      {detail.plan?.parts.map((part, index) => {
        const evidence = detail.planHistory?.flatMap(rev => rev.artifactEvidence ?? []).find(e =>
          e.partId === part.id && e.artifactId === part.continueFrom?.artifactId && e.fingerprint === part.continueFrom?.fingerprint);
        return <li key={part.id} className="break-words">
          {part.title || t("acquisition.plan.part", { index: index + 1 })}
          {evidence && <span className="ms-2 text-default-500">
            {t("acquisition.keepPrepared.fileCount", { count: evidence.manifest.entries.length })}
          </span>}
        </li>;
      })}
    </ul>
    <Textarea label={t("acquisition.keepPrepared.note")} aria-label={t("acquisition.keepPrepared.note")}
      value={note} onValueChange={setNote} minRows={2} maxLength={1000} isDisabled={submitting || stale || failure} />
    {reserve.control}
    {stale || failure ? <p role="alert" className="text-sm text-warning-600">{t(stale
      ? "acquisition.plan.replan.error.PLAN_VERSION_CONFLICT" : "acquisition.keepPrepared.saveFailed")}</p> : null}
  </Modal>;
}
