import type { TaskStatusGroup } from "../taskStatus";

import { useTranslation } from "react-i18next";

import { taskStatusGroups } from "../taskStatus";

import Tooltip from "./PostParserTooltip";
import { Button } from "@/components/bakaui";

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
  summary,
  selected,
  onSelect,
}: {
  summary?: {
    total: number;
    counts: Record<TaskStatusGroup, number>;
    stages: Record<string, number>;
  };
  selected: TaskStatusGroup | "all";
  onSelect: (group: TaskStatusGroup | "all") => void;
}) {
  const { t } = useTranslation();
  const stages = Object.entries(summary?.stages ?? {});

  return (
    <div
      aria-label={t("postParser.summary.label")}
      className="flex w-full flex-wrap items-center gap-1 text-xs text-default-500"
      role="group"
    >
      <Button
        aria-pressed={selected === "all"}
        isDisabled={!summary}
        className="h-7 min-w-0 px-2 text-xs"
        color={selected === "all" ? "primary" : "default"}
        size="sm"
        variant={selected === "all" ? "flat" : "light"}
        data-status-group="all"
        onPress={() => onSelect("all")}
      >
        {summary
          ? t("postParser.summary.total", { count: summary.total })
          : t("postParser.loading.counts")}
      </Button>
      {taskStatusGroups.map((group) => {
        const item = (
          <Button
            aria-pressed={selected === group}
            isDisabled={!summary}
            className="h-7 min-w-0 gap-1.5 px-2 text-xs"
            color={selected === group ? "primary" : "default"}
            data-status-group={group}
            size="sm"
            variant={selected === group ? "flat" : "light"}
            onPress={() => onSelect(group)}
          >
            <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${colors[group]}`} />
            {t(`postParser.summary.${group}`)}
            <span className="font-medium tabular-nums text-foreground">
              {summary?.counts[group] ?? "—"}
            </span>
          </Button>
        );

        return group === "running" && stages.length ? (
          <Tooltip
            key={group}
            content={
              <div className="space-y-1">
                {stages.map(([stage, count]) => (
                  <div key={stage} className="flex items-center justify-between gap-4">
                    <span>
                      {t(
                        stage === "running"
                          ? "postParser.label.processing"
                          : `postParser.stage.${stage}`,
                      )}
                    </span>
                    <span className="tabular-nums">{count}</span>
                  </div>
                ))}
              </div>
            }
          >
            {item}
          </Tooltip>
        ) : (
          <span key={group}>{item}</span>
        );
      })}
    </div>
  );
}
