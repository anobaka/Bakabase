import { type ReactNode, useState } from "react";
import { CloudDownloadOutlined, FileDoneOutlined, FileSearchOutlined, HistoryOutlined, ToolOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { Button, Spinner, Tooltip } from "@/components/bakaui";
import StageExecutionRecords from "./StageExecutionRecords";
import { AttemptStamp, AttemptStatus, assessmentCode, type Attempt } from "./acquisitionAttempt";
import type { PlanTaskDetail } from "./models";
import WorkflowFileLocation from "@/components/Workflow/WorkflowFileLocation";
import { AcquisitionStatus } from "@/sdk/constants";

const stages = ["download", "processing", "delivery"] as const;
const stageIcons = { download: CloudDownloadOutlined, processing: ToolOutlined, delivery: FileDoneOutlined };
const stageStyles = {
  download: { background: "bg-primary-50/30", icon: "bg-primary-100 text-primary-600" },
  processing: { background: "bg-secondary-50/30", icon: "bg-secondary-100 text-secondary-600" },
  delivery: { background: "bg-success-50/30", icon: "bg-success-100 text-success-600" },
};
export type AcquisitionStage = (typeof stages)[number];
/** Preparation has no file workspace or workflow execution cursor of its own. */
export type AcquisitionPresentationStage = "preparation" | AcquisitionStage;

export function asAcquisitionStage(value?: string | null): AcquisitionStage | undefined {
  return stages.find((stage) => stage === value);
}

function AssessmentResult({
  attempt,
  code,
  onInspect,
}: {
  attempt: Attempt;
  code?: string | null;
  onInspect: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex min-w-0 flex-wrap items-baseline gap-x-2">
      <span className="text-xs text-default-500">
        {t("workflow.resume.guide.run")} #{attempt.runId} ·{" "}
        {t("workflow.metadata.attempt", { number: attempt.attempt })}
      </span>
      {code && (
        <span className="break-words text-sm text-warning-700">
          {t(`cloudDownload.problemCode.${code}`)}
        </span>
      )}
      <Button size="sm" variant="light" color="primary" onPress={onInspect}>
        {t("acquisition.records.viewIo")}
      </Button>
    </div>
  );
}

function StageRow({
  taskId,
  runId,
  stage,
  attempts,
  activeId,
  pending,
  actions,
  headerActions,
  currentProblemCode,
  directories,
  onOpenRun,
}: {
  taskId: number;
  runId?: number | null;
  stage: AcquisitionStage;
  attempts: Attempt[];
  activeId?: string;
  pending: boolean;
  actions?: ReactNode;
  headerActions?: ReactNode;
  currentProblemCode?: string | null;
  directories: { path: string; title?: string }[];
  onOpenRun: (id: number) => void;
}) {
  const { t } = useTranslation();
  const [record, setRecord] = useState<{ id: string; io: boolean }>();
  const latest = attempts[attempts.length - 1];
  const StageIcon = stageIcons[stage];
  const style = stageStyles[stage];
  const current = !!latest && latest.id === activeId;
  const live = current && ["running", "waiting"].includes(latest.status);
  const previousResult = pending && !!latest && latest.runId !== runId;
  const recentResults =
    stage === "processing"
      ? attempts
          .flatMap((attempt) => {
            const code = assessmentCode(attempt);
            return code || attempt.hasAssessmentCalls ? [{ attempt, code }] : [];
          })
          .slice(-1)
      : [];
  return (
    <li
      className={`relative min-w-0 rounded-r-lg border-l-2 pl-6 pr-2 py-2 ${current ? `${style.background} border-primary-300` : "border-default-200"}`}
      aria-current={live ? "step" : undefined}
    >
      <span aria-hidden className={`absolute left-0 top-2 -translate-x-1/2 flex h-8 w-8 items-center justify-center rounded-full ring-2 ring-background ${style.icon}`}>
        <StageIcon />
      </span>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        <div className="inline-flex items-center gap-2">
          <h4 className="text-sm font-semibold text-foreground">{t(`acquisition.timeline.stage.${stage}`)}</h4>
          <AttemptStatus status={previousResult ? "pending" : latest?.status ?? (pending ? "pending" : "unknown")} />
          {live && latest.status === "running" && (
            <Spinner size="sm" aria-label={t("acquisition.timeline.state.running")} />
          )}
        </div>
        {latest && <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
          <Tooltip content={t("acquisition.records.open")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              className="h-7 w-7 min-w-7"
              aria-label={t("acquisition.records.open")}
              aria-haspopup="dialog"
              onPress={() => setRecord({ id: latest.id, io: false })}
            >
              <HistoryOutlined aria-hidden />
            </Button>
          </Tooltip>
          <Tooltip content={t("acquisition.timeline.attemptHint")}>
            <span className="text-xs text-default-500">
              {t(previousResult ? "acquisition.timeline.previousResult" : live
                ? "acquisition.timeline.liveAttempt" : "acquisition.timeline.latestAttempt")}
            </span>
          </Tooltip>
          {previousResult && <AttemptStatus status={latest.status} />}
          <AttemptStamp attempt={latest} />
        </div>}
        <div className="ml-auto flex flex-wrap items-center justify-end gap-1">{headerActions}</div>
      </div>
      {actions}
      {directories.length > 0 && <div className="my-1 min-w-0 space-y-1">
        {directories.map(directory => <div key={directory.path} className="min-w-0">
          {directories.length > 1 && directory.title && <p className="text-xs text-default-500">{directory.title}</p>}
          <WorkflowFileLocation path={directory.path} available
            taskDirectory={runId ? { taskId, runId } : undefined} />
        </div>)}
      </div>}
      {recentResults.length > 0 && (
        <section aria-label={t("acquisition.timeline.aiAnalysis")} className="my-1 space-y-1">
          {recentResults.map(({ attempt, code }) => (
            <AssessmentResult
              key={attempt.id}
              attempt={attempt}
              code={attempt.runId === runId && code === currentProblemCode ? undefined : code}
              onInspect={() => setRecord({ id: attempt.id, io: true })}
            />
          ))}
        </section>
      )}
      {record && (
        <StageExecutionRecords
          taskId={taskId}
          stage={stage}
          attempts={attempts}
          initial={record}
          onClose={() => setRecord(undefined)}
          onOpenRun={(id) => {
            setRecord(undefined);
            onOpenRun(id);
          }}
        />
      )}
    </li>
  );
}

export default function AcquisitionExecutionTimeline({
  detail,
  actions,
  headerActions = {},
  currentProblemCode,
  currentStepIndex,
  onOpenRun,
  onPreparationHistory,
  preparationWaiting = false,
}: {
  detail: PlanTaskDetail;
  actions: Partial<Record<AcquisitionPresentationStage, ReactNode>>;
  headerActions?: Partial<Record<AcquisitionPresentationStage, ReactNode>>;
  currentProblemCode?: string | null;
  currentStepIndex?: number | null;
  onOpenRun: (id: number) => void;
  onPreparationHistory?: () => void;
  preparationWaiting?: boolean;
}) {
  const { t } = useTranslation();
  const attempts = detail.stageAttempts ?? [];
  // Current bindings are separate from historical attempt outputs; never derive or rename paths.
  const directories = detail.stageDirectories;
  const active = attempts
    .slice()
    .reverse()
    .find((attempt) => attempt.runId === detail.task.workflowRunId);
  const currentStage = asAcquisitionStage(currentStepIndex != null
    ? detail.steps[currentStepIndex]?.stage : undefined) ?? asAcquisitionStage(active?.stage);
  const preparationComplete = !preparationWaiting && (!!detail.task.workflowRunId || detail.task.phase === "ready");
  const preparationStatus = preparationComplete ? "completed" :
    detail.task.status === AcquisitionStatus.Cancelled ? "cancelled" :
    detail.task.phase === "searching" ? "running" : "waiting";
  const sourceCount = detail.plan?.parts.reduce((count, part) => count + (part.sources?.length ?? 0), 0) ?? detail.draftSources?.length ?? 0;
  const hasPreparationHistory = !!detail.preparationAttempts?.length || !!detail.task.creationOrigins?.length;
  return (
    <section aria-label={t("acquisition.timeline.title")} className="min-w-0">
      <h3 className="mb-2 text-sm font-medium">{t("acquisition.timeline.title")}</h3>
      <ol className="ml-4 space-y-1">
        <li className={`relative min-w-0 rounded-r-lg border-l-2 pl-6 pr-2 py-2 ${preparationComplete ? "border-default-200" : "border-primary-300 bg-primary-50/30"}`}
          aria-current={!preparationComplete ? "step" : undefined}>
          <span aria-hidden className="absolute left-0 top-2 -translate-x-1/2 flex h-8 w-8 items-center justify-center rounded-full ring-2 ring-background bg-warning-100 text-warning-700">
            <FileSearchOutlined />
          </span>
          <div className="flex flex-wrap items-center gap-2">
            <h4 className="text-sm font-semibold text-foreground">{t("acquisition.timeline.stage.preparation")}</h4>
            <AttemptStatus status={preparationStatus} />
            {hasPreparationHistory && onPreparationHistory && <Tooltip content={t("acquisition.preparation.history")}>
              <Button isIconOnly size="sm" variant="light" className="h-7 w-7 min-w-7"
                aria-label={t("acquisition.preparation.history")} aria-haspopup="dialog" onPress={onPreparationHistory}>
                <HistoryOutlined aria-hidden />
              </Button>
            </Tooltip>}
            <div className="ml-auto flex flex-wrap items-center justify-end gap-1">{headerActions.preparation}</div>
          </div>
          <p className="my-1 text-xs text-default-500">{t(preparationComplete ? "acquisition.preparation.readySummary" :
            sourceCount ? "acquisition.preparation.sourcesSummary" : "acquisition.preparation.emptySummary", { count: sourceCount })}</p>
          {actions.preparation}
        </li>
        {stages.map((stage) => {
          const currentDirectories = new Map<string, string[]>();
          if (directories?.taskId === detail.task.id) for (const [partId, locations] of Object.entries(directories.parts)) {
            const path = locations[stage]?.directory;
            if (!path) continue;
            const title = detail.plan?.parts.find(part => part.id === partId)?.title || partId;
            currentDirectories.set(path, [...(currentDirectories.get(path) ?? []), title]);
          }
          const firstStep = detail.steps.findIndex((step) => step.stage === stage);
          const stageAttempts = attempts.filter((attempt) => attempt.stage === stage);
          const attemptedThisRun = stageAttempts.some(attempt => attempt.runId === detail.task.workflowRunId);
          const pending =
            !detail.task.workflowRunId || firstStep >= 0 &&
            (!detail.task.workflowRunId ||
              (detail.task.status !== AcquisitionStatus.Completed && !attemptedThisRun &&
                (currentStage ? stages.indexOf(stage) > stages.indexOf(currentStage) :
                  currentStepIndex != null && firstStep > currentStepIndex)));
          return (
            <StageRow
              key={`${detail.task.id}:${stage}`}
              taskId={detail.task.id}
              runId={detail.task.workflowRunId}
              stage={stage}
              attempts={stageAttempts}
              activeId={preparationWaiting ? undefined : active?.id}
              pending={pending}
              actions={actions[stage]}
              headerActions={headerActions[stage]}
              currentProblemCode={currentProblemCode}
              directories={Array.from(currentDirectories, ([path, titles]) => ({ path, title: titles.join(" · ") }))}
              onOpenRun={onOpenRun}
            />
          );
        })}
      </ol>
    </section>
  );
}
