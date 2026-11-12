import { useTranslation } from "react-i18next";
import { Chip } from "@/components/bakaui";
import ExecutionTiming from "@/components/Workflow/ExecutionTiming";
import { isAdaptiveAssessmentDiagnostic } from "@/components/Workflow/adaptiveAssessmentDiagnostic";
import type { PlanTaskDetail } from "./models";

export type Attempt = NonNullable<PlanTaskDetail["stageAttempts"]>[number];
const statusColors = {
  completed: "success",
  waiting: "warning",
  interrupted: "warning",
  failed: "danger",
  running: "primary",
  pending: "default",
  unknown: "default",
  cancelled: "default",
} as const;

export function AttemptStatus({ status }: { status: string }) {
  const { t } = useTranslation();
  const state = Object.prototype.hasOwnProperty.call(statusColors, status)
    ? (status as keyof typeof statusColors)
    : "unknown";
  return (
    <Chip size="sm" variant="flat" color={statusColors[state]}>
      {t(`acquisition.timeline.state.${state}`)}
    </Chip>
  );
}

export function AttemptStamp({ attempt }: { attempt: Attempt }) {
  const { t } = useTranslation();
  return (
    <>
      <span className="text-xs text-default-500">
        {t("workflow.metadata.attempt", { number: attempt.attempt })}
      </span>
      {attempt.legacy && (
        <span className="text-xs text-default-500">{t("acquisition.timeline.legacy")}</span>
      )}
      <ExecutionTiming startedAt={attempt.startedAt} completedAt={attempt.completedAt} />
    </>
  );
}

export function assessmentCode(attempt: Attempt) {
  return attempt.outputs.find(
    (block) =>
      block.key === "adaptive-assessment" &&
      block.kind === "warning" &&
      isAdaptiveAssessmentDiagnostic(block.code),
  )?.code;
}
