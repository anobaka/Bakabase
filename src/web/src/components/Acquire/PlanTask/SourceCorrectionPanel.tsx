import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { v4 as uuidv4 } from "uuid";
import { Button, Input, Spinner } from "@/components/bakaui";
import AcquisitionSourceDetails from "../AcquisitionSourceDetails";
import { planTaskApi } from "./api";
import type { SourceCorrections } from "./useSourceCorrections";

const knownReasons = new Set([
  "TASK_NOT_FOUND",
  "TASK_DELETED",
  "TASK_ACTIVE",
  "TASK_CANCELLED",
  "ALREADY_COMPLETED",
  "NO_RUN",
  "PENDING_ANSWER",
  "CHECKPOINT_UNSUPPORTED",
  "SOURCE_UNSUPPORTED",
  "ALREADY_ACQUIRED",
  "HISTORY_LIMIT",
  "OPERATION_UNCERTAIN",
  "TASK_CHANGED",
  "INVALID_INPUT",
  "CODE_UNCHANGED",
]);
const knownReason = (code?: string | null) => {
  const suffix = code?.replace(/^SOURCE_CORRECTION_/, "");
  return suffix && knownReasons.has(suffix) ? suffix : "unknown";
};

type CorrectionRow = SourceCorrections["sources"][number];

function CorrectionInput({
  row,
  disabled,
  submitting,
  onSubmit,
}: {
  row: CorrectionRow;
  disabled: boolean;
  submitting: boolean;
  onSubmit: (accessCode: string | null) => void;
}) {
  const { t } = useTranslation();
  const [value, setValue] = useState(row.currentSource?.accessCode ?? row.accessCode ?? "");
  const current = row.currentSource?.accessCode ?? row.accessCode ?? "";
  return (
    <div className="flex flex-wrap items-start gap-2">
      <Input
        className="min-w-48 flex-1"
        size="sm"
        label={t("acquisition.sourceCorrection.accessCode")}
        description={t("acquisition.sourceCorrection.accessCodeHint")}
        value={value}
        onValueChange={setValue}
        isDisabled={disabled || submitting}
        maxLength={512}
        autoComplete="off"
      />
      <Button
        size="sm"
        color="primary"
        isLoading={submitting}
        isDisabled={disabled || value === current}
        onPress={() => onSubmit(value.length ? value : null)}
      >
        {t("acquisition.sourceCorrection.saveAndContinue")}
      </Button>
    </div>
  );
}

/** Changes one explicitly selected source, using the server's frozen checkpoint identity. */
export default function SourceCorrectionPanel({
  taskId,
  preview,
  loading,
  failed,
  disabled,
  readOnly,
  onRefresh,
  onBusyChange,
  onApplied,
}: {
  taskId: number;
  preview?: SourceCorrections;
  loading: boolean;
  failed: boolean;
  disabled: boolean;
  readOnly: boolean;
  onRefresh: () => Promise<void>;
  onBusyChange: (value: boolean) => void;
  onApplied: () => void;
}) {
  const { t } = useTranslation();
  const [submitting, setSubmitting] = useState<string>();
  const [outcome, setOutcome] = useState<"accepted" | "unknown" | "rejected">();
  const [errorCode, setErrorCode] = useState<string>();
  const busy = useRef(false);
  const attempted = useRef(new Set<string>());
  const checkpoint = preview?.checkpointVersion ?? "";
  const consumed = !!checkpoint && attempted.current.has(checkpoint);
  const rows = preview?.sources ?? [];
  const submit = async (row: CorrectionRow, accessCode: string | null) => {
    if (
      readOnly ||
      disabled ||
      loading ||
      busy.current ||
      !preview?.canCorrect ||
      !row.canCorrect ||
      !preview.expectedRunId ||
      !checkpoint ||
      consumed
    )
      return;
    busy.current = true;
    attempted.current.add(checkpoint);
    setSubmitting(row.targetId + ":" + row.sourceId);
    setOutcome(undefined);
    setErrorCode(undefined);
    onBusyChange(true);
    try {
      const result = await planTaskApi.correctAccessCode(taskId, {
        expectedRevision: preview.expectedRevision,
        expectedRunId: preview.expectedRunId,
        checkpointVersion: checkpoint,
        correctionId: uuidv4().replace(/-/g, ""),
        targetId: row.targetId,
        sourceId: row.sourceId,
        expectedSourceIdentity: row.expectedSourceIdentity,
        newAccessCode: accessCode ?? undefined,
      });
      if (!result.accepted) throw new Error("SOURCE_CORRECTION_TASK_CHANGED");
      setOutcome("accepted");
    } catch (error) {
      const reason = knownReason(error instanceof Error ? error.message : undefined);
      setOutcome(reason === "unknown" ? "unknown" : "rejected");
      setErrorCode(reason);
    } finally {
      busy.current = false;
      setSubmitting(undefined);
      onBusyChange(false);
      onApplied();
      await onRefresh();
    }
  };
  // Non-actionable source details are available in the read-only task diagnostics.
  const shouldShow =
    failed || outcome || (preview?.canCorrect && rows.some((row) => row.canCorrect));
  if (!shouldShow) return null;
  return (
    <section
      aria-label={t("acquisition.sourceCorrection.title")}
      className="space-y-3 rounded-lg border border-primary-200 p-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("acquisition.sourceCorrection.title")}</h3>
        <Button
          size="sm"
          variant="light"
          isDisabled={!!submitting || disabled}
          isLoading={loading}
          onPress={() => void onRefresh()}
        >
          {t("acquisition.sourceCorrection.refresh")}
        </Button>
      </div>
      <p className="text-xs text-default-500">{t("acquisition.sourceCorrection.preserveHint")}</p>
      {outcome && (
        <p
          role={outcome === "accepted" ? "status" : "alert"}
          className={`text-sm ${outcome === "accepted" ? "text-success-600" : "text-warning-600"}`}
        >
          {t(
            outcome === "rejected"
              ? `acquisition.sourceCorrection.reason.${errorCode}`
              : `acquisition.sourceCorrection.${outcome}`,
          )}
        </p>
      )}
      {failed && (
        <p role="alert" className="text-sm text-danger">
          {t("acquisition.sourceCorrection.loadFailed")}
        </p>
      )}
      {loading && !preview && (
        <Spinner size="sm" aria-label={t("acquisition.sourceCorrection.loading")} />
      )}
      {rows.map((row) => (
        <div
          key={row.targetId + ":" + row.sourceId}
          className="space-y-2 rounded-lg bg-default-50 p-2"
        >
          {row.currentSource && <AcquisitionSourceDetails source={row.currentSource} />}
          {preview?.canCorrect && row.canCorrect && !readOnly ? (
            <CorrectionInput
              key={JSON.stringify([
                row.targetId,
                row.sourceId,
                row.expectedSourceIdentity,
                checkpoint,
              ])}
              row={row}
              disabled={disabled || loading || consumed}
              submitting={submitting === row.targetId + ":" + row.sourceId}
              onSubmit={(value) => void submit(row, value)}
            />
          ) : (
            <p className="text-xs text-default-500">
              {t(
                `acquisition.sourceCorrection.reason.${knownReason(row.reasonCode || preview?.reasonCode)}`,
              )}
            </p>
          )}
        </div>
      ))}
    </section>
  );
}
