import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { millisecondsUntil, parseServerTime } from "@/core/serverTime";
import {
  Button,
  Checkbox,
  Chip,
  Modal,
  Pagination,
  Progress,
  Spinner,
  Tab,
  Tabs,
  Tooltip,
} from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import AcquisitionSourceDetails from "@/components/Acquire/AcquisitionSourceDetails";
import SourceHealthBadge from "./SourceHealthBadge";
import { sourceAvailabilityApi } from "./api";
import {
  eligibleSource,
  type SourceAvailabilityAction,
  type SourceAvailabilityPreview,
  type SourceAvailabilityResult,
} from "./models";

const pageSize = 20;
/** Captures IDs on opening, then confirms the server's fixed evidence snapshot. */
export default function SourceAvailabilityReviewModal({
  taskIds,
  scopeLabel,
  onChanged,
  onChecked,
  onDestroyed,
}: DestroyableProps & {
  taskIds: number[];
  scopeLabel?: string;
  onChanged?: () => void;
  onChecked?: () => void;
}) {
  const { t } = useTranslation();
  const capturedIds = useRef([
    ...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0)),
  ]);
  const [visible, setVisible] = useState(true);
  const [action, setAction] = useState<SourceAvailabilityAction>("disable");
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<SourceAvailabilityPreview>();
  const [result, setResult] = useState<SourceAvailabilityResult>();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string>();
  const [confirmation, setConfirmation] = useState(false);
  const [page, setPage] = useState(1);
  const [statusFilter, setStatusFilter] = useState("all");
  const [now, setNow] = useState(Date.now());
  const initialPreview = useRef<Promise<SourceAvailabilityPreview>>();
  const mounted = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const reading = useRef(false);
  const attempted = useRef(new Set<string>());
  const actionRef = useRef(action);
  actionRef.current = action;
  const onCheckedRef = useRef(onChecked);
  onCheckedRef.current = onChecked;
  const onChangedRef = useRef(onChanged);
  onChangedRef.current = onChanged;
  const load = useCallback(async (force = false) => {
    if (busy.current || (force && reading.current)) return;
    reading.current = true;
    const request = ++sequence.current;
    setLoading(true);
    setError(undefined);
    setPreview(undefined);
    setSelected(new Set());
    setConfirmation(false);
    setResult(undefined);
    setPage(1);
    try {
      let next = await (force
        ? sourceAvailabilityApi.preview(capturedIds.current, true)
        : (initialPreview.current ??= sourceAvailabilityApi.preview(capturedIds.current, false)));
      while (mounted.current && request === sequence.current && next.status === "checking") {
        setPreview(next);
        await new Promise((resolve) => setTimeout(resolve, 1000));
        if (!mounted.current || request !== sequence.current) return;
        next = await sourceAvailabilityApi.batch(next.batchId);
      }
      if (next.status !== "ready") throw new Error("SOURCE_AVAILABILITY_CHECK_FAILED");
      if (!mounted.current || request !== sequence.current) return;
      setPreview(next);
      setSelected(
        new Set(
          next.rows.filter((row) => eligibleSource(row, actionRef.current)).map((row) => row.key),
        ),
      );
      setNow(Date.now());
      onCheckedRef.current?.();
    } catch (failure) {
      if (mounted.current && request === sequence.current)
        setError(failure instanceof Error ? failure.message : "");
    } finally {
      if (mounted.current && request === sequence.current) {
        reading.current = false;
        setLoading(false);
      }
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
  const consumed = !!preview && attempted.current.has(preview.batchId);
  const rows = (preview?.rows ?? []).filter((row) =>
    action === "restore" ? row.disabled : !row.disabled,
  );
  const eligible = rows.filter((row) => eligibleSource(row, action));
  const selectedRows = eligible.filter((row) => selected.has(row.key));
  const selectedTaskCount = new Set(selectedRows.map((row) => row.taskId)).size;
  const noAlternativeCount = new Set(
    selectedRows
      .filter((row) => row.hasAlternative === false)
      .map((row) => `${row.taskId}:${row.partId}`),
  ).size;
  const filteredRows = confirmation
    ? selectedRows
    : statusFilter === "all"
      ? rows
      : rows.filter((row) => (row.health?.status ?? "unknown") === statusFilter);
  const pages = Math.max(1, Math.ceil(filteredRows.length / pageSize));
  const shown = filteredRows.slice(
    (Math.min(page, pages) - 1) * pageSize,
    Math.min(page, pages) * pageSize,
  );
  const locked = loading || submitting || expired || consumed || preview?.status !== "ready";
  const submit = async () => {
    if (busy.current || !preview || locked || !confirmation || !selectedRows.length) return;
    const batch = preview;
    const keys = selectedRows.map((row) => row.key);
    // An ambiguous network outcome must never leave a re-submittable old snapshot.
    attempted.current.add(batch.batchId);
    busy.current = true;
    setSubmitting(true);
    setError(undefined);
    try {
      const response = await sourceAvailabilityApi.apply(batch.batchId, keys, action);
      if (mounted.current) {
        setResult(response);
        setConfirmation(false);
        setSelected(new Set());
      }
      onChangedRef.current?.();
    } catch (failure) {
      if (mounted.current) setError(failure instanceof Error ? failure.message : "");
    } finally {
      busy.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };
  return (
    <Modal
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      size="6xl"
      scrollBehavior="inside"
      title={t("sourceAvailability.review.title")}
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button variant="light" onPress={() => setVisible(false)}>
            {t("common.action.close")}
          </Button>
          {confirmation ? (
            <>
              <Button variant="flat" isDisabled={submitting} onPress={() => setConfirmation(false)}>
                {t("sourceAvailability.review.back")}
              </Button>
              <Button
                color={action === "disable" ? "danger" : "primary"}
                isDisabled={locked || !selectedRows.length}
                isLoading={submitting}
                onPress={() => void submit()}
              >
                {t(`sourceAvailability.review.confirm.${action}`, { count: selectedRows.length })}
              </Button>
            </>
          ) : (
            <Button
              color={action === "disable" ? "danger" : "primary"}
              variant="flat"
              isDisabled={locked || !selectedRows.length}
              onPress={() => {
                setConfirmation(true);
                setPage(1);
              }}
            >
              {t(`sourceAvailability.review.action.${action}`, { count: selectedRows.length })}
            </Button>
          )}
        </div>
      }
    >
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
        <span>
          {t("sourceAvailability.review.scope", { count: capturedIds.current.length })}
          {scopeLabel ? ` · ${scopeLabel}` : ""}
        </span>
        <Tooltip content={t("sourceAvailability.review.preserveHint")}>
          <span className="cursor-help underline decoration-dotted underline-offset-2">
            {t("sourceAvailability.review.preserveLabel")}
          </span>
        </Tooltip>
      </div>
      <Tooltip content={t("sourceAvailability.review.savedPlanHint")}>
        <span className="w-fit cursor-help text-xs text-default-400 underline decoration-dotted underline-offset-2">
          {t("sourceAvailability.review.savedPlanLabel")}
        </span>
      </Tooltip>
      <Tabs
        selectedKey={action}
        size="sm"
        isDisabled={submitting || confirmation}
        onSelectionChange={(key) => {
          const next = key as SourceAvailabilityAction;
          setAction(next);
          setStatusFilter("all");
          setPage(1);
          setSelected(
            new Set(
              (preview?.rows ?? [])
                .filter((row) => eligibleSource(row, next))
                .map((row) => row.key),
            ),
          );
        }}
      >
        <Tab key="disable" title={t("sourceAvailability.review.tab.disable")} />
        <Tab key="restore" title={t("sourceAvailability.review.tab.restore")} />
      </Tabs>
      <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
        <span>
          {t("sourceAvailability.review.counts", {
            count: rows.length,
            eligible: eligible.length,
            selected: selectedRows.length,
          })}
        </span>
        <div className="flex flex-wrap gap-1">
          <Button
            size="sm"
            variant="light"
            isDisabled={locked || confirmation || !eligible.length}
            onPress={() => setSelected(new Set(eligible.map((row) => row.key)))}
          >
            {t("sourceAvailability.review.selectEligible")}
          </Button>
          <Button
            size="sm"
            variant="light"
            isDisabled={locked || confirmation || !selected.size}
            onPress={() => setSelected(new Set())}
          >
            {t("sourceAvailability.review.selectNone")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            isDisabled={loading || submitting}
            onPress={() => void load(true)}
          >
            {t("sourceAvailability.review.refresh")}
          </Button>
        </div>
      </div>
      {loading && (
        <div className="flex min-h-48 flex-col items-center justify-center gap-3" role="status">
          <Spinner aria-label={t("sourceAvailability.loading")} />
          <p className="text-sm">
            {t("sourceAvailability.review.checking", { count: capturedIds.current.length })}
          </p>
          {preview?.status === "checking" && (
            <Progress
              className="w-full max-w-sm"
              aria-label={t("sourceAvailability.review.progress")}
              value={preview.processed}
              maxValue={Math.max(preview.total, 1)}
              label={t("sourceAvailability.review.progressCount", {
                count: preview.processed,
                total: preview.total,
              })}
            />
          )}
          <p className="text-xs text-default-500">{t("sourceAvailability.review.checkHint")}</p>
        </div>
      )}
      {error != null && (
        <p role="alert" className="text-sm text-danger">
          {error
            ? t(`sourceAvailability.error.${error}`, { defaultValue: error })
            : t("sourceAvailability.loadFailed")}
          {consumed && ` ${t("sourceAvailability.review.unknownOutcome")}`}
        </p>
      )}
      {expired && !result && (
        <p role="alert" className="text-sm text-warning-600">
          {t("sourceAvailability.review.expired")}
        </p>
      )}
      {result && (
        <div role="status" className="rounded-lg bg-success-50 p-3 text-sm">
          {t("sourceAvailability.review.result", {
            count: result.changedCount,
            skipped: result.skippedCount,
          })}
          <p className="mt-1 text-xs text-default-500">
            {t("sourceAvailability.review.refreshAfterApply")}
          </p>
          {!!result.skippedCount && (
            <ul className="mt-2 space-y-1 text-xs">
              {result.rows
                ?.filter((item) => item.status === "skipped")
                .map((item) => (
                  <li key={item.key}>
                    #{preview?.rows.find((row) => row.key === item.key)?.taskId} ·{" "}
                    {t(`sourceAvailability.reason.${item.code}`, { defaultValue: item.code })}
                  </li>
                ))}
            </ul>
          )}
        </div>
      )}
      {confirmation && (
        <section
          className="space-y-2 rounded-lg border border-warning-200 bg-warning-50 p-3"
          aria-label={t("sourceAvailability.review.confirmation")}
        >
          <p className="text-sm font-medium">
            {t(`sourceAvailability.review.confirmation.${action}`, {
              count: selectedRows.length,
              tasks: selectedTaskCount,
            })}
          </p>
          <p className="text-xs">{t("sourceAvailability.review.preserveHint")}</p>
          {action === "disable" && noAlternativeCount > 0 && (
            <p className="text-xs text-warning-700">
              {t("sourceAvailability.review.noAlternativeSummary", { count: noAlternativeCount })}
            </p>
          )}
        </section>
      )}
      {!loading && preview && (
        <div className="space-y-2">
          <div
            className="flex flex-wrap gap-1"
            aria-label={t("sourceAvailability.review.statusFilter")}
          >
            {[
              "all",
              "confirmedUnavailable",
              "available",
              "accessRequired",
              "temporarilyUnavailable",
              "unknown",
              "unsupported",
            ].map((status) => {
              const count =
                status === "all"
                  ? rows.length
                  : rows.filter((row) => (row.health?.status ?? "unknown") === status).length;
              return (
                <Button
                  key={status}
                  size="sm"
                  variant={statusFilter === status ? "flat" : "light"}
                  color={statusFilter === status ? "primary" : "default"}
                  className="h-7 min-w-0 px-2 text-xs"
                  aria-pressed={statusFilter === status}
                  isDisabled={confirmation}
                  onPress={() => {
                    setStatusFilter(status);
                    setPage(1);
                  }}
                >
                  {t(
                    status === "all"
                      ? "sourceAvailability.review.allStatuses"
                      : `sourceAvailability.status.${status}`,
                  )}{" "}
                  · {count}
                </Button>
              );
            })}
          </div>
          {!filteredRows.length && (
            <p className="py-8 text-center text-sm text-default-500">
              {t(
                rows.length
                  ? "sourceAvailability.review.emptyFilter"
                  : `sourceAvailability.review.empty.${action}`,
              )}
            </p>
          )}
          {shown.map((row) => (
            <article
              key={row.key}
              className="rounded-lg border border-default-200 p-3"
              data-source-key={row.key}
            >
              <div className="flex items-start gap-3">
                <Checkbox
                  aria-label={t("sourceAvailability.review.selectSource", {
                    id: row.taskId,
                    url: row.url,
                  })}
                  isSelected={selected.has(row.key) && eligibleSource(row, action)}
                  isDisabled={locked || confirmation || !eligibleSource(row, action)}
                  onValueChange={(checked) =>
                    setSelected((previous) => {
                      const next = new Set(previous);
                      if (checked) next.add(row.key);
                      else next.delete(row.key);
                      return next;
                    })
                  }
                />
                <div className="min-w-0 flex-1 space-y-1.5">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="break-words text-sm font-medium">
                      #{row.taskId} {row.taskTitle}
                    </span>
                    <SourceHealthBadge health={row.health} disabled={row.disabled} />
                    {row.inUse && (
                      <Chip size="sm" variant="flat">
                        {t("sourceAvailability.review.inUse")}
                      </Chip>
                    )}
                  </div>
                  <AcquisitionSourceDetails source={row} />
                  {row.health?.message && (
                    <p className="break-words text-xs text-default-500">{row.health.message}</p>
                  )}
                  {row.health?.checkedAt && (
                    <p className="text-xs text-default-400">
                      {t("sourceAvailability.checkedAt", {
                        time:
                          parseServerTime(row.health.checkedAt)?.toLocaleString() ??
                          row.health.checkedAt,
                      })}
                    </p>
                  )}
                  {row.reasonCode && (
                    <p className="text-xs text-default-500">
                      {t(`sourceAvailability.reason.${row.reasonCode}`, {
                        defaultValue: row.reasonCode,
                      })}
                    </p>
                  )}
                  {row.hasAlternative === false && !row.inUse && (
                    <p className="text-xs text-warning-600">
                      {t("sourceAvailability.review.noAlternative")}
                    </p>
                  )}
                </div>
              </div>
            </article>
          ))}
          {pages > 1 && (
            <div className="flex justify-center pt-2">
              <Pagination
                page={Math.min(page, pages)}
                total={pages}
                onChange={setPage}
                showControls
                isDisabled={submitting}
              />
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}
