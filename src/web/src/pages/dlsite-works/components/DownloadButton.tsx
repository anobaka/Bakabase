import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, CircularProgress, Tooltip } from "@heroui/react";
import { AiOutlineDownload, AiOutlineStop } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";
import type { DLsiteWork } from "../types";
import { DOWNLOAD_TASK_ID_PREFIX } from "../types";

/** Standalone download button - subscribes to BTask store directly to bypass Table memoization */
export function DownloadButton({ work, hasDownloadDir, onSetWorksLocalPath }: {
  work: DLsiteWork;
  hasDownloadDir: boolean;
  onSetWorksLocalPath: (workId: string, localPath: string) => void;
}) {
  const { t } = useTranslation();
  const downloadTask = useBTasksStore((s) => s.tasks.find((task) => task.id === `${DOWNLOAD_TASK_ID_PREFIX}${work.workId}`));
  const [isStarting, setIsStarting] = useState(false);

  // Clear optimistic state once BTask arrives
  useEffect(() => {
    if (isStarting && downloadTask) {
      setIsStarting(false);
    }
  }, [isStarting, downloadTask]);

  const isDownloading = isStarting || downloadTask?.status === BTaskStatus.Running || downloadTask?.status === BTaskStatus.NotStarted;

  const handleDownload = async () => {
    setIsStarting(true);
    try {
      const rsp = await BApi.dlsiteWork.downloadDLsiteWork(work.workId);
      if (rsp.code) {
        setIsStarting(false);
      } else if (rsp.data) {
        onSetWorksLocalPath(work.workId, rsp.data as string);
      }
    } catch {
      setIsStarting(false);
    }
  };

  const handleStop = async () => {
    const taskId = `${DOWNLOAD_TASK_ID_PREFIX}${work.workId}`;
    await BApi.backgroundTask.stopBackgroundTask(taskId);
  };

  if (isDownloading) {
    return (
      <Tooltip content={
        <div className="text-center">
          <div>{downloadTask?.process || t("resourceSource.dlsite.action.downloading")}</div>
          <div className="text-xs mt-1">{t("resourceSource.dlsite.action.stopDownload")}</div>
        </div>
      }>
        <div
          className="relative w-8 h-8 flex items-center justify-center cursor-pointer group"
          onClick={handleStop}
        >
          <CircularProgress
            className="group-hover:hidden"
            color="primary"
            showValueLabel
            size="sm"
            value={downloadTask?.percentage ?? 0}
          />
          <AiOutlineStop className="text-lg text-danger hidden group-hover:block" />
        </div>
      </Tooltip>
    );
  }

  if (work.isDownloaded) return null;

  return (
    <Tooltip content={hasDownloadDir ? t("resourceSource.dlsite.action.download") : t("resourceSource.dlsite.action.setDownloadDir")}>
      <span>
        <Button
          color="warning"
          isDisabled={!hasDownloadDir}
          isIconOnly
          size="sm"
          variant="light"
          onPress={handleDownload}
        >
          <AiOutlineDownload className="text-lg" />
        </Button>
      </span>
    </Tooltip>
  );
}
