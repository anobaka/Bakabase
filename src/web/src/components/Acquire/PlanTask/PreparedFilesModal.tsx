import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Textarea } from "@/components/bakaui";
import { planTaskApi } from "./api";
import type { PlanTaskDetail } from "./models";
import useAttemptStorageReserve, { type AttemptRunOptions } from "./useAttemptStorageReserve";

type PreparedChoice = "keep" | "defer";

export function canChoosePreparedFiles(detail: PlanTaskDetail | undefined, choice: PreparedChoice) {
  return !!detail && (choice === "keep" ? detail.canKeepPreparedFiles : detail.canDeferPreparedProcessing) === true && detail.task.phase === "ready" &&
    detail.task.workflowRunId == null && !detail.task.isDeleted && !detail.task.deletedAt &&
    !!detail.plan?.parts.length && detail.plan.parts.every(part => part.continueFrom?.taskId === detail.task.id &&
      part.continueFrom.artifactId && part.continueFrom.fingerprint);
}

/** Starting uses the server's saved revision and the exact inputs the user reviewed. */
function matchesConfirmation(before: PlanTaskDetail, after: PlanTaskDetail, choice: PreparedChoice, reason: string) {
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
        !next.useAdaptiveAssessment && next.processing?.requirement === (choice === "keep" ? "notRequired" : "unknown") && next.processing.steps.length === 0;
    });
}

export default function PreparedFilesModal({ detail, choice, stale, onClose, onSaved, onRefresh }: {
  choice: PreparedChoice;
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
    if (submitted.current || stale || !reserve.options || !canChoosePreparedFiles(detail, choice)) return;
    submitted.current = true;
    setSubmitting(true);
    try {
      const keeping = choice === "keep";
      const reason = keeping ? note.trim() || "delivery.keepPreparedFiles" : "processing.deferPreparedFiles";
      const next = keeping
        ? await planTaskApi.keepPreparedFiles(detail.task.id, detail.task.planRevision, note.trim() || undefined)
        : await planTaskApi.deferPreparedProcessing(detail.task.id, detail.task.planRevision);
      if (!matchesConfirmation(detail, next, choice, reason)) throw new Error("Unexpected saved revision");
      onSaved(next, reserve.options);
    } catch {
      setFailure(true);
      onRefresh();
    } finally {
      setSubmitting(false);
    }
  };
  const labels = choice === "keep" ? "acquisition.keepPrepared" : "acquisition.deferPrepared";
  const title = t(`${labels}.title`);
  return <Modal visible size="xl" title={title} aria-label={title} onClose={onClose}
    isDismissable={!submitting} isKeyboardDismissDisabled={submitting} hideCloseButton={submitting}
    footer={<>
      <Button variant="light" isDisabled={submitting} onPress={onClose}>{t("acquisition.plan.close")}</Button>
      <Button color="primary" isLoading={submitting} isDisabled={stale || failure || !reserve.ready}
        onPress={() => void submit()}>{t(`${labels}.confirm`)}</Button>
    </>}>
    <p className="text-sm">{t(`${labels}.hint`)}</p>
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
    {choice === "keep" && <Textarea label={t("acquisition.keepPrepared.note")} aria-label={t("acquisition.keepPrepared.note")}
      value={note} onValueChange={setNote} minRows={2} maxLength={1000} isDisabled={submitting || stale || failure} />}
    {reserve.control}
    {stale || failure ? <p role="alert" className="text-sm text-warning-600">{t(stale
      ? "acquisition.plan.replan.error.PLAN_VERSION_CONFLICT" : `${labels}.saveFailed`)}</p> : null}
  </Modal>;
}
