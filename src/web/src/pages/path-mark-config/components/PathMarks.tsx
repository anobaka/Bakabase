"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { BTask } from "@/core/models/BTask";

import React, { useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineAim,
  AiOutlineWarning,
  AiOutlineCheck,
  AiOutlineClockCircle,
  AiOutlineDelete,
} from "react-icons/ai";

import MarkConfigModal from "./MarkConfigModal";
import MarkDescription from "./MarkDescription";

import {
  Chip,
  Button,
  Tooltip,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
  Spinner,
  CircularProgress,
} from "@/components/bakaui";
import { PathMarkType, PathMarkSyncStatus, BTaskStatus } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  ResourceDescription,
  PropertyDescription,
  MediaLibraryDescription,
} from "@/components/Chips/Terms";
import { useBTasksStore } from "@/stores/bTasks";

type Props = {
  entry: Entry;
  marks?: BakabaseAbstractionsModelsDomainPathMark[];
  onSaveMark?: (
    entry: Entry,
    mark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark,
  ) => void;
  onDeleteMark?: (entry: Entry, mark: BakabaseAbstractionsModelsDomainPathMark) => void;
  onTaskComplete?: () => void;
};

// Build task ID for a single mark sync
const buildMarkTaskId = (markId: number) => `SyncPathMark_${markId}`;

const getSyncStatusIcon = (status?: number) => {
  switch (status) {
    case PathMarkSyncStatus.Pending:
      return <AiOutlineClockCircle className="text-warning" />;
    case PathMarkSyncStatus.Syncing:
      return <Spinner className="w-3 h-3" size="sm" />;
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

const PathMarks = ({ entry, marks = [], onSaveMark, onDeleteMark, onTaskComplete }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  // Watch BTask store for individual mark sync tasks
  const bTasks = useBTasksStore((state) => state.tasks);

  // Track previous task statuses to detect completion
  const prevTaskStatusesRef = useRef<Map<string, BTaskStatus>>(new Map());

  // Detect task completion and trigger refresh
  useEffect(() => {
    if (!marks.length || !bTasks) return;

    const prevStatuses = prevTaskStatusesRef.current;
    let hasCompletedTask = false;

    for (const mark of marks) {
      if (!mark.id) continue;
      const taskId = buildMarkTaskId(mark.id);
      const task = bTasks.find((t) => t.id === taskId) as BTask | undefined;

      if (task) {
        const prevStatus = prevStatuses.get(taskId);

        // Check if task just completed (was running/not started, now completed)
        if (
          prevStatus !== undefined &&
          (prevStatus === BTaskStatus.Running || prevStatus === BTaskStatus.NotStarted) &&
          task.status === BTaskStatus.Completed
        ) {
          hasCompletedTask = true;
        }

        prevStatuses.set(taskId, task.status);
      }
    }

    if (hasCompletedTask && onTaskComplete) {
      onTaskComplete();
    }
  }, [bTasks, marks, onTaskComplete]);

  const resourceMarks = marks.filter(
    (m) => m.type === PathMarkType.Resource && m.syncStatus !== PathMarkSyncStatus.PendingDelete,
  );
  const propertyMarks = marks.filter(
    (m) => m.type === PathMarkType.Property && m.syncStatus !== PathMarkSyncStatus.PendingDelete,
  );
  const mediaLibraryMarks = marks.filter(
    (m) =>
      m.type === PathMarkType.MediaLibrary && m.syncStatus !== PathMarkSyncStatus.PendingDelete,
  );

  const handleAddMark = useCallback(
    (markType: PathMarkType) => {
      createPortal(MarkConfigModal, {
        markType,
        rootPath: entry.path,
        onSave: async (mark) => {
          if (onSaveMark) {
            onSaveMark(entry, mark);
          }
        },
      });
    },
    [createPortal, entry, onSaveMark],
  );

  const handleEditMark = useCallback(
    (mark: BakabaseAbstractionsModelsDomainPathMark) => {
      createPortal(MarkConfigModal, {
        mark,
        markType: mark.type!,
        rootPath: entry.path,
        onSave: async (newMark) => {
          if (onSaveMark) {
            onSaveMark(entry, newMark, mark);
          }
        },
      });
    },
    [createPortal, entry, onSaveMark],
  );

  const handleDeleteMark = useCallback(
    (mark: BakabaseAbstractionsModelsDomainPathMark) => {
      if (onDeleteMark) {
        onDeleteMark(entry, mark);
      }
    },
    [entry, onDeleteMark],
  );

  // Get task for a specific mark
  const getMarkTask = (markId: number): BTask | undefined => {
    return bTasks?.find((t) => t.id === buildMarkTaskId(markId)) as BTask | undefined;
  };

  const renderMarkChip = (
    mark: BakabaseAbstractionsModelsDomainPathMark,
    idx: number,
    type: "resource" | "property" | "mediaLibrary",
  ) => {
    const color = getMarkTypeColor(mark.type);
    const label = getMarkTypeLabel(mark.type, t);
    const isPendingDelete = mark.syncStatus === PathMarkSyncStatus.PendingDelete;

    // Check if there's an active task for this mark
    const markTask = getMarkTask(mark.id!);
    const isTaskRunning =
      markTask?.status === BTaskStatus.Running || markTask?.status === BTaskStatus.NotStarted;
    const taskProgress = markTask?.percentage || 0;

    return (
      <Tooltip
        key={`${type}-${idx}-${mark.id}`}
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
          </div>
        }
      >
        <Chip
          className={`cursor-pointer hover:opacity-80 ${isPendingDelete ? "line-through opacity-50" : ""}`}
          color={color as any}
          size="sm"
          variant="flat"
          onClick={() => !isPendingDelete && handleEditMark(mark)}
          onContextMenu={(e) => {
            e.preventDefault();
            if (!isPendingDelete) {
              handleDeleteMark(mark);
            }
          }}
        >
          <div className="flex items-center gap-1 text-xs">
            {/* Show task progress or sync status icon */}
            {isTaskRunning ? (
              <CircularProgress className="w-3 h-3" size="sm" value={taskProgress} />
            ) : (
              getSyncStatusIcon(mark.syncStatus)
            )}
            <span className="font-medium">
              [{label}#{mark.priority}]
            </span>
            <MarkDescription mark={mark} />
          </div>
        </Chip>
      </Tooltip>
    );
  };

  return (
    <div
      className="flex items-center gap-2 ml-2"
      onClick={(e) => e.stopPropagation()}
      onMouseDown={(e) => e.stopPropagation()}
    >
      {/* Add Mark Dropdown */}
      <div className="flex items-center gap-1">
        <Dropdown>
          <DropdownTrigger>
            <Button
              className="min-w-0 px-2 text-default-400 hover:text-primary transition-colors"
              size="sm"
              startContent={<AiOutlineAim className="text-lg" />}
              variant="light"
            >
              {t("Add Mark")}
            </Button>
          </DropdownTrigger>
          <DropdownMenu
            aria-label="Mark type selection"
            className="max-w-[600px]"
            onAction={(key) => handleAddMark(Number(key) as PathMarkType)}
          >
            <DropdownItem
              key={PathMarkType.Resource}
              className="text-success"
              description={<ResourceDescription />}
            >
              {t("Resource")}
            </DropdownItem>
            <DropdownItem
              key={PathMarkType.Property}
              className="text-primary"
              description={<PropertyDescription />}
            >
              {t("Property")}
            </DropdownItem>
            <DropdownItem
              key={PathMarkType.MediaLibrary}
              className="text-secondary"
              description={<MediaLibraryDescription />}
            >
              {t("Media Library")}
            </DropdownItem>
          </DropdownMenu>
        </Dropdown>
      </div>

      {/* Display Marks - Click to Edit */}
      <div className="flex items-center gap-1 flex-wrap">
        {resourceMarks.map((mark, idx) => renderMarkChip(mark, idx, "resource"))}
        {propertyMarks.map((mark, idx) => renderMarkChip(mark, idx, "property"))}
        {mediaLibraryMarks.map((mark, idx) => renderMarkChip(mark, idx, "mediaLibrary"))}
      </div>
    </div>
  );
};

PathMarks.displayName = "PathRuleMarks";

export default PathMarks;
