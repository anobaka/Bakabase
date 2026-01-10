import { useState, useEffect, useCallback, useImperativeHandle, forwardRef, useRef } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineClockCircle } from "react-icons/ai";

import type { BTask } from "@/core/models/BTask";

import PendingSyncListModal from "./PendingSyncListModal";

import { Button, Badge } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

export interface PendingSyncButtonRef {
  refresh: () => void;
}

interface PendingSyncButtonProps {
  buttonSize?: "sm" | "md";
  className?: string;
  onSyncComplete?: () => void;
}

// Match PathMark sync task IDs
const isPathMarkSyncTask = (taskId: string) =>
  taskId === "SyncPathMarks" || taskId.startsWith("SyncPathMark_");

const PendingSyncButton = forwardRef<PendingSyncButtonRef, PendingSyncButtonProps>(
  ({ buttonSize = "sm", className, onSyncComplete }, ref) => {
    const { t } = useTranslation();

    const [pendingSyncCount, setPendingSyncCount] = useState(0);
    const [showPendingSyncModal, setShowPendingSyncModal] = useState(false);

    // Track previous task statuses to detect completion
    const prevTaskStatusesRef = useRef<Map<string, BTaskStatus>>(new Map());

    // Watch BTask store for PathMark sync tasks
    const bTasks = useBTasksStore((state) => state.tasks);

    // Load pending sync count
    const loadPendingSyncCount = useCallback(async () => {
      try {
        const response = await BApi.pathMark.getPendingPathMarksCount();
        setPendingSyncCount(response?.data ?? 0);
      } catch (error) {
        console.error("Failed to load pending sync count", error);
      }
    }, []);

    useEffect(() => {
      loadPendingSyncCount();
    }, [loadPendingSyncCount]);

    // Auto-refresh when PathMark sync tasks complete
    useEffect(() => {
      if (!bTasks) return;

      const prevStatuses = prevTaskStatusesRef.current;
      let hasCompletedTask = false;

      for (const task of bTasks as BTask[]) {
        if (!isPathMarkSyncTask(task.id)) continue;

        const prevStatus = prevStatuses.get(task.id);

        // Check if task just completed (was running/not started, now completed)
        if (
          prevStatus !== undefined &&
          (prevStatus === BTaskStatus.Running || prevStatus === BTaskStatus.NotStarted) &&
          task.status === BTaskStatus.Completed
        ) {
          hasCompletedTask = true;
        }

        prevStatuses.set(task.id, task.status);
      }

      if (hasCompletedTask) {
        loadPendingSyncCount();
        onSyncComplete?.();
      }
    }, [bTasks, loadPendingSyncCount, onSyncComplete]);

    // Expose refresh method via ref
    useImperativeHandle(ref, () => ({
      refresh: loadPendingSyncCount,
    }), [loadPendingSyncCount]);

    const handleSyncComplete = useCallback(() => {
      loadPendingSyncCount();
      onSyncComplete?.();
    }, [loadPendingSyncCount, onSyncComplete]);

    return (
      <>
        <Badge
          color="warning"
          content={pendingSyncCount}
          isInvisible={pendingSyncCount === 0}
          size="sm"
        >
          <Button
            className={className}
            color={pendingSyncCount > 0 ? "warning" : "default"}
            size={buttonSize}
            startContent={<AiOutlineClockCircle />}
            variant="flat"
            onPress={() => setShowPendingSyncModal(true)}
          >
            {t("pathMarkConfig.action.pendingSync")}
          </Button>
        </Badge>

        {showPendingSyncModal && (
          <PendingSyncListModal
            visible={showPendingSyncModal}
            onClose={() => setShowPendingSyncModal(false)}
            onSyncComplete={handleSyncComplete}
          />
        )}
      </>
    );
  }
);

PendingSyncButton.displayName = "PendingSyncButton";

export default PendingSyncButton;
