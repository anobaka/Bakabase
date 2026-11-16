import { type ReactNode, useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import StorageReserveCheckbox, { emergencyReserveBytes } from "../StorageReserveCheckbox";
import TaskReserveCapacity from "./TaskReserveCapacity";
import { millisecondsUntil } from "@/core/serverTime";
import { runtimeProblemLabel } from "./runtimeOverview";
import { ResumeSubmissionContext } from "@/components/Workflow/ResumeSubmissionContext";
import ResumeActions, { ResumeActionsGuard } from "@/components/Workflow/ResumeActions";
import {
  storageOverrideApi,
  type StorageOverridePreview,
  type StorageOverrideResult,
} from "./storageOverrideApi";

const knownErrorCode = (error: unknown) =>
  error instanceof Error &&
  /^(STORAGE_OVERRIDE_[A-Z_]+|ACQUISITION_TASK_CHANGED)$/.test(error.message)
    ? error.message
    : undefined;

/** A preview grants nothing. Confirmation remains bound to this task and its captured run. */
export default function StorageReserveOption({
  taskId,
  workflowRunId,
  onChanged,
  onBusyChange,
  actionLabel,
  destination,
  children,
  scope,
  inheritGlobalDefault = false,
}: {
  taskId: number;
  workflowRunId: number;
  onChanged?: () => void;
  onBusyChange?: (busy: boolean) => void;
  actionLabel: string;
  destination?: string;
  children: ReactNode;
  scope?: "download";
  /** Recovery starts a new attempt and clears an earlier run-only choice when omitted. */
  inheritGlobalDefault?: boolean;
}) {
  const { t } = useTranslation();
  const reasonLabel = (code: string) => code.startsWith("STORAGE_OVERRIDE_") || code === "ACQUISITION_TASK_CHANGED"
    ? t(`acquisition.storageOverride.reason.${code}`, { defaultValue: t("acquisition.recovery.reason.unknown") })
    : runtimeProblemLabel(t, code);
  const captured = useRef({ taskId, workflowRunId, scope });
  const [preview, setPreview] = useState<StorageOverridePreview>();
  const [result, setResult] = useState<StorageOverrideResult>();
  const [choice, setChoice] = useState<boolean>();
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<"preview" | "submit">();
  const [errorCode, setErrorCode] = useState<string>();
  const [now, setNow] = useState(Date.now());
  const mounted = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const attempted = useRef(new Set<string>());
  const changed = useRef(onChanged);
  changed.current = onChanged;
  const busyChanged = useRef(onBusyChange);
  busyChanged.current = onBusyChange;
  const load = useCallback(async () => {
    if (busy.current) return;
    const request = ++sequence.current;
    setLoading(true);
    setPreview(undefined);
    setChoice(undefined);
    setError(undefined);
    setErrorCode(undefined);
    try {
      const next = captured.current.scope === "download"
        ? await storageOverrideApi.preview(captured.current.taskId, "download")
        : await storageOverrideApi.preview(captured.current.taskId);
      if (!mounted.current || request !== sequence.current) return;
      if (
        next.taskId !== captured.current.taskId ||
        next.workflowRunId !== captured.current.workflowRunId ||
        !next.token ||
        next.overrideMinimumFreeBytes !== emergencyReserveBytes ||
        (captured.current.scope === "download" && (next.scope !== "download" ||
          typeof next.useEmergencySpaceReserve !== "boolean"))
      )
        throw new Error("STORAGE_OVERRIDE_PREVIEW_MISMATCH");
      setPreview(next);
      setNow(Date.now());
    } catch (failure) {
      if (mounted.current && request === sequence.current) {
        setError("preview");
        setErrorCode(knownErrorCode(failure));
      }
    } finally {
      if (mounted.current && request === sequence.current) setLoading(false);
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      mounted.current = false;
      sequence.current++;
      clearInterval(timer);
    };
  }, [load]);
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const consumed = !!preview && attempted.current.has(preview.token);
  const locked = loading || submitting || !preview || expired || consumed || !!result;
  const defaultLowered = scope === "download" || inheritGlobalDefault ? preview?.useEmergencySpaceReserve === true : preview?.effectiveReserveMode
    ? preview.effectiveReserveMode === "emergency" : preview?.useEmergencySpaceReserve === true;
  const lowered = choice ?? defaultLowered;
  const override = lowered !== defaultLowered;
  const submit = async (signalJson?: string): Promise<boolean> => {
    if (
      locked ||
      (scope !== "download" && !override) ||
      (scope === "download" && signalJson == null) ||
      !preview ||
      busy.current ||
      millisecondsUntil(preview.expiresAt) <= 0
    )
      return false;
    busy.current = true;
    busyChanged.current?.(true);
    attempted.current.add(preview.token);
    setSubmitting(true);
    setError(undefined);
    setErrorCode(undefined);
    try {
      const next = scope === "download"
        ? await storageOverrideApi.resumeDownload(captured.current.taskId, preview.token, signalJson!, lowered)
        : await storageOverrideApi.apply(captured.current.taskId, preview.token, lowered ? "emergency" : "configured");
      if (
        next.taskId !== captured.current.taskId ||
        next.workflowRunId !== captured.current.workflowRunId ||
        !["applied", "revoked"].includes(next.status)
      )
        throw new Error("STORAGE_OVERRIDE_RESULT_MISMATCH");
      if (mounted.current) setResult(next);
      return next.continuationAccepted;
    } catch (failure) {
      if (mounted.current) {
        setError("submit");
        setErrorCode(knownErrorCode(failure));
      }
      return false;
    } finally {
      busy.current = false;
      busyChanged.current?.(false);
      if (mounted.current) setSubmitting(false);
      changed.current?.();
    }
  };
  return (
    <div className="min-w-0 space-y-3">
      {preview && !result && (
        <>
          {(scope !== "download" || preview.minimumFreeBytes > emergencyReserveBytes) && (
            <StorageReserveCheckbox scope="operation" minimumBytes={preview.minimumFreeBytes} selected={lowered}
              disabled={locked} onChange={setChoice} />
          )}
          <TaskReserveCapacity key={preview.token} taskId={preview.taskId} workflowRunId={preview.workflowRunId}
            minimumBytes={lowered ? Math.min(preview.minimumFreeBytes, preview.overrideMinimumFreeBytes) : preview.minimumFreeBytes} />
        </>
      )}
      {override && destination && (
        <p className="select-text break-all text-sm">{t("cloudDownload.publication.directory")} · {destination}</p>
      )}
      {loading && <Spinner size="sm" aria-label={t("acquisition.plan.loading")} />}
      {submitting && (
        <div role="status" className="flex items-center gap-2 text-sm text-default-500">
          <Spinner size="sm" aria-label={t("acquisition.plan.loading")} />
          {t("acquisition.storageOverride.verifying")}
        </div>
      )}
      {expired && !result && (
        <p role="alert" className="text-sm text-warning-600">
          {t("acquisition.storageOverride.expired")}
        </p>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {t(`acquisition.storageOverride.error.${error}`)}
          {errorCode && (
            <>
              {" "}
              {reasonLabel(errorCode)}
            </>
          )}
        </p>
      )}
      {!result && (expired || error) && (
        <Button
          size="sm"
          variant="flat"
          isDisabled={loading || submitting}
          onPress={() => void load()}
        >
          {t("acquisition.storageOverride.refresh")}
        </Button>
      )}
      {!result && (
        <>
          {scope === "download" ? (
            <fieldset disabled={locked} className="min-w-0">
              <ResumeSubmissionContext.Provider value={{ runId: captured.current.workflowRunId, downloadTargets: preview?.downloadTargets, submit }}>
                <ResumeActionsGuard disabled={locked}>{children}</ResumeActionsGuard>
              </ResumeSubmissionContext.Provider>
            </fieldset>
          ) : <fieldset hidden={override} disabled={override} className="min-w-0">
            <ResumeActionsGuard disabled={override} hidden={override}>{children}</ResumeActionsGuard>
          </fieldset>}
          {override && scope !== "download" && (
            <ResumeActions busy={submitting}>
              <Button color="primary" isLoading={submitting} isDisabled={locked} onPress={() => void submit()}>
                {actionLabel}
              </Button>
            </ResumeActions>
          )}
        </>
      )}
      {result && (
        <div role="status" className="space-y-2 rounded-lg bg-default-100 p-3 text-sm">
          <p>
            {t(
              result.status === "revoked"
                ? "acquisition.storageOverride.result.revoked"
                : result.continuationAccepted
                  ? "acquisition.storageOverride.result.accepted"
                  : "acquisition.storageOverride.result.uncertain",
            )}
          </p>
          {result.reasonCode && (
            <p>
              {reasonLabel(result.reasonCode)}
            </p>
          )}
          <p>{t("acquisition.storageOverride.result.hint")}</p>
        </div>
      )}
    </div>
  );
}
