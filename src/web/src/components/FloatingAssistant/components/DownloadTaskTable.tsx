import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import {
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  RocketOutlined,
} from "@ant-design/icons";
import {
  Chip,
  Progress,
  Tooltip,
} from "@/components/bakaui";
import { DownloadTaskStatus } from "@/sdk/constants";
import type { DownloadTask } from "@/core/models/DownloadTask";

interface DownloadTaskTableProps {
  tasks: DownloadTask[];
}

const VISIBLE_STATUSES = new Set([
  DownloadTaskStatus.InQueue,
  DownloadTaskStatus.Starting,
  DownloadTaskStatus.Downloading,
  DownloadTaskStatus.Failed,
]);

const statusOrder: Record<number, number> = {
  [DownloadTaskStatus.Downloading]: 0,
  [DownloadTaskStatus.Starting]: 1,
  [DownloadTaskStatus.InQueue]: 2,
  [DownloadTaskStatus.Failed]: 3,
};

function DownloadStatusIcon({ status }: { status: DownloadTaskStatus }) {
  switch (status) {
    case DownloadTaskStatus.InQueue:
      return (
        <Chip color="warning" size="sm" variant="light">
          <ClockCircleOutlined className="text-base" />
        </Chip>
      );
    case DownloadTaskStatus.Starting:
      return (
        <Chip color="secondary" size="sm" variant="light">
          <RocketOutlined className="text-base" />
        </Chip>
      );
    case DownloadTaskStatus.Downloading:
      return (
        <Chip size="sm" variant="light">
          <LoadingOutlined className="text-base" />
        </Chip>
      );
    case DownloadTaskStatus.Failed:
      return (
        <Chip color="danger" size="sm" variant="light">
          <ExclamationCircleOutlined className="text-base" />
        </Chip>
      );
    default:
      return null;
  }
}

function statusLabel(status: DownloadTaskStatus, t: (key: string) => string): string {
  switch (status) {
    case DownloadTaskStatus.InQueue:
      return t("floatingAssistant.downloadStatus.inQueue");
    case DownloadTaskStatus.Starting:
      return t("floatingAssistant.downloadStatus.starting");
    case DownloadTaskStatus.Downloading:
      return t("floatingAssistant.downloadStatus.downloading");
    case DownloadTaskStatus.Failed:
      return t("floatingAssistant.downloadStatus.failed");
    default:
      return "";
  }
}

export function DownloadTaskTable({ tasks }: DownloadTaskTableProps) {
  const { t } = useTranslation();

  const visibleTasks = useMemo(() => {
    return tasks
      .filter((task) => VISIBLE_STATUSES.has(task.status))
      .sort((a, b) => (statusOrder[a.status] ?? 99) - (statusOrder[b.status] ?? 99));
  }, [tasks]);

  if (visibleTasks.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-1">
      <div className="text-xs font-semibold text-default-500 px-1">
        {t("floatingAssistant.downloadTasks")} ({visibleTasks.length})
      </div>
      {visibleTasks.map((task) => (
        <div
          key={task.id}
          className={`flex flex-col gap-1 px-2 py-1.5 rounded-md ${
            task.status === DownloadTaskStatus.Failed
              ? "bg-danger-50"
              : "bg-default-100"
          }`}
        >
          <div className="flex items-center gap-1.5">
            <DownloadStatusIcon status={task.status} />
            <span className="text-sm truncate max-w-[200px]">
              {task.displayName || task.name}
            </span>
            <span className="text-xs text-default-400 ml-auto whitespace-nowrap">
              {statusLabel(task.status, t)}
            </span>
          </div>
          {task.status === DownloadTaskStatus.Downloading && (
            <div className="relative min-w-[60px]">
              <Progress
                color="primary"
                size="sm"
                value={task.progress}
                className="task-progress-animated"
                classNames={{
                  indicator: "progress-bar-indicator",
                }}
              />
              <div className="absolute top-0 left-0 flex items-center justify-center w-full h-full text-xs">
                {task.progress}%
              </div>
            </div>
          )}
          {task.status === DownloadTaskStatus.Failed && task.message && (
            <Tooltip content={task.message} placement="top" color="danger">
              <span className="text-xs text-danger truncate max-w-[280px] cursor-help">
                {task.message}
              </span>
            </Tooltip>
          )}
        </div>
      ))}
    </div>
  );
}
