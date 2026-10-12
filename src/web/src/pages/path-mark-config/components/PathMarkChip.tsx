"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineWarning,
  AiOutlineCheck,
  AiOutlineClockCircle,
  AiOutlineDelete,
  AiOutlineSync,
} from "react-icons/ai";
import { AiOutlineFieldTime } from "react-icons/ai";

import MarkDescription from "./MarkDescription";
import {
  getPathMarkSyncProgress,
  getSyncTaskForMark,
  isPathMarkSyncTaskActive,
} from "./pathMarkSyncTask";

import {
  Chip,
  Tooltip,
  CircularProgress,
  formatDuration,
  Checkbox,
  Button,
  toast,
} from "@/components/bakaui";
import { PathMarkType, PathMarkSyncStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { usePathMarksStore } from "@/stores/pathMarks";
import BApi from "@/sdk/BApi";

export interface PathMarkChipProps {
  mark: BakabaseAbstractionsModelsDomainPathMark;
  onClick?: () => void;
  onContextMenu?: () => void;
  selectable?: boolean;
  selected?: boolean;
  onSelectionChange?: (selected: boolean) => void;
  /** Allow long descriptions to wrap in bounded confirmation cards. */
  wrapContent?: boolean;
}

const getSyncStatusIcon = (status?: number, isTaskRunning?: boolean, taskProgress?: number) => {
  // If task is running, show CircularProgress
  if (isTaskRunning) {
    return (
      <CircularProgress
        aria-label="Syncing"
        classNames={{
          svg: "w-3.5 h-3.5",
        }}
        isIndeterminate={taskProgress == null}
        size="sm"
        value={taskProgress}
      />
    );
  }

  switch (status) {
    case PathMarkSyncStatus.Pending:
      return <AiOutlineClockCircle className="text-warning" />;
    case PathMarkSyncStatus.Syncing:
      return (
        <CircularProgress
          isIndeterminate
          aria-label="Syncing"
          classNames={{
            svg: "w-3.5 h-3.5",
          }}
          size="sm"
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
      return translate("pathMarkConfig.status.pending");
    case PathMarkSyncStatus.Syncing:
      return translate("pathMarkConfig.status.syncing");
    case PathMarkSyncStatus.Synced:
      return translate("pathMarkConfig.status.synced");
    case PathMarkSyncStatus.Failed:
      return translate("pathMarkConfig.status.failed");
    case PathMarkSyncStatus.PendingDelete:
      return translate("pathMarkConfig.status.pendingDelete");
    default:
      return "";
  }
};

const getMarkTypeLabel = (type?: number, t?: (key: string) => string) => {
  const translate = t || ((key: string) => key);

  switch (type) {
    case PathMarkType.Resource:
      return translate("common.label.resource");
    case PathMarkType.Property:
      return translate("common.label.property");
    case PathMarkType.MediaLibrary:
      return translate("common.label.mediaLibrary");
    default:
      return translate("pathMarkConfig.status.unknown");
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

const PathMarkChip = ({
  mark: propMark,
  onClick,
  onContextMenu,
  selectable,
  selected,
  onSelectionChange,
  wrapContent = false,
}: PathMarkChipProps) => {
  const { t } = useTranslation();
  const [syncing, setSyncing] = useState(false);

  // Resource marks use resource indexing; property/library marks use the effects pipeline.
  const bTasks = useBTasksStore((state) => state.tasks);

  // Get the latest mark state from store (updated via SignalR)
  const storeMark = usePathMarksStore((state) =>
    propMark.id != null ? state.marks.get(propMark.id) : undefined,
  );

  // Use store mark if available (has latest syncStatus), otherwise use prop mark
  const mark = storeMark ?? propMark;

  const color = getMarkTypeColor(mark.type);
  const label = getMarkTypeLabel(mark.type, t);
  const isPendingDelete = mark.syncStatus === PathMarkSyncStatus.PendingDelete;

  const pathMarkSyncTask = getSyncTaskForMark(bTasks, mark.type);
  const isMarkSyncing = mark.syncStatus === PathMarkSyncStatus.Syncing;
  const isTaskRunning = isMarkSyncing && isPathMarkSyncTaskActive(pathMarkSyncTask);
  const taskProgress = getPathMarkSyncProgress(pathMarkSyncTask);

  // Sync this mark immediately
  const handleSyncMark = useCallback(async () => {
    if (!mark.id || syncing || isMarkSyncing) return;

    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSync([mark.id]);
      toast.success(t("pathMarkConfig.success.syncStarted"));
    } catch (err) {
      toast.danger(t("pathMarkConfig.error.syncFailed"));
    } finally {
      setSyncing(false);
    }
  }, [mark.id, syncing, isMarkSyncing, t]);

  const chipContent = (
    <Chip
      className={`cursor-pointer hover:opacity-80 ${isPendingDelete ? "line-through opacity-50" : ""} ${wrapContent ? "h-auto min-h-6 min-w-0 max-w-full whitespace-normal py-1" : ""}`}
      classNames={wrapContent ? { content: "min-w-0 whitespace-normal" } : undefined}
      color={color as any}
      size="sm"
      variant="flat"
      onClick={() => {
        if (selectable) {
          onSelectionChange?.(!selected);
        } else if (!isPendingDelete && onClick) {
          onClick();
        }
      }}
      onContextMenu={(e) => {
        e.preventDefault();
        if (!selectable && !isPendingDelete && onContextMenu) {
          onContextMenu();
        }
      }}
    >
      <div
        className={`flex gap-1 text-xs ${wrapContent ? "min-w-0 max-w-full items-start [&>svg]:mt-0.5 [&>svg]:shrink-0" : "items-center"}`}
      >
        {getSyncStatusIcon(mark.syncStatus, isTaskRunning, taskProgress)}
        <MarkDescription
          className={wrapContent ? "min-w-0 whitespace-normal [overflow-wrap:anywhere]" : undefined}
          label={label}
          mark={mark}
          priority={mark.priority}
        />
      </div>
    </Chip>
  );

  if (selectable) {
    return (
      <div className="flex items-center gap-1">
        <Checkbox isSelected={selected} size="sm" onValueChange={onSelectionChange} />
        {chipContent}
      </div>
    );
  }

  // Can sync if mark has an id and is not already syncing/running
  const canSync = mark.id && !syncing && !isMarkSyncing && !isPendingDelete;

  return (
    <Tooltip
      content={
        <div className="flex flex-col gap-1">
          <span>{t("pathMarkConfig.tip.clickToEditRightClickDelete")}</span>
          {mark.syncStatus !== undefined && (
            <span className="text-xs opacity-80">
              {getSyncStatusTooltip(mark.syncStatus, t)}
              {mark.syncError && `: ${mark.syncError}`}
            </span>
          )}
          {isTaskRunning && (
            <span className="text-xs opacity-80">
              {taskProgress == null
                ? t("pathMarkConfig.status.syncing")
                : t("pathMarkConfig.status.overallSyncProgress", {
                    progress: Math.round(taskProgress),
                  })}
            </span>
          )}
          {mark.expiresInSeconds != null && mark.expiresInSeconds > 0 && (
            <span className="text-xs opacity-80 flex items-center gap-1">
              <AiOutlineFieldTime className="text-base" />
              {t("pathMarkConfig.tip.recheckAfter")} {formatDuration(mark.expiresInSeconds, t)}
            </span>
          )}
          {canSync && (
            <Button
              className="mt-1"
              color="success"
              isLoading={syncing}
              size="sm"
              startContent={!syncing && <AiOutlineSync className="text-base" />}
              variant="flat"
              onPress={handleSyncMark}
            >
              {t("pathMarkConfig.action.syncNow")}
            </Button>
          )}
        </div>
      }
      delay={500}
    >
      {chipContent}
    </Tooltip>
  );
};

PathMarkChip.displayName = "PathMarkChip";

export default PathMarkChip;
