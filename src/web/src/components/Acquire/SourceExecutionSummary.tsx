import { lazy, Suspense, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { WorkflowRunStatus } from "@/sdk/constants";
import type { SourceRun, SourceRuns } from "./sourceHistory";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import { downloadProblemSummary } from "@/components/CloudDownload/problemLabels";
import WorkflowFileLocation from "@/components/Workflow/WorkflowFileLocation";
import type { CompletedDownloadWait } from "@/components/Workflow/WorkflowRunDetailModal";

const RunDetail = lazy(() => import("@/components/Workflow/WorkflowRunDetailModal"));
const bytes = (value: number) => {
  if (value < 1024) return value + " B";
  const unit = Math.min(3, Math.floor(Math.log(value) / Math.log(1024)));
  return (value / 1024 ** unit).toFixed(1) + " " + ["B", "KiB", "MiB", "GiB"][unit];
};

export default function SourceExecutionSummary({
  value,
  onHistory,
  loading = false,
  error,
  onRetry,
  hideWhenEmpty = false,
  readOnly = false,
  onOpenRun,
  completedDownloadWait,
}: {
  value?: SourceRuns;
  onHistory: () => void;
  loading?: boolean;
  error?: string;
  onRetry?: () => void;
  hideWhenEmpty?: boolean;
  readOnly?: boolean;
  /** Let an owning task show the run in its current detail view. */
  onOpenRun?: (runId: number) => void;
  completedDownloadWait?: CompletedDownloadWait;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const latest = value?.runs[0];
  const waiting = value?.statusCounts?.waiting ?? 0;
  if (hideWhenEmpty && value?.runCount === 0 && !loading && !error) return null;
  return (
    <section
      className="min-w-0 space-y-2 rounded-lg border border-default-200 bg-default-50/50 p-2.5"
      aria-label={t("acquire.history.processingTitle")}
    >
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
        <span className="font-medium text-foreground">{t("acquire.history.processingTitle")}</span>
        {!!value?.runCount && (
          <Button
            size="sm"
            variant="light"
            className="h-auto whitespace-normal px-0 py-0.5 text-xs"
            onPress={onHistory}
          >
            {t("acquire.history.count", { count: value.runCount })}
          </Button>
        )}
      </div>
      {error ? (
        <div className="space-y-1 text-xs text-warning-600" role="status">
          <p>{t("acquire.history.loadFailed")}</p>
          {onRetry && (
            <Button size="sm" variant="light" onPress={onRetry}>
              {t("common.action.retry")}
            </Button>
          )}
        </div>
      ) : !value ? (
        <p className="flex items-center gap-2 text-xs text-default-500" role="status">
          {loading && <Spinner size="sm" />}
          {t(loading ? "acquire.history.loading" : "acquire.history.statusUnknown")}
          {!loading && onRetry && (
            <Button size="sm" variant="light" onPress={onRetry}>
              {t("common.action.retry")}
            </Button>
          )}
        </p>
      ) : !latest ? (
        <div className="space-y-1 text-xs">
          <p className="font-medium">{t("acquire.history.notStarted")}</p>
          <p className="leading-relaxed text-default-500">{t("acquire.history.notStartedHint")}</p>
        </div>
      ) : (
        <>
          {waiting > 0 && (
            <p className="text-xs font-medium text-warning-600" role="status">
              {t("acquire.history.waitingCount", { count: waiting })}
            </p>
          )}
          <SourceRunSummary value={latest} compact completedDownloadWait={completedDownloadWait}>
            <Button
              size="sm"
              variant="light"
              className="h-auto max-w-full whitespace-normal break-words px-0 py-0.5 text-left text-xs"
              onPress={() => {
                if (onOpenRun) {
                  onOpenRun(latest.run.id);
                  return;
                }
                createPortal(
                  ({ onDestroyed }: DestroyableProps) => (
                    <Suspense fallback={<Spinner />}>
                      <RunDetail
                        runId={latest.run.id}
                        readOnly={readOnly}
                        onDestroyed={onDestroyed}
                      />
                    </Suspense>
                  ),
                  {},
                );
              }}
            >
              {latest.workflowName
                ? workflowLabel(
                    { name: latest.workflowName, isBuiltin: latest.workflowIsBuiltin },
                    t,
                  )
                : t("acquire.openRun", { id: latest.run.id })}
            </Button>
          </SourceRunSummary>
        </>
      )}
    </section>
  );
}

export function SourceRunSummary({
  value: latest,
  children,
  compact = false,
  sharedRunIds = [],
  completedDownloadWait,
}: {
  value: SourceRun;
  children?: ReactNode;
  compact?: boolean;
  sharedRunIds?: number[];
  completedDownloadWait?: CompletedDownloadWait;
}) {
  const { t } = useTranslation();
  const active = [
    WorkflowRunStatus.Pending,
    WorkflowRunStatus.Running,
    WorkflowRunStatus.Waiting,
  ].includes(latest.run.status);
  const locations = latest.outputLocations ?? [];
  // The server resolves published artifact locations against the current ledger.
  // deliveredDirectory is a frozen execution hint, not another current artifact.
  const hasResolvedDelivery = locations.some(
    (location) =>
      location.kind === "published" &&
      !location.isPlanned &&
      location.artifactId &&
      location.directory,
  );
  const hasProducedFiles =
    locations.some((location) => !location.isPlanned && location.artifactId) ||
    latest.completedFileCount > 0 ||
    !!latest.deliveredDirectory;
  const name = latest.run.activityKind && activityDisplayName(t, latest.run.activityKind);
  const completedWait = latest.run.status === WorkflowRunStatus.Waiting &&
    completedDownloadWait?.runId === latest.run.id &&
    completedDownloadWait.stepIndex === latest.run.currentStepIndex &&
    latest.run.activityKind === "download.awaitCompletion" ? completedDownloadWait : undefined;
  return (
    <div
      className="min-w-0 space-y-2 text-xs text-default-500"
      aria-label={t("acquire.history.title")}
    >
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
        {children}
        <span className="font-medium text-foreground">
          {t("acquire.history.run", { id: latest.run.id })}
        </span>
        <span
          className={
            latest.run.status === WorkflowRunStatus.Waiting ? "font-medium text-warning-600" : ""
          }
        >
          {t("acquire.phase." + latest.phase, { defaultValue: latest.phase })}
        </span>
        {latest.reused && <span className="text-success">{t("acquire.history.reused")}</span>}
        {latest.completedFileCount > 0 && (
          <span>{t("acquire.history.files", { count: latest.completedFileCount })}</span>
        )}
      </div>
      {(name || latest.run.currentStepIndex != null) && (
        <p>
          {latest.run.currentStepIndex != null
            ? t("acquire.history.currentStep", {
                number: latest.run.currentStepIndex + 1,
                name: name || t("acquire.history.stepUnknown"),
              })
            : t("acquire.history.currentActivity", { name })}
        </p>
      )}
      {completedWait && <p>{completedWait.message}</p>}
      {!completedWait && latest.run.status === WorkflowRunStatus.Waiting &&
        latest.run.waitReason &&
        !latest.problemCodes?.length && (
          <p className="text-warning-600">
            {t("workflow.waitReason." + latest.run.waitReason, {
              defaultValue: t("acquire.history.waitingHint"),
            })}
          </p>
        )}
      {!completedWait && !!latest.problemCodes?.length && (
        <div className="space-y-1 text-warning-600">
          {latest.problemCodes.map((code) => (
            <p key={code}>
              {downloadProblemSummary(code, t) ||
                t("acquire.requirement." + code, { defaultValue: "" }) ||
                t("acquire.history.problemUnknown")}
            </p>
          ))}
        </div>
      )}
      {!completedWait && active && ((latest.downloadedBytes ?? 0) > 0 || latest.run.progress != null) && (
        <div className="flex flex-wrap gap-2 tabular-nums">
          {latest.downloadedBytes != null ? (
            <span>
              {bytes(latest.downloadedBytes)}
              {latest.totalBytes != null && " / " + bytes(latest.totalBytes)}
            </span>
          ) : (
            <span>{latest.run.progress}%</span>
          )}
          {latest.speedBytesPerSecond != null && latest.speedBytesPerSecond > 0 && (
            <span>{bytes(latest.speedBytesPerSecond)}/s</span>
          )}
        </div>
      )}
      {!hasProducedFiles && <p>{t("acquire.history.noFilesYet")}</p>}
      <div className="space-y-1.5">
        {locations.map((location, index) => (
          <div key={location.kind + ":" + (location.artifactId ?? index)} className="min-w-0">
            <p className="font-medium">
              {t(
                location.isPlanned
                  ? "acquire.history.location.planned"
                  : "acquire.history.location." + location.kind,
              )}
              {location.fileCount != null &&
                !location.isPlanned &&
                " · " + t("acquire.history.files", { count: location.fileCount })}
              {location.state &&
                " · " +
                  t("workflow.metadata.artifactState." + location.state, {
                    defaultValue: location.state,
                  })}
            </p>
            {location.directory && (
              <div title={location.directory}>
                <WorkflowFileLocation
                  path={location.directory}
                  available={!location.isPlanned && location.state === "available"}
                />
              </div>
            )}
          </div>
        ))}
        {latest.deliveredDirectory &&
          !hasResolvedDelivery &&
          !locations.some((location) => location.directory === latest.deliveredDirectory) && (
            <div className="space-y-1">
              <WorkflowFileLocation
                path={latest.deliveredDirectory}
                available={false}
                label={t("acquire.history.deliveredAtExecution")}
              />
              <p>{t("acquire.history.recordedDirectoryHint")}</p>
            </div>
          )}
      </div>
      {!compact && !!latest.downloadTaskIds?.length && (
        <p>{t("acquire.history.downloadTasks", { ids: latest.downloadTaskIds.join(", ") })}</p>
      )}
      {!!sharedRunIds.length && (
        <p className="rounded-md bg-warning-50 p-2 leading-relaxed text-warning-700">
          {t("acquire.history.sharedDownload", {
            ids: sharedRunIds.map((id) => "#" + id).join(", "),
          })}
        </p>
      )}
    </div>
  );
}
