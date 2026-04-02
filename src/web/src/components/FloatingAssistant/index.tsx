"use client";

import { useEffect, useRef, useState, useCallback, useMemo } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import {
  CaretRightOutlined,
  ClearOutlined,
  CloseCircleOutlined,
  PauseOutlined,
} from "@ant-design/icons";

import { BTaskStatus } from "@/sdk/constants";
import {
  Button,
  CircularProgress,
  Divider,
  Popover,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";

import { useBTasksStore, selectTasks } from "@/stores/bTasks";
import { AssistantStatus, POLLING_INTERVAL_MS } from "./constants";
import type { AssistantStatusType } from "./constants";
import { useDraggable } from "./hooks/useDraggable";
import { TaskTable } from "./components/TaskTable";

const log = buildLogger("FloatingAssistant");

const FloatingAssistant = () => {
  const { t } = useTranslation();
  const [allDoneCircleDrawn, setAllDoneCircleDrawn] = useState("");
  const [status, setStatus] = useState<AssistantStatusType>(AssistantStatus.Working);
  const statusRef = useRef(status);
  const [tasksVisible, setTasksVisible] = useState(false);

  const { position, handleMouseDown, isDragging, isDraggingState } = useDraggable();

  const bTasks = useBTasksStore(selectTasks);

  // Tick to trigger re-render so simulated durations update every second
  const [, setNowTick] = useState<number>(Date.now());

  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  // Status computation and timer tick
  useEffect(() => {
    log("Initializing...");
    const queryTask = setInterval(() => {
      const tempTasks = useBTasksStore.getState().tasks;
      let newStatus: AssistantStatusType = AssistantStatus.AllDone;

      if (tempTasks.length > 0) {
        const ongoingTasks = tempTasks.filter(
          (a) => a.status === BTaskStatus.Running,
        );

        if (ongoingTasks.length > 0) {
          newStatus = AssistantStatus.Working;
        } else {
          const failedTasks = tempTasks.filter(
            (a) => a.status === BTaskStatus.Error,
          );

          if (failedTasks.length > 0) {
            newStatus = AssistantStatus.Failed;
          } else {
            const doneTasks = tempTasks.filter(
              (a) => a.status === BTaskStatus.Completed,
            );

            if (doneTasks.length > 0) {
              newStatus = AssistantStatus.AllDone;
            }
          }
        }
      }
      if (newStatus !== statusRef.current) {
        setStatus(newStatus);
        if (newStatus === AssistantStatus.AllDone) {
          setTimeout(() => {
            setAllDoneCircleDrawn("drawn");
          }, 300);
        }
      }
      // Tick to refresh simulated durations in UI
      setNowTick(Date.now());
    }, POLLING_INTERVAL_MS);

    return () => {
      log("Destroying...");
      clearInterval(queryTask);
    };
  }, []);

  const handleClick = useCallback(() => {
    // Only toggle popover if we didn't drag
    if (!isDragging()) {
      setTasksVisible(v => !v);
    }
  }, [isDragging]);

  const clearableTasks = useMemo(() =>
    bTasks.filter(
      (task) =>
        !task.isPersistent &&
        (task.status === BTaskStatus.Completed ||
          task.status === BTaskStatus.Error ||
          task.status === BTaskStatus.Cancelled),
    ), [bTasks]);

  const runningTasks = useMemo(() =>
    bTasks.filter((task) => task.status === BTaskStatus.Running),
    [bTasks]);

  const pausedTasks = useMemo(() =>
    bTasks.filter((task) => task.status === BTaskStatus.Paused),
    [bTasks]);

  const handlePauseAll = useCallback(async () => {
    for (const task of runningTasks) {
      await BApi.backgroundTask.pauseBackgroundTask(task.id);
    }
  }, [runningTasks]);

  const handleResumeAll = useCallback(async () => {
    for (const task of pausedTasks) {
      await BApi.backgroundTask.resumeBackgroundTask(task.id);
    }
  }, [pausedTasks]);

  const overallProgress = useMemo(() => {
    if (runningTasks.length === 0) return 0;
    const totalProgress = runningTasks.reduce(
      (sum, task) => sum + (task.percentage ?? 0),
      0,
    );
    return Math.round(totalProgress / runningTasks.length);
  }, [runningTasks]);

  const statusClassName = useMemo(() =>
    Object.keys(AssistantStatus)[status], [status]);

  return (
    <Popover
      visible={tasksVisible}
      onOpenChange={(visible) => {
        // Only handle closing from outside click/esc, opening is handled by handleClick
        if (!visible) {
          setTasksVisible(false);
        }
      }}
      trigger={
        <div
          className={`portal ${statusClassName} floating-assistant ${tasksVisible ? "" : "hide"} ${isDraggingState ? "cursor-grabbing" : "cursor-grab"}`}
          style={{
            left: position.x,
            top: position.y,
          }}
          onMouseDown={handleMouseDown}
          onClick={handleClick}
        >
          {/* Working - CircularProgress with task count */}
          <Tooltip
            content={`${runningTasks.length} ${t("floatingAssistant.status.tasksRunning")} (${overallProgress}%)`}
            placement="right"
            className="working-tooltip"
          >
            <div className="circular-progress-wrapper">
              <CircularProgress
                aria-label="Task progress"
                value={overallProgress}
                size="lg"
                color="primary"
                showValueLabel={false}
                classNames={{
                  svg: "w-[44px] h-[44px]",
                  track: "stroke-default-200",
                  indicator: "stroke-primary",
                }}
              />
              <span className="task-count">{runningTasks.length}</span>
            </div>
          </Tooltip>
          {/* AllDone */}
          <div className="tick">
            <svg
              version="1.1"
              viewBox="0 0 37 37"
              x="0px"
              y="0px"
              enableBackground={"new 0 0 37 37"}
              className={allDoneCircleDrawn}
            >
              <path
                className="circ path"
                d="M30.5,6.5L30.5,6.5c6.6,6.6,6.6,17.4,0,24l0,0c-6.6,6.6-17.4,6.6-24,0l0,0c-6.6-6.6-6.6-17.4,0-24l0,0C13.1-0.2,23.9-0.2,30.5,6.5z"
                fill={"none"}
                stroke={"#08c29e"}
                strokeLinejoin={"round"}
                strokeMiterlimit={10}
                strokeWidth={3}
              />
              <polyline
                className="tick path"
                fill={"none"}
                points="11.6,20 15.9,24.2 26.4,13.8"
                stroke={"#08c29e"}
                strokeLinejoin={"round"}
                strokeMiterlimit={10}
                strokeWidth={3}
              />
            </svg>
          </div>
          {/* Failed */}
          <Tooltip
            content={t("floatingAssistant.status.someTasksFailed")}
            placement="right"
            color="danger"
          >
            <div className="failed flex items-center justify-center w-[48px] h-[48px]">
              <CloseCircleOutlined className="text-4xl" />
            </div>
          </Tooltip>
        </div>
      }
    >
      <div className={"flex flex-col gap-2 p-2 min-w-[300px]"}>
        <div className="flex flex-col gap-1 max-h-[600px] mt-2 overflow-auto">
          <TaskTable tasks={bTasks} />
        </div>
        <Divider orientation={"horizontal"} />
        <div className="flex items-center gap-2 flex-wrap">
          {runningTasks.length > 0 && (
            <Tooltip content={t("floatingAssistant.tip.pauseAllRunningTasks")}>
              <Button
                size={"sm"}
                variant={"ghost"}
                color={"warning"}
                onPress={handlePauseAll}
              >
                <PauseOutlined className={"text-base"} />
                {t("common.action.pauseAll")}
              </Button>
            </Tooltip>
          )}
          {pausedTasks.length > 0 && (
            <Tooltip content={t("floatingAssistant.tip.resumeAllPausedTasks")}>
              <Button
                size={"sm"}
                variant={"ghost"}
                color={"secondary"}
                onPress={handleResumeAll}
              >
                <CaretRightOutlined className={"text-base"} />
                {t("common.action.resumeAll")}
              </Button>
            </Tooltip>
          )}
          {clearableTasks.length > 0 && (
            <Tooltip content={t("floatingAssistant.tip.clearInactiveTasks")}>
              <Button
                size={"sm"}
                variant={"ghost"}
                onPress={() =>
                  BApi.backgroundTask.cleanInactiveBackgroundTasks()
                }
              >
                <ClearOutlined className={"text-base"} />
                {t("floatingAssistant.action.clearInactiveTasks")}
              </Button>
            </Tooltip>
          )}
        </div>
      </div>
    </Popover>
  );
};

FloatingAssistant.displayName = "FloatingAssistant";

export default FloatingAssistant;
