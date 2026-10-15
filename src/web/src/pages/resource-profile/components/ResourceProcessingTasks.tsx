import type { ProfileSource } from "../sourceProfiles";
import type { ProcessingJob, ProcessingControl } from "../processingApi";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ReloadOutlined } from "@ant-design/icons";

import {
  processingApi,
  processingActions,
  processingIsActive,
  PROCESSING_JOBS_CHANGED,
} from "../processingApi";

import { Button, Chip } from "@/components/bakaui";
import { parseServerTime } from "@/core/serverTime";

const states = new Set([
  "Queued",
  "Running",
  "Paused",
  "Cancelled",
  "Completed",
  "Failed",
  "Conflict",
]);
const phases = new Set([
  "Scanning",
  "Preparing",
  "Processing",
  "DiscoveringFiles",
  "CheckingFiles",
  "FilesDiscovered",
  "EnhancementComputed",
  "Transferring",
  "Applying",
  "WaitingForDevice",
  "WaitingForWorker",
  "Completed",
]);

export default function ResourceProcessingTasks({ sources }: { sources: ProfileSource[] }) {
  const { t } = useTranslation();
  const loadError = t<string>("resourceProfile.processing.loadFailed");
  const [jobs, setJobs] = useState<ProcessingJob[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState<string>();
  const [pageCursor, setPageCursor] = useState<string>();
  const cursorRef = useRef<string>();
  const [hasMore, setHasMore] = useState(false);
  const requestRef = useRef<AbortController>();
  const controlRef = useRef(false);
  const load = useCallback(
    async (nextCursor?: string | null) => {
      requestRef.current?.abort();
      const controller = new AbortController();

      requestRef.current = controller;
      setLoading(true);
      try {
        const beforeId = nextCursor === null ? undefined : (nextCursor ?? cursorRef.current);
        const result = await processingApi.list(controller.signal, beforeId);

        if (controller.signal.aborted) return;
        if (!Array.isArray(result)) throw new Error(loadError);
        setJobs(result);
        cursorRef.current = beforeId;
        setPageCursor(beforeId);
        setHasMore(result.length === 30);
        setError(undefined);
      } catch (cause) {
        if (!controller.signal.aborted)
          setError(cause instanceof Error ? cause.message : loadError);
      } finally {
        if (!controller.signal.aborted) setLoading(false);
      }
    },
    [loadError],
  );

  useEffect(() => {
    void load();
    const refresh = () => {
      void load(null);
    };

    window.addEventListener(PROCESSING_JOBS_CHANGED, refresh);

    return () => {
      requestRef.current?.abort();
      window.removeEventListener(PROCESSING_JOBS_CHANGED, refresh);
    };
  }, [load]);
  const active = jobs.some(processingIsActive);

  useEffect(() => {
    if (!active) return;
    const timer = window.setInterval(() => {
      if (!controlRef.current) void load();
    }, 3000);

    return () => window.clearInterval(timer);
  }, [active, load]);
  const control = async (job: ProcessingJob, action: ProcessingControl) => {
    if (controlRef.current) return;
    controlRef.current = true;
    requestRef.current?.abort();
    setBusy(job.id);
    setError(undefined);
    try {
      const result = await processingApi.control(job, action);

      setJobs((previous) => previous.map((item) => (item.id === job.id ? result : item)));
    } catch (cause) {
      setError(
        cause instanceof Error
          ? cause.message
          : t<string>("resourceProfile.processing.controlFailed"),
      );
      // Reload current versions after a conflict, but keep the rejection visible.
      const message =
        cause instanceof Error
          ? cause.message
          : t<string>("resourceProfile.processing.controlFailed");

      await load();
      setError(message);
    } finally {
      setBusy(undefined);
      controlRef.current = false;
      setLoading(false);
    }
  };

  return (
    <section
      aria-label={t<string>("resourceProfile.processing.title")}
      className="rounded-xl border border-default-200 p-3"
    >
      <div className="flex items-center justify-between gap-3">
        <div>
          <h3 className="text-sm font-semibold">{t("resourceProfile.processing.title")}</h3>
          <p className="mt-1 text-xs text-default-500">{t("resourceProfile.processing.hint")}</p>
        </div>
        <Button
          isIconOnly
          aria-label={t<string>("common.action.refresh")}
          isDisabled={loading || !!busy}
          size="sm"
          variant="light"
          onPress={() => void load()}
        >
          <ReloadOutlined />
        </Button>
      </div>
      {error && (
        <p className="mt-2 text-xs text-danger" role="alert">
          {error}
        </p>
      )}
      {!jobs.length && !error && (
        <p className="py-3 text-xs text-default-400" role="status">
          {t(loading ? "common.state.loading" : "resourceProfile.processing.empty")}
        </p>
      )}
      <div className="mt-2 space-y-2">
        {jobs.map((job) => (
          <div key={job.id} className="rounded-lg bg-default-50 p-3" data-job-id={job.jobId}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="min-w-0 text-xs">
                <span className="font-medium">
                  {t(
                    job.kind === "PlayableFiles"
                      ? "resourceProfile.rule.playableFileOptions"
                      : "resourceProfile.rule.enhancerOptions",
                  )}
                </span>
                <span className="ml-2 text-default-500">
                  {sources.find((source) => source.deviceId === job.ownerId)?.name ?? job.ownerId}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <Chip
                  color={
                    job.state === "Failed" || job.state === "Conflict"
                      ? "danger"
                      : job.state === "Completed"
                        ? "success"
                        : "default"
                  }
                  size="sm"
                  variant="flat"
                >
                  {t(
                    `resourceProfile.processing.state.${states.has(job.state) ? job.state : "Unknown"}`,
                  )}
                </Chip>
                {processingActions(job).map((action) => (
                  <Button
                    key={action}
                    isDisabled={!!busy}
                    isLoading={busy === job.id}
                    size="sm"
                    variant="light"
                    onPress={() => void control(job, action)}
                  >
                    {t(`resourceProfile.processing.action.${action}`)}
                  </Button>
                ))}
              </div>
            </div>
            <div className="mt-2 flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
              <span>
                {processingIsActive(job) && job.cancelRequested
                  ? t("resourceProfile.processing.cancelling")
                  : processingIsActive(job) && job.pauseRequested
                    ? t("resourceProfile.processing.pausing")
                    : phases.has(job.phase ?? "")
                      ? t(`resourceProfile.processing.phase.${job.phase}`)
                      : ""}
              </span>
              <span>
                {job.totalCount == null
                  ? t("resourceProfile.processing.processed", { count: job.processedCount })
                  : `${job.processedCount.toLocaleString()} / ${job.totalCount.toLocaleString()}`}
              </span>
            </div>
            {job.currentStepProcessedCount != null && (
              <div className="mt-1 flex flex-wrap justify-between gap-2 text-xs text-default-500">
                <span>
                  {phases.has(job.currentStepPhase ?? "")
                    ? t(`resourceProfile.processing.phase.${job.currentStepPhase}`)
                    : t("resourceProfile.processing.currentResource")}
                </span>
                <span>
                  {t("resourceProfile.processing.currentResource")}
                  {": "}
                  {job.currentStepTotalCount == null
                    ? t("resourceProfile.processing.stepProcessed", {
                        count: job.currentStepProcessedCount,
                      })
                    : `${job.currentStepProcessedCount.toLocaleString()} / ${job.currentStepTotalCount.toLocaleString()}`}
                </span>
              </div>
            )}
            {processingIsActive(job) && (
              <progress
                aria-label={t<string>("resourceProfile.processing.progress")}
                className="mt-2 h-1 w-full accent-primary"
                max={job.totalCount && job.totalCount > 0 ? job.totalCount : undefined}
                value={job.totalCount && job.totalCount > 0 ? job.processedCount : undefined}
              />
            )}
            {job.state === "Conflict" && (
              <p className="mt-2 text-xs text-danger">{t("resourceProfile.processing.conflict")}</p>
            )}
            {job.error && (
              <details className="mt-2 text-xs text-default-500">
                <summary className="cursor-pointer">
                  {t("resourceProfile.processing.errorDetails")}
                </summary>
                <pre className="mt-2 max-h-32 overflow-auto whitespace-pre-wrap break-words">
                  {job.errorCode ? `${job.errorCode}\n` : ""}
                  {job.error}
                </pre>
              </details>
            )}
            <time
              className="mt-2 block text-[11px] text-default-400"
              dateTime={parseServerTime(job.updatedAtUtc)?.toISOString() ?? job.updatedAtUtc}
            >
              {parseServerTime(job.updatedAtUtc)?.toLocaleString() ?? job.updatedAtUtc}
            </time>
          </div>
        ))}
      </div>
      {pageCursor && (
        <Button
          className="mt-2"
          isDisabled={loading || !!busy}
          size="sm"
          variant="light"
          onPress={() => void load(null)}
        >
          {t("resourceProfile.processing.latest")}
        </Button>
      )}
      {hasMore && (
        <Button
          className="mt-2"
          isDisabled={loading || !!busy}
          size="sm"
          variant="light"
          onPress={() => void load(jobs.at(-1)?.id)}
        >
          {t("resourceProfile.processing.older")}
        </Button>
      )}
    </section>
  );
}
