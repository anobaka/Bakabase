import type { BakabaseServiceModelsViewDownloadResultViewModel as DownloadResult } from "@/sdk/Api";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { FiCheckCircle, FiFile, FiGitBranch, FiRefreshCw } from "react-icons/fi";

import { Button, Chip, Spinner } from "@/components/bakaui";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import BApi from "@/sdk/BApi";
import { DownloadResultKind, WorkflowRunStatus } from "@/sdk/constants";

const statusColor = (
  status: WorkflowRunStatus,
): "default" | "primary" | "success" | "danger" | "warning" => {
  switch (status) {
    case WorkflowRunStatus.Running:
      return "primary";
    case WorkflowRunStatus.Success:
      return "success";
    case WorkflowRunStatus.Failed:
      return "danger";
    case WorkflowRunStatus.Waiting:
    case WorkflowRunStatus.Interrupted:
    case WorkflowRunStatus.Cancelled:
      return "warning";
    default:
      return "default";
  }
};

export default function DownloadResultsPanel({ taskId }: { taskId: number }) {
  const { t } = useTranslation();
  const [results, setResults] = useState<DownloadResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [retryingId, setRetryingId] = useState<number>();
  const [retryFailedId, setRetryFailedId] = useState<number>();
  const [selectedResult, setSelectedResult] = useState<DownloadResult>();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await BApi.downloader.getDownloadResults({ taskId });

      setResults(response.data ?? []);
      setLoadFailed(false);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, [taskId]);

  useEffect(() => {
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 5000);

    return () => clearInterval(timer);
  }, [load]);

  const retry = async (resultId: number) => {
    setRetryingId(resultId);
    setRetryFailedId(undefined);
    try {
      const response = await BApi.downloader.retryDownloadResultWorkflow(resultId);

      if (response.code) throw new Error(response.message);
      await load();
    } catch {
      setRetryFailedId(resultId);
    } finally {
      setRetryingId(undefined);
    }
  };

  const name = (result: DownloadResult) =>
    workflowLabel(
      {
        name:
          result.workflowName ??
          t("downloader.results.workflowFallback", { id: result.workflowDefinitionId }),
        isBuiltin: result.workflowIsBuiltin,
      },
      t,
    );

  return (
    <section className="mt-3 space-y-3 border-t border-default-100 pt-4">
      <div className="flex items-center justify-between gap-2">
        <h3 className="text-sm font-medium">
          {t("downloader.results.title")}
          {results.length > 0 && (
            <span className="ml-2 text-xs text-default-400">{results.length}</span>
          )}
        </h3>
        <Button
          isIconOnly
          aria-label={t("downloader.results.refresh")}
          isDisabled={loading}
          size="sm"
          variant="light"
          onPress={() => void load()}
        >
          <FiRefreshCw aria-hidden size={16} />
        </Button>
      </div>
      <p className="text-xs leading-relaxed text-default-500">
        {t("downloader.results.description")}
      </p>
      {loadFailed && (
        <p className="text-xs text-danger" role="alert">
          {t("downloader.results.loadFailed")}
        </p>
      )}
      {loading && results.length === 0 ? (
        <Spinner size="sm" />
      ) : results.length === 0 ? (
        <p className="rounded-lg bg-default-50 p-3 text-xs text-default-500">
          {t("downloader.results.empty")}
        </p>
      ) : (
        <div className="max-h-96 space-y-2 overflow-y-auto pr-1">
          {results.map((result) => (
            <article key={result.id} className="space-y-2 rounded-xl bg-default-50 p-3">
              <div className="flex flex-wrap items-center gap-2">
                {result.contentsReady ? (
                  <FiCheckCircle aria-hidden className="text-success" />
                ) : (
                  <FiFile aria-hidden className="text-default-500" />
                )}
                <span className="min-w-0 flex-1 break-words text-sm font-medium">
                  {result.name || `#${result.id}`}
                </span>
                <Chip color={result.contentsReady ? "success" : "warning"} size="sm" variant="flat">
                  {t(
                    result.contentsReady
                      ? "downloader.results.contentsReady"
                      : result.kind === DownloadResultKind.TorrentMetadata
                        ? "downloader.results.torrentReady"
                        : "downloader.results.filesUnavailable",
                  )}
                </Chip>
              </div>
              {result.contentsReady && result.contentsDirectory && (
                <p className="break-all text-xs text-default-500">{result.contentsDirectory}</p>
              )}
              {result.workflowDefinitionId != null ? (
                <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
                  <FiGitBranch aria-hidden />
                  <span>{name(result)}</span>
                  {result.workflowStatus != null ? (
                    <Chip color={statusColor(result.workflowStatus)} size="sm" variant="flat">
                      {t(`workflow.runs.status.${WorkflowRunStatus[result.workflowStatus]}`)}
                    </Chip>
                  ) : (
                    !result.filterDidNotMatch &&
                    !result.error && <span>{t("downloader.results.queued")}</span>
                  )}
                </div>
              ) : (
                <p className="text-xs text-default-500">{t("downloader.results.saveOnly")}</p>
              )}
              {result.filterDidNotMatch && (
                <p className="text-xs text-default-500">{t("downloader.results.filtered")}</p>
              )}
              {result.acquisitionTaskId != null && (
                <p className="text-xs text-default-500">
                  {t("downloader.results.acquisitionOwned", { id: result.acquisitionTaskId })}
                </p>
              )}
              {result.error && <p className="break-words text-xs text-danger">{result.error}</p>}
              {retryFailedId === result.id && (
                <p className="text-xs text-danger" role="alert">
                  {t("downloader.results.retryFailed")}
                </p>
              )}
              <div className="flex flex-wrap gap-1">
                {result.workflowDefinitionId != null && result.workflowRunId != null && (
                  <Button size="sm" variant="light" onPress={() => setSelectedResult(result)}>
                    {t("downloader.results.viewRun", { id: result.workflowRunId })}
                  </Button>
                )}
                {result.canRetry && (
                  <Button
                    color="primary"
                    isDisabled={retryingId != null}
                    isLoading={retryingId === result.id}
                    size="sm"
                    startContent={<FiRefreshCw aria-hidden size={14} />}
                    variant="flat"
                    onPress={() => void retry(result.id)}
                  >
                    {t("downloader.results.retry")}
                  </Button>
                )}
                {result.acquisitionTaskId != null && (
                  <Button
                    as="a"
                    href="#/acquisitions?tab=all"
                    rel="noopener noreferrer"
                    size="sm"
                    target="_blank"
                    variant="light"
                  >
                    {t("downloader.results.viewAcquisition")}
                  </Button>
                )}
              </div>
            </article>
          ))}
        </div>
      )}
      {selectedResult?.workflowDefinitionId != null && (
        <WorkflowRunsDrawer
          isOpen
          workflowDefinitionId={selectedResult.workflowDefinitionId}
          workflowName={`${name(selectedResult)} · #${selectedResult.workflowRunId}`}
          onClose={() => setSelectedResult(undefined)}
        />
      )}
    </section>
  );
}
