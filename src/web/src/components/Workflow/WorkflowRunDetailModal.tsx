import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";

import { type ReactNode, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { activityDisplayName } from "./displayNames";
import { ResumeRunContent } from "./ResumeRunModal";

import { Button, Chip, Modal, Progress, Spinner } from "@/components/bakaui";
import { WorkflowRunStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import WorkflowNodeMetadata from "./WorkflowNodeMetadata";
import { workflowWaitReasonLabel } from "./waitReasonLabel";
import DownloadProblemMessage from "@/components/CloudDownload/DownloadProblemMessage";
import WorkflowParameterSummary from "./Parameters/WorkflowParameterSummary";
import CurrentProcessingFiles from "./CurrentProcessingFiles";
import { WorkflowArtifactBrowserButton } from "./WorkflowArtifactBrowserModal";
import ExecutionTiming from "./ExecutionTiming";

type Run = BakabaseModulesWorkflowAbstractionsModelsViewWorkflowRunViewModel;

/** Current download evidence may supersede an older persisted wait at this exact node. */
export interface CompletedDownloadWait {
  runId: number;
  stepIndex: number;
  message: string;
  onDiagnose?: () => void;
}

/** Opens one persisted instance, independent of the definition's run history. */
export function WorkflowRunDetailContent({
  runId,
  readOnly = false,
  respondInitially = false,
  responseOnly = false,
  completedDownloadWait,
  wrapWaitingResponse,
  onChanged,
}: {
  runId: number;
  readOnly?: boolean;
  respondInitially?: boolean;
  responseOnly?: boolean;
  completedDownloadWait?: CompletedDownloadWait;
  wrapWaitingResponse?: (run: Run, form: ReactNode) => ReactNode;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const [responseRun, setResponseRun] = useState<Run>();
  const initialResponseConsumed = useRef(false);
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
      // Keep edited answers stable during polling, but never show an answer form for an old wait.
      setResponseRun((current) =>
        current &&
        response.data!.status === WorkflowRunStatus.Waiting &&
        current.currentStepIndex === response.data!.currentStepIndex &&
        current.waitReason === response.data!.waitReason &&
        current.waitPromptJson === response.data!.waitPromptJson
          ? current
          : undefined,
      );
      if (respondInitially && !readOnly && !initialResponseConsumed.current) {
        initialResponseConsumed.current = true;
        if (response.data.status === WorkflowRunStatus.Waiting) setResponseRun(response.data);
      }
      setFailed(false);
    } catch {
      if (request === requestId.current) setFailed(true);
    }
  }, [runId, readOnly, respondInitially]);

  useEffect(() => {
    setRun(undefined);
    setResponseRun(undefined);
    initialResponseConsumed.current = false;
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 2000);
    return () => {
      clearInterval(timer);
      requestId.current++;
    };
  }, [load]);

  const completedWait =
    run?.status === WorkflowRunStatus.Waiting &&
    completedDownloadWait?.runId === run.id &&
    completedDownloadWait.stepIndex === run.currentStepIndex &&
    (run.activityKind ??
      run.planNodes?.find((node) => node.order === run.currentStepIndex)?.kind) ===
      "download.awaitCompletion"
      ? completedDownloadWait
      : undefined;

  const fileTask = responseOnly && run?.status === WorkflowRunStatus.Waiting &&
    (run.waitReason === "cloud.fileProcessing" || run.waitReason === "files.processing" ||
      (run.waitReason === "artifacts.publication" &&
        (run.activityKind ?? run.planNodes?.find(n => n.order === run.currentStepIndex)?.kind) === "fs.publishArtifacts"));
  const form = !readOnly && responseRun && (
    <ResumeRunContent
      key={JSON.stringify([
        responseRun.id,
        responseRun.currentStepIndex,
        responseRun.waitPromptJson,
      ])}
      fileTask={fileTask}
      compact={responseOnly}
      runId={runId}
      workflowDefinitionId={responseRun.workflowDefinitionId}
      currentStepIndex={responseRun.currentStepIndex}
      waitReason={responseRun.waitReason}
      waitPromptJson={responseRun.waitPromptJson}
      activityKind={
        responseRun.activityKind ??
        responseRun.planNodes?.find(
          (node) => node.order === responseRun.currentStepIndex,
        )?.kind
      }
      onClose={() => setResponseRun(undefined)}
      onResumed={() => {
        void load();
        onChanged?.();
      }}
    />
  );
  const answerForm = form && responseRun && wrapWaitingResponse
    ? wrapWaitingResponse(responseRun, form)
    : form;
  return (
    <>
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
      ) : responseOnly ? (
        <div className="min-w-0 space-y-3">
          {fileTask && <CurrentProcessingFiles run={run} />}
          {completedWait ? <>
            <p className="text-sm">{completedWait.message}</p>
            {!readOnly && completedWait.onDiagnose && <Button color="secondary" size="sm" onPress={completedWait.onDiagnose}>
              {t("acquisition.runtime.diagnoseContinuation")}
            </Button>}
          </> : answerForm || (!readOnly && run.status === WorkflowRunStatus.Waiting && (
            <Button size="sm" variant="flat" onPress={() => setResponseRun(run)}>
              {t("workflow.resume.respond")}
            </Button>
          ))}
        </div>
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
            <ExecutionTiming startedAt={run.startedAt} completedAt={run.completedAt} />
            <WorkflowArtifactBrowserButton
              allowFileOperations={!readOnly}
              runId={runId}
              executions={run.nodeExecutions}
            />
            {!readOnly && run.workflowDefinitionId > 0 && (
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
            )}
          </div>
          {!responseOnly && <WorkflowParameterSummary sources={run.parameterSources} />}
          {run.progress != null && !completedWait && (
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
              {!completedWait && run.stage && ` · ${run.stage}`}
              {!completedWait && run.process && ` · ${run.process}`}
            </p>
          )}
          {run.errorMessage && !completedWait && (
            <DownloadProblemMessage
              className="space-y-1 break-words text-sm text-danger"
              message={run.errorMessage}
            />
          )}
          {run.status === WorkflowRunStatus.Waiting && (
            <div className="space-y-2 rounded-lg bg-secondary-50 p-3">
              {completedWait ? (
                <>
                  <p className="text-sm">{completedWait.message}</p>
                  {!readOnly && completedWait.onDiagnose && (
                    <Button color="secondary" size="sm" onPress={completedWait.onDiagnose}>
                      {t("acquisition.runtime.diagnoseContinuation")}
                    </Button>
                  )}
                </>
              ) : (
                <>
                  <p className="text-sm">
                    {t("workflow.resume.waitingFor", {
                      reason: workflowWaitReasonLabel(t, run.waitReason, run.waitPromptJson),
                    })}
                  </p>
                  {!readOnly && !responseRun && (
                    <Button color="secondary" size="sm" onPress={() => setResponseRun(run)}>
                      {t("workflow.resume.respond")}
                    </Button>
                  )}
                  {answerForm}
                </>
              )}
            </div>
          )}
          {!responseOnly && (
            <>
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
              <WorkflowNodeMetadata
                readOnly={readOnly}
                runId={runId}
                executions={run.nodeExecutions}
                legacyOutput={run.outputItemsJson}
                planNodes={run.hasPlanSnapshot ? run.planNodes : undefined}
                planName={run.planName}
                stepStats={run.stepStats}
              />
            </>
          )}
        </div>
      )}
    </>
  );
}

/** Standalone run viewer; embedding consumers keep navigation inside their existing dialog. */
export default function WorkflowRunDetailModal({
  runId,
  readOnly,
  onDestroyed,
}: DestroyableProps & { runId: number; readOnly?: boolean }) {
  const { t } = useTranslation();
  return (
    <Modal
      defaultVisible
      footer={false}
      size="3xl"
      title={t("workflow.runDetail.title", { id: runId })}
      onDestroyed={onDestroyed}
    >
      <WorkflowRunDetailContent runId={runId} readOnly={readOnly} />
    </Modal>
  );
}
