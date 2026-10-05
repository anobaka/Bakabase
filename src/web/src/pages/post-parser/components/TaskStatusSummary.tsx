import type { TaskDisplayStatus, TaskStatusGroup } from "../taskStatus";

import { useTranslation } from "react-i18next";

import { summarizeTaskStatuses, taskStatusGroups } from "../taskStatus";

import Tooltip from "./PostParserTooltip";

const colors: Record<TaskStatusGroup, string> = {
  running: "bg-primary",
  queued: "bg-primary/50",
  ready: "bg-default-400",
  attention: "bg-warning",
  success: "bg-success",
  failed: "bg-danger",
  paused: "bg-default-500",
};

export default function TaskStatusSummary({
  statuses,
}: {
  statuses: readonly TaskDisplayStatus[];
}) {
  const { t } = useTranslation();
  const { total, counts, stages } = summarizeTaskStatuses(statuses);

  return (
    <div
      aria-label={t("postParser.summary.label")}
      className="flex w-full flex-wrap items-center gap-x-4 gap-y-1 border-t border-default-200/60 pt-2 text-xs text-default-500"
      role="group"
    >
      <span>{t("postParser.summary.total", { count: total })}</span>
      {taskStatusGroups.map((group) => {
        const item = (
          <span className="inline-flex items-center gap-1.5" data-status-group={group}>
            <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${colors[group]}`} />
            {t(`postParser.summary.${group}`)}
            <span className="font-medium tabular-nums text-foreground">{counts[group]}</span>
          </span>
        );

        return group === "running" && stages.size ? (
          <Tooltip
            key={group}
            content={
              <div className="space-y-1">
                {[...stages].map(([label, count]) => (
                  <div key={label} className="flex items-center justify-between gap-4">
                    <span>{t(label)}</span>
                    <span className="tabular-nums">{count}</span>
                  </div>
                ))}
              </div>
            }
          >
            <button className="cursor-help rounded-sm" type="button">
              {item}
            </button>
          </Tooltip>
        ) : (
          <span key={group}>{item}</span>
        );
      })}
    </div>
  );
}
