"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { BTask } from "@/core/models/BTask";

import React, { useCallback, useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineAim, AiOutlineCopy } from "react-icons/ai";

import MarkConfigModal from "./MarkConfigModal";
import PathMarkChip from "./PathMarkChip";

import {
  Button,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
} from "@/components/bakaui";
import { PathMarkType, BTaskStatus } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  ResourceDescription,
  PropertyDescription,
  MediaLibraryDescription,
} from "@/components/Chips/Terms";
import { useBTasksStore } from "@/stores/bTasks";
import { useCopyMarksStore } from "@/stores/copyMarks";

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

const PathMarks = ({ entry, marks = [], onSaveMark, onDeleteMark, onTaskComplete }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  // Watch BTask store for individual mark sync tasks
  const bTasks = useBTasksStore((state) => state.tasks);

  // Copy marks store
  const {
    copyModeEntryPath,
    selectedMarkIds,
    enterCopyMode,
    exitCopyMode,
    toggleMarkSelection,
    selectAllMarks,
    confirmSelection,
  } = useCopyMarksStore();

  const isInCopyMode = copyModeEntryPath === entry.path;

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
      const prevStatus = prevStatuses.get(taskId);

      if (task) {
        // Check if task just completed (was running/not started, now completed)
        if (
          prevStatus !== undefined &&
          (prevStatus === BTaskStatus.Running || prevStatus === BTaskStatus.NotStarted) &&
          task.status === BTaskStatus.Completed
        ) {
          hasCompletedTask = true;
        }

        prevStatuses.set(taskId, task.status);
      } else if (
        prevStatus !== undefined &&
        (prevStatus === BTaskStatus.Running || prevStatus === BTaskStatus.NotStarted)
      ) {
        // Task was running but is now removed from the list - treat as completed
        hasCompletedTask = true;
        prevStatuses.delete(taskId);
      }
    }

    if (hasCompletedTask && onTaskComplete) {
      onTaskComplete();
    }
  }, [bTasks, marks, onTaskComplete]);

  const resourceMarks = marks.filter((m) => m.type === PathMarkType.Resource);
  const propertyMarks = marks.filter((m) => m.type === PathMarkType.Property);
  const mediaLibraryMarks = marks.filter((m) => m.type === PathMarkType.MediaLibrary);
  const allMarks = [...resourceMarks, ...propertyMarks, ...mediaLibraryMarks];

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

  const handleEnterCopyMode = useCallback(() => {
    if (marks.length > 0) {
      enterCopyMode(entry.path);
      // Select all marks by default
      const markIds = marks.filter((m) => m.id !== undefined).map((m) => m.id!);
      selectAllMarks(markIds);
    }
  }, [enterCopyMode, entry.path, marks, selectAllMarks]);

  const handleConfirmCopy = useCallback(() => {
    confirmSelection(entry.path, marks);
  }, [confirmSelection, entry.path, marks]);

  const handleCancelCopy = useCallback(() => {
    exitCopyMode();
  }, [exitCopyMode]);

  const renderMarkChip = (mark: BakabaseAbstractionsModelsDomainPathMark, keyPrefix: string) => (
    <PathMarkChip
      key={`${keyPrefix}-${mark.id}`}
      mark={mark}
      selectable={isInCopyMode}
      selected={mark.id !== undefined && selectedMarkIds.includes(mark.id)}
      onClick={() => handleEditMark(mark)}
      onContextMenu={() => handleDeleteMark(mark)}
      onSelectionChange={() => {
        if (mark.id !== undefined) {
          toggleMarkSelection(mark.id);
        }
      }}
    />
  );

  return (
    <div
      className="flex items-center gap-2 ml-2"
      onClick={(e) => e.stopPropagation()}
      onMouseDown={(e) => e.stopPropagation()}
    >
      {/* Add Mark Dropdown - hidden in copy mode */}
      {!isInCopyMode && (
        <div className="flex items-center gap-1">
          <Dropdown>
            <DropdownTrigger>
              <Button
                className="min-w-0 px-2 text-default-400 hover:text-primary transition-colors"
                size="sm"
                startContent={<AiOutlineAim className="text-lg" />}
                variant="light"
              >
                {t("pathMarkConfig.action.addMark")}
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
                {t("common.label.resource")}
              </DropdownItem>
              <DropdownItem
                key={PathMarkType.Property}
                className="text-primary"
                description={<PropertyDescription />}
              >
                {t("common.label.property")}
              </DropdownItem>
              <DropdownItem
                key={PathMarkType.MediaLibrary}
                className="text-secondary"
                description={<MediaLibraryDescription />}
              >
                {t("common.label.mediaLibrary")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
        </div>
      )}

      {/* Display Marks - Click to Edit */}
      <div className="flex items-center gap-1 flex-wrap">
        {resourceMarks.map((mark) => renderMarkChip(mark, "resource"))}
        {propertyMarks.map((mark) => renderMarkChip(mark, "property"))}
        {mediaLibraryMarks.map((mark) => renderMarkChip(mark, "mediaLibrary"))}
      </div>

      {/* Copy Marks Button - only show if there are marks */}
      {marks.length > 0 && !isInCopyMode && (
        <Button
          className="min-w-0 px-2 text-default-400 hover:text-warning transition-colors"
          size="sm"
          startContent={<AiOutlineCopy className="text-lg" />}
          variant="light"
          onPress={handleEnterCopyMode}
        >
          {t("pathMarkConfig.action.copy")}
        </Button>
      )}

      {/* Copy mode actions */}
      {isInCopyMode && (
        <div className="flex items-center gap-1 ml-2">
          <Button
            color="primary"
            size="sm"
            isDisabled={selectedMarkIds.length === 0}
            onPress={handleConfirmCopy}
          >
            {t("pathMarkConfig.action.confirmSelection")} ({selectedMarkIds.length}/{allMarks.length})
          </Button>
          <Button
            size="sm"
            variant="light"
            onPress={handleCancelCopy}
          >
            {t("common.action.cancel")}
          </Button>
        </div>
      )}
    </div>
  );
};

PathMarks.displayName = "PathRuleMarks";

export default PathMarks;
