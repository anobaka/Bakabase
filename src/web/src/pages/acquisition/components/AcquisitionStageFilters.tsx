import type { AcquisitionStageFilter } from "@/components/Acquire/taskStage";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { acquisitionStages } from "@/components/Acquire/taskStage";

const stages: AcquisitionStageFilter[] = ["all", ...acquisitionStages];

export default function AcquisitionStageFilters({
  counts,
  value,
  onChange,
  loading = false,
}: {
  counts?: Record<string, number> | null;
  value: AcquisitionStageFilter;
  onChange: (stage: AcquisitionStageFilter) => void;
  loading?: boolean;
}) {
  const { t } = useTranslation();
  const hintId = useId();

  return (
    <div
      role="group"
      aria-label={t("acquisition.tasks.filter")}
      aria-describedby={hintId}
      className="flex flex-wrap items-center gap-1 text-xs text-default-500"
    >
      <span id={hintId} className="sr-only">
        {t("acquisition.tasks.stageCountsHint")}
      </span>
      {stages.map((stage) => (
        <Button
          key={stage}
          aria-label={`${t(`acquisition.stage.${stage}`)} ${loading ? "—" : (counts?.[stage] ?? 0)}`}
          aria-pressed={value === stage}
          data-acquisition-stage={stage}
          className="h-7 min-w-0 gap-1.5 px-2 text-xs"
          color={value === stage ? "primary" : "default"}
          size="sm"
          variant={value === stage ? "flat" : "light"}
          isDisabled={loading}
          onPress={() => onChange(value === stage ? "all" : stage)}
        >
          <span>{t(`acquisition.stage.${stage}`)}</span>
          <span className="font-medium tabular-nums text-foreground">
            {loading ? "—" : (counts?.[stage] ?? 0)}
          </span>
        </Button>
      ))}
    </div>
  );
}
