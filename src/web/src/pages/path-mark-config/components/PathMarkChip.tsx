"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { BTask } from "@/core/models/BTask";

import { useTranslation } from "react-i18next";
import {
  AiOutlineWarning,
  AiOutlineCheck,
  AiOutlineClockCircle,
  AiOutlineDelete,
} from "react-icons/ai";

import MarkDescription from "./MarkDescription";

import { Chip, Tooltip, CircularProgress, formatDuration } from "@/components/bakaui";
import { PathMarkType, PathMarkSyncStatus, BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { AiOutlineFieldTime } from "react-icons/ai";

export interface PathMarkChipProps {
  mark: BakabaseAbstractionsModelsDomainPathMark;
  onClick?: () => void;
  onContextMenu?: () => void;
}

// Build task ID for a single mark sync
const buildMarkTaskId = (markId: number) => `SyncPathMark_${markId}`;

const getSyncStatusIcon = (status?: number, isTaskRunning?: boolean, taskProgress?: number) => {
  // If task is running, show CircularProgress
  if (isTaskRunning) {
    return (
      <CircularProgress
        aria-label="Syncing"
        classNames={{
          svg: "w-3.5 h-3.5",
        }}
        size="sm"
        value={taskProgress || 0}
      />
    );
  }

  switch (status) {
    case PathMarkSyncStatus.Pending:
      return <AiOutlineClockCircle className="text-warning" />;
    case PathMarkSyncStatus.Syncing:
      return (
        <CircularProgress
          aria-label="Syncing"
          classNames={{
            svg: "w-3.5 h-3.5",
          }}
          size="sm"
          isIndeterminate
        />
      );
    case PathMarkSyncStatus.Synced:
      return <AiOutlineCheck className="text-success" />;
    case PathMarkSyncStatus.Failed:
      return <AiOutlineWarning className="text-danger" />;
    case PathMarkSyncStatus.PendingDelete:
      return <AiOutlineDelete className="text-danger" />;
    default:
      return null;
  }
};

const getSyncStatusTooltip = (status?: number, t?: (key: string) => string) => {
  const translate = t || ((key: string) => key);

  switch (status) {
    case PathMarkSyncStatus.Pending:
      return translate("Pending Sync");
    case PathMarkSyncStatus.Syncing:
      return translate("Syncing...");
    case PathMarkSyncStatus.Synced:
      return translate("Synced");
    case PathMarkSyncStatus.Failed:
      return translate("Sync Failed");
    case PathMarkSyncStatus.PendingDelete:
      return translate("Pending Delete");
    default:
      return "";
  }
};

const getMarkTypeLabel = (type?: number, t?: (key: string) => string) => {
  const translate = t || ((key: string) => key);

  switch (type) {
    case PathMarkType.Resource:
      return translate("Resource");
    case PathMarkType.Property:
      return translate("Property");
    case PathMarkType.MediaLibrary:
      return translate("Media Library");
    default:
      return translate("Unknown");
  }
};

const getMarkTypeColor = (type?: number) => {
  switch (type) {
    case PathMarkType.Resource:
      return "success";
    case PathMarkType.Property:
      return "primary";
    case PathMarkType.MediaLibrary:
      return "secondary";
    default:
      return "default";
  }
};

const PathMarkChip = ({ mark, onClick, onContextMenu }: PathMarkChipProps) => {
  const { t } = useTranslation();

  // Watch BTask store for this mark's sync task
  const bTasks = useBTasksStore((state) => state.tasks);

  const color = getMarkTypeColor(mark.type);
  const label = getMarkTypeLabel(mark.type, t);
  const isPendingDelete = mark.syncStatus === PathMarkSyncStatus.PendingDelete;

  // Check if there's an active task for this mark
  const markTask = mark.id
    ? (bTasks?.find((task) => task.id === buildMarkTaskId(mark.id!)) as BTask | undefined)
    : undefined;
  const isTaskRunning =
    markTask?.status === BTaskStatus.Running || markTask?.status === BTaskStatus.NotStarted;
  const taskProgress = markTask?.percentage || 0;

  return (
    <Tooltip
      content={
        <div className="flex flex-col gap-1">
          <span>{t("Click to edit, right-click to delete")}</span>
          {mark.syncStatus !== undefined && (
            <span className="text-xs opacity-80">
              {getSyncStatusTooltip(mark.syncStatus, t)}
              {mark.syncError && `: ${mark.syncError}`}
            </span>
          )}
          {isTaskRunning && (
            <span className="text-xs opacity-80">
              {t("Syncing")}: {Math.round(taskProgress)}%
            </span>
          )}
          {mark.expiresInSeconds != null && mark.expiresInSeconds > 0 && (
            <span className="text-xs opacity-80 flex items-center gap-1">
              <AiOutlineFieldTime />
              {t("Re-check after")} {formatDuration(mark.expiresInSeconds, t)}
            </span>
          )}
        </div>
      }
    >
      <Chip
        className={`cursor-pointer hover:opacity-80 ${isPendingDelete ? "line-through opacity-50" : ""}`}
        color={color as any}
        size="sm"
        variant="flat"
        onClick={() => {
          if (!isPendingDelete && onClick) {
            onClick();
          }
        }}
        onContextMenu={(e) => {
          e.preventDefault();
          if (!isPendingDelete && onContextMenu) {
            onContextMenu();
          }
        }}
      >
        <div className="flex items-center gap-1 text-xs">
          {getSyncStatusIcon(mark.syncStatus, isTaskRunning, taskProgress)}
          <MarkDescription mark={mark} label={label} priority={mark.priority} />
        </div>
      </Chip>
    </Tooltip>
  );
};

PathMarkChip.displayName = "PathMarkChip";

export default PathMarkChip;
