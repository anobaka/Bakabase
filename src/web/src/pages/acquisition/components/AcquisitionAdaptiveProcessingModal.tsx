import AcquisitionContinuationMethodTabs from "./AcquisitionContinuationMethodTabs";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Chip, Modal, Select, Spinner, Tooltip } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import {
  acquisitionAdaptiveProcessingApi,
  type AdaptiveMigrationPreview,
  type AdaptiveMigrationResult,
} from "./acquisitionAdaptiveProcessingApi";

export default function AcquisitionAdaptiveProcessingModal({
  taskIds = [],
  scopeLabel,
  onChanged,
  onOpenTask,
  onBackToSelection,
  onDestroyed,
}: DestroyableProps & {
  taskIds?: number[];
  scopeLabel?: string;
  onChanged?: () => void;
  onOpenTask?: (taskId: number) => void;
  onBackToSelection?: () => void;
}) {
  const { t } = useTranslation();
  const captured = useRef([...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0))]);
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<AdaptiveMigrationPreview>();
  const [selected, setSelected] = useState(new Set<number>());
  const [result, setResult] = useState<AdaptiveMigrationResult>();
  const [error, setError] = useState<string>();
  const [maxIterations, setMaxIterations] = useState(3);
  const attempted = useRef(false);

  const load = useCallback(async (signal: AbortSignal) => {
    if (!captured.current.length) { setLoading(false); return; }
    setLoading(true);
    setPreview(undefined);
    setError(undefined);
    try {
      const next = await acquisitionAdaptiveProcessingApi.preview(captured.current, maxIterations, signal);
      if (signal.aborted) return;
      const returned = new Set(next.rows.map(r => r.taskId));
      if (!next.token || next.rows.length !== returned.size || next.rows.some(r => !captured.current.includes(r.taskId)))
        throw new Error("ADAPTIVE_MIGRATION_INVALID_PREVIEW");
      setPreview(next);
      setSelected(new Set(next.rows.filter((row) => row.canMigrate).map((row) => row.taskId)));
    } catch (e) {
      if (!signal.aborted) setError(e instanceof Error ? e.message : "ADAPTIVE_MIGRATION_PREVIEW_FAILED");
    } finally {
      if (!signal.aborted) setLoading(false);
    }
  }, [maxIterations]);
  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const rows = preview?.rows ?? [];
  const selectable = useMemo(() => rows.filter((row) => row.canMigrate), [rows]);
  const manualReviewRows = useMemo(() => rows.filter((row) => !row.canMigrate), [rows]);
  const selectedCount = selectable.filter((row) => selected.has(row.taskId)).length;
  const reasonLabel = useCallback(
    (row: AdaptiveMigrationPreview["rows"][number]) => {
      if (row.reason) {
        const key = `acquisition.plan.replan.error.${row.reason}`;
        const localized = t(key, { defaultValue: row.reason });
        return localized === key ? row.reason : localized;
      }
      return t(`acquisition.adaptive.reason.${row.reasonCode}`, {
        defaultValue: row.canMigrate ? t("acquisition.adaptive.ready") : row.reasonCode,
      });
    },
    [t],
  );
  const close = () => {
    if (submitting) return;
    setVisible(false);
  };
  const submit = async () => {
    if (!preview || !selectedCount || submitting || attempted.current) return;
    attempted.current = true;
    setSubmitting(true);
    setError(undefined);
    try {
      const next = await acquisitionAdaptiveProcessingApi.apply(preview.token, [...selected]);
      setResult(next);
      onChanged?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : "ADAPTIVE_MIGRATION_APPLY_FAILED");
    } finally {
      setSubmitting(false);
    }
  };
  return (
    <Modal
      visible={visible}
      title={t(onBackToSelection ? "acquisition.continueEntry.open" : "acquisition.adaptive.title")}
      size="5xl"
      scrollBehavior="inside"
      onClose={close}
      onDestroyed={onDestroyed}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      hideCloseButton={submitting}
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button variant="light" isDisabled={submitting} onPress={close}>
            {t("common.action.close")}
          </Button>
          {!result && (
            <Button
              color="primary"
              isLoading={submitting}
              isDisabled={loading || !preview || !selectedCount || attempted.current}
              onPress={() => void submit()}
            >
              {t("acquisition.adaptive.confirm", { count: selectedCount })}
            </Button>
          )}
        </div>
      }
    >
      <div className="space-y-2">
        {onBackToSelection && !result && (
          <AcquisitionContinuationMethodTabs value="adaptive" disabled={submitting} onSwitch={onBackToSelection} />
        )}
        <div className="rounded-lg border border-primary-200 bg-primary-50 p-3 text-sm text-primary-900">
          <p className="font-medium">{t("acquisition.adaptive.whatItDoes")}</p>
          <p className="mt-1 text-xs leading-5 text-primary-800">
            {t("acquisition.adaptive.hint")}
          </p>
          {scopeLabel && <p className="mt-1 text-xs font-medium text-primary-900">{scopeLabel}</p>}
        </div>
        <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
          <Chip size="sm" color="success" variant="flat">
            {t("acquisition.adaptive.readyCount", { count: selectable.length })}
          </Chip>
          {manualReviewRows.length > 0 && (
            <Chip size="sm" color="warning" variant="flat">
              {t("acquisition.adaptive.manualCount", { count: manualReviewRows.length })}
            </Chip>
          )}
          <Tooltip content={t("acquisition.adaptive.iterationsHint")}>
            <div className="flex items-center gap-1">
              <span>{t("acquisition.adaptive.iterations")}</span>
              <Select
                aria-label={t("acquisition.adaptive.iterations")}
                size="sm"
                className="w-28"
                selectedKeys={[String(maxIterations)]}
                disallowEmptySelection
                dataSource={[1, 2, 3, 4, 5].map((value) => ({
                  value: String(value),
                  label: String(value),
                }))}
                onSelectionChange={(keys) => {
                  const value = Number(Array.from(keys)[0]);
                  if (value >= 1 && value <= 5) setMaxIterations(value);
                }}
              />
            </div>
          </Tooltip>
        </div>
      </div>
      {error && (
        <div role="alert" className="mt-2 rounded-lg bg-danger-50 p-2 text-sm text-danger">
          {error}
        </div>
      )}
      {loading ? (
        <div className="flex min-h-48 items-center justify-center">
          <Spinner />
        </div>
      ) : result ? (
        <div className="space-y-3 py-4">
          <p role="status" className="text-sm">
            {t("acquisition.adaptive.result", {
              migrated: result.migratedTaskIds.length,
              skipped: result.skipped.length,
            })}
          </p>
          {!!result.skipped.length && (
            <div className="max-h-64 overflow-y-auto rounded-lg border border-default-200 p-2 text-xs">
              {result.skipped.map((row) => (
                <div key={row.taskId} className="border-b border-default-100 py-1 last:border-0">
                  #{row.taskId} · {reasonLabel(row)}
                </div>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="mt-3 min-w-0 overflow-hidden rounded-lg border border-default-200">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-default-200 bg-default-50 p-2 text-sm">
            <Checkbox
              size="sm"
              isSelected={selectedCount === selectable.length && selectable.length > 0}
              isIndeterminate={selectedCount > 0 && selectedCount < selectable.length}
              onValueChange={(checked) =>
                setSelected(new Set(checked ? selectable.map((row) => row.taskId) : []))
              }
            >
              {t("acquisition.adaptive.selectAll", { count: selectable.length })}
            </Checkbox>
            <Chip size="sm" variant="flat">
              {t("acquisition.adaptive.migratable", { count: selectable.length })}
            </Chip>
          </div>
          <div className="max-h-[55vh] overflow-y-auto divide-y divide-default-100">
            {rows.map((row) => (
              <div
                key={row.taskId}
                className={`flex min-w-0 items-start gap-2 p-2 text-sm ${row.canMigrate ? "" : "opacity-55"}`}
              >
                <Checkbox
                  size="sm"
                  className="mt-0.5 shrink-0"
                  isSelected={selected.has(row.taskId)}
                  isDisabled={!row.canMigrate || submitting}
                  onValueChange={(checked) =>
                    setSelected((previous) => {
                      const next = new Set(previous);
                      if (checked) next.add(row.taskId);
                      else next.delete(row.taskId);
                      return next;
                    })
                  }
                />
                <span className="min-w-0 flex-1 break-words">
                  <strong>#{row.taskId}</strong>
                  {row.title ? ` · ${row.title}` : ""}
                  <span
                    className={`ml-2 text-xs ${row.canMigrate ? "text-success-700" : "text-warning-700"}`}
                  >
                    {row.canMigrate
                      ? reasonLabel(row)
                      : `${t("acquisition.adaptive.manualReview")}：${reasonLabel(row)}`}
                  </span>
                </span>
                {!row.canMigrate && onOpenTask && (
                  <Button
                    size="sm"
                    variant="light"
                    className="shrink-0"
                    isDisabled={submitting}
                    onPress={() => {
                      setVisible(false);
                      onOpenTask(row.taskId);
                    }}
                  >
                    {t("acquisition.adaptive.openTask")}
                  </Button>
                )}
              </div>
            ))}
            {!rows.length && (
              <p className="p-4 text-center text-sm text-default-500">
                {t("acquisition.adaptive.empty")}
              </p>
            )}
          </div>
        </div>
      )}
    </Modal>
  );
}
