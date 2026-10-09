"use client";

import type { BTask } from "@/core/models/BTask";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { SettingOutlined } from "@ant-design/icons";
import dayjs from "dayjs";
import moment from "moment";

import ScheduleModal from "./ScheduleModal";
import "./index.scss";

import { Button } from "@/components/bakaui";
import { formatDuration } from "@/components/bakaui/components/DurationInput";
import { TaskTable } from "@/components/FloatingAssistant/components/TaskTable";
import { useBTasksStore } from "@/stores/bTasks";

export default function BackgroundTaskPage() {
  const { t } = useTranslation();
  const tasks = useBTasksStore((state) => state.tasks);
  const [editing, setEditing] = useState<BTask>();

  return (
    <div className="background-task-page flex min-w-0 flex-col gap-3">
      <header>
        <h1 className="text-lg font-semibold">{t("backgroundTask.page.title")}</h1>
        <p className="mt-1 text-xs leading-relaxed text-default-500">
          {t("backgroundTask.page.description")}
        </p>
      </header>
      <TaskTable
        presentation="page"
        renderSchedule={(task) => {
          if (!task.isPersistent)
            return (
              <span className="text-xs text-default-400">
                {t("backgroundTask.schedule.oneOff")}
              </span>
            );
          const next = task.nextTimeStartAt;
          const at =
            next && task.enableAfter && dayjs(task.enableAfter).isAfter(next)
              ? task.enableAfter
              : next;

          return (
            <Button
              aria-label={t("backgroundTask.label.scheduleFor", { taskName: task.name })}
              className="h-auto min-h-8 min-w-0 justify-start gap-2 px-1.5 py-1 text-left"
              size="sm"
              variant="light"
              onPress={() => setEditing(task)}
            >
              <span className="min-w-0 text-xs">
                <span className="block text-default-600">
                  {task.interval
                    ? t("backgroundTask.schedule.every", {
                        interval: formatDuration(moment.duration(task.interval).asSeconds(), t),
                      })
                    : t("backgroundTask.schedule.notRecurring")}
                </span>
                {at ? (
                  <span
                    className="block tabular-nums text-default-400"
                    title={dayjs(at).format("YYYY-MM-DD HH:mm:ss")}
                  >
                    {t("backgroundTask.schedule.next", {
                      time: dayjs(at).format("MM-DD HH:mm:ss"),
                    })}
                  </span>
                ) : task.enableAfter ? (
                  <span className="block tabular-nums text-default-400">
                    {t("backgroundTask.schedule.after", {
                      time: dayjs(task.enableAfter).format("MM-DD HH:mm"),
                    })}
                  </span>
                ) : null}
              </span>
              <SettingOutlined aria-hidden className="shrink-0 text-default-400" />
            </Button>
          );
        }}
        tasks={tasks}
      />
      {editing && (
        <ScheduleModal key={editing.id} task={editing} onClose={() => setEditing(undefined)} />
      )}
    </div>
  );
}
