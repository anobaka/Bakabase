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
  const { computeDisplayElapsedMs, computeDisplayRemainingMs } = useTaskTimingSimulation(tasks);

  const columns = useMemo(() => [
    { key: "name", label: t("Task list") },
    { key: "status", label: t("Status") },
    { key: "process", label: t("Process") },
    { key: "progress", label: t("Progress") },
    { key: "startedAt", label: t("Started at") },
    { key: "elapsed", label: t("Elapsed") },
    { key: "estimateRemainingTime", label: t("Estimate remaining time") },
    { key: "nextTimeStartAt", label: t("Next time start at") },
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
            <Button
              key={`${action}-${task.id}`}
              isIconOnly
              color="secondary"
              size="sm"
              variant="light"
              isLoading={isLoading}
              onPress={() => handleTaskAction(task, action)}
            >
              <CaretRightOutlined className="text-base" />
            </Button>
          );
          break;
        case TaskAction.Pause:
          actions.push(
            <Button
              key={`pause-${task.id}`}
              isIconOnly
              color="warning"
              size="sm"
              variant="light"
              isLoading={isLoading}
              onPress={() => handleTaskAction(task, action)}
            >
              <PauseOutlined className="text-base" />
            </Button>
          );
          break;
        case TaskAction.Stop:
          actions.push(
            <Button
              key={`stop-${task.id}`}
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
          );
          break;
        case TaskAction.Clean:
          actions.push(
            <Button
              key={`clean-${task.id}`}
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => setCleaningTaskId(task.id)}
            >
              <ClearOutlined className="text-base" />
            </Button>
          );
          break;
        case TaskAction.Config:
          actions.push(
            <Button
              key={`config-${task.id}`}
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
          );
          break;
      }
    });

    return actions;
  };

  if (tasks.length === 0) {
    return (
      <div className="h-[80px] flex items-center justify-center">
        {t("No background task")}
      </div>
    );
  }

  return (
    <Table isCompact isStriped removeWrapper aria-label="Background tasks">
      <TableHeader columns={columns}>
        {(column) => <TableColumn key={column.key}>{column.label}</TableColumn>}
      </TableHeader>
      <TableBody>
        {tasks.map((task) => (
          <TableRow
            key={task.id}
            className={`transition-opacity ${task.id === cleaningTaskId ? "opacity-0" : ""}`}
            onTransitionEnd={(evt) => {
              if (evt.propertyName === "opacity" && task.id === cleaningTaskId) {
                BApi.backgroundTask.cleanBackgroundTask(task.id);
              }
            }}
          >
            <TableCell>
              <div className="flex items-center gap-1">
                {task.name}
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
              </div>
            </TableCell>
            <TableCell>
              <TaskStatusIcon task={task} onShowError={() => handleShowError(task)} />
            </TableCell>
            <TableCell>
              {task.process && (
                <Chip color="success" variant="light">{task.process}</Chip>
              )}
            </TableCell>
            <TableCell>
              <div className="relative">
                <Progress color="primary" size="sm" value={task.percentage} />
                <div className="absolute top-0 left-0 flex items-center justify-center w-full h-full">
                  {task.percentage}%
                </div>
              </div>
            </TableCell>
            <TableCell>
              {task.startedAt && dayjs(task.startedAt).format("HH:mm:ss")}
            </TableCell>
            <TableCell>
              {(() => {
                const ms = computeDisplayElapsedMs(task);
                return ms != null ? dayjs.duration(ms).format("HH:mm:ss") : undefined;
              })()}
            </TableCell>
            <TableCell>
              {(() => {
                const ms = computeDisplayRemainingMs(task);
                return ms != null ? dayjs.duration(ms).format("HH:mm:ss") : undefined;
              })()}
            </TableCell>
            <TableCell>
              {task.nextTimeStartAt && dayjs(task.nextTimeStartAt).format("HH:mm:ss")}
            </TableCell>
            <TableCell>
              <div className="flex items-center gap-1">
                {renderTaskActions(task)}
              </div>
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
