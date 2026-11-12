"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useContext, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { getWorkflowActivityUI } from "./Activities";
import { activityDisplayName } from "./displayNames";

import BApi from "@/sdk/BApi";
import { Button, Modal, Spinner, toast } from "@/components/bakaui";
import { ResumeActivityContext, ResumeDetails } from "./ResumePresentation";
import { workflowWaitReasonLabel } from "./waitReasonLabel";
import { ResumeSubmissionContext } from "./ResumeSubmissionContext";
import ResumeActions from "./ResumeActions";

export interface ResumeRunProps {
  runId: number;
  workflowDefinitionId: number;
  /** Index of the step that is waiting; the activity there owns the prompt. */
  currentStepIndex?: number | null;
  waitReason?: string | null;
  waitPromptJson?: string | null;
  /** Frozen activity identity from the run; older records may need the definition fallback. */
  activityKind?: string | null;
  onResumed?: () => void;
}

/**
 * Answers a waiting run. The form comes from the activity that suspended — it wrote the prompt
 * and it will read the answer, so nothing in between needs to understand either. A known
 * activity without a form may acknowledge a wait; an unresolved activity must not accept an
 * empty signal because it could repeat a remote operation.
 */
export const ResumeRunContent = ({
  runId,
  workflowDefinitionId,
  currentStepIndex,
  waitReason,
  waitPromptJson,
  activityKind: frozenActivityKind,
  onResumed,
  onClose,
  fileTask = false,
  compact = false,
}: ResumeRunProps & { onClose?: () => void; fileTask?: boolean; compact?: boolean }) => {
  const { t } = useTranslation();
  const taskSubmission = useContext(ResumeSubmissionContext);
  const fallbackKey = JSON.stringify([workflowDefinitionId, currentStepIndex]);
  const [resolved, setResolved] = useState<{ key: string; kind?: string; failed?: boolean }>();
  const [retry, setRetry] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const fallback = resolved?.key === fallbackKey ? resolved : undefined;
  const needsFallback = !frozenActivityKind && currentStepIndex != null;
  const resolving = needsFallback && !fallback;
  const resolutionFailed = needsFallback && !!fallback?.failed;
  const activityKind = frozenActivityKind ?? fallback?.kind;

  // A historical wait belongs to the activity captured by that run, even if its definition changes.
  useEffect(() => {
    if (frozenActivityKind) return;
    if (currentStepIndex == null) return;
    let active = true;
    setResolved(undefined);
    void BApi.workflow
      .getWorkflow(workflowDefinitionId)
      .then((r) => {
        const kind = r.data?.activities?.[currentStepIndex]?.kind;
        if (r.code || !kind) throw new Error("WORKFLOW_WAITING_ACTIVITY_UNAVAILABLE");
        if (active) setResolved({ key: fallbackKey, kind });
      })
      .catch(() => {
        if (active) setResolved({ key: fallbackKey, failed: true });
      });
    return () => {
      active = false;
    };
  }, [frozenActivityKind, workflowDefinitionId, currentStepIndex, retry]);

  const send = async (signalJson: string) => {
    const accepted = taskSubmission?.runId === runId
      ? await taskSubmission.submit(signalJson)
      : !(await BApi.workflow.resumeWorkflowRun(runId, { signalJson })).code;
    if (accepted) {
      toast.success(t<string>("workflow.resume.done"));
      onResumed?.();
      return true;
    }
    return false;
  };

  /** Both the node-owned form and acknowledgement use the same guarded submission path. */
  const submitFromForm = async (signalJson: string) => {
    setSubmitting(true);
    try {
      if (await send(signalJson)) onClose?.();
    } finally {
      setSubmitting(false);
    }
  };

  const activityUI = activityKind ? getWorkflowActivityUI(activityKind) : undefined;
  const ResumeForm = activityUI?.ResumeForm;
  const unsupported = !activityUI && !resolving && !resolutionFailed;
  const reason = workflowWaitReasonLabel(t, waitReason, waitPromptJson);

  const form = ResumeForm && (
    <ResumeActivityContext.Provider value={activityKind}>
      <ResumeForm
        key={JSON.stringify([runId, currentStepIndex, activityKind])}
        promptJson={waitPromptJson ?? null}
        submitting={submitting}
        onSubmit={submitFromForm}
        fileTask={fileTask || compact}
      />
    </ResumeActivityContext.Provider>
  );
  if ((fileTask || compact) && form) return form;

  return (
    <div className="flex min-w-0 flex-col gap-5">
      {!compact && <div className="space-y-2 rounded-xl bg-default-50 p-4">
        <p className="text-xs text-default-500">{t("workflow.resume.guide.currentStep")}</p>
        <p className="break-words text-base font-medium">
          {currentStepIndex != null && `${currentStepIndex + 1}. `}
          {activityKind
            ? activityDisplayName(t, activityKind)
            : t("workflow.resume.guide.resolving")}
        </p>
        <p className="text-sm text-default-600">{reason}</p>
      </div>}
      {!compact && <p className="text-xs leading-relaxed text-default-500">
        {t("workflow.resume.guide.viewOnly")}
      </p>}

      {resolving ? (
        <Spinner size="sm" />
      ) : resolutionFailed ? (
        <div role="alert" className="space-y-2 text-sm text-warning-600">
          <p>{t("workflow.resume.activityUnavailable")}</p>
          <Button size="sm" variant="flat" onPress={() => setRetry((value) => value + 1)}>
            {t("common.action.retry")}
          </Button>
        </div>
      ) : unsupported ? (
        <p role="alert" className="text-sm text-warning-600">
          {t("workflow.resume.guide.unsupported")}
        </p>
      ) : form ? (
        form
      ) : (
        <div className="text-sm">{t<string>("workflow.resume.acknowledge")}</div>
      )}
      {!compact && <ResumeDetails>
        <dl className="space-y-1 break-all">
          <div>
            <dt>{t("workflow.resume.guide.run")}</dt>
            <dd>#{runId}</dd>
          </div>
          {activityKind && (
            <div>
              <dt>{t("workflow.resume.guide.nodeKind")}</dt>
              <dd>{activityKind}</dd>
            </div>
          )}
          {waitReason && (
            <div>
              <dt>{t("workflow.resume.guide.reasonCode")}</dt>
              <dd>{waitReason}</dd>
            </div>
          )}
        </dl>
      </ResumeDetails>}
      <ResumeActions busy={submitting} cancel={onClose && (
          <Button variant="light" isDisabled={submitting} onPress={onClose}>
            {t("workflow.resume.guide.leaveWaiting")}
          </Button>
        )}>
        {!ResumeForm && !resolving && !resolutionFailed && !unsupported && (
          <Button color="primary" isLoading={submitting} onPress={() => void submitFromForm("{}")}>
            {t("workflow.resume.continue")}
          </Button>
        )}
      </ResumeActions>
    </div>
  );
};

/** Standalone entry; callers with an existing task panel reuse the content directly. */
export default function ResumeRunModal({
  onDestroyed,
  ...props
}: ResumeRunProps & DestroyableProps) {
  const { t } = useTranslation();
  return (
    <Modal
      defaultVisible
      footer={false}
      size="3xl"
      title={t("workflow.resume.title", { runId: props.runId })}
      onDestroyed={onDestroyed}
    >
      <ResumeRunContent {...props} onClose={onDestroyed} />
    </Modal>
  );
}
