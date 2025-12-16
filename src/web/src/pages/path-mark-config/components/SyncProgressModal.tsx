"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useState, useEffect, useCallback, useRef } from "react";
import { useTranslation } from "react-i18next";
import {
  Modal as NextUiModal,
  ModalBody,
  ModalContent,
  ModalHeader,
  ModalFooter,
  Progress,
} from "@heroui/react";
import { AiOutlineStop, AiOutlinePause, AiOutlinePlayCircle } from "react-icons/ai";

import { Button, Chip } from "@/components/bakaui";
import { BTaskStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";
import type { BTask } from "@/core/models/BTask";

export interface SyncProgressModalProps extends DestroyableProps {
  visible?: boolean;
  onClose?: () => void;
  onComplete?: () => void;
}

const SyncTaskId = "SyncPathMarks";

const SyncProgressModal = ({ visible = true, onClose, onComplete, onDestroyed }: SyncProgressModalProps) => {
  const { t } = useTranslation();
  const [isOpen, setIsOpen] = useState(visible);
  const bTasks = useBTasksStore((state) => state.tasks);
  const task = bTasks?.find((d) => d.id === SyncTaskId) as BTask | undefined;
  const prevStatusRef = useRef(task?.status);

  const isSyncing = task?.status === BTaskStatus.Running || task?.status === BTaskStatus.NotStarted;
  const isPaused = task?.status === BTaskStatus.Paused;
  const isCompleted = task?.status === BTaskStatus.Completed;
  const isError = task?.status === BTaskStatus.Error;
  const isCancelled = task?.status === BTaskStatus.Cancelled;

  const progress = task?.percentage || 0;
  const currentProcess = task?.process || "";
  const error = task?.error;
  const data = task?.data as { resourcesCreated?: number; propertiesApplied?: number; failedMarks?: number } | undefined;

  // Start sync when modal opens
  useEffect(() => {
    if (visible) {
      BApi.pathMark.startPathMarkSyncAll().catch(console.error);
    }
  }, [visible]);

  // Detect completion
  useEffect(() => {
    if (
      prevStatusRef.current !== undefined &&
      prevStatusRef.current !== BTaskStatus.Completed &&
      task?.status === BTaskStatus.Completed
    ) {
      onComplete?.();
    }
    prevStatusRef.current = task?.status;
  }, [onComplete, task?.status]);

  const handlePause = useCallback(async () => {
    await BApi.backgroundTask.pauseBackgroundTask(SyncTaskId);
  }, []);

  const handleResume = useCallback(async () => {
    await BApi.backgroundTask.resumeBackgroundTask(SyncTaskId);
  }, []);

  const handleStop = useCallback(async () => {
    await BApi.pathMark.stopPathMarkSync();
  }, []);

  const handleClose = useCallback(() => {
    if (!isSyncing && !isPaused) {
      setIsOpen(false);
      onClose?.();
    }
  }, [isSyncing, isPaused, onClose]);

  useEffect(() => {
    setIsOpen(visible);
  }, [visible]);

  return (
    <NextUiModal
      isOpen={isOpen}
      isDismissable={false}
      hideCloseButton={isSyncing || isPaused}
      isKeyboardDismissDisabled={isSyncing || isPaused}
      onClose={handleClose}
      onOpenChange={(open) => {
        if (!open && !isSyncing && !isPaused) {
          handleClose();
          onDestroyed?.();
        }
      }}
    >
      <ModalContent>
        <ModalHeader className="flex flex-col gap-1">
          {t("Synchronizing Marks")}
        </ModalHeader>
        <ModalBody>
          <div className="flex flex-col gap-4">
            {/* Progress */}
            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between text-sm">
                <span>{t("Progress")}</span>
                <span className="text-default-500">
                  {Math.round(progress)}%
                </span>
              </div>
              <Progress
                aria-label="Sync progress"
                color={isError ? "danger" : isPaused ? "warning" : "primary"}
                isIndeterminate={(isSyncing || isPaused) && progress === 0}
                value={progress}
                className="w-full"
              />
            </div>

            {/* Current process */}
            {currentProcess && (isSyncing || isPaused) && (
              <div className="flex flex-col gap-1">
                <span className="text-sm text-default-500">{t("Currently syncing")}:</span>
                <span className="text-sm truncate" title={currentProcess}>
                  {currentProcess}
                </span>
              </div>
            )}

            {/* Error message */}
            {isError && error && (
              <div className="flex flex-col gap-1">
                <span className="text-sm text-danger">{t("Error")}:</span>
                <span className="text-sm text-danger truncate" title={error}>
                  {error}
                </span>
              </div>
            )}

            {/* Paused message */}
            {isPaused && (
              <div className="text-sm text-warning">
                {t("Synchronization paused")}
              </div>
            )}

            {/* Cancelled message */}
            {isCancelled && (
              <div className="text-sm text-warning">
                {t("Synchronization cancelled")}
              </div>
            )}

            {/* Completed summary */}
            {isCompleted && data && (
              <div className="flex flex-col gap-2">
                <span className="text-sm text-success">{t("Synchronization completed!")}</span>
                <div className="flex items-center gap-4 text-sm">
                  {data.resourcesCreated !== undefined && (
                    <Chip color="success" size="sm" variant="flat">
                      {t("Resources")}: {data.resourcesCreated}
                    </Chip>
                  )}
                  {data.propertiesApplied !== undefined && (
                    <Chip color="primary" size="sm" variant="flat">
                      {t("Properties")}: {data.propertiesApplied}
                    </Chip>
                  )}
                  {(data.failedMarks || 0) > 0 && (
                    <Chip color="danger" size="sm" variant="flat">
                      {t("Failed")}: {data.failedMarks}
                    </Chip>
                  )}
                </div>
              </div>
            )}
          </div>
        </ModalBody>
        <ModalFooter>
          {isSyncing ? (
            <div className="flex items-center gap-2">
              <Button
                color="warning"
                variant="flat"
                startContent={<AiOutlinePause />}
                onPress={handlePause}
              >
                {t("Pause")}
              </Button>
              <Button
                color="danger"
                variant="flat"
                startContent={<AiOutlineStop />}
                onPress={handleStop}
              >
                {t("Stop")}
              </Button>
            </div>
          ) : isPaused ? (
            <div className="flex items-center gap-2">
              <Button
                color="primary"
                variant="flat"
                startContent={<AiOutlinePlayCircle />}
                onPress={handleResume}
              >
                {t("Resume")}
              </Button>
              <Button
                color="danger"
                variant="flat"
                startContent={<AiOutlineStop />}
                onPress={handleStop}
              >
                {t("Stop")}
              </Button>
            </div>
          ) : (
            <Button color="primary" onPress={handleClose}>
              {t("Close")}
            </Button>
          )}
        </ModalFooter>
      </ModalContent>
    </NextUiModal>
  );
};

SyncProgressModal.displayName = "SyncProgressModal";

export default SyncProgressModal;
