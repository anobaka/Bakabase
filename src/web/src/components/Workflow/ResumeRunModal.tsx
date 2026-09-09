"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { getWorkflowActivityUI } from "./Activities";
import { activityDisplayName } from "./displayNames";

import BApi from "@/sdk/BApi";
import { Modal, toast } from "@/components/bakaui";

interface Props extends DestroyableProps {
  runId: number;
  workflowDefinitionId: number;
  /** Index of the step that is waiting; the activity there owns the prompt. */
  currentStepIndex?: number | null;
  waitReason?: string | null;
  waitPromptJson?: string | null;
  onResumed?: () => void;
}

/**
 * Answers a waiting run. The form comes from the activity that suspended — it wrote the prompt
 * and it will read the answer, so nothing in between needs to understand either. When that
 * activity ships no form, the wait is one that only needs acknowledging, and a Continue button
 * is the whole interface.
 */
const ResumeRunModal = ({
  runId,
  workflowDefinitionId,
  currentStepIndex,
  waitReason,
  waitPromptJson,
  onResumed,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();
  const [activityKind, setActivityKind] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // The waiting step's kind isn't on the run row — the definition is where the chain lives.
  useEffect(() => {
    if (currentStepIndex == null) return;
    void BApi.workflow.getWorkflow(workflowDefinitionId).then((r) => {
      const activities = r.data?.activities ?? [];

      setActivityKind(activities[currentStepIndex]?.kind ?? null);
    });
  }, [workflowDefinitionId, currentStepIndex]);

  const send = async (signalJson: string) => {
    const rsp = await BApi.workflow.resumeWorkflowRun(runId, { signalJson });

    if (!rsp.code) {
      toast.success(t<string>("workflow.resume.done"));
      onResumed?.();
    }
  };

  /** The form path closes itself; the Continue path lets the modal do it. */
  const submitFromForm = async (signalJson: string) => {
    setSubmitting(true);
    try {
      await send(signalJson);
      onDestroyed?.();
    } finally {
      setSubmitting(false);
    }
  };

  const ResumeForm = activityKind ? getWorkflowActivityUI(activityKind)?.ResumeForm : undefined;

  return (
    <Modal
      defaultVisible
      footer={
        ResumeForm
          ? { actions: ["cancel"] }
          : {
              actions: ["cancel", "ok"],
              okProps: { children: t<string>("workflow.resume.continue") },
            }
      }
      size="lg"
      title={t<string>("workflow.resume.title", { runId })}
      onOk={ResumeForm ? undefined : () => send("{}")}
    >
      <div className="flex flex-col gap-3">
        <div className="text-sm text-default-500">
          {waitReason
            ? t<string>(`workflow.waitReason.${waitReason}`, { defaultValue: waitReason })
            : t<string>("workflow.resume.unknownReason")}
          {activityKind && (
            <span className="text-default-400"> · {activityDisplayName(t, activityKind)}</span>
          )}
        </div>

        {ResumeForm ? (
          <ResumeForm
            promptJson={waitPromptJson ?? null}
            submitting={submitting}
            onSubmit={submitFromForm}
          />
        ) : (
          <div className="text-sm">{t<string>("workflow.resume.acknowledge")}</div>
        )}
      </div>
    </Modal>
  );
};

export default ResumeRunModal;
