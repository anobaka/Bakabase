import { useTranslation } from "react-i18next";
import { Button, Chip, Popover, Progress, Tooltip } from "@/components/bakaui";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import ProcessingProblemSummary from "@/components/Workflow/ProcessingProblemSummary";
import { humanFileSize } from "@/components/utils";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import AcquisitionSourceDetails from "../AcquisitionSourceDetails";
import { downloadContinuationLabel, runtimeProblemLabel, type RuntimeOverview, type RuntimeSource } from "./runtimeOverview";

const validBytes = (value?: number | null): value is number =>
  value != null && Number.isFinite(value) && value >= 0;

function RuntimeSourceEvidence({ source }: { source: RuntimeSource }) {
  const { t } = useTranslation();
  const historical =
    source.isHistorical || (!!source.expiresAt && millisecondsUntil(source.expiresAt) <= 0);
  const reason = runtimeProblemLabel(
    t,
    source.normalizedReasonCode ?? source.reasonCode,
    source.message,
  );
  const status = [
    "available",
    "confirmedUnavailable",
    "accessRequired",
    "temporarilyUnavailable",
    "unknown",
    "unsupported",
  ].includes(source.status)
    ? source.status
    : "unknown";
  return (
    <div className="space-y-2 rounded-lg border border-default-200 p-2">
      <div className="flex flex-wrap items-center gap-1 text-xs">
        <Chip
          size="sm"
          variant="flat"
          color={!historical && status === "confirmedUnavailable" ? "danger" : "default"}
        >
          {historical
            ? t("acquisition.runtime.historicalStatus", {
                status: t(`sourceAvailability.status.${status}`),
              })
            : t(`sourceAvailability.status.${status}`)}
        </Chip>
        {source.selected && (
          <span className="text-default-500">{t("acquisition.runtime.selectedSource")}</span>
        )}
      </div>
      {reason && <p className="break-words text-xs">{reason}</p>}
      <AcquisitionSourceDetails source={source} />
      <div className="flex flex-wrap gap-x-2 gap-y-1 text-xs text-default-500">
        <span>
          {t(
            `acquisition.runtime.evidence.${["probe", "download", "workflow"].includes(source.evidenceKind ?? "") ? source.evidenceKind : "unknown"}`,
          )}
        </span>
        <span>
          {source.observedAt
            ? t("acquisition.runtime.evidenceTime", {
                time: parseServerTime(source.observedAt)?.toLocaleString() ?? source.observedAt,
              })
            : t("acquisition.runtime.evidenceTimeUnknown")}
        </span>
      </div>
      {historical && (
        <p className="text-xs text-default-500">{t("acquisition.runtime.historicalHint")}</p>
      )}
    </div>
  );
}

export function RuntimeSources({ overview }: { overview: RuntimeOverview }) {
  const { t } = useTranslation();
  const sources = overview.sources ?? [];
  if (!sources.length) return null;
  return (
    <Popover
      trigger={
        <Button size="sm" variant="light" className="h-6 min-w-0 px-1 text-xs">
          {t("acquisition.runtime.sourceDetails", { count: sources.length })}
        </Button>
      }
    >
      <div className="max-h-[60vh] w-[min(32rem,85vw)] space-y-2 overflow-y-auto p-2">
        <p className="text-xs text-default-500">
          {t("acquisition.runtime.sourceEvidenceHint")}
        </p>
        {sources.map((source, index) => (
          <RuntimeSourceEvidence
            key={`${source.partId}:${source.sourceId}:${source.downloadTaskId ?? index}:${source.evidenceKind}`}
            source={source}
          />
        ))}
        {overview.sourcesTruncated && (
          <p className="text-xs text-default-500">
            {t("acquisition.runtime.sourcesTruncated")}
          </p>
        )}
      </div>
    </Popover>
  );
}

/** Measured transfer progress and current execution evidence, never a guessed whole-plan percentage. */
export default function RuntimeOverviewSummary({ overview, showSources = true, problemDetail }: {
  overview?: RuntimeOverview; showSources?: boolean; problemDetail?: string;
}) {
  const { t } = useTranslation();
  if (!overview) return <div className="min-h-8" />;
  const step = overview.currentStepIndex;
  const stepCount = overview.stepCount;
  const stepNumber =
    step != null && Number.isInteger(step) && step >= 0 && stepCount != null && step < stepCount
      ? step + 1
      : undefined;
  const problem = runtimeProblemLabel(
    t,
    overview.problemReasonCode ?? overview.problemCode,
    overview.problem,
  );
  const progress =
    overview.progressScope === "download" &&
    overview.progress != null &&
    Number.isFinite(overview.progress)
      ? Math.min(100, Math.max(0, overview.progress))
      : undefined;
  const completedBytes = overview.completedBytes;
  const totalBytes = overview.totalBytes;
  const hasDownloads = overview.downloadCount > 0;
  const continuation = downloadContinuationLabel(t, overview);

  return (
    <div className="min-h-8 space-y-1" data-testid={`acquisition-runtime-${overview.taskId}`}>
      <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-xs text-default-500">
        {overview.currentNodeKind && (
          <Tooltip
            content={t("acquisition.runtime.updatedAt", {
              time: parseServerTime(overview.observedAt)?.toLocaleString() ?? overview.observedAt,
            })}
          >
            <span className="text-default-700">
              {t("acquisition.runtime.currentStep", {
                name: activityDisplayName(t, overview.currentNodeKind),
              })}
            </span>
          </Tooltip>
        )}
        {stepNumber != null && (
          <span>
            {t("acquisition.runtime.stepNumber", { current: stepNumber, total: stepCount })}
          </span>
        )}
        {overview.stage && overview.stage !== overview.currentNodeKind && (
          <span>
            {t(`downloadCenter.stage.${overview.stage}`, { defaultValue: overview.stage })}
          </span>
        )}
        {hasDownloads && (
          <span>
            {t("acquisition.runtime.downloadCounts", {
              complete: overview.completedDownloadCount,
              total: overview.downloadCount,
              active: overview.activeDownloadCount,
            })}
          </span>
        )}
        {validBytes(completedBytes) && (
          <span>
            {overview.bytesPartial
              ? t("acquisition.runtime.downloadedPartial", { size: humanFileSize(completedBytes) })
              : validBytes(totalBytes)
                ? t("acquisition.runtime.downloadedOfTotal", {
                    downloaded: humanFileSize(completedBytes),
                    total: humanFileSize(totalBytes),
                  })
                : t("acquisition.runtime.downloaded", { size: humanFileSize(completedBytes) })}
          </span>
        )}
        {overview.activeDownloadCount > 0 && validBytes(overview.speedBytesPerSecond) && (
          <span>
            {t("acquisition.runtime.downloadSpeed", {
              speed: humanFileSize(overview.speedBytesPerSecond),
            })}
          </span>
        )}
        {showSources && <RuntimeSources overview={overview} />}
      </div>
      {continuation && <p role="status" className="text-xs text-default-700">{continuation}</p>}
      {progress != null && (
        <div className="flex items-center gap-2 text-xs text-default-500">
          <Progress
            aria-label={t("acquisition.runtime.downloadProgress")}
            className="max-w-48"
            size="sm"
            value={progress}
          />
          <span>
            {t("acquisition.runtime.downloadProgressValue", { progress: Math.round(progress) })}
          </span>
        </div>
      )}
      {problemDetail ? <ProcessingProblemSummary message={problemDetail} summary={problem}
        reason={overview.problemReasonCode ?? overview.problemCode ?? undefined} /> : problem && (
        <p
          className="line-clamp-2 whitespace-pre-wrap break-words text-xs text-warning-600"
          title={problem}
        >
          {problem}
        </p>
      )}
    </div>
  );
}
