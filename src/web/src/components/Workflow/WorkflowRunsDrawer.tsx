"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Drawer, DrawerBody, DrawerContent, DrawerFooter, DrawerHeader } from "@heroui/react";

import { activityDisplayName } from "./displayNames";

import BApi from "@/sdk/BApi";
import { Chip, Pagination, Spinner } from "@/components/bakaui";
import { WorkflowRunStatus, WorkflowRunStatusLabel } from "@/sdk/constants";

type RunVm =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowRunViewModel"];

interface Props {
  workflowDefinitionId: number;
  workflowName: string;
  isOpen: boolean;
  onClose: () => void;
}

const PAGE_SIZE = 20;

const StatusColor: Record<
  WorkflowRunStatus,
  "default" | "primary" | "success" | "danger" | "warning"
> = {
  [WorkflowRunStatus.Pending]: "default",
  [WorkflowRunStatus.Running]: "primary",
  [WorkflowRunStatus.Success]: "success",
  [WorkflowRunStatus.Failed]: "danger",
  [WorkflowRunStatus.Cancelled]: "warning",
  [WorkflowRunStatus.Interrupted]: "warning",
};

function formatDuration(start: string, end?: string | null): string | null {
  try {
    const s = new Date(start).getTime();
    const e = end ? new Date(end).getTime() : NaN;
    if (isNaN(e)) return null;
    const ms = e - s;
    if (ms < 1000) return `${ms}ms`;
    if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
    return `${Math.floor(ms / 60_000)}m ${Math.floor((ms % 60_000) / 1000)}s`;
  } catch {
    return null;
  }
}

const WorkflowRunsDrawer: React.FC<Props> = ({
  workflowDefinitionId,
  workflowName,
  isOpen,
  onClose,
}) => {
  const { t } = useTranslation();
  const [runs, setRuns] = useState<RunVm[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.workflow.searchWorkflowRuns(workflowDefinitionId, {
        workflowDefinitionId,
        pageIndex: page,
        pageSize: PAGE_SIZE,
      });
      setRuns((rsp.data ?? []) as RunVm[]);
      setTotalCount(rsp.totalCount ?? 0);
    } finally {
      setLoading(false);
    }
  }, [workflowDefinitionId, page]);

  // Refetch on open or when paginating; closing the drawer just leaves stale state in
  // place so reopening doesn't flicker.
  useEffect(() => {
    if (isOpen) void load();
  }, [isOpen, load]);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <Drawer isOpen={isOpen} placement="right" size="lg" onClose={onClose}>
      <DrawerContent>
        <DrawerHeader className="flex flex-col gap-1">
          <span>{t<string>("workflow.runs.title")}</span>
          <span className="text-xs text-default-500 font-normal">{workflowName}</span>
        </DrawerHeader>
        <DrawerBody>
          {loading && runs.length === 0 ? (
            <div className="flex justify-center py-10">
              <Spinner size="lg" />
            </div>
          ) : runs.length === 0 ? (
            <div className="text-center text-default-500 py-10">
              {t<string>("workflow.runs.empty")}
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              {runs.map((r) => {
                const status = r.status as WorkflowRunStatus;
                const duration = formatDuration(r.startedAt, r.completedAt);
                return (
                  <div
                    key={r.id}
                    className="border border-default-200 rounded-lg p-3 flex flex-col gap-1"
                  >
                    <div className="flex items-center gap-2">
                      <Chip color={StatusColor[status] ?? "default"} size="sm" variant="flat">
                        {WorkflowRunStatusLabel[status]}
                      </Chip>
                      <span className="text-xs text-default-500">
                        #{r.id} · {new Date(r.startedAt).toLocaleString()}
                      </span>
                      {duration && (
                        <span className="text-xs text-default-400">· {duration}</span>
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
                    {r.errorMessage && (
                      <div className="text-xs text-danger break-words">
                        {r.errorMessage}
                      </div>
                    )}

                    {/* Per-step funnel: shows where items entered/left/failed. */}
                    {r.stepStats && r.stepStats.length > 0 && (
                      <div className="mt-1 flex flex-col gap-0.5">
                        {r.stepStats.map((s) => {
                          const name = activityDisplayName(t, s.kind);
                          const dropped = s.inputCount - s.outputCount;
                          return (
                            <div
                              key={s.stepIndex}
                              className="text-xs text-default-500 flex items-center gap-2"
                            >
                              <span className="text-default-400 w-4 text-right">
                                {s.stepIndex + 1}
                              </span>
                              <span className="flex-1 truncate">{name}</span>
                              <span className="tabular-nums">
                                {s.inputCount} → {s.outputCount}
                              </span>
                              {dropped > s.failedCount && (
                                <span className="tabular-nums text-default-400">
                                  −{dropped - s.failedCount}
                                </span>
                              )}
                              {s.failedCount > 0 && (
                                <span className="tabular-nums text-warning-600">
                                  ⚠{s.failedCount}
                                </span>
                              )}
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
        </DrawerBody>
        {totalPages > 1 && (
          <DrawerFooter className="justify-center">
            <Pagination page={page} size="sm" total={totalPages} onChange={setPage} />
          </DrawerFooter>
        )}
      </DrawerContent>
    </Drawer>
  );
};

export default WorkflowRunsDrawer;
