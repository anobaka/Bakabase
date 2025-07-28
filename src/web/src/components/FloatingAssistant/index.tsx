"use client";

import { useEffect, useRef, useState } from "react";
import "./index.scss";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import {
  CaretRightOutlined,
  CheckCircleOutlined,
  ClearOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  CloseOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  PauseCircleOutlined,
  PauseOutlined,
  PushpinOutlined,
  QuestionCircleOutlined,
  SettingOutlined,
  StopOutlined,
} from "@ant-design/icons";
import moment from "moment";
import dayjs from "dayjs";

import { BTaskStatus } from "@/sdk/constants";
import {
  Button,
  Chip,
  Divider,
  Modal,
  Popover,
  Progress,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { buildLogger } from "@/components/utils";

import type { BTask } from "@/core/models/BTask";

import { useBTasksStore } from "@/stores/bTasks";

const AssistantStatus = {
  Idle: 0,
  Working: 1,
  AllDone: 2,
  Failed: 3,
};

enum TaskAction {
  Start = 1,
  Pause = 2,
  Resume = 3,
  Stop = 4,
  Clean = 5,
  Config = 6,
}

const ActionsFilter: Record<TaskAction, (task: BTask) => boolean> = {
  [TaskAction.Start]: (task) =>
    task.isPersistent &&
    (task.status == BTaskStatus.Cancelled ||
      task.status == BTaskStatus.Error ||
      task.status == BTaskStatus.Completed ||
      task.status == BTaskStatus.NotStarted),
  [TaskAction.Pause]: (task) => task.status == BTaskStatus.Running,
  [TaskAction.Resume]: (task) => task.status == BTaskStatus.Paused,
  [TaskAction.Stop]: (task) => task.status == BTaskStatus.Running,
  [TaskAction.Clean]: (task) =>
    !task.isPersistent &&
    (task.status == BTaskStatus.Completed ||
      task.status == BTaskStatus.Error ||
      task.status == BTaskStatus.Cancelled),
  [TaskAction.Config]: (task) => task.isPersistent,
};

const log = buildLogger("FloatingAssistant");
const FloatingAssistant = () => {
  const [allDoneCircleDrawn, setAllDoneCircleDrawn] = useState("");
  const [status, setStatus] = useState(AssistantStatus.Working);
  const statusRef = useRef(status);
  const [cleaningTaskId, setCleaningTaskId] = useState<string | undefined>();
  const [tasksVisible, setTasksVisible] = useState(false);
  const { createPortal } = useBakabaseContext();
  const { t } = useTranslation();
  const portalRef = useRef<HTMLDivElement | null>(null);
  const navigate = useNavigate();

  const bTasks = useBTasksStore((state) => state.tasks);

  // Tick to trigger re-render so simulated durations update every second
  const [nowTick, setNowTick] = useState<number>(Date.now());

  // read to satisfy linter; this state is used to trigger re-renders
  void nowTick;

  // Per-task simulation state to auto-elapse durations between backend updates
  const simTimingRef = useRef<
    Map<
      string,
      {
        baseElapsedMs?: number;
        baseRemainingMs?: number;
        lastSeenAt: number;
        lastStatus: number;
        lastRawElapsedMs?: number;
        lastRawRemainingMs?: number;
      }
    >
  >(new Map());

  // When tasks update from the store, (re)initialize or adjust simulation baselines
  useEffect(() => {
    const now = Date.now();
    const map = simTimingRef.current;
    const existingIds = new Set(map.keys());

    bTasks.forEach((task) => {
      const rawElapsedMs = task.elapsed
        ? moment.duration(task.elapsed).asMilliseconds()
        : undefined;
      const rawRemainingMs = task.estimateRemainingTime
        ? moment.duration(task.estimateRemainingTime).asMilliseconds()
        : undefined;

      const rec = map.get(task.id);

      if (!rec) {
        map.set(task.id, {
          baseElapsedMs: rawElapsedMs,
          baseRemainingMs: rawRemainingMs,
          lastSeenAt: now,
          lastStatus: task.status,
          lastRawElapsedMs: rawElapsedMs,
          lastRawRemainingMs: rawRemainingMs,
        });
      } else {
        const incomingChanged =
          rec.lastRawElapsedMs !== rawElapsedMs ||
          rec.lastRawRemainingMs !== rawRemainingMs;

        if (incomingChanged) {
          // Override simulation with incoming backend updates
          rec.baseElapsedMs = rawElapsedMs;
          rec.baseRemainingMs = rawRemainingMs;
          rec.lastSeenAt = now;
          rec.lastRawElapsedMs = rawElapsedMs;
          rec.lastRawRemainingMs = rawRemainingMs;
          rec.lastStatus = task.status;
        } else if (task.status !== rec.lastStatus) {
          // Commit simulated delta on status change, then reset the baseline
          const delta = Math.max(0, now - rec.lastSeenAt);

          if (rec.lastStatus === BTaskStatus.Running) {
            rec.baseElapsedMs = (rec.baseElapsedMs ?? 0) + delta;
            if (rec.baseRemainingMs != null) {
              rec.baseRemainingMs = Math.max(0, rec.baseRemainingMs - delta);
            }
          }
          rec.lastSeenAt = now;
          rec.lastStatus = task.status;
        }
      }

      existingIds.delete(task.id);
    });

    // Cleanup records of tasks that no longer exist
    existingIds.forEach((id) => map.delete(id));
  }, [bTasks]);

  const computeDisplayElapsedMs = (task: BTask): number | undefined => {
    const rec = simTimingRef.current.get(task.id);
    const baseMs = rec?.baseElapsedMs;

    if (baseMs == null) return undefined;
    if (task.status === BTaskStatus.Running) {
      return baseMs + Math.max(0, Date.now() - (rec?.lastSeenAt ?? Date.now()));
    }

    return baseMs;
  };

  const computeDisplayRemainingMs = (task: BTask): number | undefined => {
    const rec = simTimingRef.current.get(task.id);
    const baseMs = rec?.baseRemainingMs;

    if (baseMs == null) return undefined;
    if (task.status === BTaskStatus.Running) {
      const ms =
        baseMs - Math.max(0, Date.now() - (rec?.lastSeenAt ?? Date.now()));

      return Math.max(0, ms);
    }

    return baseMs;
  };

  const columns = useRef<{ key: string; label: string }[]>(
    [
      {
        key: "name",
        label: "Task list",
      },
      {
        key: "status",
        label: "Status",
      },
      {
        key: "process",
        label: "Process",
      },
      {
        key: "progress",
        label: "Progress",
      },
      {
        key: "startedAt",
        label: "Started at",
      },
      {
        key: "elapsed",
        label: "Elapsed",
      },
      {
        key: "estimateRemainingTime",
        label: "Estimate remaining time",
      },
      {
        key: "nextTimeStartAt",
        label: "Next time start at",
      },
      {
        key: "operations",
        label: "Operations",
      },
    ].map((x) => ({
      ...x,
      label: t<string>(x.label),
    })),
  );

  useEffect(() => {
    statusRef.current = status;
  }, [status]);

  useEffect(() => {
    log("Initializing...");
    const queryTask = setInterval(() => {
      const tempTasks = useBTasksStore.getState().tasks;
      let newStatus = AssistantStatus.AllDone;

      if (tempTasks.length > 0) {
        const ongoingTasks = tempTasks.filter(
          (a) => a.status == BTaskStatus.Running,
        );

        if (ongoingTasks.length > 0) {
          newStatus = AssistantStatus.Working;
        } else {
          const failedTasks = tempTasks.filter(
            (a) => a.status == BTaskStatus.Error,
          );

          if (failedTasks.length > 0) {
            newStatus = AssistantStatus.Failed;
          } else {
            const doneTasks = tempTasks.filter(
              (a) => a.status == BTaskStatus.Completed,
            );

            if (doneTasks.length > 0) {
              newStatus = AssistantStatus.AllDone;
            }
          }
        }
      }
      if (newStatus != statusRef.current) {
        setStatus(newStatus);
        if (newStatus == AssistantStatus.AllDone) {
          setTimeout(() => {
            setAllDoneCircleDrawn("drawn");
          }, 300);
        }
      }
      // Tick every second to refresh simulated durations in UI
      setNowTick(Date.now());
    }, 1000);

    return () => {
      log("Destroying...");
      clearInterval(queryTask);
    };
  }, []);

  const renderTaskStatus = (task: BTask) => {
    switch (task.status) {
      case BTaskStatus.NotStarted:
        return (
          <Chip color={"warning"} size={"sm"} variant={"light"}>
            <ClockCircleOutlined className={"text-base"} />
          </Chip>
        );
      case BTaskStatus.Cancelled:
        return (
          <Chip color={"warning"} size={"sm"} variant={"light"}>
            <CloseOutlined className={"text-base"} />
          </Chip>
        );
      case BTaskStatus.Paused:
        return (
          <Chip color={"warning"} size={"sm"} variant={"light"}>
            <PauseCircleOutlined className={"text-base"} />
          </Chip>
        );
      case BTaskStatus.Error:
        return (
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              if (task.error) {
                createPortal(Modal, {
                  defaultVisible: true,
                  size: "xl",
                  classNames: {
                    wrapper: "floating-assistant-modal",
                  },
                  title: t<string>("Error"),
                  children: <pre>{task.error}</pre>,
                  footer: {
                    actions: ["cancel"],
                  },
                });
              }
            }}
          >
            <ExclamationCircleOutlined className={"text-base"} />
          </Button>
        );
      case BTaskStatus.Running:
        return (
          <Chip size={"sm"} variant={"light"}>
            <LoadingOutlined className={"text-base"} />
          </Chip>
        );
      case BTaskStatus.Completed:
        return (
          <Chip color={"success"} size={"sm"} variant={"light"}>
            <CheckCircleOutlined className={"text-base"} />
          </Chip>
        );
    }
  };

  const renderTaskOpts = (task: BTask) => {
    const opts: any[] = [];

    Object.keys(ActionsFilter).forEach((key) => {
      const filter = ActionsFilter[key as unknown as TaskAction];

      if (filter && filter(task)) {
        const action: TaskAction = parseInt(key, 10) as TaskAction;

        switch (action) {
          case TaskAction.Start:
            opts.push(
              <Button
                isIconOnly
                color={"secondary"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  BApi.backgroundTask.startBackgroundTask(task.id);
                }}
              >
                <CaretRightOutlined className={"text-base"} />
              </Button>,
            );
            break;
          case TaskAction.Pause:
            opts.push(
              <Button
                isIconOnly
                color={"warning"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  BApi.backgroundTask.pauseBackgroundTask(task.id);
                }}
              >
                <PauseOutlined className={"text-base"} />
              </Button>,
            );
            break;
          case TaskAction.Resume:
            opts.push(
              <Button
                isIconOnly
                color={"secondary"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  BApi.backgroundTask.resumeBackgroundTask(task.id);
                }}
              >
                <CaretRightOutlined className={"text-base"} />
              </Button>,
            );
            break;
          case TaskAction.Stop:
            opts.push(
              <Button
                isIconOnly
                color={"danger"}
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("Stopping task: {{taskName}}", {
                      taskName: task.name,
                    }),
                    children:
                      task.messageOnInterruption ??
                      t<string>("Are you sure to stop this task?"),
                    onOk: async () =>
                      await BApi.backgroundTask.stopBackgroundTask(task.id),
                    classNames: {
                      wrapper: "floating-assistant-modal",
                    },
                  });
                }}
              >
                <StopOutlined className={"text-base"} />
              </Button>,
            );
            break;
          case TaskAction.Clean:
            opts.push(
              <Button
                // color={'success'}
                isIconOnly
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  setCleaningTaskId(task.id);
                }}
              >
                <ClearOutlined className={"text-base"} />
              </Button>,
            );
            break;
          case TaskAction.Config:
            opts.push(
              <Button
                isIconOnly
                size={"sm"}
                variant={"light"}
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("About to leave current page"),
                    children: t<string>("Sure?"),
                    onOk: async () => {
                      navigate("/backgroundtask");
                    },
                    classNames: {
                      wrapper: "floating-assistant-modal",
                    },
                  });
                }}
              >
                <SettingOutlined className={"text-base"} />
              </Button>,
            );
            break;
        }
      }
    });

    return opts;
  };

  const renderTasks = () => {
    if (bTasks?.length > 0) {
      return (
        <Table
          isCompact
          isStriped
          removeWrapper
          aria-label="Example table with dynamic content"
        >
          <TableHeader columns={columns.current}>
            {(column) => (
              <TableColumn key={column.key}>{column.label}</TableColumn>
            )}
          </TableHeader>
          <TableBody>
            {bTasks.map((task) => {
              return (
                <TableRow
                  key={task.id}
                  className={`transition-opacity ${task.id == cleaningTaskId ? "opacity-0" : ""}`}
                  onTransitionEnd={(evt) => {
                    if (
                      evt.propertyName == "opacity" &&
                      task.id == cleaningTaskId
                    ) {
                      BApi.backgroundTask.cleanBackgroundTask(task.id);
                    }
                  }}
                >
                  <TableCell>
                    <div className={"flex items-center gap-1"}>
                      {task.name}
                      {task.isPersistent && (
                        <PushpinOutlined className={"text-base opacity-40"} />
                      )}
                      {task.description && (
                        <Tooltip color={"secondary"} content={task.description}>
                          <QuestionCircleOutlined
                            className={"text-base opacity-60"}
                          />
                        </Tooltip>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>{renderTaskStatus(task)}</TableCell>
                  <TableCell>
                    {task.process && (
                      <Chip color={"success"} variant={"light"}>
                        {task.process}
                      </Chip>
                    )}
                  </TableCell>
                  <TableCell>
                    <div className={"relative"}>
                      <Progress
                        color="primary"
                        size="sm"
                        value={task.percentage}
                      />
                      <div
                        className={
                          "absolute top-0 left-0 flex items-center justify-center w-full h-full"
                        }
                      >
                        {task.percentage}%
                      </div>
                    </div>
                  </TableCell>
                  <TableCell>
                    {task.startedAt && dayjs(task.startedAt).format("HH:mm:ss")}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      {(() => {
                        const ms = computeDisplayElapsedMs(task);

                        return ms != null
                          ? dayjs.duration(ms).format("HH:mm:ss")
                          : undefined;
                      })()}
                    </div>
                  </TableCell>
                  <TableCell>
                    {(() => {
                      const ms = computeDisplayRemainingMs(task);

                      return ms != null
                        ? dayjs.duration(ms).format("HH:mm:ss")
                        : undefined;
                    })()}
                  </TableCell>
                  <TableCell>
                    {task.nextTimeStartAt &&
                      dayjs(task.nextTimeStartAt).format("HH:mm:ss")}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      {renderTaskOpts(task)}
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      );
    }

    return (
      <div className="h-[80px] flex items-center justify-center">
        {t<string>("No background task")}
      </div>
    );
  };

  const clearableTasks = bTasks.filter(
    (t) =>
      !t.isPersistent &&
      (t.status == BTaskStatus.Completed ||
        t.status == BTaskStatus.Error ||
        t.status == BTaskStatus.Cancelled),
  );

  // log(tasks);

  return (
    <>
      <Popover
        trigger={
          <div
            ref={portalRef}
            className={`portal ${Object.keys(AssistantStatus)[status]} floating-assistant ${tasksVisible ? "" : "hide"}`}
          >
            {/* Working */}
            <div className="loader">
              <span />
            </div>
            {/* AllDone */}
            <div className="tick">
              <svg
                version="1.1"
                viewBox="0 0 37 37"
                x="0px"
                y="0px"
                enableBackground={'new 0 0 37 37'}
                // style="enable-background:new 0 0 37 37;"
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
            <div
              className={
                "failed flex items-center justify-center w-[48px] h-[48px] "
              }
            >
              <CloseCircleOutlined className={"text-5xl"} />
            </div>
          </div>
        }
        onOpenChange={(visible) => {
          setTasksVisible(visible);
        }}
      >
        <div className={"flex flex-col gap-2 p-2 min-w-[300px]"}>
          {/* <div className={'font-bold'}>{t<string>('Task list')}</div> */}
          {/* <Divider orientation={'horizontal'} /> */}
          <div className="flex flex-col gap-1 max-h-[600px] mt-2 overflow-auto">
            {renderTasks()}
          </div>
          <Divider orientation={"horizontal"} />
          <div className="flex items-center gap-2">
            {clearableTasks.length > 0 && (
              <Button
                size={"sm"}
                variant={"ghost"}
                onPress={() =>
                  BApi.backgroundTask.cleanInactiveBackgroundTasks()
                }
              >
                <ClearOutlined className={"text-base"} />
                {t<string>("Clear inactive tasks")}
              </Button>
            )}
          </div>
        </div>
      </Popover>
    </>
  );
};

FloatingAssistant.displayName = "FloatingAssistant";

export default FloatingAssistant;
