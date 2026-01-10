"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { PathMarkSyncStatus as PathMarkSyncStatusType } from "@/sdk/constants";

import React, { useState, useEffect, useCallback } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineSync,
  AiOutlineClockCircle,
  AiOutlineWarning,
  AiOutlineCheck,
  AiOutlineDelete,
} from "react-icons/ai";

import { Modal, Button, Chip, Spinner, Tooltip, CircularProgress } from "@/components/bakaui";
import { PathMarkSyncStatus, PathMarkType, BTaskStatus } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";
import { usePathMarksStore } from "@/stores/pathMarks";
import type { BTask } from "@/core/models/BTask";
import SyncProgressModal from "./SyncProgressModal";

export interface PendingSyncListModalProps extends DestroyableProps {
  visible?: boolean;
  onClose?: () => void;
  onSyncComplete?: () => void;
}

// Group marks by path
interface PathGroup {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
}

const getSyncStatusIcon = (status?: PathMarkSyncStatusType) => {
  switch (status) {
    case PathMarkSyncStatus.Pending:
      return <AiOutlineClockCircle className="text-warning" />;
    case PathMarkSyncStatus.Syncing:
      return <Spinner size="sm" className="w-3 h-3" />;
    case PathMarkSyncStatus.Synced:
      return <AiOutlineCheck className="text-success" />;
    case PathMarkSyncStatus.Failed:
      return <AiOutlineWarning className="text-danger" />;
    case PathMarkSyncStatus.PendingDelete:
      return <AiOutlineDelete className="text-danger" />;
    default:
      return <AiOutlineClockCircle className="text-default-400" />;
  }
};

const getSyncStatusLabel = (status?: PathMarkSyncStatusType, t?: (key: string) => string) => {
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
      return translate("pathMarkConfig.status.unknown");
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

// Build task ID for a single mark sync
const buildMarkTaskId = (markId: number) => `SyncPathMark_${markId}`;

const PendingSyncListModal = ({
  visible = true,
  onClose,
  onSyncComplete,
  onDestroyed,
}: PendingSyncListModalProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [isOpen, setIsOpen] = useState(visible);
  const [loading, setLoading] = useState(false);
  const [pendingMarks, setPendingMarks] = useState<BakabaseAbstractionsModelsDomainPathMark[]>([]);

  // Watch BTask store for individual mark sync tasks
  const bTasks = useBTasksStore((state) => state.tasks);

  // Watch PathMarks store for real-time status updates via SignalR
  const pathMarksStore = usePathMarksStore((state) => state.marks);

  const loadPendingMarks = useCallback(async () => {
    setLoading(true);
    try {
      const response = await BApi.pathMark.getPendingPathMarks();
      setPendingMarks(response?.data || []);
    } catch (error) {
      console.error("Failed to load pending marks", error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (visible) {
      loadPendingMarks();
    }
  }, [visible, loadPendingMarks]);

  useEffect(() => {
    setIsOpen(visible);
  }, [visible]);

  const handleClose = useCallback(() => {
    setIsOpen(false);
    onClose?.();
  }, [onClose]);

  // Group marks by path, using store marks for latest status
  const groupedMarks: PathGroup[] = React.useMemo(() => {
    const groups: Map<string, BakabaseAbstractionsModelsDomainPathMark[]> = new Map();

    for (const propMark of pendingMarks) {
      // Use store mark if available (has latest syncStatus from SignalR)
      const storeMark = propMark.id != null ? pathMarksStore.get(propMark.id) : undefined;
      const mark = storeMark ?? propMark;

      const path = mark.path || "Unknown";
      if (!groups.has(path)) {
        groups.set(path, []);
      }
      groups.get(path)!.push(mark);
    }

    return Array.from(groups.entries()).map(([path, marks]) => ({
      path,
      marks: marks.sort((a, b) => (a.priority || 0) - (b.priority || 0)),
    }));
  }, [pendingMarks, pathMarksStore]);

  // Sync a single mark using BTask
  const handleSyncMark = useCallback(async (mark: BakabaseAbstractionsModelsDomainPathMark) => {
    if (!mark.id) return;

    try {
      // Call the new API to start syncing specific marks
      await BApi.pathMark.startPathMarkSync([mark.id]);
    } catch (error) {
      console.error("Failed to start sync for mark", mark.id, error);
    }
  }, []);

  const handleSyncAll = useCallback(() => {
    // Open the sync progress modal
    createPortal(SyncProgressModal, {
      visible: true,
      onComplete: () => {
        loadPendingMarks();
        onSyncComplete?.();
      },
    });
  }, [createPortal, loadPendingMarks, onSyncComplete]);

  // Calculate total pending using store marks for latest status
  const totalPending = React.useMemo(() => {
    return pendingMarks.filter((propMark) => {
      const storeMark = propMark.id != null ? pathMarksStore.get(propMark.id) : undefined;
      const mark = storeMark ?? propMark;
      return mark.syncStatus === PathMarkSyncStatus.Pending || mark.syncStatus === PathMarkSyncStatus.PendingDelete;
    }).length;
  }, [pendingMarks, pathMarksStore]);

  // Get task for a specific mark
  const getMarkTask = (markId: number): BTask | undefined => {
    return bTasks?.find((t) => t.id === buildMarkTaskId(markId)) as BTask | undefined;
  };

  return (
    <Modal
      visible={isOpen}
      size="lg"
      title={
        <div className="flex items-center gap-2">
          <span>{t("pathMarkConfig.modal.pendingSyncMarks")}</span>
          {totalPending > 0 && (
            <Chip color="warning" size="sm" variant="flat">
              {totalPending}
            </Chip>
          )}
        </div>
      }
      footer={
        <div className="flex items-center justify-between w-full">
          <Button
            color="primary"
            startContent={<AiOutlineSync />}
            isDisabled={pendingMarks.length === 0 || loading}
            onPress={handleSyncAll}
          >
            {t("pathMarkConfig.action.syncAll")}
          </Button>
          <Button color="default" variant="light" onPress={handleClose}>
            {t("common.action.close")}
          </Button>
        </div>
      }
      onClose={handleClose}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4 max-h-[60vh] overflow-y-auto">
        {loading ? (
          <div className="flex items-center justify-center py-8">
            <Spinner size="lg" />
          </div>
        ) : pendingMarks.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-8 text-default-500">
            <AiOutlineCheck className="text-4xl text-success mb-2" />
            <span>{t("pathMarkConfig.status.allSynced")}</span>
          </div>
        ) : (
          groupedMarks.map((group) => (
            <div key={group.path} className="flex flex-col gap-2">
              {/* Path header */}
              <div className="flex items-center gap-2 px-2 py-1 bg-default-100 rounded-lg">
                <span className="text-sm font-medium truncate flex-1" title={group.path}>
                  {group.path}
                </span>
                <Chip size="sm" variant="flat">
                  {group.marks.length}
                </Chip>
              </div>

              {/* Marks under this path */}
              <div className="flex flex-col gap-1 pl-4">
                {group.marks.map((mark) => {
                  const markTask = getMarkTask(mark.id!);
                  const isTaskRunning = markTask?.status === BTaskStatus.Running || markTask?.status === BTaskStatus.NotStarted;
                  const isPendingDelete = mark.syncStatus === PathMarkSyncStatus.PendingDelete;

                  return (
                    <div
                      key={mark.id}
                      className={`flex items-center gap-2 p-2 rounded-lg border border-default-200 ${
                        isPendingDelete ? "opacity-50 line-through" : ""
                      }`}
                    >
                      {/* Sync status icon */}
                      <Tooltip content={getSyncStatusLabel(mark.syncStatus, t)}>
                        <span className="flex items-center">
                          {isTaskRunning ? <Spinner size="sm" className="w-4 h-4" /> : getSyncStatusIcon(mark.syncStatus)}
                        </span>
                      </Tooltip>

                      {/* Mark type chip */}
                      <Chip
                        color={getMarkTypeColor(mark.type) as any}
                        size="sm"
                        variant="flat"
                      >
                        {getMarkTypeLabel(mark.type, t)}
                      </Chip>

                      {/* Priority */}
                      <span className="text-xs text-default-500">
                        #{mark.priority}
                      </span>

                      {/* Task progress if running */}
                      {isTaskRunning && markTask && (
                        <CircularProgress
                          size="sm"
                          value={markTask.percentage || 0}
                          showValueLabel
                          className="w-8"
                        />
                      )}

                      {/* Error message */}
                      {mark.syncError && (
                        <Tooltip content={mark.syncError}>
                          <span className="text-danger text-xs truncate max-w-[150px]">
                            {mark.syncError}
                          </span>
                        </Tooltip>
                      )}

                      {/* Spacer */}
                      <div className="flex-1" />

                      {/* Sync button */}
                      <Button
                        size="sm"
                        color="primary"
                        variant="light"
                        isIconOnly
                        isLoading={isTaskRunning}
                        isDisabled={isPendingDelete || isTaskRunning}
                        onPress={() => handleSyncMark(mark)}
                      >
                        <AiOutlineSync />
                      </Button>
                    </div>
                  );
                })}
              </div>
            </div>
          ))
        )}
      </div>
    </Modal>
  );
};

PendingSyncListModal.displayName = "PendingSyncListModal";

export default PendingSyncListModal;
