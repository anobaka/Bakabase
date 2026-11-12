import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Button,
  Checkbox,
  Chip,
  Modal,
  Pagination,
  Spinner,
  Tooltip,
  Select,
} from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import StorageReserveOption from "@/components/Acquire/PlanTask/StorageReserveOption";
import StorageReserveCheckbox, { emergencyReserveBytes } from "@/components/Acquire/StorageReserveCheckbox";
import useStorageReserveDefaults from "@/components/Acquire/useStorageReserveDefaults";
import { runtimeProblemLabel } from "@/components/Acquire/PlanTask/runtimeOverview";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import {
  acquisitionRecoveryApi,
  type AcquisitionRecoveryPreview,
  type AcquisitionRecoveryResult,
  type AcquisitionRecoveryRow,
  type AcquisitionRecoveryScope,
} from "./acquisitionRecoveryApi";
import AcquisitionLocalFilesRecoveryPreview, {
  hasLocalFilesRecoveryEvidence,
} from "./AcquisitionLocalFilesRecoveryPreview";
import AcquisitionContinueScopeSelect, {
  type AcquisitionContinuationNavigation,
  type AcquisitionContinuationScope,
} from "./AcquisitionContinueScopeSelect";
import { startAcquisitionDiagnosis, type AcquisitionDiagnosis } from "./acquisitionDiagnosis";
import {
  acquisitionContinuationApi,
  type AcquisitionContinuation,
  type AcquisitionContinuationPreview,
} from "./acquisitionContinuationApi";
import { AcquisitionContinuationStatus } from "./AcquisitionContinuationJobsModal";

const pageSize = 20;
const knownRequestError = (error: unknown) =>
  error instanceof Error &&
  /^RECOVERY_(PREVIEW_LIMIT|PREVIEW_EXPIRED|SELECTION_INVALID|OUTCOME_UNKNOWN|SCOPE_MISMATCH|SCOPE_INVALID|DIAGNOSIS_STOP_UNCONFIRMED)$/.test(
    error.message,
  )
    ? error.message
    : undefined;

/** Unknown actions cannot become executable on an older client. Apply checks the state again. */
const isSafelyContinuable = (row: AcquisitionRecoveryRow, scope: AcquisitionRecoveryScope) =>
  row.canContinue === true &&
  (scope === "downloads"
    ? row.action === "downloads"
    : scope === "localFiles"
      ? row.action === "localFiles" && hasLocalFilesRecoveryEvidence(row.localFiles)
      : row.action === "resume" || row.action === "retry" || row.action === "start");

/** Diagnoses the captured IDs only. Opening this modal never resumes an execution. */
export default function AcquisitionRecoveryModal({
  taskIds,
  scopeLabel,
  initialScope = "workflow",
  onChanged,
  onDestroyed,
  continuationScope,
  onContinuationScopeChange,
  fixedScope = false,
  reserveRunId,
}: DestroyableProps & AcquisitionContinuationNavigation & {
  taskIds: number[];
  scopeLabel?: string;
  initialScope?: AcquisitionRecoveryScope;
  onChanged?: () => void;
  fixedScope?: boolean;
  reserveRunId?: number;
}) {
  const { t } = useTranslation();
  const captured = useRef([...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0))]);
  const [visible, setVisible] = useState(true);
  const [scope, setScope] = useState<AcquisitionRecoveryScope>(initialScope);
  const reserveDefaults = useStorageReserveDefaults(scope !== "localFiles" && !reserveRunId);
  const [reserveChoice, setReserveChoice] = useState<boolean>();
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<AcquisitionRecoveryPreview>();
  const [result, setResult] = useState<AcquisitionRecoveryResult>();
  const [selected, setSelected] = useState(new Set<number>());
  const [error, setError] = useState<"preview" | "submit">();
  const [errorCode, setErrorCode] = useState<string>();
  const [page, setPage] = useState(1);
  const [now, setNow] = useState(Date.now());
  const [transition, setTransition] = useState<"stopping" | "preparing" | "submitting">();
  const [deferred, setDeferred] = useState(false);
  const [continuationPreview, setContinuationPreview] = useState<AcquisitionContinuationPreview>();
  const [continuation, setContinuation] = useState<AcquisitionContinuation>();
  const [receiptToken, setReceiptToken] = useState<string>();
  const mounted = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const attempted = useRef(new Set<string>());
  const initialPreview = useRef<AcquisitionDiagnosis>();
  const continuationController = useRef<AbortController>();
  const changed = useRef(onChanged);
  changed.current = onChanged;
  const load = useCallback(
    async (refresh = false) => {
      if (busy.current) return;
      const request = ++sequence.current;
      setLoading(true);
      setPreview(undefined);
      setSelected(new Set());
      setError(undefined);
      setErrorCode(undefined);
      setResult(undefined);
      setPage(1);
      try {
        if (refresh || initialPreview.current?.scope !== scope) {
          await initialPreview.current?.stop();
          if (!mounted.current || request !== sequence.current) return;
          initialPreview.current = startAcquisitionDiagnosis(captured.current, scope);
        }
        const next = await initialPreview.current!.request;
        if (!mounted.current || request !== sequence.current) return;
        if (next.scope !== scope) throw new Error("RECOVERY_SCOPE_MISMATCH");
        setPreview(next);
        setSelected(
          new Set(
            next.rows
              .filter(
                (row) => captured.current.includes(row.taskId) && isSafelyContinuable(row, scope),
              )
              .map((row) => row.taskId),
          ),
        );
        setNow(Date.now());
      } catch (failure) {
        if (mounted.current && request === sequence.current) {
          setError("preview");
          setErrorCode(knownRequestError(failure));
        }
      } finally {
        if (mounted.current && request === sequence.current) setLoading(false);
      }
    },
    [scope],
  );
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      mounted.current = false;
      sequence.current++;
      clearInterval(timer);
      // StrictMode immediately remounts this effect. Only a real departure cancels it.
      queueMicrotask(() => {
        if (!mounted.current) {
          continuationController.current?.abort();
          void initialPreview.current?.stop().catch(() => {});
        }
      });
    };
  }, [load]);
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const consumed = !!preview && attempted.current.has(preview.previewToken);
  const rows = (preview?.rows ?? []).filter((row) => captured.current.includes(row.taskId));
  const eligible = rows.filter((row) => isSafelyContinuable(row, scope));
  const selectedIds = eligible.filter((row) => selected.has(row.taskId)).map((row) => row.taskId);
  const locked = loading || submitting || !!transition || !preview || expired || consumed;
  const changeScopeOrClose = async (next?: AcquisitionContinuationScope) => {
    if (busy.current || receiptToken || (next === scope && visible)) return;
    busy.current = true;
    sequence.current++;
    setTransition("stopping");
    try {
      await initialPreview.current?.stop();
      if (!mounted.current) return;
      if (next == null) setVisible(false);
      else if (onContinuationScopeChange) onContinuationScopeChange(next);
      else if (next !== "localQueue") {
        initialPreview.current = undefined;
        setPreview(undefined);
        setSelected(new Set());
        setError(undefined);
        setErrorCode(undefined);
        setReserveChoice(undefined);
        setLoading(true);
        setScope(next);
      }
    } catch {
      if (mounted.current) {
        setLoading(false);
        setError("preview");
        setErrorCode("RECOVERY_DIAGNOSIS_STOP_UNCONFIRMED");
      }
    } finally {
      busy.current = false;
      if (mounted.current) setTransition(undefined);
    }
  };
  const findReceipt = async (token: string) => {
    const job = await acquisitionContinuationApi.findReceipt(token);
    if (mounted.current) { setContinuation(job); setReceiptToken(undefined); }
    changed.current?.();
  };
  const skipDiagnosis = async () => {
    if (busy.current || !loading || captured.current.length > 1000) return;
    busy.current = true;
    sequence.current++;
    setTransition("stopping");
    setError(undefined);
    setErrorCode(undefined);
    try {
      await initialPreview.current?.stop();
      if (!mounted.current) return;
      setLoading(false);
      setDeferred(true);
      setTransition("preparing");
      const controller = new AbortController();
      continuationController.current = controller;
      const snapshot = await acquisitionContinuationApi.preview(captured.current, scope, controller.signal);
      if (!mounted.current) return;
      if (snapshot.scope !== scope) throw new Error("RECOVERY_SCOPE_MISMATCH");
      if (!Array.isArray(snapshot.rows) || snapshot.rows.length !== captured.current.length ||
        new Set(snapshot.rows.map((row) => row.taskId)).size !== captured.current.length ||
        snapshot.rows.some((row) => !captured.current.includes(row.taskId) || typeof row.canQueue !== "boolean") ||
        typeof snapshot.previewToken !== "string" || !snapshot.previewToken ||
        !parseServerTime(snapshot.expiresAt) || millisecondsUntil(snapshot.expiresAt) <= 0)
        throw new Error("RECOVERY_CONTINUATION_PREVIEW_INVALID");
      setContinuationPreview(snapshot);
      const ids = snapshot.rows.filter((row) => row.canQueue && captured.current.includes(row.taskId)).map((row) => row.taskId);
      if (!ids.length) return;
      setTransition("submitting");
      // The create call is never cancelled or retried: a lost reply is resolved by its durable receipt.
      try {
        const explicitReserve = snapshot.rows.some(row => ids.includes(row.taskId) && row.canOverrideStorage === true)
          ? reserveChoice : undefined;
        const job = explicitReserve === undefined
          ? await acquisitionContinuationApi.create(snapshot.previewToken, ids)
          : await acquisitionContinuationApi.create(snapshot.previewToken, ids, explicitReserve);
        if (mounted.current) setContinuation(job);
        changed.current?.();
      } catch {
        if (mounted.current) setReceiptToken(snapshot.previewToken);
        await findReceipt(snapshot.previewToken);
      }
    } catch (failure) {
      if (mounted.current) {
        setLoading(false);
        setError("preview");
        setErrorCode(knownRequestError(failure) ?? "RECOVERY_DIAGNOSIS_STOP_UNCONFIRMED");
      }
    } finally {
      busy.current = false;
      if (mounted.current) setTransition(undefined);
    }
  };
  const submit = async () => {
    if (!preview || busy.current || locked || !selectedIds.length) return;
    busy.current = true;
    attempted.current.add(preview.previewToken);
    setSubmitting(true);
    setError(undefined);
    setErrorCode(undefined);
    try {
      const explicitReserve = eligible.some(row => selected.has(row.taskId) && row.canOverrideStorage === true)
        ? reserveChoice : undefined;
      const next = explicitReserve === undefined
        ? await acquisitionRecoveryApi.apply(preview.previewToken, selectedIds)
        : await acquisitionRecoveryApi.apply(preview.previewToken, selectedIds, explicitReserve);
      if (mounted.current)
        setResult({
          ...next,
          continuedTaskIds: next.continuedTaskIds.filter((id) => selectedIds.includes(id)),
          skipped: next.skipped.filter((row) => selectedIds.includes(row.taskId)),
          downloads: next.downloads?.filter((row) => selectedIds.includes(row.taskId)),
          localFiles: next.localFiles?.filter((row) => selectedIds.includes(row.taskId)),
        });
      changed.current?.();
    } catch (failure) {
      if (mounted.current) {
        setError("submit");
        setErrorCode(knownRequestError(failure));
      }
      // The response may have been lost after acceptance. A new preview and explicit choice are required.
      changed.current?.();
    } finally {
      busy.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };
  const reason = (code?: string | null) =>
    t(`acquisition.recovery.reason.${code}`, {
      defaultValue:
        scope === "localFiles"
          ? t(`acquisition.plan.issue.${code}`, {
              defaultValue: t(`acquire.requirement.${code}`, {
                defaultValue: t("acquisition.recovery.reason.unknown"),
              }),
            })
          : t("acquisition.recovery.reason.unknown"),
    });
  const shown = rows.slice((page - 1) * pageSize, page * pageSize);
  const scopePrefix =
    scope === "workflow" ? "acquisition.recovery" : `acquisition.recovery.${scope}`;
  const showReserve = !result && !deferred && !reserveRunId && scope !== "localFiles" &&
    (!preview || eligible.some(row => row.canOverrideStorage === true));

  const confirmSelection = (
    <Button color="primary" isLoading={submitting} isDisabled={locked || !selectedIds.length}
      onPress={() => void submit()}>
      {t(`${scopePrefix}.confirm`, { count: selectedIds.length })}
    </Button>
  );

  return (
    <Modal
      visible={visible}
      title={t(continuationScope ? "acquisition.continue.title" : "acquisition.recovery.title")}
      size="5xl"
      scrollBehavior="inside"
      onClose={() => receiptToken ? setVisible(false) : void changeScopeOrClose()}
      onDestroyed={onDestroyed}
      isDismissable={!submitting && !transition}
      isKeyboardDismissDisabled={submitting || !!transition}
      hideCloseButton={submitting || !!transition}
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button variant="light" isDisabled={submitting || !!transition} onPress={() => receiptToken ? setVisible(false) : void changeScopeOrClose()}>
            {t("common.action.close")}
          </Button>
          {!result && !deferred && !loading && (
            reserveRunId && scope === "workflow" && captured.current.length === 1 &&
            rows.length === 1 && rows[0].workflowRunId === reserveRunId ? (
              <StorageReserveOption key={reserveRunId} taskId={captured.current[0]} workflowRunId={reserveRunId}
                inheritGlobalDefault
                actionLabel={t("acquisition.continue.current")} onChanged={onChanged}
                onBusyChange={(active) => { busy.current = active; setSubmitting(active); }}>
                {confirmSelection}
              </StorageReserveOption>
            ) : confirmSelection
          )}
        </div>
      }
    >
      {!result && !deferred && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
          {fixedScope ? <Button size="sm" variant="light" isDisabled={submitting || !!transition}
            onPress={() => void changeScopeOrClose("localQueue")}>{t("acquisition.continueActions.back")}</Button> : continuationScope ? <AcquisitionContinueScopeSelect value={continuationScope}
            disabled={submitting || !!transition} onChange={(next) => void changeScopeOrClose(next)} /> : <Select
            size="sm"
            className="max-w-xs"
            aria-label={t("acquisition.recovery.scope.label")}
            label={t("acquisition.recovery.scope.label")}
            labelPlacement="outside-left"
            disallowEmptySelection
            dataSource={[
              { value: "workflow", label: t("acquisition.recovery.scope.workflow") },
              { value: "downloads", label: t("acquisition.recovery.scope.downloads") },
              { value: "localFiles", label: t("acquisition.recovery.scope.localFiles") },
            ]}
            selectedKeys={[scope]}
            isDisabled={submitting || !!transition}
            onSelectionChange={(keys) => {
              const next = Array.from(keys)[0];
              if (
                busy.current ||
                (next !== "workflow" && next !== "downloads" && next !== "localFiles") ||
                next === scope
              )
                return;
              void changeScopeOrClose(next);
            }}
          />}
          <p className="text-xs text-default-500">
            {t(
              scope === "workflow"
                ? "acquisition.recovery.scope.workflowHint"
                : `${scopePrefix}.hint`,
            )}
          </p>
        </div>
      )}
      {showReserve && (
        <div className="min-w-0 max-w-full">
          {reserveDefaults.value && reserveDefaults.value.minimumBytes > emergencyReserveBytes && <StorageReserveCheckbox scope="operation"
            minimumBytes={reserveDefaults.value.minimumBytes}
            selected={reserveChoice ?? reserveDefaults.value.selected}
            disabled={submitting || !!transition || expired || consumed}
            onChange={setReserveChoice} />}
          {!reserveDefaults.value && !reserveDefaults.failed && <Spinner size="sm"
            aria-label={t("acquisition.plan.loading")} />}
          {reserveDefaults.failed && <Button size="sm" variant="light" color="warning"
            isDisabled={submitting || !!transition} onPress={reserveDefaults.reload}>
            {t("acquisition.reserve.reload")}
          </Button>}
        </div>
      )}
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
        <span className="min-w-0 break-words">
          {t("acquisition.recovery.fixedScope", { count: captured.current.length })}
          {scopeLabel ? ` · ${scopeLabel}` : ""}
        </span>
        <Tooltip
          content={t(
            scope === "localFiles"
              ? `${scopePrefix}.preserveHint`
              : "acquisition.recovery.preserveHint",
          )}
          delay={1000}
          placement="bottom"
        >
          <span className="cursor-help underline decoration-dotted underline-offset-2">
            {t("acquisition.recovery.preserveLabel")}
          </span>
        </Tooltip>
      </div>
      {transition ? <div role="status" className="flex min-h-48 flex-col items-center justify-center gap-3">
        <Spinner /><p className="text-sm">{t(`acquisition.continue.${transition}`)}</p>
        <p className="text-xs text-default-500">{t("acquisition.continue.requiredChecks")}</p>
      </div> : deferred ? <div className="space-y-3">
        {continuation ? <AcquisitionContinuationStatus initialJob={continuation} onChanged={onChanged} /> : receiptToken ? <>
          <p role="alert" className="text-sm text-warning-600">{t("acquisition.continue.unknownOutcome")}</p>
          <Button size="sm" onPress={() => {
            if (busy.current) return;
            busy.current = true; setTransition("preparing");
            void findReceipt(receiptToken).catch(() => {}).finally(() => {
              busy.current = false; if (mounted.current) setTransition(undefined);
            });
          }}>{t("acquisition.continue.checkReceipt")}</Button>
        </> : error ? <p role="alert" className="text-danger">{t("acquisition.continue.prepareFailed")}</p>
          : <p>{t("acquisition.continue.noEligible")}</p>}
        {!!continuationPreview?.rows.some((row) => !row.canQueue) && <div className="max-h-64 overflow-y-auto divide-y divide-default-200 text-sm">
          {continuationPreview.rows.filter((row) => !row.canQueue && captured.current.includes(row.taskId)).map((row) => <p key={row.taskId} className="py-2 break-words">#{row.taskId} {row.title} · {reason(row.reasonCode)}</p>)}
        </div>}
      </div> : loading ? (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3">
          <Spinner aria-label={t("acquisition.recovery.loading")} />
          <p className="text-xs text-default-500">
            {t(
              scope === "localFiles"
                ? `${scopePrefix}.previewOnly`
                : "acquisition.recovery.previewOnly",
            )}
          </p>
          <p className="text-xs text-default-500">{t("acquisition.continue.requiredChecks")}</p>
          <Button variant="flat" color="primary" isDisabled={captured.current.length > 1000} onPress={() => void skipDiagnosis()}>
            {t("acquisition.continue.skipDiagnosis", { count: captured.current.length })}
          </Button>
        </div>
      ) : (
        <>
          {error && (
            <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
              <span>{t(`acquisition.recovery.error.${errorCode ?? error}`)}</span>
              <Button size="sm" variant="flat" onPress={() => void load(true)}>
                {t("acquisition.recovery.recheck")}
              </Button>
            </div>
          )}
          {result ? (
            <>
              <p role="status" className="text-sm">
                {t(`${scopePrefix}.result`, {
                  count: result.continuedTaskIds.length,
                  skipped: result.skipped.length,
                  started: result.localFiles?.filter((row) => row.status === "started").length ?? 0,
                  prepared:
                    result.localFiles?.filter((row) => row.status === "prepared").length ?? 0,
                })}
              </p>
              <p className="text-xs text-default-500">{t(`${scopePrefix}.resultHint`)}</p>
              {scope === "localFiles" && !!result.localFiles?.length && (
                <div className="max-h-64 divide-y divide-default-200 overflow-y-auto text-xs">
                  {result.localFiles.map((row) => (
                    <div
                      key={row.taskId}
                      className="flex flex-wrap items-center gap-x-3 gap-y-1 py-2"
                    >
                      <span className="font-medium">#{row.taskId}</span>
                      <span>
                        {t(
                          `acquisition.recovery.localFiles.status.${
                            row.status === "started" || row.status === "prepared"
                              ? row.status
                              : "unknown"
                          }`,
                        )}
                      </span>
                      <span className="text-default-500">
                        {t("acquisition.plan.revision", { revision: row.planRevision })}
                      </span>
                      <span className="text-default-500">
                        {t("acquisition.recovery.run", { id: row.previousWorkflowRunId })}
                      </span>
                      {row.workflowRunId != null && (
                        <span>
                          {t("acquisition.recovery.localFiles.newRun", { id: row.workflowRunId })}
                        </span>
                      )}
                      {row.reasonCode && <span>{reason(row.reasonCode)}</span>}
                    </div>
                  ))}
                </div>
              )}
              {scope === "localFiles" &&
                result.localFiles?.some((row) => row.status === "prepared") && (
                  <Button size="sm" variant="flat" onPress={() => void load(true)}>
                    {t("acquisition.recovery.recheck")}
                  </Button>
                )}
              {scope === "downloads" && !!result.downloads?.length && (
                <div className="max-h-64 divide-y divide-default-200 overflow-y-auto rounded-lg border border-default-200">
                  {result.downloads.map((row) => (
                    <div key={row.taskId} className="space-y-1 p-2 text-xs">
                      <span className="font-medium">#{row.taskId}</span>
                      <div className="flex flex-wrap gap-2">
                        <Chip size="sm" variant="flat" color="success">
                          {t("acquisition.recovery.downloads.started", {
                            count: row.startedDownloadTaskIds.length,
                          })}
                        </Chip>
                        {!!row.indeterminateDownloadTaskIds.length && (
                          <Chip size="sm" variant="flat" color="warning">
                            {t("acquisition.recovery.downloads.indeterminate", {
                              count: row.indeterminateDownloadTaskIds.length,
                            })}
                          </Chip>
                        )}
                      </div>
                      {!!row.startedDownloadTaskIds.length && (
                        <p className="break-words text-default-500">
                          {t("acquisition.recovery.downloads.startedIds", {
                            ids: row.startedDownloadTaskIds.map((id) => `#${id}`).join(", "),
                          })}
                        </p>
                      )}
                      {!!row.indeterminateDownloadTaskIds.length && (
                        <p className="break-words text-warning-600">
                          {t("acquisition.recovery.downloads.indeterminateIds", {
                            ids: row.indeterminateDownloadTaskIds.map((id) => `#${id}`).join(", "),
                          })}
                        </p>
                      )}
                      {row.reasonCode && <p>{reason(row.reasonCode)}</p>}
                    </div>
                  ))}
                </div>
              )}
              {result.skipped.length > 0 && (
                <div className="max-h-64 overflow-y-auto divide-y divide-default-200 rounded-lg border border-default-200">
                  {result.skipped.map((row) => (
                    <div key={row.taskId} className="p-2 text-sm">
                      <span className="mr-2 font-medium">#{row.taskId}</span>
                      {reason(row.reasonCode)}
                    </div>
                  ))}
                </div>
              )}
            </>
          ) : preview ? (
            <>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap gap-1 text-xs" aria-live="polite">
                  <Chip size="sm" variant="flat">
                    {t("acquisition.recovery.total", { count: rows.length })}
                  </Chip>
                  <Chip size="sm" variant="flat" color="success">
                    {t("acquisition.recovery.eligible", { count: eligible.length })}
                  </Chip>
                  <Chip size="sm" variant="flat">
                    {t("acquisition.recovery.manual", { count: rows.length - eligible.length })}
                  </Chip>
                </div>
                <div className="flex flex-wrap gap-1">
                  {!expired && !error && (
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={submitting}
                      onPress={() => void load(true)}
                    >
                      {t("acquisition.recovery.recheck")}
                    </Button>
                  )}
                  <Button
                    size="sm"
                    variant="light"
                    isDisabled={locked || !eligible.length}
                    onPress={() => setSelected(new Set(eligible.map((row) => row.taskId)))}
                  >
                    {t("acquisition.recovery.selectEligible")}
                  </Button>
                  <Button
                    size="sm"
                    variant="light"
                    isDisabled={locked || !selectedIds.length}
                    onPress={() => setSelected(new Set())}
                  >
                    {t("acquisition.recovery.selectNone")}
                  </Button>
                </div>
              </div>
              {expired && !error && (
                <div
                  role="alert"
                  className="flex flex-wrap items-center gap-2 text-sm text-warning-600"
                >
                  {t("acquisition.recovery.expired")}
                  <Button size="sm" variant="flat" onPress={() => void load(true)}>
                    {t("acquisition.recovery.recheck")}
                  </Button>
                </div>
              )}
              <p className="text-xs text-default-500">
                {t(
                  scope === "localFiles"
                    ? `${scopePrefix}.notGuaranteed`
                    : "acquisition.recovery.notGuaranteed",
                )}
              </p>
              <div className="divide-y divide-default-200 rounded-lg border border-default-200">
                {shown.map((row) => (
                  <article key={row.taskId} className="flex items-start gap-2 p-2.5">
                    <Checkbox
                      className="shrink-0"
                      aria-label={t("acquisition.recovery.selectTask", { id: row.taskId })}
                      isSelected={isSafelyContinuable(row, scope) && selected.has(row.taskId)}
                      isDisabled={locked || !isSafelyContinuable(row, scope)}
                      onValueChange={(checked) =>
                        setSelected((previous) => {
                          const next = new Set(previous);
                          if (checked && isSafelyContinuable(row, scope)) next.add(row.taskId);
                          else next.delete(row.taskId);
                          return next;
                        })
                      }
                    />
                    <div className="min-w-0 flex-1 space-y-1">
                      <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
                        <span className="min-w-0 flex-1 break-words text-sm font-medium">
                          <span className="mr-2 text-default-500">#{row.taskId}</span>
                          {row.title}
                        </span>
                        <span
                          className={`text-xs ${isSafelyContinuable(row, scope) ? "text-success-600" : "text-default-500"}`}
                        >
                          {t(
                            isSafelyContinuable(row, scope)
                              ? row.action === "start"
                                ? "acquisition.recovery.rowStartEligible"
                                : "acquisition.recovery.rowEligible"
                              : "acquisition.recovery.rowManual",
                          )}
                        </span>
                      </div>
                      {(row.currentProblemCode ||
                        (row.action !== "start" && row.action !== "localFiles")) && (
                        <p className="break-words text-xs">
                          {t("acquisition.recovery.currentProblem", {
                            problem:
                              runtimeProblemLabel(t, row.currentProblemCode) ||
                              t("acquisition.recovery.problemUnknown"),
                          })}
                        </p>
                      )}
                      <p className="break-words text-xs text-default-600">
                        {reason(row.reasonCode)}
                      </p>
                      {scope === "localFiles" &&
                        Array.isArray(row.localFiles?.parts) &&
                        !!row.localFiles.parts.length && (
                          <AcquisitionLocalFilesRecoveryPreview preview={row.localFiles} />
                        )}
                      {scope === "localFiles" &&
                        row.canContinue &&
                        row.action === "localFiles" &&
                        !hasLocalFilesRecoveryEvidence(row.localFiles) && (
                          <p className="text-xs text-warning-600">
                            {t("acquisition.recovery.localFiles.incompleteEvidence")}
                          </p>
                        )}
                      {row.manualAction && (
                        <p className="break-words text-xs text-default-500">
                          {t(`acquisition.recovery.manualAction.${row.manualAction}`, {
                            defaultValue: t("acquisition.recovery.manualAction.unknown"),
                          })}
                        </p>
                      )}
                      <div className="flex flex-wrap gap-x-3 gap-y-1 text-xs text-default-400">
                        {row.workflowRunId != null && (
                          <span>{t("acquisition.recovery.run", { id: row.workflowRunId })}</span>
                        )}
                        {row.currentStepKind && (
                          <span>{activityDisplayName(t, row.currentStepKind)}</span>
                        )}
                        {row.evidenceAt && (
                          <span>
                            {t("acquisition.recovery.evidenceAt", {
                              time:
                                parseServerTime(row.evidenceAt)?.toLocaleString() ?? row.evidenceAt,
                            })}
                          </span>
                        )}
                      </div>
                    </div>
                  </article>
                ))}
              </div>
              {rows.length > pageSize && (
                <Pagination
                  aria-label={t("acquisition.recovery.pagination")}
                  page={page}
                  total={Math.ceil(rows.length / pageSize)}
                  onChange={setPage}
                  isDisabled={submitting}
                />
              )}
            </>
          ) : null}
        </>
      )}
    </Modal>
  );
}
