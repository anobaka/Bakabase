"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BTask } from "@/core/models/BTask";

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

import {
  getPathMarkSyncTasks,
  getSyncTaskRunKey,
  isNewPathMarkSyncTaskRun,
  isSamePathMarkSyncTaskRun,
  isPathMarkSyncTaskActive,
  PATH_MARK_SYNC_TASK_ID,
  RESOURCE_SYNC_TASK_ID,
  waitForBTaskSnapshot,
} from "./pathMarkSyncTask";

import { Button, Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { BTaskStatus, PathMarkType, PathMarkSyncStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";

export interface SyncProgressModalProps extends DestroyableProps {
  visible?: boolean;
  /** If true, resets every mark to pending and re-syncs the whole repo. Defaults to false (sync currently-pending marks only). */
  forceResync?: boolean;
  onClose?: () => void;
  onComplete?: () => void;
}

const SyncProgressModal = ({
  visible = true,
  forceResync = false,
  onClose,
  onComplete,
  onDestroyed,
}: SyncProgressModalProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [isOpen, setIsOpen] = useState(visible);
  const [starting, setStarting] = useState(true);
  const [expectedIds, setExpectedIds] = useState<string[]>([]);
  const [startError, setStartError] = useState<string>();
  const [runTasks, setRunTasks] = useState<BTask[]>([]);
  const [monitoringLost, setMonitoringLost] = useState(false);
  const bTasks = useBTasksStore((state) => state.tasks);
  const tasksInitialized = useBTasksStore((state) => state.initialized);
  const baseline = useRef(new Map(getPathMarkSyncTasks(bTasks).map((task) => [task.id, task])));
  const reconcileNoOp = useRef<() => Promise<void>>();
  const reconcileSequence = useRef(0);
  const notified = useRef(false);

  useEffect(() => {
    setRunTasks((previous) => {
      const tasks = expectedIds.flatMap((id) => {
        const captured = previous.find((task) => task.id === id);
        const current = bTasks.find((task) => task.id === id);

        if (!captured) {
          return current && isNewPathMarkSyncTaskRun(current, baseline.current.get(id))
            ? [current]
            : [];
        }
        // Keep this request's completed result even if a later automatic run reuses its id.
        // A captured queued task first gets its start time when it begins running.
        if (
          current &&
          !(isPathMarkSyncTaskActive(current) && !isPathMarkSyncTaskActive(captured)) &&
          isSamePathMarkSyncTaskRun(current, captured)
        ) {
          return [current];
        }

        return [captured];
      });

      return tasks.length === previous.length &&
        tasks.every((task, index) => task === previous[index])
        ? previous
        : tasks;
    });
  }, [bTasks, expectedIds]);
  const observedRunTasks = runTasks.map((captured) => {
    const current = bTasks.find((task) => task.id === captured.id);

    return isSamePathMarkSyncTaskRun(current, captured) &&
      !(isPathMarkSyncTaskActive(current) && !isPathMarkSyncTaskActive(captured))
      ? current!
      : captured;
  });
  const currentProgressUnavailable = observedRunTasks.some(
    (task) =>
      isPathMarkSyncTaskActive(task) &&
      !isSamePathMarkSyncTaskRun(
        bTasks.find((current) => current.id === task.id),
        task,
      ),
  );
  const isProgressUnavailable = monitoringLost || currentProgressUnavailable;

  useEffect(() => {
    if (currentProgressUnavailable) setMonitoringLost(true);
  }, [currentProgressUnavailable]);
  const activeTasks = observedRunTasks.filter(
    (task) =>
      isPathMarkSyncTaskActive(task) &&
      isSamePathMarkSyncTaskRun(
        bTasks.find((current) => current.id === task.id),
        task,
      ),
  );
  const awaitingTask = expectedIds.some((id) => !runTasks.some((task) => task.id === id));
  const reconciliationKey = expectedIds
    .filter(
      (id) =>
        (!runTasks.some((task) => task.id === id) &&
          !isPathMarkSyncTaskActive(bTasks.find((task) => task.id === id))) ||
        runTasks.some(
          (task) =>
            task.id === id &&
            isPathMarkSyncTaskActive(task) &&
            !isSamePathMarkSyncTaskRun(
              bTasks.find((current) => current.id === id),
              task,
            ),
        ),
    )
    .map((id) => {
      const task = bTasks.find((task) => task.id === id);

      return `${id}:${task ? `${getSyncTaskRunKey(task)}:${task.status}` : "missing"}`;
    })
    .join(";");

  useEffect(() => {
    if (!starting && reconciliationKey) void reconcileNoOp.current?.();
  }, [starting, reconciliationKey]);
  const task =
    activeTasks.find((task) => task.status !== BTaskStatus.Paused) ??
    activeTasks[0] ??
    observedRunTasks.find((task) => task.status === BTaskStatus.Error) ??
    observedRunTasks[observedRunTasks.length - 1];
  const isPaused =
    !starting &&
    activeTasks.some((task) => task.status === BTaskStatus.Paused) &&
    activeTasks.every(
      (task) =>
        task.status === BTaskStatus.Paused ||
        task.status === BTaskStatus.NotStarted ||
        task.status === BTaskStatus.WaitingForInput,
    );
  const isSyncing = starting || (!isPaused && (awaitingTask || activeTasks.length > 0));
  const isSettled = !starting && !awaitingTask && activeTasks.length === 0;
  const isError =
    !!startError || observedRunTasks.some((task) => task.status === BTaskStatus.Error);
  const isCancelled = observedRunTasks.some((task) => task.status === BTaskStatus.Cancelled);
  const isCompleted = isSettled && !isError && !isCancelled && !isProgressUnavailable;
  const cannotClose = (starting && tasksInitialized) || activeTasks.length > 0;

  const progress = isCompleted ? 100 : task?.percentage || 0;
  const currentProcess = task?.process || "";
  const error =
    startError ?? observedRunTasks.find((task) => task.status === BTaskStatus.Error)?.error;
  const data = task?.data as
    | { resourcesCreated?: number; propertiesApplied?: number; failedMarks?: number }
    | undefined;

  // Ignore completed history until this request has produced its own run (or is a real no-op).
  useEffect(() => {
    if (!visible) return;
    let disposed = false;
    const controller = new AbortController();

    const start = async () => {
      try {
        if (!(await waitForBTaskSnapshot(controller.signal))) return;
        const response = forceResync
          ? await BApi.pathMark.getAllPathMarks()
          : await BApi.pathMark.getPendingPathMarks();

        if (response.code) throw new Error(response.message);
        if (disposed) return;
        const submittedMarks = (response.data ?? []).filter(
          (mark) =>
            !forceResync || (!mark.isDeleted && mark.syncStatus !== PathMarkSyncStatus.Syncing),
        );
        const ids = new Set<string>();

        for (const mark of submittedMarks) {
          ids.add(
            mark.type === PathMarkType.Resource ? RESOURCE_SYNC_TASK_ID : PATH_MARK_SYNC_TASK_ID,
          );
        }
        // Queueing while another run is active schedules a later run of the same global id.
        // Neither that existing run's completion nor its controls belong to this request.
        if (!(await waitForBTaskSnapshot(controller.signal))) return;
        baseline.current = new Map(
          getPathMarkSyncTasks(useBTasksStore.getState().tasks).map((task) => [task.id, task]),
        );
        setExpectedIds([...ids]);
        const trigger = forceResync
          ? BApi.pathMark.forceResyncAllPathMarks
          : BApi.pathMark.startPathMarkSyncAll;

        // Another request may have settled/deleted these marks between our GET and POST.
        // Confirm that no pending *or syncing* submitted mark remains before accepting a no-op.
        // A missing pending row alone is insufficient: active marks disappear from that endpoint.
        reconcileNoOp.current = async () => {
          const request = ++reconcileSequence.current;

          try {
            const pending = await BApi.pathMark.getPendingPathMarks();

            if (disposed || pending.code) return;
            const pendingIds = new Set((pending.data ?? []).map((mark) => mark.id));

            if (submittedMarks.every((mark) => pendingIds.has(mark.id))) return;
            const all = await BApi.pathMark.getAllPathMarks();

            if (disposed || all.code || request !== reconcileSequence.current) return;
            const currentMarks = new Map((all.data ?? []).map((mark) => [mark.id, mark]));
            const currentTasks = getPathMarkSyncTasks(useBTasksStore.getState().tasks);

            setExpectedIds(
              [...ids].filter((id) => {
                const task = currentTasks.find((task) => task.id === id);
                const oldTask = baseline.current.get(id);

                if (task && isNewPathMarkSyncTaskRun(task, oldTask)) return true;

                return submittedMarks.some((mark) => {
                  const kind =
                    mark.type === PathMarkType.Resource
                      ? RESOURCE_SYNC_TASK_ID
                      : PATH_MARK_SYNC_TASK_ID;
                  const currentMark = currentMarks.get(mark.id);

                  // Resource marks become Synced before their run finishes writing resources.
                  return (
                    kind === id &&
                    currentMark &&
                    (isPathMarkSyncTaskActive(task) ||
                      currentMark.syncStatus !== PathMarkSyncStatus.Synced)
                  );
                });
              }),
            );
          } catch {
            // The accepted request is still monitored through its task feed if this extra read fails.
          }
        };
        const result = await trigger();

        if (result.code) throw new Error(result.message);
        if (!disposed && ids.size > 0) await reconcileNoOp.current();
      } catch (error) {
        if (!disposed) {
          setExpectedIds([]);
          setStartError(error instanceof Error ? error.message : String(error));
        }
      } finally {
        if (!disposed) setStarting(false);
      }
    };

    start();

    return () => {
      disposed = true;
      controller.abort();
    };
  }, [visible, forceResync]);

  // Both pipelines must settle; a resource task may be followed by a property task.
  useEffect(() => {
    if ((isSettled || isProgressUnavailable) && !notified.current) {
      notified.current = true;
      onComplete?.();
    }
  }, [onComplete, isSettled, isProgressUnavailable]);

  const getControllableTasks = useCallback(
    () =>
      runTasks.flatMap((captured) => {
        const current = useBTasksStore.getState().tasks.find((task) => task.id === captured.id);

        return isSamePathMarkSyncTaskRun(current, captured) && isPathMarkSyncTaskActive(current)
          ? [current!]
          : [];
      }),
    [runTasks],
  );

  const handlePause = useCallback(async () => {
    await Promise.all(
      getControllableTasks()
        .filter((task) => task.status === BTaskStatus.Running)
        .map((task) => BApi.backgroundTask.pauseBackgroundTask(task.id)),
    );
  }, [getControllableTasks]);

  const handleResume = useCallback(async () => {
    await Promise.all(
      getControllableTasks()
        .filter((task) => task.status === BTaskStatus.Paused)
        .map((task) => BApi.backgroundTask.resumeBackgroundTask(task.id)),
    );
  }, [getControllableTasks]);

  const handleStop = useCallback(async () => {
    const results = await Promise.all(
      getControllableTasks().map(async (task) => ({
        task,
        response: await BApi.backgroundTask.stopBackgroundTask(task.id),
      })),
    );
    const confirmations = results.filter(({ response }) => response.code === 202);

    if (confirmations.length > 0) {
      createPortal(Modal, {
        defaultVisible: true,
        title: t("common.action.stop"),
        children: confirmations
          .map(({ task, response }) => task.messageOnInterruption ?? response.message)
          .join("\n"),
        onOk: () =>
          Promise.all(
            confirmations
              .filter(({ task }) =>
                isSamePathMarkSyncTaskRun(
                  useBTasksStore.getState().tasks.find((current) => current.id === task.id),
                  task,
                ),
              )
              .map(({ task }) =>
                BApi.backgroundTask.stopBackgroundTask(task.id, { confirm: true }),
              ),
          ),
      });
    }
  }, [getControllableTasks, createPortal, t]);

  const handleClose = useCallback(() => {
    if (!cannotClose) {
      setIsOpen(false);
      onClose?.();
      onDestroyed?.();
    }
  }, [cannotClose, onClose, onDestroyed]);

  useEffect(() => {
    setIsOpen(visible);
  }, [visible]);

  return (
    <NextUiModal
      hideCloseButton={cannotClose}
      isDismissable={false}
      isKeyboardDismissDisabled={cannotClose}
      isOpen={isOpen}
      onClose={handleClose}
      onOpenChange={(open) => {
        if (!open && !cannotClose) {
          handleClose();
        }
      }}
    >
      <ModalContent>
        <ModalHeader className="flex flex-col gap-1">
          {t("pathMarkConfig.modal.syncProgress")}
        </ModalHeader>
        <ModalBody>
          <div className="flex flex-col gap-4">
            {/* Progress */}
            <div className="flex flex-col gap-2">
              <div className="flex items-center justify-between text-sm">
                <span>{t("pathMarkConfig.label.progress")}</span>
                <span className="text-default-500">{Math.round(progress)}%</span>
              </div>
              <Progress
                aria-label="Sync progress"
                className="w-full"
                color={
                  isError ? "danger" : isPaused || isProgressUnavailable ? "warning" : "primary"
                }
                isIndeterminate={(isSyncing || isPaused) && progress === 0}
                value={progress}
              />
            </div>

            {/* Current process */}
            {currentProcess && (isSyncing || isPaused) && (
              <div className="flex flex-col gap-1">
                <span className="text-sm text-default-500">
                  {t("pathMarkConfig.label.currentlySyncing")}:
                </span>
                <span className="text-sm truncate" title={currentProcess}>
                  {currentProcess}
                </span>
              </div>
            )}

            {/* Error message */}
            {isError && error && (
              <div className="flex flex-col gap-1">
                <span className="text-sm text-danger">{t("pathMarkConfig.label.error")}:</span>
                <span className="text-sm text-danger truncate" title={error}>
                  {error}
                </span>
              </div>
            )}

            {/* Paused message */}
            {starting && !tasksInitialized && (
              <div className="text-sm text-default-500">
                {t("pathMarkConfig.status.waitingForConnection")}
              </div>
            )}
            {isProgressUnavailable && (
              <div className="text-sm text-warning">
                {t("pathMarkConfig.status.progressUnavailable")}
              </div>
            )}
            {isPaused && (
              <div className="text-sm text-warning">{t("pathMarkConfig.status.paused")}</div>
            )}

            {/* Cancelled message */}
            {isCancelled && (
              <div className="text-sm text-warning">{t("pathMarkConfig.status.cancelled")}</div>
            )}

            {/* Completed summary */}
            {isCompleted && (
              <div className="flex flex-col gap-2">
                <span className="text-sm text-success">{t("pathMarkConfig.status.completed")}</span>
                <div className="flex items-center gap-4 text-sm">
                  {data?.resourcesCreated !== undefined && (
                    <Chip color="success" size="sm" variant="flat">
                      {t("pathMarkConfig.label.resources")}: {data.resourcesCreated}
                    </Chip>
                  )}
                  {data?.propertiesApplied !== undefined && (
                    <Chip color="primary" size="sm" variant="flat">
                      {t("pathMarkConfig.label.properties")}: {data.propertiesApplied}
                    </Chip>
                  )}
                  {(data?.failedMarks || 0) > 0 && (
                    <Chip color="danger" size="sm" variant="flat">
                      {t("pathMarkConfig.status.failed")}: {data?.failedMarks}
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
                isDisabled={!activeTasks.some((task) => task.status === BTaskStatus.Running)}
                startContent={<AiOutlinePause />}
                variant="flat"
                onPress={handlePause}
              >
                {t("pathMarkConfig.action.pause")}
              </Button>
              <Button
                color="danger"
                isDisabled={activeTasks.length === 0}
                startContent={<AiOutlineStop />}
                variant="flat"
                onPress={handleStop}
              >
                {t("pathMarkConfig.action.stop")}
              </Button>
              {!cannotClose && (
                <Button color="default" onPress={handleClose}>
                  {t("common.action.close")}
                </Button>
              )}
            </div>
          ) : isPaused ? (
            <div className="flex items-center gap-2">
              <Button
                color="primary"
                startContent={<AiOutlinePlayCircle />}
                variant="flat"
                onPress={handleResume}
              >
                {t("pathMarkConfig.action.resume")}
              </Button>
              <Button
                color="danger"
                startContent={<AiOutlineStop />}
                variant="flat"
                onPress={handleStop}
              >
                {t("pathMarkConfig.action.stop")}
              </Button>
            </div>
          ) : (
            <Button color="primary" onPress={handleClose}>
              {t("common.action.close")}
            </Button>
          )}
        </ModalFooter>
      </ModalContent>
    </NextUiModal>
  );
};

SyncProgressModal.displayName = "SyncProgressModal";

export default SyncProgressModal;
