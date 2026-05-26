"use client";

import type { BTask } from "@/core/models/BTask";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { ClockCircleOutlined, QuestionCircleOutlined } from "@ant-design/icons";
import dayjs from "dayjs";
import moment from "moment";
import toast from "react-hot-toast";

import { Button, Card, CardBody, DateInput, TimeInput, Tooltip } from "@/components/bakaui";
import { TaskTable } from "@/components/FloatingAssistant/components/TaskTable";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks";

type EditingValue = { interval?: string; enableAfter?: string };

const BackgroundTaskPage = () => {
  const { t } = useTranslation();
  const bTasks = useBTasksStore((state) => state.tasks);
  const [editingOptions, setEditingOptions] = useState<Record<string, EditingValue>>({});

  // Only persistent tasks have a schedule. Surface them in a dedicated panel
  // beneath the live TaskTable so users can tweak the cadence without losing
  // sight of what the daemon is doing right now.
  const persistentTasks = bTasks.filter((t) => t.isPersistent);

  const patchOptions = async () => {
    await BApi.options.patchTaskOptions({
      tasks: persistentTasks.map((task) => ({
        id: task.id,
        interval: editingOptions[task.id]?.interval ?? task.interval!,
        enableAfter: editingOptions[task.id]?.enableAfter ?? task.enableAfter,
      })),
    });
    toast.success(t<string>("common.state.saved"));
    setEditingOptions({});
  };

  const editTask = (taskId: string, patch: EditingValue) => {
    setEditingOptions((prev) => ({ ...prev, [taskId]: { ...prev[taskId], ...patch } }));
  };

  const renderInterval = (task: BTask) => {
    const editingInterval = editingOptions[task.id]?.interval;

    if (editingInterval !== undefined) {
      return (
        <TimeInput
          granularity={"second"}
          size={"sm"}
          value={dayjs.duration(moment.duration(editingInterval).asMilliseconds())}
          onBlur={() => patchOptions()}
          onChange={(v) => {
            if (v) editTask(task.id, { interval: v.format("HH:mm:ss") });
          }}
        />
      );
    }

    return (
      <div className={"flex items-center gap-1"}>
        <Button
          size={"sm"}
          variant={"light"}
          onPress={() => editTask(task.id, { interval: task.interval ?? "00:05:00" })}
        >
          {task.interval
            ? dayjs.duration(moment.duration(task.interval).asMilliseconds()).format("HH:mm:ss")
            : t<string>("backgroundTask.label.notSet")}
        </Button>
        {task.interval && task.nextTimeStartAt && (
          <Tooltip
            content={
              <div>
                {t<string>("backgroundTask.label.willStartAt")}
                &nbsp;
                {dayjs(task.nextTimeStartAt).format("YYYY-MM-DD HH:mm:ss")}
              </div>
            }
          >
            <ClockCircleOutlined className={"text-base"} />
          </Tooltip>
        )}
      </div>
    );
  };

  const renderEnableAfter = (task: BTask) => {
    const format = "YYYY-MM-DD HH:mm:ss";
    const editingEnableAfter = editingOptions[task.id]?.enableAfter;

    if (editingEnableAfter !== undefined) {
      return (
        <DateInput
          granularity={"second"}
          size={"sm"}
          value={dayjs(editingEnableAfter)}
          onBlur={() => patchOptions()}
          onChange={(v) => editTask(task.id, { enableAfter: v?.format(format) })}
        />
      );
    }

    return (
      <Button
        size={"sm"}
        variant={task.enableAfter ? "light" : "flat"}
        onPress={() => {
          const initValue = task.enableAfter ? dayjs(task.enableAfter) : dayjs().add(1, "h");

          editTask(task.id, { enableAfter: initValue.format(format) });
        }}
      >
        {task.enableAfter
          ? dayjs(task.enableAfter).format(format)
          : t<string>("backgroundTask.label.notSet")}
      </Button>
    );
  };

  return (
    <div className={"flex flex-col gap-4"}>
      <TaskTable tasks={bTasks} />

      {persistentTasks.length > 0 && (
        <Card>
          <CardBody>
            <div className={"flex items-center gap-2 mb-3"}>
              <h3 className={"text-base font-medium"}>
                {t<string>("backgroundTask.label.schedule")}
              </h3>
              <Tooltip color={"secondary"} content={t<string>("backgroundTask.label.scheduleHint")}>
                <QuestionCircleOutlined className={"text-base opacity-60"} />
              </Tooltip>
            </div>

            <div
              className={"grid grid-cols-[minmax(0,1fr)_auto_auto] gap-x-4 gap-y-2 items-center"}
            >
              <div className={"text-xs text-default-500"}>
                {t<string>("backgroundTask.column.name")}
              </div>
              <div className={"text-xs text-default-500"}>
                {t<string>("backgroundTask.column.interval")}
              </div>
              <div className={"text-xs text-default-500"}>
                {t<string>("backgroundTask.column.enableAfter")}
              </div>
              {persistentTasks.map((task) => (
                <React.Fragment key={task.id}>
                  <div className={"flex items-center gap-1 min-w-0"}>
                    <span className={"truncate"}>{task.name}</span>
                    {task.description && (
                      <Tooltip color={"secondary"} content={task.description}>
                        <QuestionCircleOutlined className={"text-base opacity-60"} />
                      </Tooltip>
                    )}
                  </div>
                  <div>{renderInterval(task)}</div>
                  <div>{renderEnableAfter(task)}</div>
                </React.Fragment>
              ))}
            </div>
          </CardBody>
        </Card>
      )}
    </div>
  );
};

BackgroundTaskPage.displayName = "BackgroundTaskPage";

export default BackgroundTaskPage;
