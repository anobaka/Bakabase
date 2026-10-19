import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";
import { FiDownload, FiPlus, FiRefreshCw, FiSettings } from "react-icons/fi";

import AddBtDownloadModal from "./AddBtDownloadModal";
import BtSettingsPanel from "./BtSettingsPanel";
import { formatBtBytes } from "./format";

import { Button, Chip, Input, Progress, Select, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DownloadTaskDetailModal from "@/pages/downloader/components/TaskDetailModal";
import WorkflowRunDetailModal from "@/components/Workflow/WorkflowRunDetailModal";
import BApi from "@/sdk/BApi";
import { BtDownloadStatus } from "@/sdk/constants";

type Job = NonNullable<Awaited<ReturnType<typeof BApi.btDownloads.getBtDownloads>>["data"]>[number];

export default function BtDownloadsPage() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [searchParams, setSearchParams] = useSearchParams();
  const selectedId = searchParams.get("id");
  const [jobs, setJobs] = useState<Job[]>([]);
  const [status, setStatus] = useState("all");
  const [search, setSearch] = useState("");
  const [showSettings, setShowSettings] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [error, setError] = useState<string>();
  const [busyId, setBusyId] = useState<string>();
  const requestId = useRef(0);

  const load = useCallback(async () => {
    const request = ++requestId.current;
    try {
      const response = await BApi.btDownloads.getBtDownloads();
      if (request !== requestId.current) return;
      if (response.code) throw new Error(response.message);
      setJobs(response.data ?? []);
      setLoadFailed(false);
    } catch {
      if (request === requestId.current) setLoadFailed(true);
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
    const timer = setInterval(() => {
      if (!document.hidden) void load();
    }, 2000);
    return () => {
      clearInterval(timer);
      requestId.current++;
    };
  }, [load]);

  const act = async (job: Job, action: "stop" | "resume" | "retry" | "delete") => {
    setBusyId(job.id);
    setError(undefined);
    try {
      const result = await {
        stop: BApi.btDownloads.stopBtDownload,
        resume: BApi.btDownloads.resumeBtDownload,
        retry: BApi.btDownloads.retryBtDownload,
        delete: BApi.btDownloads.deleteBtDownload,
      }[action](job.id);
      if (result.code) throw new Error(result.message || t("bt.error.action"));
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : t("bt.error.action"));
    } finally {
      setBusyId(undefined);
    }
  };
  const filtered = jobs.filter(
    (job) =>
      (!selectedId || job.id === selectedId) &&
      (status === "all" || job.status === Number(status)) &&
      (!search ||
        `${job.name} ${job.outputDirectory}`
          .toLocaleLowerCase()
          .includes(search.toLocaleLowerCase())),
  );

  return (
    <div className="flex h-full flex-col gap-4 overflow-y-auto p-4 md:p-6">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="flex items-center gap-2 text-xl font-semibold">
            <FiDownload aria-hidden />
            {t("bt.title")}
          </h1>
          <p className="mt-1 text-sm text-default-500">{t("bt.description")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button as="a" href="#/downloader" variant="light">
            {t("downloader.page.title")}
          </Button>
          <Button
            startContent={<FiSettings aria-hidden />}
            variant="flat"
            aria-expanded={showSettings}
            onPress={() => setShowSettings((value) => !value)}
          >
            {t("bt.settings.title")}
          </Button>
          <Button
            color="primary"
            startContent={<FiPlus aria-hidden />}
            onPress={() => createPortal(AddBtDownloadModal, { onCreated: () => void load() })}
          >
            {t("bt.add.title")}
          </Button>
        </div>
      </div>
      {showSettings && <BtSettingsPanel />}
      <div className="flex flex-wrap items-center gap-2">
        <Select
          className="max-w-56"
          label={t("bt.filter.status")}
          selectedKeys={[status]}
          disallowEmptySelection
          dataSource={[
            { value: "all", label: t("bt.filter.all") },
            ...[1, 2, 3, 4, 5, 6].map((value) => ({
              value: String(value),
              label: t(`bt.status.${BtDownloadStatus[value]}`),
            })),
          ]}
          onSelectionChange={(keys) => setStatus(String(Array.from(keys)[0] ?? "all"))}
        />
        <Input
          className="min-w-48 flex-1"
          label={t("bt.filter.search")}
          value={search}
          onValueChange={setSearch}
        />
        <Button isIconOnly aria-label={t("bt.refresh")} variant="light" onPress={() => void load()}>
          <FiRefreshCw aria-hidden />
        </Button>
        {selectedId && (
          <Button size="sm" variant="flat" onPress={() => setSearchParams({})}>
            {t("bt.filter.showAll")}
          </Button>
        )}
      </div>
      {error && (
        <p className="rounded-lg bg-danger-50 p-3 text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      {loadFailed && (
        <p className="rounded-lg bg-danger-50 p-3 text-sm text-danger" role="alert">
          {t("bt.error.load")}
        </p>
      )}
      {loading ? (
        <Spinner />
      ) : filtered.length === 0 ? (
        <p className="py-12 text-center text-default-500">{t("bt.empty")}</p>
      ) : (
        <div className="space-y-3">
          {filtered.map((job) => (
            <article
              key={job.id}
              className="space-y-3 rounded-xl border border-default-200 bg-content1 p-4"
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <h2 className="min-w-0 break-all font-medium">{job.name || job.id}</h2>
                <Chip
                  size="sm"
                  variant="flat"
                  color={
                    job.status === BtDownloadStatus.Completed
                      ? "success"
                      : job.status === BtDownloadStatus.Failed
                        ? "danger"
                        : job.status === BtDownloadStatus.Running
                          ? "primary"
                          : "default"
                  }
                >
                  {t(`bt.status.${BtDownloadStatus[job.status]}`)}
                </Chip>
              </div>
              <Progress
                aria-label={t("bt.progress")}
                value={job.percentage ?? 0}
                isIndeterminate={job.status === BtDownloadStatus.Running && job.totalBytes == null}
                size="sm"
              />
              <div className="flex flex-wrap gap-x-5 gap-y-1 text-xs tabular-nums text-default-500">
                <span>
                  {formatBtBytes(job.completedBytes)} / {formatBtBytes(job.totalBytes)}
                  {job.percentage != null && ` · ${job.percentage.toFixed(1)}%`}
                </span>
                {job.status === BtDownloadStatus.Running && (
                  <span>{formatBtBytes(job.speedBytesPerSecond)}/s</span>
                )}
                {job.peers != null && <span>{t("bt.peers", { count: job.peers })}</span>}
                {job.stage && (
                  <span>{t(`bt.stage.${job.stage}`, { defaultValue: job.stage })}</span>
                )}
              </div>
              <p className="break-all text-xs text-default-500">{job.outputDirectory}</p>
              <div className="flex flex-wrap gap-3 text-xs text-default-400">
                {job.createdAt && (
                  <time dateTime={job.createdAt}>
                    {t("bt.createdAt", { time: new Date(job.createdAt).toLocaleString() })}
                  </time>
                )}
                {job.completedAt && (
                  <time dateTime={job.completedAt}>
                    {t("bt.completedAt", { time: new Date(job.completedAt).toLocaleString() })}
                  </time>
                )}
              </div>
              {job.error && <p className="break-words text-sm text-danger">{job.error}</p>}
              <div className="flex flex-wrap items-center gap-2">
                {(["stop", "resume", "retry", "delete"] as const)
                  .filter(
                    (action) =>
                      job[
                        (
                          {
                            stop: "canStop",
                            resume: "canResume",
                            retry: "canRetry",
                            delete: "canDelete",
                          } as const
                        )[action]
                      ],
                  )
                  .map((action) => (
                    <Button
                      key={action}
                      size="sm"
                      variant="flat"
                      color={action === "delete" ? "default" : "primary"}
                      isDisabled={busyId != null}
                      isLoading={busyId === job.id}
                      onPress={() => void act(job, action)}
                    >
                      {t(`bt.action.${action}`)}
                    </Button>
                  ))}
                {job.downloadTaskId != null && (
                  <Button
                    size="sm"
                    variant="light"
                    onPress={() =>
                      createPortal(DownloadTaskDetailModal, { id: job.downloadTaskId })
                    }
                  >
                    {t("bt.action.source")}
                  </Button>
                )}
                {job.workflowRunId != null && (
                  <Button
                    size="sm"
                    variant="light"
                    onPress={() =>
                      createPortal(WorkflowRunDetailModal, { runId: job.workflowRunId! })
                    }
                  >
                    {t("bt.action.workflow", { id: job.workflowRunId })}
                  </Button>
                )}
                {!job.downloadTaskId && !job.workflowRunId && (
                  <span className="text-xs text-default-400">{t("bt.source.manual")}</span>
                )}
              </div>
              {!!job.files?.length && (
                <details className="text-xs text-default-500">
                  <summary className="cursor-pointer">
                    {t("bt.files", { count: job.files.length })}
                  </summary>
                  <ul className="mt-2 space-y-1">
                    {job.files.map((file) => (
                      <li key={file} className="break-all">
                        {file}
                      </li>
                    ))}
                  </ul>
                </details>
              )}
            </article>
          ))}
        </div>
      )}
    </div>
  );
}
