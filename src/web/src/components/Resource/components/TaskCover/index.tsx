"use client";

import type { BTask } from "@/core/models/BTask";

import React, { useEffect, useRef } from "react";
import { useTranslation } from "react-i18next";
import { CloseCircleOutlined } from "@ant-design/icons";
import diff from "deep-diff";
import { useUpdate } from "react-use";
import _ from "lodash";

import { BTaskResourceType, BTaskStatus } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Chip, Modal } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { BTaskStopButton } from "@/components/BTask";
import { useBTasksStore } from "@/stores/bTasks";

interface IProps {
  resource: any;
  reload?: () => any;
  onTasksChange?: (tasks?: BTask[]) => any;
}

const log = buildLogger("TaskCover");

enum Action {
  Update = 1,
  Reload = 2,
}
const TaskCover = ({ resource, reload, onTasksChange }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const tasksRef = useRef<BTask[]>([]);

  useEffect(() => {
    const unsubscribe = useBTasksStore.subscribe(() => {
      const tasks =
        useBTasksStore
          .getState()
          .tasks?.filter(
            (t) =>
              t.resourceType == BTaskResourceType.Resource &&
              t.resourceKeys?.some((x) => x == resource.id) &&
              (t.status == BTaskStatus.Running ||
                t.status == BTaskStatus.Paused ||
                t.status == BTaskStatus.Error ||
                t.status == BTaskStatus.NotStarted ||
                t.status == BTaskStatus.Completed),
          ) ?? [];

      if (tasks.length > 0 || tasksRef.current.length > 0) {
        const taskIds = _.uniq(tasks.concat(tasksRef.current).map((t) => t.id));
        const actions: Set<Action> = new Set();

        for (const taskId of taskIds) {
          const task = tasks.find((t) => t.id == taskId);
          const prevTask = tasksRef.current.find((t) => t.id == taskId);
          const differences = diff(prevTask, task);

          if (differences) {
            log(
              "TaskChanged",
              differences,
              "current: ",
              task,
              "previous: ",
              prevTask,
            );
            actions.add(Action.Update);

            if (
              prevTask?.status != task?.status &&
              task?.status == BTaskStatus.Completed
            ) {
              log("need reload");
              actions.add(Action.Reload);
            }
          }
        }
        if (actions.has(Action.Update)) {
          forceUpdate();
        }
        if (actions.has(Action.Reload)) {
          reload?.();
        }

        if (actions.size > 0) {
          onTasksChange?.(tasks);
        }

        tasksRef.current = tasks;
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  const displayingTask = _.sortBy(tasksRef.current, (x) => {
    switch (x.status) {
      case BTaskStatus.Error:
        return -1;
      case BTaskStatus.Running:
        return 0;
      case BTaskStatus.Paused:
        return 1;
      case BTaskStatus.NotStarted:
        return 2;
      case BTaskStatus.Completed:
      case BTaskStatus.Cancelled:
        return 999;
    }
  }).filter(
    (t) =>
      t.status == BTaskStatus.Running ||
      t.status == BTaskStatus.Paused ||
      t.status == BTaskStatus.Error ||
      t.status == BTaskStatus.NotStarted,
  )[0];

  if (!displayingTask) {
    return null;
  }

  return (
    <div
      className={
        "absolute top-0 left-0 z-20 w-full h-full flex flex-col items-center justify-center gap-3"
      }
    >
      <div
        className={"absolute top-0 left-0 bg-black opacity-80 w-full h-full"}
      />
      <div className={"font-bold z-20"}>{displayingTask.name}</div>
      {displayingTask.error ? (
        <div className={"font-bold z-20 flex items-center group"}>
          <Chip
            // size={'sm'}
            className={"cursor-pointer"}
            color={"danger"}
            variant={"light"}
            onClick={() =>
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Error"),
                size: "xl",
                children: <pre>{displayingTask.error}</pre>,
              })
            }
          >
            {t<string>("Error")}
          </Chip>
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              BApi.backgroundTask.cleanBackgroundTask(displayingTask.id);
            }}
          >
            <CloseCircleOutlined className={"text-sm"} />
          </Button>
        </div>
      ) : (
        <div className={"font-bold z-20"}>
          {displayingTask.status == BTaskStatus.NotStarted
            ? t<string>("Waiting")
            : `${displayingTask.percentage}%`}
        </div>
      )}
      {displayingTask.error ? null : (
        <div className={"font-bold z-20"}>
          <BTaskStopButton
            color={"danger"}
            id={displayingTask.id}
            size={"sm"}
          />
        </div>
      )}
    </div>
  );
};

TaskCover.displayName = "TaskCover";

export default TaskCover;
