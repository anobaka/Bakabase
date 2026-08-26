"use client";

import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition } from "@/sdk/Api";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import moment from "moment";
import { ControlledMenu, MenuItem, useMenuState } from "@szhsin/react-menu";
import { useUpdate, useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";
import {
  AiOutlineAim,
  AiOutlineDelete,
  AiOutlineEdit,
  AiOutlineEllipsis,
  AiOutlineExport,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlinePlusCircle,
  AiOutlineRedo,
  AiOutlineSearch,
  AiOutlineSetting,
  AiOutlineStop,
  AiOutlineWarning,
} from "react-icons/ai";
import { MdPlayCircle, MdAccessTime, MdDelete } from "react-icons/md";

import { ThirdPartyId } from "@/sdk/constants";
import {
  Button,
  ButtonGroup,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Listbox,
  ListboxItem,
  Modal,
  toast,
  Tooltip,
} from "@/components/bakaui";
import "@szhsin/react-menu/dist/index.css";
import "@szhsin/react-menu/dist/transitions/slide.css";
import {
  DownloadTaskAction,
  DownloadTaskActionOnConflict,
  DownloadTaskStatus,
  downloadTaskStatuses,
  ResponseCode,
} from "@/sdk/constants";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import Configurations from "@/pages/downloader/components/Configurations";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import RequestStatistics from "@/pages/downloader/components/RequestStatistics";

import DownloadTaskDetailModal from "./components/TaskDetailModal";
import BatchEditModal from "./components/BatchEditModal";

import { toAbsoluteBackendUrl } from "@/config/env.ts";

import { CircularProgress } from "@heroui/react";

import { DownloadTaskTypeIconMap } from "./components/TaskDetailModal/models";

/** Row height handed to the listbox virtualizer; also how "locate" computes a scroll offset. */
const TASK_ITEM_HEIGHT = 75;

/** Statuses that count as "where the queue is right now", most specific first. */
const ACTIVE_STATUSES: DownloadTaskStatus[] = [
  DownloadTaskStatus.Downloading,
  DownloadTaskStatus.Starting,
  DownloadTaskStatus.Stopping,
  DownloadTaskStatus.InQueue,
];

/**
 * The virtualized listbox owns its own scroller, and HeroUI exposes no imperative
 * scroll API for it, so find the scrolling element by inspecting the subtree.
 */
const findScrollContainer = (root: HTMLElement | null): HTMLElement | null => {
  if (!root) return null;

  const candidates = [root, ...Array.from(root.querySelectorAll<HTMLElement>("*"))];

  return (
    candidates.find((el) => {
      const overflowY = getComputedStyle(el).overflowY;

      return (overflowY === "auto" || overflowY === "scroll") && el.scrollHeight > el.clientHeight;
    }) ?? null
  );
};

// const testTasks: DownloadTask[] = [
//   {
//     key: '123121232312321321',
//     thirdPartyId: ThirdPartyId.Bilibili,
//     name: 'eeeeeeee',
const DownloaderPage = () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [form, setForm] = useState<SearchForm>({});
  const [downloaderDefinitions, setDownloaderDefinitions] = useState<
    BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition[]
  >([]);

  const tasks = useDownloadTasksStore((state) => state.tasks);
  const patchTasks = useDownloadTasksStore((state) => state.patchTasks);
  // const tasks = testTasks;

  // Build third party filter from downloader definitions, sorted by value ASC
  const sortedThirdPartyIds = useMemo(() => {
    const uniqueThirdParties = new Map<ThirdPartyId, string>();

    downloaderDefinitions.forEach((def) => {
      if (!uniqueThirdParties.has(def.thirdPartyId)) {
        // Use ThirdPartyId enum to get the name
        const thirdPartyName = ThirdPartyId[def.thirdPartyId] || def.name;

        uniqueThirdParties.set(def.thirdPartyId, thirdPartyName);
      }
    });

    return Array.from(uniqueThirdParties.entries())
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.value - b.value); // Sort by value ASC
  }, [downloaderDefinitions]);

  const [selectedTaskIds, setSelectedTaskIds] = useState<number[]>([]);
  const selectedTaskIdsRef = useRef(selectedTaskIds);
  const selectionModeRef = useRef(SelectionMode.Default);

  const tasksRef = useRef(tasks);
  const taskListRef = useRef<HTMLDivElement | null>(null);

  const [menuProps, toggleMenu] = useMenuState();
  const { createPortal } = useBakabaseContext();

  const [taskListHeight, setTaskListHeight] = useState(0);

  /**
   * Reflect an action on the row straight away, and put the old value back if the call
   * is rejected. The server pushes authoritative state over SignalR either way; this
   * only covers the gap, which was long enough to read as "the button did nothing".
   */
  const withOptimisticStatus = async <T extends { code: number }>(
    ids: number[],
    status: DownloadTaskStatus,
    action: () => Promise<T>,
  ): Promise<T> => {
    const previous = new Map<number, DownloadTaskStatus | undefined>();

    for (const id of ids) {
      previous.set(id, tasksRef.current.find((t) => t.id == id)?.status);
    }
    patchTasks(ids, { status });

    const rsp = await action();

    if (rsp.code !== ResponseCode.Success) {
      previous.forEach((s, id) => patchTasks([id], { status: s }));
    }

    return rsp;
  };

  const startTasksManually = async (
    ids: number[],
    actionOnConflict = DownloadTaskActionOnConflict.NotSet,
  ) => {
    const rsp = await withOptimisticStatus(ids, DownloadTaskStatus.Starting, () =>
      BApi.downloadTask.startDownloadTasks(
        {
          ids,
          actionOnConflict,
        },
        {
          // 400-level rejections used to fall through this predicate (it only caught
          // >= 404), so a start refused for an expired cookie or bad configuration
          // produced no feedback whatsoever — the click looked ignored. Report every
          // error code except Conflict, which has its own modal below.
          showErrorToast: (r) => r.code >= 400 && r.code != ResponseCode.Conflict,
        },
      ),
    );

    if (rsp.code == ResponseCode.Conflict) {
      createPortal(Modal, {
        defaultVisible: true,
        size: "lg",
        title: t<string>("downloader.confirm.conflictedTasks"),
        children: rsp.message,
        footer: {
          actions: ["ok", "cancel"],
          okProps: {
            children: t<string>("downloader.action.downloadSelectedFirst"),
          },
          cancelProps: {
            children: t<string>("downloader.action.addToQueue"),
          },
        },
        onOk: async () => {
          return await BApi.downloadTask.startDownloadTasks({
            ids,
            actionOnConflict: DownloadTaskActionOnConflict.StopOthers,
          });
        },
        onClose: async () =>
          await BApi.downloadTask.startDownloadTasks({
            ids,
            actionOnConflict: DownloadTaskActionOnConflict.Ignore,
          }),
      });
    }
  };

  useUpdateEffect(() => {
    selectedTaskIdsRef.current = selectedTaskIds;
  }, [selectedTaskIds]);
  const contextMenuAnchorPointRef = useRef({
    x: 0,
    y: 0,
  });

  const renderContextMenu = useCallback(() => {
    if (selectedTaskIdsRef.current.length == 0) {
      return;
    }

    const moreThanOne = selectedTaskIdsRef.current.length > 1;

    return (
      <ControlledMenu
        {...menuProps}
        anchorPoint={contextMenuAnchorPointRef.current}
        className={"downloader-page-context-menu"}
        onClose={() => {
          toggleMenu(false);
        }}
      >
        <MenuItem
          className={"flex items-center gap-2"}
          onClick={() => {
            startTasksManually(selectedTaskIdsRef.current);
          }}
        >
          <MdPlayCircle />
          {moreThanOne && (
            <>
              {t<string>("downloader.action.bulk")}
              &nbsp;
            </>
          )}
          {t<string>("downloader.action.start")}
        </MenuItem>
        <MenuItem
          className={"flex items-center gap-2"}
          onClick={() =>
            withOptimisticStatus(selectedTaskIdsRef.current, DownloadTaskStatus.Stopping, () =>
              BApi.downloadTask.stopDownloadTasks(selectedTaskIdsRef.current),
            )
          }
        >
          <MdAccessTime />
          {moreThanOne && (
            <>
              {t<string>("downloader.action.bulk")}
              &nbsp;
            </>
          )}
          {t<string>("downloader.action.stop")}
        </MenuItem>
        {moreThanOne && (
          <MenuItem
            className={"flex items-center gap-2"}
            onClick={() => {
              const selectedTasks = tasksRef.current.filter((tk) =>
                selectedTaskIdsRef.current.includes(tk.id),
              );

              if (selectedTasks.length > 0) {
                createPortal(BatchEditModal, { tasks: selectedTasks });
              }
            }}
          >
            <AiOutlineEdit />
            {t<string>("downloader.action.bulk")}
            &nbsp;
            {t<string>("downloader.action.edit")}
          </MenuItem>
        )}
        <MenuItem
          className={"flex items-center gap-2 danger"}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("downloader.confirm.deleteTasks", {
                count: selectedTaskIdsRef.current.length,
              }),
              onOk: async () => {
                await BApi.downloadTask.deleteDownloadTasks({
                  ids: selectedTaskIdsRef.current,
                });
              },
            });
          }}
        >
          <MdDelete />
          {moreThanOne && (
            <>
              {t<string>("downloader.action.bulk")}
              &nbsp;
            </>
          )}
          {t<string>("common.action.delete")}
        </MenuItem>
        <MenuItem
          className={"flex items-center gap-2"}
          onClick={() => {
            const ids = selectedTaskIdsRef.current;

            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>(
                ids.length > 1
                  ? "downloader.confirm.clearCheckpoints"
                  : "downloader.confirm.clearCheckpoint",
                { count: ids.length },
              ),
              onOk: async () => {
                await BApi.request<void, any>({
                  path: "/download-task/checkpoint",
                  method: "DELETE",
                  body: ids,
                  type: "application/json",
                  format: "json",
                } as any);
              },
            });
          }}
        >
          <AiOutlineWarning />
          {moreThanOne && (
            <>
              {t<string>("downloader.action.bulk")}
              &nbsp;
            </>
          )}
          {t<string>("downloader.action.clearCheckpoints")}
        </MenuItem>
      </ControlledMenu>
    );
  }, [menuProps]);

  useEffect(() => {
    tasksRef.current = tasks;
  }, [tasks]);

  useEffect(() => {
    const loadDownloaderDefinitions = async () => {
      try {
        const response = await BApi.downloadTask.getAllDownloaderDefinitions();

        setDownloaderDefinitions(response.data || []);
      } catch (error) {
        console.error("Failed to load downloader definitions:", error);
      }
    };

    loadDownloaderDefinitions();
  }, []);

  const onTaskClick = (taskId: number, e?: any) => {
    const nextMode = e
      ? e.shiftKey
        ? SelectionMode.Shift
        : e.ctrlKey || e.metaKey
          ? SelectionMode.Ctrl
          : SelectionMode.Default
      : SelectionMode.Default;

    selectionModeRef.current = nextMode;
    switch (selectionModeRef.current) {
      case SelectionMode.Default:
        if (selectedTaskIdsRef.current.includes(taskId) && selectedTaskIdsRef.current.length == 1) {
          setSelectedTaskIds([]);
        } else {
          setSelectedTaskIds([taskId]);
        }
        break;
      case SelectionMode.Ctrl:
        if (selectedTaskIdsRef.current.includes(taskId)) {
          setSelectedTaskIds(selectedTaskIdsRef.current.filter((id) => id != taskId));
        } else {
          setSelectedTaskIds([...selectedTaskIdsRef.current, taskId]);
        }
        break;
      case SelectionMode.Shift:
        if (selectedTaskIdsRef.current.length == 0) {
          setSelectedTaskIds([taskId]);
        } else {
          const lastSelectedTaskId = selectedTaskIdsRef.current[selectedTaskIds.length - 1];
          const lastSelectedTaskIndex = filteredTasks.findIndex((t) => t.id == lastSelectedTaskId);
          const currentTaskIndex = filteredTasks.findIndex((t) => t.id == taskId);
          const start = Math.min(lastSelectedTaskIndex, currentTaskIndex);
          const end = Math.max(lastSelectedTaskIndex, currentTaskIndex);

          setSelectedTaskIds(filteredTasks.slice(start, end + 1).map((t) => t.id));
        }
        break;
    }
  };

  const taskFilters: ((task: any) => boolean)[] = [];

  if (form.thirdPartyId != undefined) {
    taskFilters.push((t) => t.thirdPartyId === form.thirdPartyId);
  }
  if (form.status != undefined) {
    taskFilters.push((t) => t.status === form.status);
  }

  if (form.keyword != undefined && form.keyword.length > 0) {
    const lowerCaseKeyword = form.keyword.toLowerCase();

    taskFilters.push(
      (t) =>
        t.name?.toLowerCase().includes(lowerCaseKeyword) ||
        t.key.toLowerCase().includes(lowerCaseKeyword),
    );
  }

  const filteredTasks = tasks.filter((x) => taskFilters.every((f) => f(x)));

  // Keep the latest filtered tasks available to the (once-registered) key handler.
  const filteredTasksRef = useRef(filteredTasks);

  filteredTasksRef.current = filteredTasks;

  // The active task can sit thousands of rows down; scrolling to find it by hand is
  // the reported pain point. Jump straight to it and select it so it stands out.
  const locateActiveTask = () => {
    const index = ACTIVE_STATUSES.reduce((found, status) => {
      if (found > -1) return found;

      return filteredTasks.findIndex((task) => task.status == status);
    }, -1);

    if (index < 0) {
      // Distinguish "nothing is running" from "it's running but filtered out", which
      // otherwise looks like a broken button.
      const hiddenByFilter = tasks.some((task) => ACTIVE_STATUSES.includes(task.status!));

      toast.warning(
        t<string>(
          hiddenByFilter
            ? "downloader.toast.activeTaskFilteredOut"
            : "downloader.toast.noActiveTask",
        ),
      );

      return;
    }

    const container = findScrollContainer(taskListRef.current);

    if (container) {
      // Centre it rather than pinning it to the top, so surrounding tasks give context.
      const target = index * TASK_ITEM_HEIGHT - (container.clientHeight - TASK_ITEM_HEIGHT) / 2;

      container.scrollTo({
        top: Math.max(0, target),
        behavior: "smooth",
      });
    }

    setSelectedTaskIds([filteredTasks[index].id]);
  };

  // Ctrl/Cmd+A selects all filtered tasks, but only while focus is inside the
  // task list, so it doesn't hijack the shortcut elsewhere on the page.
  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && (e.key === "a" || e.key === "A")) {
        const container = taskListRef.current;

        if (container && container.contains(document.activeElement)) {
          e.preventDefault();
          setSelectedTaskIds(filteredTasksRef.current.map((tk) => tk.id));
        }
      }
    };

    document.addEventListener("keydown", onKeyDown);

    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);

  return (
    <div className={"h-full flex flex-col gap-1"}>
      {renderContextMenu()}
      <div
        className="grid gap-x-4 gap-y-1 items-center"
        style={{ gridTemplateColumns: "auto 1fr" }}
      >
        <div>{t<string>("downloader.filter.source")}</div>
        <div className="flex items-center gap-2">
          <ButtonGroup size={"sm"}>
            {sortedThirdPartyIds.map((s) => {
              const count = tasks.filter((t) => t.thirdPartyId == s.value).length;
              const isDeveloping = isThirdPartyDeveloping(s.value);
              const isSelected = form.thirdPartyId === s.value;

              return (
                <Button
                  key={s.value}
                  // color={isSelected ? "primary" : "default"}
                  variant={isSelected ? "solid" : "flat"}
                  onPress={() => {
                    // Clicking the active source clears the filter, so there is still a way
                    // back to "all" without a separate reset control.
                    setForm({
                      ...form,
                      thirdPartyId: isSelected ? undefined : s.value,
                    });
                  }}
                >
                  <div className={"flex items-center gap-1"}>
                    <ThirdPartyIcon thirdPartyId={s.value} />
                    <span>{s.label}</span>
                    {isDeveloping && <DevelopingChip showTooltip={false} size="sm" />}
                    {count > 0 && (
                      <Chip size={"sm"} variant={"flat"}>
                        {count}
                      </Chip>
                    )}
                  </div>
                </Button>
              );
            })}
          </ButtonGroup>
        </div>
        <div>{t<string>("downloader.filter.status")}</div>
        <div className="flex items-center gap-2">
          <ButtonGroup size={"sm"}>
            {downloadTaskStatuses.map((s) => {
              const count = tasks.filter((t) => t.status == s.value).length;
              const chipColor = DownloadTaskStatusIceLabelStatusMap[s.value! as DownloadTaskStatus];
              const isSelected = form.status === s.value;

              return (
                <Button
                  key={s.value}
                  // color={chipColor}
                  variant={isSelected ? "solid" : "flat"}
                  onPress={() => {
                    setForm({
                      ...form,
                      status: isSelected ? undefined : s.value,
                    });
                  }}
                >
                  <div className="flex items-center gap-1">
                    <Chip color={isSelected ? "default" : chipColor} size={"sm"} variant={"light"}>
                      {t<string>(s.label)}
                      {count > 0 && <span>&nbsp;({count})</span>}
                    </Chip>
                  </div>
                </Button>
              );
            })}
          </ButtonGroup>
        </div>
        <div>{t<string>("downloader.filter.keyword")}</div>
        <div>
          <Input
            className={"w-[320px]"}
            fullWidth={false}
            size={"sm"}
            startContent={<AiOutlineSearch className={"text-base"} />}
            onValueChange={(keyword) =>
              setForm({
                ...form,
                keyword,
              })
            }
          />
        </div>
      </div>
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1">
          <Button
            color={"primary"}
            size={"small"}
            onPress={() => {
              createPortal(DownloadTaskDetailModal, {});
            }}
          >
            <>
              <AiOutlinePlusCircle className={"text-base"} />
              {t<string>("downloader.action.createTask")}
            </>
          </Button>
          <Button
            color={"success"}
            size={"small"}
            variant={"flat"}
            onPress={() => {
              toast.success(t<string>("downloader.toast.startingAll"));
              startTasksManually([], DownloadTaskActionOnConflict.Ignore);
            }}
          >
            <AiOutlinePlayCircle className={"text-base"} />
            {t<string>("downloader.action.startAll")}
          </Button>
          <Button
            color={"warning"}
            size={"small"}
            variant={"flat"}
            onPress={() => {
              toast.success(t<string>("downloader.toast.stoppingAll"));
              BApi.downloadTask.stopDownloadTasks([]);
            }}
          >
            <AiOutlineStop className={"text-base"} />
            {t<string>("downloader.action.stopAll")}
          </Button>
          <Tooltip content={t<string>("downloader.action.locateActive.tip")} placement="bottom">
            <Button size={"small"} variant={"flat"} onPress={locateActiveTask}>
              <AiOutlineAim className={"text-base"} />
              {t<string>("downloader.action.locateActive")}
            </Button>
          </Tooltip>
        </div>
        <div className="flex items-center gap-1">
          <Dropdown>
            <DropdownTrigger>
              <Button size={"sm"} variant={"flat"}>
                <AiOutlineDelete className={"text-base"} />
                {t<string>("downloader.action.cleanup")}
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              onAction={(key) => {
                switch (key as string) {
                  case "delete_completed": {
                    const ids = tasks
                      .filter((t) => t.status == DownloadTaskStatus.Complete)
                      .map((t) => t.id);

                    // Silently returning here used to make the menu item look broken; say why
                    // nothing happened instead.
                    if (ids.length === 0) {
                      toast.warning(t<string>("downloader.toast.noCompletedTasks"));

                      return;
                    }
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("downloader.confirm.deleteCompletedTasks", {
                        count: ids.length,
                      }),
                      onOk: async () => {
                        await BApi.downloadTask.deleteDownloadTasks({ ids });
                      },
                    });
                    break;
                  }
                  case "delete_failed": {
                    const ids = tasks
                      .filter((t) => t.status == DownloadTaskStatus.Failed)
                      .map((t) => t.id);

                    if (ids.length === 0) {
                      toast.warning(t<string>("downloader.toast.noFailedTasks"));

                      return;
                    }
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("downloader.confirm.deleteFailedTasks", {
                        count: ids.length,
                      }),
                      onOk: async () => {
                        await BApi.downloadTask.deleteDownloadTasks({ ids });
                      },
                    });
                    break;
                  }
                }
              }}
            >
              <DropdownItem
                key="delete_completed"
                startContent={<AiOutlineDelete className={"text-base"} />}
              >
                {t<string>("downloader.action.deleteCompleted")}
              </DropdownItem>
              <DropdownItem
                key="delete_failed"
                color={"danger"}
                startContent={<AiOutlineDelete className={"text-base"} />}
              >
                {t<string>("downloader.action.deleteFailed")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
          <RequestStatistics />
          <Button
            size={"sm"}
            variant={"flat"}
            onPress={() => {
              BApi.gui.openUrlInDefaultBrowser({
                url: toAbsoluteBackendUrl("/download-task/xlsx"),
              });
            }}
          >
            <AiOutlineExport className={"text-base"} />
            {t<string>("downloader.action.exportAll")}
          </Button>
          <Button
            color={"secondary"}
            size={"small"}
            variant={"flat"}
            onPress={() => {
              createPortal(Configurations, {});
            }}
          >
            <AiOutlineSetting className={"text-base"} />
            {t<string>("downloader.action.configurations")}
          </Button>
        </div>
      </div>
      <div
        ref={(r) => {
          taskListRef.current = r;
          if (r && taskListHeight == 0) {
            setTaskListHeight(r.clientHeight);
          }
        }}
        className={"grow overflow-hidden"}
      >
        {taskListHeight > 0 && (
          <Listbox
            isVirtualized
            className={"p-0"}
            // color={"primary"}
            emptyContent={t<string>("downloader.empty.noTasks")}
            label={"Select from 1000 items"}
            // selectionMode={"multiple"}
            variant={"flat"}
            virtualization={{
              maxListboxHeight: taskListHeight,
              itemHeight: TASK_ITEM_HEIGHT,
            }}
          >
            {filteredTasks.map((task) => {
              const hasErrorMessage = task.status == DownloadTaskStatus.Failed && task.message;
              const selected = selectedTaskIds.indexOf(task.id) > -1;
              const Icon = DownloadTaskTypeIconMap[task.thirdPartyId!]?.[task.type];

              return (
                <ListboxItem
                  key={task.id}
                  className={`${selected ? "bg-primary-50 dark:bg-primary-900/20" : ""}`}
                >
                  <div
                    key={task.id}
                    className={`flex flex-col gap-1`}
                    role="button"
                    tabIndex={0}
                    onContextMenu={e => {
                      e.preventDefault();
                      if (!selectedTaskIdsRef.current.includes(task.id)) {
                        setSelectedTaskIds([task.id]);
                      }
                      contextMenuAnchorPointRef.current = {
                        x: e.clientX,
                        y: e.clientY,
                      };
                      toggleMenu(true);
                      forceUpdate();
                    }}
                    // style={style}
                    onClick={(e) => onTaskClick(task.id, e)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onTaskClick(task.id, e);
                      }
                    }}
                  >
                    <div className={"flex items-center justify-between"}>
                      <div className={"flex flex-col gap-1"}>
                        <div className={"flex items-center gap-2"}>
                          <ThirdPartyIcon thirdPartyId={task.thirdPartyId} />
                          {Icon && <Icon className="text-base" />}
                          <span className={"text-lg"}>{task.name ?? task.key}</span>
                        </div>
                        <div className={"flex items-center gap-1"}>
                          <span className={"opacity-60"}>{task.name && task.key}</span>
                          {task.nextStartDt && (
                            <Chip color={"default"} size={"sm"}>
                              {t<string>("downloader.label.nextStartTime")}:
                              {moment(task.nextStartDt).format("YYYY-MM-DD HH:mm:ss")}
                            </Chip>
                          )}
                          <Chip color="default" size="sm">
                            {t("downloader.label.createdAt")}
                            &nbsp;
                            {moment(task.createdAt).format("YYYY-MM-DD HH:mm:ss")}
                          </Chip>
                        </div>
                      </div>
                      <div className={"flex items-center"}>
                        <div className={"mr-8 flex items-center gap-2"}>
                          <Chip
                            color={DownloadTaskStatusIceLabelStatusMap[task.status]}
                            variant={"light"}
                          >
                            {t<string>(DownloadTaskStatus[task.status])}
                          </Chip>
                          {task.current && (
                            <span className="text-xs text-default-400">{task.current}</span>
                          )}
                          {task.status == DownloadTaskStatus.Failed && (
                            <Button
                              isIconOnly
                              color={"danger"}
                              // size={"sm"}
                              variant={"light"}
                              onPress={() => {
                                if (hasErrorMessage) {
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    size: "xl",
                                    title: t<string>("common.label.error"),
                                    children: <pre>{task.message}</pre>,
                                  });
                                }
                              }}
                            >
                              <AiOutlineWarning className={"text-base"} />
                              {task.failureTimes}
                            </Button>
                          )}
                          <CircularProgress
                            disableAnimation
                            // value={task.progress}
                            showValueLabel
                            color={DownloadTaskStatusProgressBarColorMap[task.status]}
                            size={"lg"}
                            value={task.progress}
                            // textRender={() => `${task.progress?.toFixed(2)}%`}
                            // progressive={t.status != DownloadTaskStatus.Failed}
                          />
                        </div>
                        {task.availableActions?.map((a) => {
                          switch (a) {
                            case DownloadTaskAction.StartManually:
                            case DownloadTaskAction.Restart:
                              return (
                                <Button
                                  key={`start-${task.id}-${a}`}
                                  isIconOnly
                                  size={"sm"}
                                  variant={"light"}
                                  onPress={() => {
                                    startTasksManually([task.id]);
                                  }}
                                >
                                  {a == DownloadTaskAction.Restart ? (
                                    <AiOutlineRedo className={"text-lg"} />
                                  ) : (
                                    <AiOutlinePlayCircle className={"text-lg"} />
                                  )}
                                </Button>
                              );
                            case DownloadTaskAction.Disable:
                              return (
                                <Button
                                  key={`stop-${task.id}-${a}`}
                                  isIconOnly
                                  size={"sm"}
                                  variant={"light"}
                                  onPress={() => {
                                    withOptimisticStatus(
                                      [task.id],
                                      DownloadTaskStatus.Stopping,
                                      () => BApi.downloadTask.stopDownloadTasks([task.id]),
                                    );
                                  }}
                                >
                                  <AiOutlineStop className={"text-lg"} />
                                </Button>
                              );
                          }

                          return;
                        })}
                        <Button
                          isIconOnly
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            createPortal(DownloadTaskDetailModal, {
                              id: task.id,
                            });
                          }}
                        >
                          <AiOutlineEdit className={"text-lg"} />
                        </Button>
                        <Button
                          isIconOnly
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            BApi.tool.openFileOrDirectory({
                              path: task.downloadPath,
                            });
                          }}
                        >
                          <AiOutlineFolderOpen className={"text-lg"} />
                        </Button>
                        <Dropdown>
                          <DropdownTrigger>
                            <Button isIconOnly size={"sm"} variant={"light"}>
                              <AiOutlineEllipsis className={"text-lg"} />
                            </Button>
                          </DropdownTrigger>
                          <DropdownMenu
                            onAction={(key) => {
                              switch (key as string) {
                                case "delete":
                                  createPortal(Modal, {
                                    defaultVisible: true,
                                    title: t<string>("downloader.confirm.deleteTask"),
                                    onOk: () =>
                                      BApi.downloadTask.deleteDownloadTasks({
                                        ids: [task.id],
                                      }),
                                  });
                                  break;
                              }
                            }}
                          >
                            <DropdownItem
                              key="delete"
                              color={"danger"}
                              startContent={<AiOutlineDelete className={"text-lg"} />}
                            >
                              {t<string>("common.action.delete")}
                            </DropdownItem>
                          </DropdownMenu>
                        </Dropdown>
                      </div>
                    </div>
                    {/*<div className="progress">*/}
                    {/*  <Progress*/}
                    {/*    // value={task.progress}*/}
                    {/*    color={DownloadTaskStatusProgressBarColorMap[task.status]}*/}
                    {/*    size={"sm"}*/}
                    {/*    value={task.progress}*/}
                    {/*    // textRender={() => `${task.progress?.toFixed(2)}%`}*/}
                    {/*    // progressive={t.status != DownloadTaskStatus.Failed}*/}
                    {/*  />*/}
                    {/*</div>*/}
                    {/* <CircularProgress */}
                    {/*   value={task.progress} */}
                    {/*   color={DownloadTaskStatusProgressBarColorMap[task.status]} */}
                    {/*   size={'sm'} */}
                    {/* /> */}
                  </div>
                </ListboxItem>
              );
            })}
          </Listbox>
        )}
      </div>
    </div>
  );
};

DownloaderPage.displayName = "DownloaderPage";
//     progress: 80,
//     status: DownloadTaskStatus.Downloading,
//   },
//   {
//     key: 'cxzkocnmaqwkodn wkjodas1',
//     name: 'pppppppppppp',
//     progress: 30,
//     status: DownloadTaskStatus.Failed,
//     message: 'dawsdasda',
//   },
// ];

const DownloadTaskStatusIceLabelStatusMap: Record<DownloadTaskStatus, ChipProps["color"]> = {
  [DownloadTaskStatus.Idle]: "default",
  [DownloadTaskStatus.InQueue]: "default",
  [DownloadTaskStatus.Downloading]: "primary",
  [DownloadTaskStatus.Failed]: "danger",
  [DownloadTaskStatus.Complete]: "success",
  [DownloadTaskStatus.Starting]: "warning",
  [DownloadTaskStatus.Stopping]: "warning",
  [DownloadTaskStatus.Disabled]: "default",
};

const DownloadTaskStatusProgressBarColorMap: Record<
  DownloadTaskStatus,
  CircularProgressProps["color"]
> = {
  [DownloadTaskStatus.Idle]: "default",
  [DownloadTaskStatus.InQueue]: "default",
  [DownloadTaskStatus.Downloading]: "primary",
  [DownloadTaskStatus.Failed]: "danger",
  [DownloadTaskStatus.Complete]: "success",
  [DownloadTaskStatus.Starting]: "warning",
  [DownloadTaskStatus.Stopping]: "warning",
  [DownloadTaskStatus.Disabled]: "default",
};

enum SelectionMode {
  Default,
  Ctrl,
  Shift,
}

type SearchForm = {
  /** Single-select: clicking the active chip clears it. */
  status?: DownloadTaskStatus;
  keyword?: string;
  thirdPartyId?: ThirdPartyId;
};

export default DownloaderPage;
