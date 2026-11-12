import { type ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Modal, Tab, Tabs } from "@/components/bakaui";
import {
  WorkflowRunDetailContent,
  type CompletedDownloadWait,
} from "@/components/Workflow/WorkflowRunDetailModal";
import SourceExecutionSummary from "../SourceExecutionSummary";
import SourceExecutionHistory from "../SourceExecutionHistory";
import { sourceRefKey } from "../sourceHistory";
import { useSourceRuns } from "../useSourceRuns";
import type { PlanTaskDetail } from "./models";

export type DiagnosticsPage = { tab: "plan" } | { tab: "files"; runId?: number };

/** A separate inspection surface: even the latest run cannot be answered from history. */
export default function TaskDiagnosticsModal({
  taskId,
  detail,
  page,
  onPageChange,
  onClose,
  children,
  completedDownloadWait,
}: {
  taskId: number;
  detail: PlanTaskDetail;
  page: DiagnosticsPage;
  onPageChange: (page: DiagnosticsPage) => void;
  onClose: () => void;
  children: ReactNode;
  completedDownloadWait?: CompletedDownloadWait;
}) {
  const { t } = useTranslation();
  const [allRuns, setAllRuns] = useState(false);
  const source = { module: "acquisition", entityKind: "task", entityId: String(taskId) };
  const history = useSourceRuns(
    page.tab === "files" && !allRuns ? [source] : [],
    detail.task.updatedAt,
  );
  const runId = page.tab === "files" ? (page.runId ?? detail.task.workflowRunId) : undefined;
  const content = useRef<HTMLDivElement>(null);
  const execution = useRef<HTMLElement>(null);
  const focusAfterNavigation = useRef<"list" | "run">();
  // Inline navigation removes the button that had focus. Keep keyboard focus inside
  // the active dialog so Escape dismisses only this surface and Tab stays in context.
  useLayoutEffect(() => {
    const destination = focusAfterNavigation.current;
    if (!destination) return;
    focusAfterNavigation.current = undefined;
    const element =
      destination === "run" ? (execution.current ?? content.current) : content.current;
    element?.focus({ preventScroll: true });
    element?.scrollIntoView?.({ block: "nearest" });
  });
  const showAllRuns = (open: boolean) => {
    focusAfterNavigation.current = open ? "list" : "run";
    setAllRuns(open);
  };
  const linkedRun = page.tab === "files" ? page.runId : undefined;
  useEffect(() => {
    if (!allRuns && linkedRun != null) execution.current?.scrollIntoView?.({ block: "start" });
  }, [linkedRun, allRuns]);
  const openRun = (id: number) => {
    showAllRuns(false);
    onPageChange({ tab: "files", runId: id });
  };
  return (
    <Modal
      visible
      size="6xl"
      title={t("acquisition.plan.diagnostics.title", { id: taskId })}
      classNames={{
        base: "h-[85dvh] max-h-[calc(100dvh-1rem)] sm:max-h-[calc(100dvh-3rem)]",
        body: "min-h-0 overflow-hidden",
      }}
      onClose={onClose}
      footer={
        <Button variant="light" onPress={onClose}>
          {t("acquisition.plan.close")}
        </Button>
      }
    >
      <Tabs
        aria-label={t("acquisition.plan.diagnostics.open")}
        size="sm"
        selectedKey={page.tab}
        classNames={{ base: "shrink-0 max-w-full", tabList: "max-w-full" }}
        onSelectionChange={(key) => {
          if (key !== page.tab) {
            setAllRuns(false);
            onPageChange({ tab: key === "files" ? "files" : "plan" });
          }
        }}
      >
        <Tab key="plan" title={t("acquisition.plan.tab.plan")} />
        <Tab key="files" title={t("acquisition.plan.tab.files")} />
      </Tabs>
      <div
        key={page.tab}
        ref={content}
        tabIndex={-1}
        role="region"
        aria-label={t(
          allRuns
            ? "acquire.history.title"
            : page.tab === "plan"
              ? "acquisition.plan.tab.plan"
              : "acquisition.plan.tab.files",
        )}
        className="min-h-0 min-w-0 flex-1 overflow-y-auto overscroll-contain"
      >
        {page.tab === "plan" ? (
          children
        ) : allRuns ? (
          <SourceExecutionHistory
            inline
            source={source}
            readOnly
            onOpenRun={openRun}
            onClose={() => showAllRuns(false)}
          />
        ) : (
          <div className="space-y-3">
            <p className="text-sm text-default-500">{t("acquisition.plan.filesHint")}</p>
            <SourceExecutionSummary
              readOnly
              value={history.rows.get(sourceRefKey(source))}
              loading={history.loading}
              error={history.error}
              onRetry={history.refresh}
              onHistory={() => showAllRuns(true)}
              onOpenRun={openRun}
              completedDownloadWait={completedDownloadWait}
            />
            {runId != null && (
              <section
                ref={execution}
                tabIndex={-1}
                className="space-y-3"
                aria-label={t("workflow.runDetail.title", { id: runId })}
              >
                <h3 className="text-sm font-medium">
                  {t("workflow.runDetail.title", { id: runId })}
                </h3>
                <WorkflowRunDetailContent
                  key={runId}
                  runId={runId}
                  readOnly
                  respondInitially={false}
                  completedDownloadWait={completedDownloadWait}
                />
              </section>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
