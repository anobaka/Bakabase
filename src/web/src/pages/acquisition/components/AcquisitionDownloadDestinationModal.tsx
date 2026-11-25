import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Chip, Modal, Pagination, Spinner, Tooltip } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";
import StorageCapacitySummary from "@/components/FileSystemSelector/components/StorageCapacitySummary";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { millisecondsUntil } from "@/core/serverTime";
import { formatBtBytes } from "@/pages/bt-downloads/format";
import AcquisitionRecoveryModal from "./AcquisitionRecoveryModal";
import {
  acquisitionDownloadDestinationApi as api,
  type DownloadDestinationPreview,
  type DownloadDestinationResult,
  type DownloadDestinationRow,
} from "./acquisitionDownloadDestinationApi";

const pageSize = 20;
type ReuseScope = { taskId: number; downloadTaskId: number };
const requestErrorCode = (failure: unknown) =>
  failure instanceof Error &&
  /^(DOWNLOAD_DESTINATION|ACQUISITION_TASK|LOCAL_STORAGE)_[A-Z_]+$/.test(failure.message)
    ? failure.message
    : undefined;
const eligibleDownload = (download: DownloadDestinationRow["downloads"][number]) =>
  download.canApply === true && !!download.newDirectory?.trim();
const eligibleDownloadCount = (row: DownloadDestinationRow) =>
  row.canApply === true ? row.downloads.filter(eligibleDownload).length : 0;
const eligibleRow = (row: DownloadDestinationRow) => eligibleDownloadCount(row) > 0;

/** Captures the filter once. Changing a directory only requests a new preview, never a download. */
export default function AcquisitionDownloadDestinationModal({
  taskIds,
  scopeLabel,
  onChanged,
  onDestroyed,
}: DestroyableProps & { taskIds: number[]; scopeLabel?: string; onChanged?: () => void }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const captured = useRef([...new Set(taskIds.filter((id) => Number.isSafeInteger(id) && id > 0))]);
  const [visible, setVisible] = useState(true);
  const [loadingDefaults, setLoadingDefaults] = useState(true);
  const [defaultsFailed, setDefaultsFailed] = useState(false);
  const [target, setTarget] = useState("");
  const [reuseScope, setReuseScope] = useState<ReuseScope | null>(null);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<DownloadDestinationPreview>();
  const [result, setResult] = useState<DownloadDestinationResult>();
  const [selected, setSelected] = useState(new Set<number>());
  const [error, setError] = useState<"preview" | "submit">();
  const [errorCode, setErrorCode] = useState<string>();
  const [page, setPage] = useState(1);
  const [now, setNow] = useState(Date.now());
  const mounted = useRef(false);
  const sequence = useRef(0);
  const busy = useRef(false);
  const initialDefaults = useRef<Promise<string>>();
  const attempted = useRef(new Set<string>());
  const submittedIds = useRef<number[]>([]);
  const changed = useRef(onChanged);
  changed.current = onChanged;

  useEffect(() => {
    mounted.current = true;
    let active = true;
    void (initialDefaults.current ??= api.defaultRoot())
      .then((path) => {
        if (active) setTarget(path);
      })
      .catch(() => {
        if (active) setDefaultsFailed(true);
      })
      .finally(() => {
        if (active) setLoadingDefaults(false);
      });
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => {
      active = false;
      mounted.current = false;
      sequence.current++;
      clearInterval(timer);
    };
  }, []);

  const changeTarget = (path: string) => {
    if (busy.current) return;
    sequence.current++;
    setTarget(path);
    setPreview(undefined);
    setSelected(new Set());
    setError(undefined);
    setErrorCode(undefined);
    setLoading(false);
    setPage(1);
  };
  const load = async (scope = reuseScope) => {
    if (busy.current || loadingDefaults || !target.trim() || !captured.current.length) return;
    const request = ++sequence.current;
    setReuseScope(scope);
    setLoading(true);
    setPreview(undefined);
    setSelected(new Set());
    setError(undefined);
    setErrorCode(undefined);
    setResult(undefined);
    setPage(1);
    try {
      const next = scope
        ? await api.previewReuse(scope.taskId, scope.downloadTaskId, target.trim())
        : await api.preview(captured.current, target.trim());
      if (!mounted.current || request !== sequence.current) return;
      if (scope) {
        const [row] = next.rows;
        const download = row?.downloads[0];
        if (
          next.rows.length !== 1 ||
          row.taskId !== scope.taskId ||
          (row.canApply &&
            (row.downloads.length !== 1 ||
              download?.downloadTaskId !== scope.downloadTaskId ||
              !download.reuseEstimate))
        ) {
          throw new Error("DOWNLOAD_DESTINATION_PREVIEW_CHANGED");
        }
      }
      setPreview(next);
      setSelected(
        new Set(
          next.rows
            .filter((row) => captured.current.includes(row.taskId) && eligibleRow(row))
            .map((row) => row.taskId),
        ),
      );
      setNow(Date.now());
    } catch (failure) {
      if (mounted.current && request === sequence.current) {
        setError("preview");
        setErrorCode(requestErrorCode(failure));
      }
    } finally {
      if (mounted.current && request === sequence.current) setLoading(false);
    }
  };
  const rows = (preview?.rows ?? []).filter((row) => captured.current.includes(row.taskId));
  const eligible = rows.filter(eligibleRow);
  const selectedRows = eligible.filter((row) => selected.has(row.taskId));
  const expired = !!preview && millisecondsUntil(preview.expiresAt, now) <= 0;
  const consumed = !!preview && attempted.current.has(preview.previewToken);
  const locked = loading || submitting || !preview || expired || consumed;
  const submit = async () => {
    if (!preview || busy.current || locked || !selectedRows.length) return;
    busy.current = true;
    attempted.current.add(preview.previewToken);
    submittedIds.current = selectedRows.map((row) => row.taskId);
    setSubmitting(true);
    setError(undefined);
    setErrorCode(undefined);
    try {
      const next = await api.apply(preview.previewToken, submittedIds.current);
      if (mounted.current) setResult(next);
      changed.current?.();
    } catch (failure) {
      if (mounted.current) {
        setError("submit");
        setErrorCode(requestErrorCode(failure));
      }
      // An accepted mutation may have lost its response. Never replay this preview token.
      changed.current?.();
    } finally {
      busy.current = false;
      if (mounted.current) setSubmitting(false);
    }
  };
  const reason = (code?: string | null) =>
    t(`acquisition.downloadDestination.reason.${code}`, {
      defaultValue: t("acquisition.downloadDestination.reason.unknown"),
    });
  const resultIds = [...new Set(result?.appliedTaskIds ?? [])].filter((id) =>
    submittedIds.current.includes(id),
  );
  const shown = rows.slice((page - 1) * pageSize, page * pageSize);
  const downloadCount = rows.reduce((count, row) => count + row.downloads.length, 0);
  const eligibleDownloads = eligible.reduce((count, row) => count + eligibleDownloadCount(row), 0);
  const selectedDownloadCount = selectedRows.reduce(
    (count, row) => count + eligibleDownloadCount(row),
    0,
  );
  const partiallyApplied = result?.skipped.some(
    (row) => row.reasonCode === "DOWNLOAD_DESTINATION_PARTIALLY_APPLIED",
  );

  return (
    <Modal
      visible={visible}
      title={t("acquisition.downloadDestination.title")}
      size="5xl"
      scrollBehavior="inside"
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      hideCloseButton={submitting}
      footer={
        <div className="flex w-full flex-wrap items-center justify-end gap-2">
          <Button variant="light" isDisabled={submitting} onPress={() => setVisible(false)}>
            {t("common.action.close")}
          </Button>
          {result ? (
            resultIds.length > 0 &&
            (reuseScope ? (
              <a
                className="text-sm text-primary"
                href={`#/downloader?id=${encodeURIComponent(`site:${reuseScope.downloadTaskId}`)}`}
                onClick={() => setVisible(false)}
              >
                {t("acquisition.downloadDestination.viewSelectedDownload", {
                  id: reuseScope.downloadTaskId,
                })}
              </a>
            ) : (
              <Button
                color="primary"
                onPress={() => {
                  setVisible(false);
                  createPortal(AcquisitionRecoveryModal, {
                    taskIds: resultIds,
                    scopeLabel: t("acquisition.downloadDestination.revisedScope"),
                    initialScope: "downloads",
                    onChanged: changed.current,
                  });
                }}
              >
                {t("acquisition.downloadDestination.diagnose", { count: resultIds.length })}
              </Button>
            ))
          ) : (
            <Button
              color="primary"
              isLoading={submitting}
              isDisabled={locked || !selectedRows.length}
              onPress={() => void submit()}
            >
              {t(
                reuseScope
                  ? "acquisition.downloadDestination.applyReuse"
                  : "acquisition.downloadDestination.apply",
                {
                  id: reuseScope?.downloadTaskId,
                  count: selectedRows.length,
                  downloads: selectedDownloadCount,
                },
              )}
            </Button>
          )}
        </div>
      }
    >
      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
          <span>
            {reuseScope
              ? t("acquisition.downloadDestination.download", { id: reuseScope.downloadTaskId })
              : t("acquisition.downloadDestination.scope", { count: captured.current.length })}
            {scopeLabel ? ` · ${scopeLabel}` : ""}
          </span>
          <Tooltip
            content={t("acquisition.downloadDestination.preserveHint")}
            delay={1000}
            placement="bottom"
          >
            <span className="cursor-help underline decoration-dotted underline-offset-2">
              {t("acquisition.downloadDestination.preserveLabel")}
            </span>
          </Tooltip>
        </div>
        {result ? (
          <>
            <p role="status" className="text-sm">
              {t("acquisition.downloadDestination.result", {
                count: resultIds.length,
                downloads: result.appliedDownloadCount,
                skipped: result.skipped.length,
              })}
            </p>
            <p className="text-xs text-default-500">
              {t(
                reuseScope
                  ? "acquisition.downloadDestination.resumeHint"
                  : "acquisition.downloadDestination.resultHint",
              )}
            </p>
            {partiallyApplied && (
              <p role="alert" className="text-sm text-warning">
                {reason("DOWNLOAD_DESTINATION_PARTIALLY_APPLIED")}
              </p>
            )}
            {!!result.skipped.length && (
              <Button size="sm" variant="flat" onPress={() => void load()}>
                {t("acquisition.downloadDestination.recheck")}
              </Button>
            )}
            {!!result.skipped.length && (
              <div className="max-h-72 divide-y divide-default-200 overflow-y-auto rounded-lg border border-default-200">
                {result.skipped.map((row) => (
                  <div key={row.taskId} className="p-2 text-sm">
                    <span className="mr-2 font-medium">#{row.taskId}</span>
                    {reason(row.reasonCode)}
                  </div>
                ))}
              </div>
            )}
          </>
        ) : (
          <>
            <section className="space-y-2" aria-label={t("acquisition.downloadDestination.target")}>
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-sm font-medium">
                  {t("acquisition.downloadDestination.target")}
                </span>
                {loadingDefaults ? (
                  <Spinner
                    size="sm"
                    aria-label={t("acquisition.downloadDestination.loadingDefaults")}
                  />
                ) : (
                  <FileSystemSelectorButton
                    key={target || "select-download-root"}
                    aria-label={t("acquisition.downloadDestination.chooseDirectory")}
                    className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
                    isDisabled={submitting}
                    fileSystemSelectorProps={{
                      targetType: "folder",
                      multiple: false,
                      startPath: target || undefined,
                      defaultSelectedPath: target || undefined,
                      onSelected: (entry) => {
                        if (entry.path?.trim()) changeTarget(entry.path);
                      },
                    }}
                  >
                    {t("acquisition.downloadDestination.chooseDirectory")}
                  </FileSystemSelectorButton>
                )}
                <Button
                  size="sm"
                  variant="flat"
                  isLoading={loading}
                  isDisabled={
                    loadingDefaults || submitting || !target.trim() || !captured.current.length
                  }
                  onPress={() => void load()}
                >
                  {t("acquisition.downloadDestination.preview")}
                </Button>
              </div>
              <StorageCapacitySummary path={target} />
              {reuseScope && (
                <Button
                  size="sm"
                  variant="light"
                  isDisabled={submitting}
                  onPress={() => void load(null)}
                >
                  {t("acquisition.downloadDestination.backToSelection")}
                </Button>
              )}
              <p className="text-xs text-default-500">
                {t("acquisition.downloadDestination.targetHint")}
              </p>
              {defaultsFailed && (
                <p className="text-xs text-warning">
                  {t("acquisition.downloadDestination.defaultsFailed")}
                </p>
              )}
            </section>
            {error && (
              <div role="alert" className="space-y-1 text-sm text-danger">
                <p>{t(`acquisition.downloadDestination.error.${error}`)}</p>
                {errorCode && <p>{reason(errorCode)}</p>}
              </div>
            )}
            {loading ? (
              <div className="flex min-h-40 items-center justify-center">
                <Spinner aria-label={t("acquisition.downloadDestination.loading")} />
              </div>
            ) : preview ? (
              <>
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="flex flex-wrap gap-1" aria-live="polite">
                    <Chip size="sm" variant="flat">
                      {t("acquisition.downloadDestination.total", {
                        count: rows.length,
                        downloads: downloadCount,
                      })}
                    </Chip>
                    <Chip size="sm" variant="flat" color="success">
                      {t("acquisition.downloadDestination.eligible", {
                        count: eligible.length,
                        downloads: eligibleDownloads,
                      })}
                    </Chip>
                    <Chip size="sm" variant="flat">
                      {t("acquisition.downloadDestination.protected", {
                        count: downloadCount - eligibleDownloads,
                      })}
                    </Chip>
                  </div>
                  <div className="flex gap-1">
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={locked || !eligible.length}
                      onPress={() => setSelected(new Set(eligible.map((row) => row.taskId)))}
                    >
                      {t("acquisition.downloadDestination.selectEligible")}
                    </Button>
                    <Button
                      size="sm"
                      variant="light"
                      isDisabled={locked || !selected.size}
                      onPress={() => setSelected(new Set())}
                    >
                      {t("acquisition.downloadDestination.clearSelection")}
                    </Button>
                  </div>
                </div>
                {expired && (
                  <p role="alert" className="text-sm text-warning">
                    {t("acquisition.downloadDestination.expired")}
                  </p>
                )}
                <div className="divide-y divide-default-200 rounded-lg border border-default-200">
                  {shown.map((row) => {
                    const count = eligibleDownloadCount(row);
                    const canApply = count > 0;
                    const mixed = canApply && count < row.downloads.length;

                    return (
                      <div key={row.taskId} className="flex items-start gap-2 p-3">
                        <Checkbox
                          aria-label={t("acquisition.downloadDestination.selectTask", {
                            id: row.taskId,
                          })}
                          isSelected={selected.has(row.taskId) && canApply}
                          isDisabled={locked || !canApply}
                          onValueChange={(checked) =>
                            setSelected((previous) => {
                              const next = new Set(previous);
                              if (checked && canApply) next.add(row.taskId);
                              else next.delete(row.taskId);
                              return next;
                            })
                          }
                        />
                        <div className="min-w-0 flex-1 space-y-2">
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="min-w-0 flex-1 break-words text-sm font-medium">
                              <span className="mr-2 text-default-500">#{row.taskId}</span>
                              {row.title}
                            </span>
                            <span
                              className={`text-xs ${mixed ? "text-warning-600" : canApply ? "text-success-600" : "text-default-500"}`}
                            >
                              {mixed
                                ? t(
                                    "acquisition.downloadDestination.reason.DOWNLOAD_DESTINATION_ELIGIBLE_PARTS",
                                    {
                                      count,
                                      downloads: row.downloads.length,
                                    },
                                  )
                                : canApply
                                  ? t("acquisition.downloadDestination.rowEligible")
                                  : reason(row.reasonCode)}
                            </span>
                          </div>
                          {row.downloads.map((download, index) => {
                            const canApplyDownload = canApply && eligibleDownload(download);

                            return (
                              <div
                                key={download.downloadTaskId ?? index}
                                className="space-y-1 rounded bg-default-100 p-2 text-xs"
                              >
                                <div className="flex flex-wrap justify-between gap-1 text-default-500">
                                  <span>
                                    {download.downloadTaskId != null
                                      ? t("acquisition.downloadDestination.download", {
                                          id: download.downloadTaskId,
                                        })
                                      : t("acquisition.downloadDestination.downloadUnknown")}
                                  </span>
                                  {!canApplyDownload && (
                                    <span>
                                      {download.canApply !== true && download.reasonCode
                                        ? reason(download.reasonCode)
                                        : t("acquisition.downloadDestination.keepDirectory")}
                                    </span>
                                  )}
                                </div>
                                <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1">
                                  <dt className="text-default-500">
                                    {t(
                                      download.canPreviewReuse || download.reuseEstimate
                                        ? "acquisition.downloadDestination.sourceDirectory"
                                        : "acquisition.downloadDestination.oldDirectory",
                                    )}
                                  </dt>
                                  <dd className="break-all">
                                    {download.oldDirectory ||
                                      t("acquisition.downloadDestination.notSet")}
                                  </dd>
                                  {canApplyDownload && (
                                    <>
                                      <dt className="text-default-500">
                                        {t("acquisition.downloadDestination.newDirectory")}
                                      </dt>
                                      <dd className="break-all">{download.newDirectory}</dd>
                                    </>
                                  )}
                                </dl>
                                {download.reuseEstimate && reuseScope && (
                                  <div className="space-y-1 py-1">
                                    {download.reasonCode && <p>{reason(download.reasonCode)}</p>}
                                    <p>
                                      {t("acquisition.downloadDestination.reuseCandidates", {
                                        count: download.reuseEstimate.candidateFileCount,
                                        total: download.reuseEstimate.fileCount,
                                        bytes: formatBtBytes(download.reuseEstimate.candidateBytes),
                                      })}
                                    </p>
                                    <p>
                                      {t("acquisition.downloadDestination.reuseBudget", {
                                        minimum: formatBtBytes(
                                          download.reuseEstimate.requiredDownloadBytes,
                                        ),
                                        total: formatBtBytes(download.reuseEstimate.totalBytes),
                                      })}
                                    </p>
                                    <p>{t("acquisition.downloadDestination.reuseOriginals")}</p>
                                  </div>
                                )}
                                {!reuseScope &&
                                  download.canPreviewReuse &&
                                  download.downloadTaskId != null && (
                                    <Button
                                      size="sm"
                                      variant="flat"
                                      isDisabled={submitting}
                                      onPress={() =>
                                        void load({
                                          taskId: row.taskId,
                                          downloadTaskId: download.downloadTaskId!,
                                        })
                                      }
                                    >
                                      {t("acquisition.downloadDestination.previewReuse", {
                                        id: download.downloadTaskId,
                                      })}
                                    </Button>
                                  )}
                              </div>
                            );
                          })}
                        </div>
                      </div>
                    );
                  })}
                </div>
                {rows.length > pageSize && (
                  <div className="flex justify-center">
                    <Pagination
                      page={page}
                      total={Math.ceil(rows.length / pageSize)}
                      onChange={setPage}
                    />
                  </div>
                )}
              </>
            ) : (
              !error && (
                <p className="py-4 text-sm text-default-500">
                  {t("acquisition.downloadDestination.previewHint")}
                </p>
              )
            )}
          </>
        )}
      </div>
    </Modal>
  );
}
