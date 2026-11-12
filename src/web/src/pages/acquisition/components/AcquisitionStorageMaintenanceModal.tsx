import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Chip, Modal, Pagination, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { millisecondsUntil } from "@/core/serverTime";
import {
  acquisitionStorageApi,
  type StorageMaintenanceMode,
  type StorageMaintenancePreview,
  type StorageMaintenanceResult,
} from "./acquisitionStorageApi";

const bytesLabel = (bytes = 0) =>
  `${(bytes / 1024 ** 3).toLocaleString(undefined, { maximumFractionDigits: 2 })} GiB`;
const pageSize = 20;

/** A captured task set and single-use preview protect against live filter, revision and file changes. */
export default function AcquisitionStorageMaintenanceModal({
  mode,
  taskIds,
  scopeLabel,
  onChanged,
  onDestroyed,
}: DestroyableProps & {
  mode: StorageMaintenanceMode;
  taskIds: number[];
  scopeLabel?: string;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const captured = useRef([...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0))]);
  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [overrideRetention, setOverrideRetention] = useState(false);
  const retentionConsent = useRef(false);
  const currentPreviewId = useRef<string>();
  const [preview, setPreview] = useState<StorageMaintenancePreview>();
  const [result, setResult] = useState<StorageMaintenanceResult>();
  const [selected, setSelected] = useState(new Set<number>());
  const [error, setError] = useState<"preview" | "submit">();
  const [page, setPage] = useState(1);
  const [now, setNow] = useState(Date.now());
  const mounted = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const attempted = useRef(new Set<string>());
  const initial = useRef<Promise<StorageMaintenancePreview>>();
  const previewController = useRef<AbortController>();
  const load = useCallback(
    async (refresh = false) => {
      if (busy.current) return;
      const request = ++sequence.current;
      currentPreviewId.current = undefined;
      setLoading(true);
      setPreview(undefined);
      setSelected(new Set());
      setError(undefined);
      setResult(undefined);
      setPage(1);
      try {
        if (refresh || !initial.current) {
          previewController.current?.abort();
          previewController.current = new AbortController();
          initial.current = retentionConsent.current
            ? acquisitionStorageApi.preview(mode, captured.current, previewController.current.signal, true)
            : acquisitionStorageApi.preview(mode, captured.current, previewController.current.signal);
        }
        const next = await initial.current;
        if (!mounted.current || request !== sequence.current) return;
        currentPreviewId.current = next.previewId;
        setPreview(next);
        setSelected(
          new Set(
            mode === "policy"
              ? next.rows
                  .filter((row) => row.eligible === true && captured.current.includes(row.taskId))
                  .map((row) => row.taskId)
              : [],
          ),
        );
        setNow(Date.now());
      } catch {
        if (mounted.current && request === sequence.current) setError("preview");
      } finally {
        if (mounted.current && request === sequence.current) setLoading(false);
      }
    },
    [mode],
  );
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      mounted.current = false;
      sequence.current++;
      clearInterval(timer);
      // StrictMode remounts effects synchronously; keep its shared preview alive.
      // A real unmount cancels only the read-only scan, never an apply request.
      queueMicrotask(() => {
        if (!mounted.current) previewController.current?.abort();
      });
    };
  }, [load]);
  const rows = (preview?.rows ?? []).filter((row) => captured.current.includes(row.taskId));
  const eligible = rows.filter((row) => row.eligible === true);
  const selectedRows = eligible.filter((row) => selected.has(row.taskId));
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const consumed = !!preview && attempted.current.has(preview.previewId);
  const locked = loading || submitting || !preview || expired || consumed;
  const close = () => {
    if (busy.current) return;
    sequence.current++;
    previewController.current?.abort();
    setVisible(false);
  };
  const reason = (code?: string) =>
    t(`acquisition.storage.reason.${code}`, {
      defaultValue: t("acquisition.storage.reason.unknown"),
    });
  const submit = async () => {
    if (!preview || currentPreviewId.current !== preview.previewId || busy.current || locked || !selectedRows.length) return;
    busy.current = true;
    attempted.current.add(preview.previewId);
    setSubmitting(true);
    setError(undefined);
    try {
      const next = await acquisitionStorageApi.apply(
        mode,
        preview.previewId,
        selectedRows.map((row) => row.taskId),
      );
      if (mounted.current) setResult(next);
      onChanged?.();
    } catch {
      if (mounted.current) setError("submit");
      onChanged?.();
    } finally {
      busy.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };
  return (
    <Modal
      visible={visible}
      title={t(`acquisition.storage.batch.${mode}.title`)}
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
              color={mode === "cleanup" ? "danger" : "primary"}
              isLoading={submitting}
              isDisabled={locked || !selectedRows.length}
              onPress={() => void submit()}
            >
              {t(`acquisition.storage.batch.${mode}.confirm`, { count: selectedRows.length })}
            </Button>
          )}
        </div>
      }
    >
      <p className="text-xs text-default-500">
        {t("acquisition.storage.batch.scope", { count: captured.current.length })}
        {scopeLabel ? ` · ${scopeLabel}` : ""}
      </p>
      <p className="text-xs text-default-500">{t(`acquisition.storage.batch.${mode}.hint`)}</p>
      {mode === "cleanup" && !result && (
        <Checkbox
          size="sm"
          aria-label={t("acquisition.storage.batch.cleanup.overrideRetention")}
          isSelected={overrideRetention}
          isDisabled={submitting}
          onValueChange={(value) => {
            if (busy.current) return;
            retentionConsent.current = value;
            setOverrideRetention(value);
            void load(true);
          }}
        >
          <span>{t("acquisition.storage.batch.cleanup.overrideRetention")}</span>
          <span className="block text-xs text-default-500">
            {t("acquisition.storage.batch.cleanup.overrideRetentionHint")}
          </span>
        </Checkbox>
      )}
      {loading ? (
        <div className="flex min-h-48 items-center justify-center">
          <Spinner aria-label={t("acquisition.storage.batch.loading")} />
        </div>
      ) : (
        <>
          {error && (
            <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
              {t(`acquisition.storage.batch.error.${error}`)}
              <Button size="sm" variant="flat" onPress={() => void load(true)}>
                {t("acquisition.storage.batch.refresh")}
              </Button>
            </div>
          )}
          {result ? (
            <>
              <p role="status" className="text-sm">
                {t(`acquisition.storage.batch.${mode}.result`, {
                  count: result.taskIds.length,
                  skipped: result.skipped.length,
                  files: result.removedFiles ?? 0,
                  size: bytesLabel(result.removedBytes),
                })}
              </p>
              {mode === "cleanup" && (
                <p className="text-xs text-default-500">
                  {t("acquisition.storage.batch.cleanup.spaceHint")}
                </p>
              )}
              {!!result.skipped.length && (
                <div className="max-h-64 divide-y divide-default-200 overflow-y-auto rounded-lg border border-default-200">
                  {result.skipped.map((row) => (
                    <p key={row.taskId} className="p-2 text-sm">
                      #{row.taskId} · {reason(row.reasonCode)}
                    </p>
                  ))}
                </div>
              )}
            </>
          ) : (
            preview && (
              <>
                {mode === "policy" && preview.policy && (
                  <div className="rounded-lg bg-default-100 p-2 text-xs">
                    <p>
                      {t("acquisition.storage.cleanupIntermediates")} ·{" "}
                      {t(
                        preview.policy.cleanupIntermediates
                          ? "acquisition.storage.enabled"
                          : "acquisition.storage.disabled",
                      )}
                    </p>
                    <p>
                      {t("acquisition.storage.sourceCleanup")} ·{" "}
                      {t(`acquisition.storage.sourceCleanup.${preview.policy.sourceCleanup}`, {
                        defaultValue: preview.policy.sourceCleanup,
                      })}
                    </p>
                    {preview.policy.sourceCleanup !== "keep" && (
                      <p className="mt-1 text-warning-600">
                        {t(`acquisition.storage.sourceCleanup.${preview.policy.sourceCleanup}Hint`)}
                      </p>
                    )}
                  </div>
                )}
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-wrap gap-1">
                    <Chip size="sm" variant="flat">
                      {t("acquisition.storage.batch.eligible", { count: eligible.length })}
                    </Chip>
                    <Chip size="sm" variant="flat">
                      {t("acquisition.storage.batch.skipped", {
                        count: rows.length - eligible.length,
                      })}
                    </Chip>
                    {mode === "cleanup" && (
                      <Chip size="sm" variant="flat">
                        {t("acquisition.storage.batch.selectedSize", {
                          size: bytesLabel(
                            selectedRows.reduce((sum, row) => sum + (row.bytes ?? 0), 0),
                          ),
                        })}
                      </Chip>
                    )}
                  </div>
                  <div className="flex flex-wrap gap-1">
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={submitting}
                      onPress={() => void load(true)}
                    >
                      {t("acquisition.storage.batch.refresh")}
                    </Button>
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={locked || !eligible.length}
                      onPress={() => setSelected(new Set(eligible.map((row) => row.taskId)))}
                    >
                      {t("acquisition.storage.batch.selectAll")}
                    </Button>
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={locked || !selectedRows.length}
                      onPress={() => setSelected(new Set())}
                    >
                      {t("acquisition.storage.batch.selectNone")}
                    </Button>
                  </div>
                </div>
                {expired && (
                  <p role="alert" className="text-sm text-warning-600">
                    {t("acquisition.storage.batch.expired")}
                  </p>
                )}
                <div className="divide-y divide-default-200 rounded-lg border border-default-200">
                  {rows.slice((page - 1) * pageSize, page * pageSize).map((row) => (
                    <div key={row.taskId} className="flex items-start gap-3 p-3">
                      <Checkbox
                        aria-label={t("acquisition.storage.batch.selectTask", { id: row.taskId })}
                        size="sm"
                        isSelected={row.eligible === true && selected.has(row.taskId)}
                        isDisabled={locked || row.eligible !== true}
                        onValueChange={(checked) =>
                          setSelected((current) => {
                            const next = new Set(current);
                            checked ? next.add(row.taskId) : next.delete(row.taskId);
                            return next;
                          })
                        }
                      />
                      <div className="min-w-0 flex-1 space-y-1">
                        <p className="break-words text-sm font-medium">
                          #{row.taskId} ·{" "}
                          {row.title || t("acquisition.unnamed", { id: row.taskId })}
                        </p>
                        <p className="text-xs text-default-500">
                          {reason(row.reasonCode ?? (row.eligible ? "eligible" : undefined))}
                        </p>
                        {mode === "cleanup" && row.eligible && (
                          <p className="text-xs">
                            {t("acquisition.storage.batch.files", {
                              count: row.fileCount ?? 0,
                              size: bytesLabel(row.bytes),
                            })}
                          </p>
                        )}
                        {mode === "policy" &&
                          [
                            ["downloadPreview", row.downloadPreview],
                            ["processingPreview", row.processingPreview],
                            ["deliveryPreview", row.deliveryPreview],
                          ].map(
                            ([label, path]) =>
                              path && (
                                <p key={label} className="break-all text-xs">
                                  <span className="text-default-500">
                                    {t(`acquisition.storage.batch.${label}`)} ·{" "}
                                  </span>
                                  <code>{path}</code>
                                </p>
                              ),
                          )}
                      </div>
                    </div>
                  ))}
                </div>
                {rows.length > pageSize && (
                  <div className="flex justify-center">
                    <Pagination
                      page={page}
                      total={Math.ceil(rows.length / pageSize)}
                      onChange={setPage}
                      isDisabled={submitting}
                    />
                  </div>
                )}
              </>
            )
          )}
        </>
      )}
    </Modal>
  );
}
