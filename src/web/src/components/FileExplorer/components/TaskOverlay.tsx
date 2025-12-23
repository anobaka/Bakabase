"use client";

import type { BTask } from "@/core/models/BTask";

import { useTranslation } from "react-i18next";

import { Spinner } from "@/components/bakaui";
import { BTaskStatus } from "@/sdk/constants";
import { BTaskStopButton } from "@/components/BTask";

type TaskOverlayProps = {
  task: BTask;
};

const TaskOverlay = ({ task }: TaskOverlayProps) => {
  const { t } = useTranslation();

  if (task.error) {
    return null;
  }

  return (
    <div className="running-task-cover absolute inset-0 z-[1] bg-white/30 backdrop-blur-[2px]">
      <div className="absolute inset-0 flex items-center justify-center">
        <div
          className="absolute left-0 top-0 h-full transition-[width] duration-300 ease-out"
          style={{
            width: `${task.percentage}%`,
            background: "linear-gradient(90deg, rgba(105, 226, 248, 0.15), rgba(105, 226, 248, 0.3))",
          }}
        />
        <Spinner size="sm" />
        &nbsp;
        <div className="bg-[var(--theme-body-background)] px-6 py-1 rounded-md text-xs font-medium shadow-md">
          {task.name}
          &nbsp;
          {task.status == BTaskStatus.NotStarted
            ? t<string>("Waiting")
            : `${task.percentage}%`}
        </div>
      </div>
      <div className="stop absolute inset-0 hidden items-center justify-center hover:flex">
        <BTaskStopButton color={"warning"} id={task.id} size={"small"} />
      </div>
    </div>
  );
};

TaskOverlay.displayName = "TaskOverlay";

export default TaskOverlay;
