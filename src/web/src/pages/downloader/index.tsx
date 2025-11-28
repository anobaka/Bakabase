"use client";

import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition } from "@/sdk/Api";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import moment from "moment";
import { ControlledMenu, MenuItem, useMenuState } from "@szhsin/react-menu";
import { useUpdate, useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";
import {
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
import { buildLogger, useTraceUpdate } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import RequestStatistics from "@/pages/downloader/components/RequestStatistics";

import DownloadTaskDetailModal from "./components/TaskDetailModal";

import envConfig from "@/config/env.ts";

import { CircularProgress } from "@heroui/react";
import { DownloadTaskTypeIconMap } from "./components/TaskDetailModal/models";

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

  const [menuProps, toggleMenu] = useMenuState();
  const { createPortal } = useBakabaseContext();

  const [taskListHeight, setTaskListHeight] = useState(0);

  useTraceUpdate(
    {
      form,
      tasks,
      selectedTaskIds,
      menuProps,
    },
    "DownloaderPage",
  );

  log("Rendering");

  const startTasksManually = async (
    ids: number[],
    actionOnConflict = DownloadTaskActionOnConflict.NotSet,
  ) => {
    const rsp = await BApi.downloadTask.startDownloadTasks(
      {
        ids,
        actionOnConflict,
      },
      {
        showErrorToast: (r) => (r.code >= 404 || r.code < 200) && r.code != ResponseCode.Conflict,
      },
    );

    if (rsp.code == ResponseCode.Conflict) {
      createPortal(Modal, {
        defaultVisible: true,
        size: "lg",
        title: t<string>("Found some conflicted tasks"),
        children: rsp.message,
        footer: {
          actions: ["ok", "cancel"],
          okProps: {
            children: t<string>("Download selected tasks firstly"),
          },
          cancelProps: {
            children: t<string>("Add selected tasks to the queue"),
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
              {t<string>("Bulk")}
              &nbsp;
            </>
          )}
          {t<string>("Start")}
        </MenuItem>
        <MenuItem
          className={"flex items-center gap-2"}
          onClick={() => BApi.downloadTask.stopDownloadTasks(selectedTaskIdsRef.current)}
        >
          <MdAccessTime />
          {moreThanOne && (
            <>
              {t<string>("Bulk")}
              &nbsp;
            </>
          )}
          {t<string>("Stop")}
        </MenuItem>
        <MenuItem
          className={"flex items-center gap-2 danger"}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Deleting {{count}} download tasks", {
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
              {t<string>("Bulk")}
              &nbsp;
            </>
          )}
          {t<string>("Delete")}
        </MenuItem>
        <MenuItem
          className={"flex items-center gap-2"}
          onClick={() => {
            const ids = selectedTaskIdsRef.current;

            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>(
                ids.length > 1 ? "Clear checkpoints for {{count}} tasks" : "Clear checkpoint",
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
              {t<string>("Bulk")}
              &nbsp;
            </>
          )}
          {t<string>("Clear checkpoints")}
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

  console.log(selectedTaskIdsRef.current, SelectionMode[selectionModeRef.current]);

  const onTaskClick = (taskId: number, e?: any) => {
    const nextMode = e
      ? e.shiftKey
        ? SelectionMode.Shift
        : e.ctrlKey || e.metaKey
          ? SelectionMode.Ctrl
          : SelectionMode.Default
      : SelectionMode.Default;

    selectionModeRef.current = nextMode;
    console.log(SelectionMode[selectionModeRef.current]);
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

  if (form.thirdPartyIds && form.thirdPartyIds.length > 0) {
    taskFilters.push((t) => form.thirdPartyIds!.includes(t.thirdPartyId));
  }
  if (form.statuses && form.statuses.length > 0) {
    taskFilters.push((t) => form.statuses!.includes(t.status));
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

  console.log(form, filteredTasks);

  return (
    <div className={"h-full flex flex-col gap-1"}>
      {renderContextMenu()}
      <div
        className="grid gap-x-4 gap-y-1 items-center"
        style={{ gridTemplateColumns: "auto 1fr" }}
      >
        <div>{t<string>("Source")}</div>
        <div className="flex items-center gap-2">
          <ButtonGroup size={"sm"}>
            {sortedThirdPartyIds.map((s) => {
              const count = tasks.filter((t) => t.thirdPartyId == s.value).length;
              const isDeveloping = isThirdPartyDeveloping(s.value);
              const isSelected = form.thirdPartyIds?.some((a) => a == s.value);

              return (
                <Button
                  key={s.value}
                  // color={isSelected ? "primary" : "default"}
                  variant={isSelected ? "solid" : "flat"}
                  onPress={() => {
                    let thirdPartyIds = form.thirdPartyIds || [];

                    if (isSelected) {
                      thirdPartyIds = thirdPartyIds.filter((a) => a != s.value);
                    } else {
                      thirdPartyIds.push(s.value);
                    }
                    setForm({
                      ...form,
                      thirdPartyIds,
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
        <div>{t<string>("Status")}</div>
        <div className="flex items-center gap-2">
          <ButtonGroup size={"sm"}>
            {downloadTaskStatuses.map((s) => {
              const count = tasks.filter((t) => t.status == s.value).length;
              const chipColor = DownloadTaskStatusIceLabelStatusMap[s.value! as DownloadTaskStatus];
              const isSelected = form.statuses?.some((a) => a == s.value);

              return (
                <Button
                  key={s.value}
                  // color={chipColor}
                  variant={isSelected ? "solid" : "flat"}
                  onPress={() => {
                    let statuses = form.statuses || [];

                    if (isSelected) {
                      statuses = statuses.filter((a) => a != s.value);
                    } else {
                      statuses.push(s.value);
                    }
                    setForm({
                      ...form,
                      statuses,
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
        <div>{t<string>("Keyword")}</div>
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
              {t<string>("Create task")}
            </>
          </Button>
          <Button
            color={"success"}
            size={"small"}
            variant={"flat"}
            onPress={() => {
              startTasksManually([], DownloadTaskActionOnConflict.Ignore);
            }}
          >
            <AiOutlinePlayCircle className={"text-base"} />
            {t<string>("Start all")}
          </Button>
          <Button
            color={"warning"}
            size={"small"}
            variant={"flat"}
            onPress={() => {
              BApi.downloadTask.stopDownloadTasks([]);
            }}
          >
            <AiOutlineStop className={"text-base"} />
            {t<string>("Stop all")}
          </Button>
        </div>
        <div className="flex items-center gap-1">
          <Dropdown>
            <DropdownTrigger>
              <Button size={"sm"} variant={"flat"}>
                <AiOutlineDelete className={"text-base"} />
                {t<string>("Cleanup")}
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              onAction={(key) => {
                switch (key as string) {
                  case "delete_completed": {
                    const ids = tasks
                      .filter((t) => t.status == DownloadTaskStatus.Complete)
                      .map((t) => t.id);

                    if (ids.length === 0) return;
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Deleting {{count}} download tasks", { count: ids.length }),
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

                    if (ids.length === 0) return;
                    createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Deleting {{count}} download tasks", { count: ids.length }),
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
                {t<string>("Delete all completed tasks")}
              </DropdownItem>
              <DropdownItem
                key="delete_failed"
                color={"danger"}
                startContent={<AiOutlineDelete className={"text-base"} />}
              >
                {t<string>("Delete all failed tasks")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
          <RequestStatistics />
          <Button
            size={"sm"}
            variant={"flat"}
            onPress={() => {
              BApi.gui.openUrlInDefaultBrowser({
                url: `${envConfig.apiEndpoint}/download-task/xlsx`,
              });
            }}
          >
            <AiOutlineExport className={"text-base"} />
            {t<string>("Export all tasks")}
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
            {t<string>("Configurations")}
          </Button>
        </div>
      </div>
      <div
        ref={(r) => {
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
            emptyContent={t<string>("No tasks found")}
            label={"Select from 1000 items"}
            // selectionMode={"multiple"}
            variant={"flat"}
            virtualization={{
              maxListboxHeight: taskListHeight,
              itemHeight: 75,
            }}
          >
            {filteredTasks.map((task) => {
              const hasErrorMessage = task.status == DownloadTaskStatus.Failed && task.message;
              const selected = selectedTaskIds.indexOf(task.id) > -1;
              const Icon = DownloadTaskTypeIconMap[task.thirdPartyId!]?.[task.type];
              log("rendering task", task);

              return (
                <ListboxItem
                  key={task.id}
                  className={`${selected ? "bg-primary-50 dark:bg-primary-900/20" : ""}`}
                >
                  <div
                    key={task.id}
                    onContextMenu={e => {
                      console.log(`Opening context menu from ${task.id}:${task.name}`);
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
                    className={`flex flex-col gap-1`}
                    // style={style}
                    onClick={(e) => onTaskClick(task.id, e)}
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
                              {t<string>("Next start time")}:
                              {moment(task.nextStartDt).format("YYYY-MM-DD HH:mm:ss")}
                            </Chip>
                          )}
                          <Chip color="default" size="sm">
                            {t('Created at')}
                            &nbsp;
                            {moment(task.createdAt).format("YYYY-MM-DD HH:mm:ss")}
                            </Chip>
                        </div>
                      </div>
                      <div className={"flex items-center"}>
                        <div className={"mr-8 flex items-center gap-2"}>
                          <Chip
                            color={DownloadTaskStatusIceLabelStatusMap[task.status]}
                            // size={"lg"}
                            // size={"sm"}
                            variant={"light"}
                          >
                            {t<string>(DownloadTaskStatus[task.status])}
                            {task.current}
                          </Chip>
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
                                    title: t<string>("Error"),
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
                                    BApi.downloadTask.stopDownloadTasks([task.id]);
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
                                    title: t<string>("Are you sure to delete it?"),
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
                              {t<string>("Delete")}
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
      ß
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
  statuses?: DownloadTaskStatus[];
  keyword?: string;
  thirdPartyIds?: ThirdPartyId[];
};

const log = buildLogger("DownloadPage");

export default DownloaderPage;
