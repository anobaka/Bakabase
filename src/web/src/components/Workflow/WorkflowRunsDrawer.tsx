"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Drawer, DrawerBody, DrawerContent, DrawerFooter, DrawerHeader } from "@heroui/react";

import { activityDisplayName } from "./displayNames";
import RenamePlanPanel from "./RenamePlanPanel";
import ResumeRunModal from "./ResumeRunModal";
import WorkflowStepCounts from "./WorkflowStepCounts";

import BApi from "@/sdk/BApi";
import { Button, Chip, Pagination, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { WorkflowRunStatus, WorkflowRunStatusLabel } from "@/sdk/constants";
import WorkflowNodeMetadata from "./WorkflowNodeMetadata";
import DetailExpansionControls from "./DetailExpansionControls";
import { useDetailExpansion } from "@/hooks/useDetailExpansion";
import { workflowLabel } from "./builtinLabels";
import { workflowWaitReasonLabel } from "./waitReasonLabel";
import DownloadProblemMessage from "@/components/CloudDownload/DownloadProblemMessage";
import WorkflowParameterSummary from "./Parameters/WorkflowParameterSummary";
import ExecutionTiming from "./ExecutionTiming";

type RunVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowRunViewModel"] & {
    workflowName?: string;
    workflowIsBuiltin?: boolean;
  };

export interface WorkflowRunPageInput {
  pageIndex: number;
  pageSize: number;
}

export type WorkflowRunLoader = (input: WorkflowRunPageInput) => Promise<{
  data?: RunVm[] | null;
  totalCount?: number | null;
}>;

interface SharedProps {
  workflowName: string;
  headerContent?: React.ReactNode;
  emptyContent?: React.ReactNode;
  /** A stable scope identity; changing it clears the previous scope's rows and pagination. */
  runSourceKey?: string;
  /** Lets fs-domain runs surface their rename plan; other domains have none to show. */
  triggerKind?: string;
  /** Optional source-owned projection, independent of a run's output JSON shape. */
  renderRunSummary?: (run: RunVm) => React.ReactNode;
  inline?: boolean;
  isOpen: boolean;
  readOnly?: boolean;
  onClose: () => void;
  onOpenRun?: (runId: number, respond?: boolean) => void;
}

type Props = SharedProps &
  (
    | { workflowDefinitionId: number; loadRuns?: WorkflowRunLoader }
    | { workflowDefinitionId?: never; loadRuns: WorkflowRunLoader }
  );

const PAGE_SIZE = 20;

const StatusColor: Record<
  WorkflowRunStatus,
  "default" | "primary" | "success" | "danger" | "warning" | "secondary"
> = {
  [WorkflowRunStatus.Pending]: "default",
  [WorkflowRunStatus.Running]: "primary",
  [WorkflowRunStatus.Success]: "success",
  [WorkflowRunStatus.Failed]: "danger",
  [WorkflowRunStatus.Cancelled]: "warning",
  [WorkflowRunStatus.Interrupted]: "warning",
  [WorkflowRunStatus.Waiting]: "secondary",
};

/** "3 days" reads very differently from "3 minutes" when it's a person being waited on. */
function formatWaitingFor(since: string): string | null {
  try {
    const ms = Date.now() - new Date(since).getTime();

    if (isNaN(ms) || ms < 0) return null;
    if (ms < 60_000) return `${Math.floor(ms / 1000)}s`;
    if (ms < 3_600_000) return `${Math.floor(ms / 60_000)}m`;
    if (ms < 86_400_000) return `${Math.floor(ms / 3_600_000)}h`;

    return `${Math.floor(ms / 86_400_000)}d`;
  } catch {
    return null;
  }
}

const ScopedWorkflowRunsDrawer: React.FC<Props> = ({
  workflowDefinitionId,
  loadRuns,
  workflowName,
  triggerKind,
  renderRunSummary,
  headerContent,
  emptyContent,
  inline = false,
  isOpen,
  onClose,
  onOpenRun,
  readOnly = false,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [runs, setRuns] = useState<RunVm[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const expansion = useDetailExpansion(undefined, page);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState<string>();
  const requestId = useRef(0);
  const activeRequest = useRef<number>();

  const load = useCallback(
    async (background = false) => {
      if (background && activeRequest.current != null) return;
      const request = ++requestId.current;
      activeRequest.current = request;

      if (!background) setLoading(true);
      try {
        const pagination = { pageIndex: page, pageSize: PAGE_SIZE };
        const rsp = loadRuns
          ? await loadRuns(pagination)
          : await BApi.workflow.searchWorkflowRuns(workflowDefinitionId!, {
              workflowDefinitionId,
              ...pagination,
            });

        if (request !== requestId.current) return;
        if ("code" in rsp && rsp.code)
          throw new Error(
            "message" in rsp && typeof rsp.message === "string" && rsp.message
              ? rsp.message
              : "WORKFLOW_HISTORY_FAILED",
          );
        setRuns((rsp.data ?? []) as RunVm[]);
        setTotalCount(rsp.totalCount ?? 0);
        setLoadError(undefined);
      } catch (failure) {
        if (request === requestId.current)
          setLoadError(failure instanceof Error ? failure.message : String(failure));
      } finally {
        if (activeRequest.current === request) activeRequest.current = undefined;
        if (request === requestId.current) setLoading(false);
      }
    },
    [workflowDefinitionId, loadRuns, page],
  );

  // Refetch on open or when paginating; closing the drawer just leaves stale state in
  // place so reopening doesn't flicker.
  useEffect(() => {
    if (isOpen) void load();

    return () => {
      requestId.current++;
    };
  }, [isOpen, load]);

  const hasActiveRuns = runs.some(
    (run) => run.status === WorkflowRunStatus.Pending || run.status === WorkflowRunStatus.Running,
  );
  useEffect(() => {
    if (!isOpen || !hasActiveRuns) return;
    const timer = setInterval(() => {
      if (!document.hidden)
        void load(true).catch(() => {
          /* Retain the current rows when a background refresh fails. */
        });
    }, 2000);
    return () => clearInterval(timer);
  }, [isOpen, hasActiveRuns, load]);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const content = (
    <>
      {headerContent}
      {!!runs.length && (
        <div className="flex justify-end">
          <DetailExpansionControls onChange={expansion.setAll} currentPage />
        </div>
      )}
      {loadError && (
        <div className="space-y-2 text-sm text-danger" role="alert">
          <p>{t<string>("workflow.runs.loadFailed")}</p>
          <p className="break-words">{loadError}</p>
          <Button size="sm" variant="flat" isLoading={loading} onPress={() => void load()}>
            {t<string>("common.action.retry")}
          </Button>
        </div>
      )}
      {loading && runs.length === 0 ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : runs.length === 0 && !loadError ? (
        <div className="text-center text-default-500 py-10">
          {emptyContent ?? t<string>("workflow.runs.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {runs.map((r) => {
            const status = r.status as WorkflowRunStatus;

            return (
              <div
                key={r.id}
                className="border border-default-200 rounded-lg p-3 flex flex-col gap-1"
              >
                {r.workflowName && (
                  <p className="text-sm font-medium">
                    {workflowLabel({ name: r.workflowName, isBuiltin: r.workflowIsBuiltin }, t)}
                  </p>
                )}
                <div className="flex items-center gap-2">
                  <Chip color={StatusColor[status] ?? "default"} size="sm" variant="flat">
                    {t(`workflow.runs.status.${WorkflowRunStatus[status]}`, {
                      defaultValue: WorkflowRunStatusLabel[status],
                    })}
                  </Chip>
                  <span className="text-xs text-default-500">
                    #{r.id}
                  </span>
                  <ExecutionTiming startedAt={r.startedAt} completedAt={r.completedAt} />
                  {onOpenRun && (
                    <Button
                      className="ml-auto"
                      size="sm"
                      variant="flat"
                      color={
                        !readOnly && status === WorkflowRunStatus.Waiting ? "secondary" : "default"
                      }
                      onPress={() =>
                        onOpenRun(r.id, !readOnly && status === WorkflowRunStatus.Waiting)
                      }
                    >
                      {t(
                        !readOnly && status === WorkflowRunStatus.Waiting
                          ? "workflow.resume.respond"
                          : "acquire.openRun",
                        { id: r.id },
                      )}
                    </Button>
                  )}
                  {!onOpenRun && !readOnly && status === WorkflowRunStatus.Waiting && (
                    <Button
                      className="ml-auto"
                      color="secondary"
                      size="sm"
                      variant="flat"
                      onPress={() =>
                        createPortal(ResumeRunModal, {
                          runId: r.id,
                          workflowDefinitionId: r.workflowDefinitionId,
                          currentStepIndex: r.currentStepIndex,
                          waitReason: r.waitReason,
                          waitPromptJson: r.waitPromptJson,
                          activityKind: r.activityKind,
                          onResumed: load,
                        })
                      }
                    >
                      {t<string>("workflow.resume.respond")}
                    </Button>
                  )}
                  {["fs.manualScan", "fs.scheduledScan", "fs.watch"].includes(
                    triggerKind ?? "",
                  ) && (
                    <Button
                      className="ml-auto"
                      size="sm"
                      variant="light"
                      onPress={() =>
                        // A finished run's plan is the confirm surface (interactive);
                        // anything still moving — or already dead — is only a record.
                        createPortal(RenamePlanPanel, {
                          runId: r.id,
                          readOnly: readOnly || status !== WorkflowRunStatus.Success,
                        })
                      }
                    >
                      {t<string>("workflow.renamePlan.open")}
                    </Button>
                  )}
                </div>
                <div className="text-xs text-default-500 flex flex-wrap gap-x-3">
                  <span>
                    {t<string>("workflow.runs.input")}: {r.inputCount}
                  </span>
                  <span>
                    {t<string>("workflow.runs.output")}: {r.outputCount}
                  </span>
                  {r.failedItemCount > 0 && (
                    <span className="text-warning-600">
                      {t<string>("workflow.runs.failed")}: {r.failedItemCount}
                    </span>
                  )}
                </div>
                {renderRunSummary?.(r)}
                <WorkflowParameterSummary sources={r.parameterSources} />
                {status === WorkflowRunStatus.Waiting && (
                  <div className="text-xs text-secondary-600 break-words">
                    {t<string>("workflow.resume.waitingFor", {
                      reason: workflowWaitReasonLabel(t, r.waitReason, r.waitPromptJson),
                    })}
                    {r.waitingSince && ` · ${formatWaitingFor(r.waitingSince)}`}
                  </div>
                )}
                {r.errorMessage && (
                  <DownloadProblemMessage
                    className="space-y-1 text-xs text-danger break-words"
                    message={r.errorMessage}
                  />
                )}

                <WorkflowNodeMetadata
                  readOnly={readOnly}
                  runId={r.id}
                  executions={r.nodeExecutions}
                  legacyOutput={r.outputItemsJson}
                  expansion={expansion.request}
                />

                {/* Per-step funnel: shows where items entered/left/failed. */}
                {r.stepStats && r.stepStats.length > 0 && (
                  <div className="mt-1 flex flex-col gap-0.5">
                    {r.stepStats.map((s) => {
                      const name = activityDisplayName(t, s.kind);

                      return (
                        <div
                          key={s.stepIndex}
                          className="text-xs text-default-500 flex items-center gap-2"
                        >
                          <span className="text-default-400 w-4 text-right">{s.stepIndex + 1}</span>
                          <span className="flex-1 truncate">{name}</span>
                          <WorkflowStepCounts
                            kind={s.kind}
                            inputCount={s.inputCount}
                            outputCount={s.outputCount}
                            failedCount={s.failedCount}
                          />
                        </div>
                      );
                    })}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </>
  );
  const pagination = totalPages > 1 && (
    <Pagination page={page} size="sm" total={totalPages} onChange={setPage} />
  );
  if (inline)
    return isOpen ? (
      <section className="space-y-3" aria-label={workflowName}>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-sm font-medium">{workflowName}</h3>
          <Button variant="light" size="sm" onPress={onClose}>
            {t("acquisition.plan.diagnostics.backToRun")}
          </Button>
        </div>
        {content}
        {pagination}
      </section>
    ) : null;

  return (
    <Drawer isOpen={isOpen} placement="right" size="lg" onClose={onClose}>
      <DrawerContent>
        <DrawerHeader className="flex flex-col gap-1">
          <span>{t<string>("workflow.runs.title")}</span>
          <span className="text-xs text-default-500 font-normal">{workflowName}</span>
        </DrawerHeader>
        <DrawerBody>{content}</DrawerBody>
        {pagination && <DrawerFooter className="justify-center">{pagination}</DrawerFooter>}
      </DrawerContent>
    </Drawer>
  );
};

const WorkflowRunsDrawer: React.FC<Props> = (props) => (
  <ScopedWorkflowRunsDrawer key={props.runSourceKey ?? props.workflowDefinitionId} {...props} />
);

export default WorkflowRunsDrawer;
