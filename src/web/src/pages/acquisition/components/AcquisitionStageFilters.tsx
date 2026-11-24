import type { AcquisitionStageFilter } from "@/components/Acquire/taskStage";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { acquisitionStages } from "@/components/Acquire/taskStage";
import { acquisitionFilterCount, showAcquisitionFilter } from "../acquisitionFilters";

const stages: AcquisitionStageFilter[] = ["all", ...acquisitionStages];

export default function AcquisitionStageFilters({
  counts,
  value,
  onChange,
}: {
  counts?: Record<string, number> | null;
  value: AcquisitionStageFilter;
  onChange: (stage: AcquisitionStageFilter) => void;
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
      {stages.map((stage) => {
        const selected = value === stage;
        const count = acquisitionFilterCount(counts, stage);
        const canClearSelection = value !== "all" && (selected || stage === "all");
        if (!showAcquisitionFilter(count, selected || stage === "all")) return null;
        return (
          <Button
            key={stage}
            aria-label={`${t(`acquisition.stage.${stage}`)} ${count ?? "—"}`}
            aria-pressed={selected}
            data-acquisition-stage={stage}
            className="h-7 min-w-0 gap-1.5 px-2 text-xs"
            color={selected ? "primary" : "default"}
            size="sm"
            variant={selected ? "flat" : "light"}
            isDisabled={count == null && !canClearSelection}
            onPress={() => onChange(selected ? "all" : stage)}
          >
            <span>{t(`acquisition.stage.${stage}`)}</span>
            <span className="font-medium tabular-nums text-foreground">{count ?? "—"}</span>
          </Button>
        );
      })}
    </div>
  );
}
