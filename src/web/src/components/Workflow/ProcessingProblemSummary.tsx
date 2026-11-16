import { useTranslation } from "react-i18next";
import { LuInfo } from "react-icons/lu";
import { Button, Tooltip } from "@/components/bakaui";
import { fileProcessingRecoveryReasons } from "./fileProcessingFailure";
import { isAdaptiveAssessmentDiagnostic } from "./adaptiveAssessmentDiagnostic";
import { localStorageProblemCode } from "@/components/CloudDownload/problemLabels";

const classifiedReasons = new Set([
  "PasswordUnknown", "StorageUnavailable", "ExtractionLayoutConflict", "WaitingShared",
  "RecursiveBudgetExceeded", "DepthLimit", "ArchiveCountLimit", "FileCountLimit",
  "ExpandedBytesLimit", "DiskSpaceLimit",
]);

/** Generic review codes do not explain a model's observation. Keep those explanations visible. */
export function hasSpecificProcessingProblem(reason?: string): boolean {
  return !!reason && (classifiedReasons.has(reason) || fileProcessingRecoveryReasons.has(reason) ||
    isAdaptiveAssessmentDiagnostic(reason) || !!localStorageProblemCode(reason));
}

/** Diagnostic text can come from a tool, executor or model; never treat it as markup. */
export default function ProcessingProblemSummary({ message, summary, reason }: {
  message: string; summary?: string; reason?: string;
}) {
  const { t } = useTranslation();
  const classified = !!summary && summary !== reason && hasSpecificProcessingProblem(reason);
  const distinct = !!summary && summary.trim() !== message.trim();
  if (classified) return (
    <div className="flex min-w-0 flex-wrap items-center gap-1 text-warning-700">
      <p className="whitespace-pre-wrap break-words text-sm">{summary}</p>
      {distinct && <Tooltip placement="bottom" content={<span className="block max-w-md whitespace-pre-wrap [overflow-wrap:anywhere]">{message}</span>}>
        <Button isIconOnly size="sm" variant="light" className="h-6 min-w-6 w-6 text-default-500"
          aria-label={t("workflow.processing.problem.details")}>
          <LuInfo aria-hidden size={14} />
        </Button>
      </Tooltip>}
    </div>
  );
  return (
    <div className="min-w-0 space-y-1">
      <p className="whitespace-pre-wrap break-words text-sm text-warning-700">{message}</p>
      {distinct && (
        <p className="whitespace-pre-wrap break-words text-xs text-default-500">{summary}</p>
      )}
    </div>
  );
}
