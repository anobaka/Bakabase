"use client";

import type { BTask } from "@/core/models/BTask";

import React, { useEffect, useState } from "react";
// import './index.scss';
import { useTranslation } from "react-i18next";
import { ClockCircleOutlined, QuestionCircleOutlined } from "@ant-design/icons";
import dayjs from "dayjs";
import moment from "moment";
import toast from "react-hot-toast";

import {
  Button,
  DateInput,
  Progress,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  TimeInput,
  Tooltip,
} from "@/components/bakaui";
import { BTaskStatus } from "@/sdk/constants";
import { useTaskOptionsStore } from "@/models/options";
import { useBTasksStore } from "@/models/bTasks";

export default () => {
  const { t } = useTranslation();
  const taskOptions = useTaskOptionsStore((state) => state.data);
  const bTasks = useBTasksStore((state) => state.tasks);

  const [editingOptions, setEditingOptions] = useState<
    Record<string, { interval?: string; enableAfter?: string }>
  >({});

  const columns = [
    {
      key: "name",
      label: "Name",
    },
    {
      key: "status",
      label: "Status",
    },
    {
      key: "progress",
      label: "Progress",
    },
    {
      key: "startedAt",
      label: "Started At",
    },
    {
      key: "interval",
      label: "Interval",
    },
    {
      key: "nextTimeStartAt",
      label: "Next time start at",
    },
    {
      key: "elapsed",
      label: "Elapsed",
    },
    {
      key: "enableAfter",
      label: "Enable after",
    },
  ];

  useEffect(() => {}, []);

  const patchOptions = async () => {
    await BApi.options.patchTaskOptions({
      tasks: bTasks
        .filter((t) => t.isPersistent)
        .map((t) => {
          return {
            id: t.id,
            interval: editingOptions[t.id]?.interval ?? t.interval!,
            enableAfter: editingOptions[t.id]?.enableAfter ?? t.enableAfter,
          };
        }),
    });
    toast.success(t<string>("Saved"));
    setEditingOptions({});
  };

  const renderInterval = (task: BTask) => {
    if (!task.isPersistent) {
      return t<string>("Not supported");
    }

    const editingInterval = editingOptions[task.id]?.interval;

    if (editingInterval) {
      return (
        <TimeInput
          autoFocus
          granularity={"second"}
          size={"sm"}
          value={dayjs.duration(
            moment.duration(editingInterval).asMilliseconds(),
          )}
          onBlur={() => {
            patchOptions();
          }}
          onChange={(v) => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                interval: v.format("HH:mm:ss"),
              },
            });
          }}
        />
      );
    } else {
      return (
        <div className={"flex items-center gap-1"}>
          <div>
            <Button
              onPress={() => {
                setEditingOptions({
                  ...editingOptions,
                  [task.id]: {
                    interval: task.interval ?? '00:05:00',
                  },
                });
              }}
              variant={'light'}
              // color={'primary'}
              size={'sm'}
            >
              {task.interval
                ? dayjs
                    .duration(moment.duration(task.interval).asMilliseconds())
                    .format("HH:mm:ss")
                : t<string>("Not set")}
            </Button>
          </div>
          {task.interval && (
            <Tooltip
              content={
                <div>
                  {t<string>("Will start at")}
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
    }
  };

  const renderEnableAfter = (task: BTask) => {
    if (!task.isPersistent) {
      return t<string>("Not supported");
    }

    const format = "YYYY-MM-DD HH:mm:ss";

    const editingEnableAfter = editingOptions[task.id]?.enableAfter;

    if (editingEnableAfter) {
      return (
        <DateInput
          autoFocus
          granularity={"second"}
          size={"sm"}
          value={dayjs(editingEnableAfter)}
          onBlur={() => {
            patchOptions();
          }}
          onChange={(v) => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                enableAfter: v?.format(format),
              },
            });
          }}
        />
      );
    } else {
      return (
        <Button
          onPress={() => {
            const initValue = task.enableAfter ? dayjs(task.enableAfter) : dayjs().add(1, 'h');
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                enableAfter: initValue.format(format),
              },
            });
          }}
          variant={task.enableAfter ? 'light' : 'flat'}
          // color={'primary'}
          size={'sm'}
        >
          {task.enableAfter
            ? dayjs(task.enableAfter).format(format)
            : t<string>("Not set")}
        </Button>
      );
    }
  };

  return (
    <Table
      isStriped
      removeWrapper
      aria-label="Example table with dynamic content"
    >
      <TableHeader columns={columns}>
        {(column) => <TableColumn key={column.key}>{column.label}</TableColumn>}
      </TableHeader>
      <TableBody>
        {bTasks.map((task) => {
          return (
            <TableRow key={task.id}>
              <TableCell>
                <div className={"flex items-center gap-1"}>
                  {task.name}
                  {task.description && (
                    <Tooltip color={"secondary"} content={task.description}>
                      <QuestionCircleOutlined className={"text-base"} />
                    </Tooltip>
                  )}
                </div>
              </TableCell>
              <TableCell>
                {t<string>(BTaskStatus[task.status])}
                {task.error && (
                  <Tooltip color={"danger"} content={task.error}>
                    <QuestionCircleOutlined />
                  </Tooltip>
                )}
                {task.reasonForUnableToStart && (
                  <Tooltip
                    color={"warning"}
                    content={task.reasonForUnableToStart}
                  >
                    <QuestionCircleOutlined className={"text-base"} />
                  </Tooltip>
                )}
              </TableCell>
              <TableCell>
                <div className={"relative"}>
                  <Progress color="primary" size="sm" value={task.percentage} />
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
                {task.startedAt &&
                  dayjs(task.startedAt).format("YYYY-MM-DD HH:mm:ss")}
              </TableCell>
              <TableCell>{renderInterval(task)}</TableCell>
              <TableCell>
                {task.nextTimeStartAt &&
                  dayjs(task.nextTimeStartAt).format("YYYY-MM-DD HH:mm:ss")}
              </TableCell>
              <TableCell>
                {task.elapsed &&
                  dayjs
                    .duration(moment.duration(task.elapsed).asMilliseconds())
                    .format("HH:mm:ss")}
              </TableCell>
              <TableCell>{renderEnableAfter(task)}</TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
};
