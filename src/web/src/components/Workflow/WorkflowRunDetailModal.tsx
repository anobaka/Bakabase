import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel as Run } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { activityDisplayName } from "./displayNames";
import ResumeRunModal from "./ResumeRunModal";

import { Button, Chip, Modal, Progress, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { WorkflowRunStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";

/** Opens one persisted instance, independent of the definition's run history. */
export default function WorkflowRunDetailModal({
  runId,
  onDestroyed,
}: DestroyableProps & { runId: number }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [run, setRun] = useState<Run>();
  const [failed, setFailed] = useState(false);
  const requestId = useRef(0);
  const load = useCallback(async () => {
    const request = ++requestId.current;
    try {
      const response = await BApi.workflow.getWorkflowRun(runId);
      if (request !== requestId.current) return;
      if (response.code || !response.data) throw new Error(response.message);
      setRun(response.data);
      setFailed(false);
    } catch {
      if (request === requestId.current) setFailed(true);
    }
  }, [runId]);

  useEffect(() => {
    setRun(undefined);
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 2000);
    return () => {
      clearInterval(timer);
      requestId.current++;
    };
  }, [load]);

  return (
    <Modal
      defaultVisible
      footer={false}
      size="3xl"
      title={t("workflow.runDetail.title", { id: runId })}
      onDestroyed={onDestroyed}
    >
      {failed && (
        <div className="flex items-center gap-2 text-danger" role="alert">
          <span>{t("workflow.runDetail.loadFailed")}</span>
          <Button size="sm" variant="light" onPress={() => void load()}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      {!run ? (
        !failed && <Spinner />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-3">
            <Chip
              size="sm"
              variant="flat"
              color={
                run.status === WorkflowRunStatus.Success
                  ? "success"
                  : run.status === WorkflowRunStatus.Failed
                    ? "danger"
                    : "primary"
              }
            >
              {t(`workflow.runs.status.${WorkflowRunStatus[run.status]}`)}
            </Chip>
            <span className="text-xs text-default-500">
              {new Date(run.startedAt).toLocaleString()}
            </span>
            <Button
              as="a"
              href={`#/workflows/editor?id=${run.workflowDefinitionId}`}
              target="_blank"
              rel="noopener noreferrer"
              size="sm"
              variant="light"
            >
              {t("workflow.runDetail.definition")}
            </Button>
          </div>
          {run.progress != null && (
            <Progress
              aria-label={t("workflow.runDetail.progress")}
              size="sm"
              value={run.progress}
              showValueLabel
            />
          )}
          {(run.activityKind || run.stage || run.process) && (
            <p className="break-words text-sm text-default-500">
              {run.activityKind && activityDisplayName(t, run.activityKind)}
              {run.stage && ` · ${run.stage}`}
              {run.process && ` · ${run.process}`}
            </p>
          )}
          {run.errorMessage && (
            <p className="break-words text-sm text-danger">{run.errorMessage}</p>
          )}
          {run.status === WorkflowRunStatus.Waiting && (
            <div className="space-y-2 rounded-lg bg-secondary-50 p-3">
              <p className="text-sm">
                {t("workflow.resume.waitingFor", {
                  reason: run.waitReason
                    ? t(`workflow.waitReason.${run.waitReason}`, { defaultValue: run.waitReason })
                    : t("workflow.resume.unknownReason"),
                })}
              </p>
              <Button
                color="secondary"
                size="sm"
                onPress={() =>
                  createPortal(ResumeRunModal, {
                    runId,
                    workflowDefinitionId: run.workflowDefinitionId,
                    currentStepIndex: run.currentStepIndex,
                    waitReason: run.waitReason,
                    waitPromptJson: run.waitPromptJson,
                    onResumed: load,
                  })
                }
              >
                {t("workflow.resume.respond")}
              </Button>
            </div>
          )}
          <div className="flex flex-wrap gap-4 text-xs text-default-500">
            <span>
              {t("workflow.runs.input")}: {run.inputCount}
            </span>
            <span>
              {t("workflow.runs.output")}: {run.outputCount}
            </span>
            <span>
              {t("workflow.runs.failed")}: {run.failedItemCount}
            </span>
          </div>
          {!!run.stepStats?.length && (
            <ol className="space-y-2">
              {run.stepStats.map((step) => (
                <li
                  key={step.stepIndex}
                  className="flex flex-wrap items-center gap-3 rounded-lg bg-default-50 p-3 text-sm"
                >
                  <span className="text-default-400">{step.stepIndex + 1}</span>
                  <span className="flex-1">{activityDisplayName(t, step.kind)}</span>
                  <span className="text-xs tabular-nums">
                    {step.inputCount} → {step.outputCount}
                  </span>
                  {step.failedCount > 0 && (
                    <span className="text-xs text-danger">
                      {t("workflow.runs.failed")}: {step.failedCount}
                    </span>
                  )}
                </li>
              ))}
            </ol>
          )}
          {run.outputItemsJson != null && (
            <details className="rounded-lg bg-default-50 p-3 text-xs">
              <summary className="cursor-pointer">{t("workflow.runs.resultPreview")}</summary>
              {run.outputPreviewTruncated && (
                <p className="mt-2 text-warning-600">{t("workflow.runs.resultPreviewTruncated")}</p>
              )}
              <pre className="mt-2 max-h-80 overflow-auto whitespace-pre-wrap break-all">
                {formatOutput(run.outputItemsJson)}
              </pre>
            </details>
          )}
        </div>
      )}
    </Modal>
  );
}

function formatOutput(value: string): string {
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
