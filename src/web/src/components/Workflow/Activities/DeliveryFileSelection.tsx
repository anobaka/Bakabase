import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Checkbox, Pagination, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type ManifestPage = NonNullable<Awaited<ReturnType<typeof BApi.workflow.getWorkflowArtifactFiles>>["data"]>;
const pageSize = 50;
const maxExcludedFiles = 256;

interface Props {
  runId: number;
  artifactId: string;
  submitting: boolean;
  onCancel: () => void;
  onConfirm: (excludedFiles: string[]) => void;
}

export default function DeliveryFileSelection({ runId, artifactId, submitting, onCancel, onConfirm }: Props) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [result, setResult] = useState<ManifestPage>();
  const [failed, setFailed] = useState(false);
  const [excluded, setExcluded] = useState<Set<string>>(() => new Set());
  useEffect(() => {
    let current = true;
    setResult(undefined);
    setFailed(false);
    void BApi.workflow.getWorkflowArtifactFiles(runId, artifactId, { pageIndex: page, pageSize }, { showErrorToast: false })
      .then(({ code, data }) => {
        if (!current) return;
        if (code || !data || data.artifactId !== artifactId || data.pageIndex !== page || data.state !== "available")
          throw new Error("MANIFEST_UNAVAILABLE");
        setResult(data);
      }).catch(() => { if (current) setFailed(true); });
    return () => { current = false; };
  }, [runId, artifactId, page, retry]);
  const manifest = result?.pageIndex === page ? result : undefined;
  const selectedCount = (manifest?.fileCount ?? 0) - excluded.size;
  return (
    <div className="min-w-0 space-y-2">
      <p className="text-sm">{t("cloudDownload.deliveryReview.selectionHint")}</p>
      {failed ? (
        <div role="alert" className="text-sm text-warning-600">
          {t("workflow.metadata.filesUnavailable")}
          <Button size="sm" variant="light" onPress={() => setRetry(n => n + 1)}>{t("common.action.retry")}</Button>
        </div>
      ) : !manifest ? <Spinner size="sm" /> : (
        <>
          <p aria-live="polite" className="text-sm">
            {t("cloudDownload.deliveryReview.selectionCount", { selected: selectedCount, total: manifest.fileCount })}
          </p>
          <div className="max-h-72 space-y-1 overflow-y-auto" role="group" aria-label={t("workflow.metadata.fileList")}>
            {manifest.entries.map(entry => (
              <div key={entry.relativePath}>
                <Checkbox
                  size="sm"
                  isSelected={!excluded.has(entry.relativePath)}
                  isDisabled={submitting || (!excluded.has(entry.relativePath) && (excluded.size >= maxExcludedFiles || entry.relativePath.length > 4096))}
                  onValueChange={checked => setExcluded(previous => {
                    const next = new Set(previous);
                    if (checked) next.delete(entry.relativePath);
                    else if (next.size < maxExcludedFiles) next.add(entry.relativePath);
                    return next;
                  })}
                ><span className="break-all">{entry.relativePath}</span></Checkbox>
              </div>
            ))}
          </div>
          {excluded.size === maxExcludedFiles && <p className="text-sm text-warning-600">
            {t("cloudDownload.deliveryReview.exclusionLimit", { count: maxExcludedFiles })}
          </p>}
          {manifest.fileCount > pageSize && <Pagination size="sm" page={page} total={Math.ceil(manifest.fileCount / pageSize)}
            onChange={setPage} isDisabled={submitting} />}
        </>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <Button size="sm" variant="light" isDisabled={submitting} onPress={onCancel}>{t("common.action.cancel")}</Button>
        <Button size="sm" color="primary" isLoading={submitting}
          isDisabled={submitting || !manifest || failed || selectedCount < 1}
          onPress={() => onConfirm(Array.from(excluded))}>{t("cloudDownload.deliveryReview.confirmSelection")}</Button>
      </div>
    </div>
  );
}
