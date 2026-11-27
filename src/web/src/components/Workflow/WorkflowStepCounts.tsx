import { useTranslation } from "react-i18next";
import { Tooltip } from "@/components/bakaui";
import { getWorkflowActivityUI } from "./Activities";

/** An activity owns the meaning of its work items; these counts are not file totals. */
export default function WorkflowStepCounts({
  kind,
  inputCount,
  outputCount,
  failedCount = 0,
}: {
  kind: string;
  inputCount: number;
  outputCount: number;
  failedCount?: number;
}) {
  const { t } = useTranslation();
  const labels = getWorkflowActivityUI(kind)?.executionLabels;
  return (
    <Tooltip
      focusableTrigger
      delay={1000}
      placement="bottom"
      content={t(labels?.explanationKey ?? "workflow.counts.explanation")}
    >
      <span
        className="inline-flex flex-wrap gap-x-3 gap-y-1 text-xs tabular-nums text-default-500"
      >
        <span>
          {t(labels?.inputLabelKey ?? "workflow.counts.input")}: {inputCount}
        </span>
        <span>
          {t(labels?.outputLabelKey ?? "workflow.counts.output")}: {outputCount}
        </span>
        {failedCount > 0 && (
          <span className="text-danger">
            {t("workflow.counts.failed")}: {failedCount}
          </span>
        )}
      </span>
    </Tooltip>
  );
}
