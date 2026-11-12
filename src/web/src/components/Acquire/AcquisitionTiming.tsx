import { useTranslation } from "react-i18next";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionTask as Task } from "@/sdk/Api";
import { AcquisitionStatus } from "@/sdk/constants";

type TimingTask = Pick<
  Task,
  "createdAt" | "workflowRunId" | "status" | "executionStartedAt" | "executionCompletedAt"
>;

/** Dates come from the task and its recorded execution, never from last-update timestamps. */
export default function AcquisitionTiming({ task }: { task: TimingTask }) {
  const { t, i18n } = useTranslation();
  const locale = i18n?.language === "cn" ? "zh-CN" : i18n?.language || undefined;
  const hasRun = task.workflowRunId != null;
  const terminal = [
    AcquisitionStatus.Completed,
    AcquisitionStatus.Failed,
    AcquisitionStatus.Cancelled,
  ].includes(task.status);
  const date = (value: string | null | undefined, fallback: string) => {
    const parsed = value ? new Date(value) : undefined;
    return parsed && Number.isFinite(parsed.getTime()) ? (
      <time dateTime={value!}>
        {parsed.toLocaleString(locale, {
          year: "numeric",
          month: "2-digit",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
          second: "2-digit",
          hour12: false,
        })}
      </time>
    ) : (
      <span>{fallback}</span>
    );
  };
  return (
    <dl
      className="flex flex-wrap gap-x-5 gap-y-1 text-xs text-default-500"
      aria-label={t("acquisition.timing.label")}
    >
      <div className="flex flex-wrap gap-x-2">
        <dt>{t("acquisition.timing.created")}</dt>
        <dd>{date(task.createdAt, t("acquisition.timing.unavailable"))}</dd>
      </div>
      <div className="flex flex-wrap gap-x-2">
        <dt>{t("acquisition.timing.started")}</dt>
        <dd>
          {date(
            task.executionStartedAt,
            t(hasRun ? "acquisition.timing.unavailable" : "acquisition.timing.notStarted"),
          )}
        </dd>
      </div>
      <div className="flex flex-wrap gap-x-2">
        <dt>{t("acquisition.timing.ended")}</dt>
        <dd>
          {date(
            task.executionCompletedAt,
            !hasRun
              ? "—"
              : t(
                  terminal || !task.executionStartedAt
                    ? "acquisition.timing.unavailable"
                    : "acquisition.timing.notEnded",
                ),
          )}
        </dd>
      </div>
    </dl>
  );
}
