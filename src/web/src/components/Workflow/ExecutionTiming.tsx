import { useTranslation } from "react-i18next";
import { parseServerTime } from "@/core/serverTime";

/** Wall-clock duration; includes time spent waiting rather than claiming CPU time. */
export function executionDuration(startedAt?: string | null, completedAt?: string | null) {
  const start = parseServerTime(startedAt);
  const end = parseServerTime(completedAt);
  if (!start || !end || end < start) return undefined;
  const seconds = Math.floor((end.getTime() - start.getTime()) / 1000);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor(seconds / 60) % 60;
  return `${String(hours).padStart(2, "0")}:${String(minutes).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
}

/** One timestamp presentation for phase attempts, workflow nodes, and run history. */
export default function ExecutionTiming({ startedAt, completedAt }: {
  startedAt?: string | null;
  completedAt?: string | null;
}) {
  const { t, i18n } = useTranslation();
  const start = parseServerTime(startedAt);
  const end = parseServerTime(completedAt);
  if (!start) return null;
  const duration = executionDuration(startedAt, completedAt);
  const language = i18n?.resolvedLanguage || i18n?.language || "en";
  const locale = language === "cn" ? "zh-CN" : language;
  const dateTime = new Intl.DateTimeFormat(locale, {
    year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  });
  const sameDay = end && start.getFullYear() === end.getFullYear() &&
    start.getMonth() === end.getMonth() && start.getDate() === end.getDate();
  const endTime = sameDay ? new Intl.DateTimeFormat(locale, {
    hour: "2-digit", minute: "2-digit", second: "2-digit", hour12: false,
  }) : dateTime;
  return <span className="inline-flex min-w-0 flex-wrap items-baseline gap-x-1 text-xs text-default-500">
    <time dateTime={start.toISOString()} title={dateTime.format(start)}>{dateTime.format(start)}</time>
    {end && <>– <time dateTime={end.toISOString()} title={dateTime.format(end)}>{endTime.format(end)}</time></>}
    {duration && <span className="whitespace-nowrap">· {t("workflow.timing.duration", { duration })}</span>}
  </span>;
}
