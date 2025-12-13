import { useState, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import dayjs from "dayjs";
import {
  CaretRightOutlined,
  CheckCircleOutlined,
  ClearOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  PauseCircleOutlined,
  PauseOutlined,
  PushpinOutlined,
  QuestionCircleOutlined,
  SettingOutlined,
  StopOutlined,
  CloseOutlined,
} from "@ant-design/icons";
import {
  Button,
  Chip,
  Modal,
  Progress,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
  toast,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { BTaskStatus } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import type { BTask } from "@/core/models/BTask";
import { TaskAction, ActionsFilter } from "../constants";
import { useTaskTimingSimulation } from "../hooks/useTaskTimingSimulation";

interface TaskTableProps {
  tasks: BTask[];
}

type TaskFilter = "all" | "running" | "pending" | "completed" | "failed";

const TaskStatusIcon = ({ task, onShowError }: { task: BTask; onShowError: () => void }) => {
  switch (task.status) {
    case BTaskStatus.NotStarted:
      return (
        <Chip color="warning" size="sm" variant="light">
          <ClockCircleOutlined className="text-base" />
        </Chip>
      );
    case BTaskStatus.Cancelled:
      return (
        <Chip color="warning" size="sm" variant="light">
          <CloseOutlined className="text-base" />
        </Chip>
      );
    case BTaskStatus.Paused:
      return (
        <Chip color="warning" size="sm" variant="light">
          <PauseCircleOutlined className="text-base" />
        </Chip>
      );
    case BTaskStatus.Error:
      return (
        <Tooltip
          color="danger"
          content={task.briefError || task.error?.substring(0, 100) || "Error"}
          placement="top"
        >
          <Button
            isIconOnly
            color="danger"
            size="sm"
            variant="light"
            onPress={onShowError}
          >
            <ExclamationCircleOutlined className="text-base" />
          </Button>
        </Tooltip>
      );
    case BTaskStatus.Running:
      return (
        <Chip size="sm" variant="light">
          <LoadingOutlined className="text-base" />
        </Chip>
      );
    case BTaskStatus.Completed:
      return (
        <Chip color="success" size="sm" variant="light">
          <CheckCircleOutlined className="text-base" />
        </Chip>
      );
    default:
      return null;
  }
};

export function TaskTable({ tasks }: TaskTableProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();
  const [cleaningTaskId, setCleaningTaskId] = useState<string | undefined>();
  const [loadingTasks, setLoadingTasks] = useState<Map<string, TaskAction>>(new Map());
  const [filter, setFilter] = useState<TaskFilter>("all");
  const { computeDisplayElapsedMs, computeDisplayRemainingMs } = useTaskTimingSimulation(tasks);

  // Task counts by status
  const taskCounts = useMemo(() => {
    const counts = {
      all: tasks.length,
      running: 0,
      pending: 0,
      completed: 0,
      failed: 0,
    };
    tasks.forEach((task) => {
      switch (task.status) {
        case BTaskStatus.Running:
          counts.running++;
          break;
        case BTaskStatus.NotStarted:
        case BTaskStatus.Paused:
          counts.pending++;
          break;
        case BTaskStatus.Completed:
          counts.completed++;
          break;
        case BTaskStatus.Error:
        case BTaskStatus.Cancelled:
          counts.failed++;
          break;
      }
    });
    return counts;
  }, [tasks]);

  // Filter and sort tasks (running first, then by status)
  const filteredTasks = useMemo(() => {
    let result = tasks;

    // Apply filter
    if (filter !== "all") {
      result = tasks.filter((task) => {
        switch (filter) {
          case "running":
            return task.status === BTaskStatus.Running;
          case "pending":
            return task.status === BTaskStatus.NotStarted || task.status === BTaskStatus.Paused;
          case "completed":
            return task.status === BTaskStatus.Completed;
          case "failed":
            return task.status === BTaskStatus.Error || task.status === BTaskStatus.Cancelled;
          default:
            return true;
        }
      });
    }

    // Sort: running first, then errors, then others
    return [...result].sort((a, b) => {
      const order = {
        [BTaskStatus.Running]: 0,
        [BTaskStatus.Error]: 1,
        [BTaskStatus.Paused]: 2,
        [BTaskStatus.NotStarted]: 3,
        [BTaskStatus.Completed]: 4,
        [BTaskStatus.Cancelled]: 5,
      };
      return (order[a.status] ?? 99) - (order[b.status] ?? 99);
    });
  }, [tasks, filter]);

  const columns = useMemo(() => [
    { key: "name", label: t("Task list") },
    { key: "status", label: t("Status") },
    { key: "progress", label: t("Progress") },
    { key: "time", label: t("Time") },
    { key: "remaining", label: t("Remaining") },
    { key: "operations", label: t("Operations") },
  ], [t]);

  const handleShowError = (task: BTask) => {
    if (task.error) {
      createPortal(Modal, {
        defaultVisible: true,
        size: "xl",
        classNames: { wrapper: "floating-assistant-modal" },
        title: t("Error"),
        children: <pre>{task.error}</pre>,
        footer: { actions: ["cancel"] },
      });
    }
  };

  const setTaskLoading = (taskId: string, action: TaskAction) => {
    setLoadingTasks(new Map(loadingTasks.set(taskId, action)));
  };

  const clearTaskLoading = (taskId: string) => {
    setLoadingTasks((prev) => {
      const next = new Map(prev);
      next.delete(taskId);
      return next;
    });
  };

  const getActionLabel = (action: TaskAction): string => {
    switch (action) {
      case TaskAction.Start: return t("Started");
      case TaskAction.Pause: return t("Paused");
      case TaskAction.Resume: return t("Resumed");
      case TaskAction.Stop: return t("Stopped");
      default: return "";
    }
  };

  const handleTaskAction = async (task: BTask, action: TaskAction) => {
    setTaskLoading(task.id, action);
    try {
      switch (action) {
        case TaskAction.Start:
          await BApi.backgroundTask.startBackgroundTask(task.id);
          break;
        case TaskAction.Pause:
          await BApi.backgroundTask.pauseBackgroundTask(task.id);
          break;
        case TaskAction.Resume:
          await BApi.backgroundTask.resumeBackgroundTask(task.id);
          break;
        case TaskAction.Stop:
          await BApi.backgroundTask.stopBackgroundTask(task.id);
          break;
      }
      toast.success({ title: `${task.name}: ${getActionLabel(action)}` });
    } catch (error) {
      const message = error instanceof Error ? error.message : t("Unknown error");
      toast.danger({ title: `${task.name}: ${message}` });
    } finally {
      clearTaskLoading(task.id);
    }
  };

  const renderTaskActions = (task: BTask) => {
    const actions: React.ReactNode[] = [];

    Object.entries(ActionsFilter).forEach(([key, filter]) => {
      if (!filter(task)) return;
      const action = parseInt(key, 10) as TaskAction;
      const isLoading = loadingTasks.get(task.id) === action;

      switch (action) {
        case TaskAction.Start:
        case TaskAction.Resume:
          actions.push(
            <Tooltip key={`${action}-${task.id}`} content={action === TaskAction.Start ? t("Start") : t("Resume")} placement="top">
              <Button
                isIconOnly
                color="secondary"
                size="sm"
                variant="light"
                isLoading={isLoading}
                onPress={() => handleTaskAction(task, action)}
              >
                <CaretRightOutlined className="text-base" />
              </Button>
            </Tooltip>
          );
          break;
        case TaskAction.Pause:
          actions.push(
            <Tooltip key={`pause-${task.id}`} content={t("Pause")} placement="top">
              <Button
                isIconOnly
                color="warning"
                size="sm"
                variant="light"
                isLoading={isLoading}
                onPress={() => handleTaskAction(task, action)}
              >
                <PauseOutlined className="text-base" />
              </Button>
            </Tooltip>
          );
          break;
        case TaskAction.Stop:
          actions.push(
            <Tooltip key={`stop-${task.id}`} content={t("Stop")} placement="top" color="danger">
              <Button
                isIconOnly
                color="danger"
                size="sm"
                variant="light"
                isLoading={isLoading}
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t("Stopping task: {{taskName}}", { taskName: task.name }),
                    children: task.messageOnInterruption ?? t("Are you sure to stop this task?"),
                    onOk: () => handleTaskAction(task, TaskAction.Stop),
                    classNames: { wrapper: "floating-assistant-modal" },
                  });
                }}
              >
                <StopOutlined className="text-base" />
              </Button>
            </Tooltip>
          );
          break;
        case TaskAction.Clean:
          actions.push(
            <Tooltip key={`clean-${task.id}`} content={t("Remove from list")} placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => setCleaningTaskId(task.id)}
              >
                <ClearOutlined className="text-base" />
              </Button>
            </Tooltip>
          );
          break;
        case TaskAction.Config:
          actions.push(
            <Tooltip key={`config-${task.id}`} content={t("Configure")} placement="top">
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t("About to leave current page"),
                    children: t("Sure?"),
                    onOk: () => navigate("/background-task"),
                    classNames: { wrapper: "floating-assistant-modal" },
                  });
                }}
              >
                <SettingOutlined className="text-base" />
              </Button>
            </Tooltip>
          );
          break;
      }
    });

    return actions;
  };

  const filterOptions: { key: TaskFilter; label: string; color?: "default" | "primary" | "success" | "danger" | "warning" }[] = [
    { key: "all", label: t("All") },
    { key: "running", label: t("Running"), color: "primary" },
    { key: "pending", label: t("Pending"), color: "warning" },
    { key: "completed", label: t("Completed"), color: "success" },
    { key: "failed", label: t("Failed"), color: "danger" },
  ];

  if (tasks.length === 0) {
    return (
      <div className="h-[80px] flex items-center justify-center">
        {t("No background task")}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {/* Filter chips */}
      <div className="flex flex-wrap gap-1">
        {filterOptions.map((option) => {
          const count = taskCounts[option.key];
          const isActive = filter === option.key;
          return (
            <Chip
              key={option.key}
              size="sm"
              variant={isActive ? "solid" : "flat"}
              color={isActive ? (option.color ?? "default") : "default"}
              className="cursor-pointer"
              onClick={() => setFilter(option.key)}
            >
              {option.label} ({count})
            </Chip>
          );
        })}
      </div>

      <Table isCompact isStriped removeWrapper aria-label="Background tasks">
        <TableHeader columns={columns}>
          {(column) => <TableColumn key={column.key}>{column.label}</TableColumn>}
        </TableHeader>
        <TableBody emptyContent={t("No tasks match the filter")}>
          {filteredTasks.map((task) => (
          <TableRow
            key={task.id}
            className={`transition-opacity ${task.id === cleaningTaskId ? "opacity-0" : ""} ${task.status === BTaskStatus.Error ? "task-row-error" : ""}`}
            onTransitionEnd={(evt) => {
              if (evt.propertyName === "opacity" && task.id === cleaningTaskId) {
                BApi.backgroundTask.cleanBackgroundTask(task.id);
              }
            }}
          >
            {/* Name */}
            <TableCell>
              <div className="flex items-center gap-1">
                <span className="truncate max-w-[200px]">{task.name}</span>
                {task.isPersistent && (
                  <Tooltip color="secondary" content={t("Persistent scheduled task")}>
                    <PushpinOutlined className="text-base opacity-40" />
                  </Tooltip>
                )}
                {task.description && (
                  <Tooltip color="secondary" content={task.description}>
                    <QuestionCircleOutlined className="text-base opacity-60" />
                  </Tooltip>
                )}
                {task.process && (
                  <Chip color="success" variant="light" size="sm">{task.process}</Chip>
                )}
              </div>
            </TableCell>
            {/* Status */}
            <TableCell>
              <div className="flex items-center gap-1">
                <TaskStatusIcon task={task} onShowError={() => handleShowError(task)} />
                {task.status === BTaskStatus.Error && task.briefError && (
                  <span className="text-xs text-danger truncate max-w-[100px]" title={task.briefError}>
                    {task.briefError}
                  </span>
                )}
              </div>
            </TableCell>
            {/* Progress */}
            <TableCell>
              <div className="relative min-w-[60px]">
                <Progress
                  color="primary"
                  size="sm"
                  value={task.percentage}
                  className={task.status === BTaskStatus.Running ? "task-progress-animated" : ""}
                  classNames={{
                    indicator: task.status === BTaskStatus.Running ? "progress-bar-indicator" : "",
                  }}
                />
                <div className="absolute top-0 left-0 flex items-center justify-center w-full h-full text-xs">
                  {task.percentage}%
                </div>
              </div>
            </TableCell>
            {/* Time - combined started + elapsed */}
            <TableCell>
              <div className="flex flex-col text-xs whitespace-nowrap">
                {task.startedAt && (
                  <span className="text-default-500">{dayjs(task.startedAt).format("HH:mm:ss")}</span>
                )}
                {(() => {
                  const ms = computeDisplayElapsedMs(task);
                  return ms != null ? (
                    <span className="font-medium">{dayjs.duration(ms).format("HH:mm:ss")}</span>
                  ) : null;
                })()}
              </div>
            </TableCell>
            {/* Remaining */}
            <TableCell>
              {(() => {
                const ms = computeDisplayRemainingMs(task);
                if (ms == null) {
                  // Show next run time for persistent tasks
                  if (task.nextTimeStartAt) {
                    return (
                      <Tooltip content={t("Next run")} placement="top">
                        <span className="text-xs text-default-500">{dayjs(task.nextTimeStartAt).format("HH:mm")}</span>
                      </Tooltip>
                    );
                  }
                  return null;
                }
                const remaining = dayjs.duration(ms).format("HH:mm:ss");
                const estimatedEndTime = dayjs().add(ms, "millisecond").format("HH:mm");
                return (
                  <Tooltip content={`${t("Estimated completion")}: ${estimatedEndTime}`} placement="top">
                    <span className="cursor-help text-xs">{remaining}</span>
                  </Tooltip>
                );
              })()}
            </TableCell>
            {/* Operations */}
            <TableCell>
              <div className="flex items-center gap-1">
                {renderTaskActions(task)}
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
    </div>
  );
}
