"use client";

import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { ThirdPartyId } from "@/sdk/constants";

import React, { useCallback, useEffect, useRef, useState } from "react";
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

import TaskDetailModal from "./components/TaskDetailModal";

import {
  Button,
  Checkbox,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Listbox,
  ListboxItem,
  Modal,
  Progress,
} from "@/components/bakaui";
import "@szhsin/react-menu/dist/index.css";
import "@szhsin/react-menu/dist/transitions/slide.css";
import {
  DownloadTaskAction,
  DownloadTaskActionOnConflict,
  DownloadTaskDtoStatus,
  downloadTaskDtoStatuses,
  ResponseCode,
  thirdPartyIds,
} from "@/sdk/constants";
import Configurations from "@/pages/downloader/components/Configurations";
import BApi from "@/sdk/BApi";
import { buildLogger, useTraceUpdate } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { useDownloadTasksStore } from "@/models/downloadTasks";
import RequestStatistics from "@/pages/downloader/components/RequestStatistics";

// const testTasks: DownloadTask[] = [
//   {
//     key: '123121232312321321',
//     thirdPartyId: ThirdPartyId.Bilibili,
//     name: 'eeeeeeee',
//     progress: 80,
//     status: DownloadTaskDtoStatus.Downloading,
//   },
//   {
//     key: 'cxzkocnmaqwkodn wkjodas1',
//     name: 'pppppppppppp',
//     progress: 30,
//     status: DownloadTaskDtoStatus.Failed,
//     message: 'dawsdasda',
//   },
// ];

const DownloadTaskDtoStatusIceLabelStatusMap: Record<
  DownloadTaskDtoStatus,
  ChipProps["color"]
> = {
  [DownloadTaskDtoStatus.Idle]: "default",
  [DownloadTaskDtoStatus.InQueue]: "default",
  [DownloadTaskDtoStatus.Downloading]: "primary",
  [DownloadTaskDtoStatus.Failed]: "danger",
  [DownloadTaskDtoStatus.Complete]: "success",
  [DownloadTaskDtoStatus.Starting]: "warning",
  [DownloadTaskDtoStatus.Stopping]: "warning",
  [DownloadTaskDtoStatus.Disabled]: "default",
};

const DownloadTaskDtoStatusProgressBarColorMap: Record<
  DownloadTaskDtoStatus,
  CircularProgressProps["color"]
> = {
  [DownloadTaskDtoStatus.Idle]: "default",
  [DownloadTaskDtoStatus.InQueue]: "default",
  [DownloadTaskDtoStatus.Downloading]: "primary",
  [DownloadTaskDtoStatus.Failed]: "danger",
  [DownloadTaskDtoStatus.Complete]: "success",
  [DownloadTaskDtoStatus.Starting]: "warning",
  [DownloadTaskDtoStatus.Stopping]: "warning",
  [DownloadTaskDtoStatus.Disabled]: "default",
};

enum SelectionMode {
  Default,
  Ctrl,
  Shift,
}

type SearchForm = {
  statuses?: DownloadTaskDtoStatus[];
  keyword?: string;
  thirdPartyIds?: ThirdPartyId[];
};

const log = buildLogger("DownloadPage");

export default () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [form, setForm] = useState<SearchForm>({});

  const tasks = useDownloadTasksStore((state) => state.tasks);
  // const tasks = testTasks;

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
        // @ts-ignore
        ignoreError: (rsp) => rsp.code == ResponseCode.Conflict,
      },
    );

    if (rsp.code == ResponseCode.Conflict) {
      createPortal(Modal, {
        defaultVisible: true,
        size: "lg",
        title: t<string>("Found some conflicted tasks"),
        children: rsp.message,
        footer: {
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
          onClick={() => {
            startTasksManually(selectedTaskIdsRef.current);
          }}
        >
          <div>
            <MdPlayCircle />
            {moreThanOne && (
              <>
                {t<string>("Bulk")}
                &nbsp;
              </>
            )}
            {t<string>("Start")}
          </div>
        </MenuItem>
        <MenuItem
          onClick={() =>
            BApi.downloadTask.stopDownloadTasks(selectedTaskIdsRef.current)
          }
        >
          <div>
            <MdAccessTime />
            {moreThanOne && (
              <>
                {t<string>("Bulk")}
                &nbsp;
              </>
            )}
            {t<string>("Stop")}
          </div>
        </MenuItem>
        <MenuItem
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
          <div className={"danger"}>
            <MdDelete />
            {moreThanOne && (
              <>
                {t<string>("Bulk")}
                &nbsp;
              </>
            )}
            {t<string>("Delete")}
          </div>
        </MenuItem>
      </ControlledMenu>
    );
  }, [menuProps]);

  useEffect(() => {
    tasksRef.current = tasks;
  }, [tasks]);

  useEffect(() => {
    // const onMouseDown = (e) => {
    //   // console.log(e.target, clearTaskSelectionTargetsRef.current);
    // };
    //
    // const onKeydown = (e: KeyboardEvent) => {
    //   // if (equalsOrIsChildOf(e.target as HTMLElement, tasksDomRef.current)) {
    //   switch (e.key) {
    //     case "Control":
    //       selectionModeRef.current = SelectionMode.Ctrl;
    //       break;
    //     case "Shift":
    //       selectionModeRef.current = SelectionMode.Shift;
    //       break;
    //     case "Escape":
    //       setSelectedTaskIds([]);
    //       break;
    //     case "a":
    //       if (e.ctrlKey) {
    //         e.stopPropagation();
    //         e.preventDefault();
    //         setSelectedTaskIds(tasksRef.current?.map((t) => t.id) || []);
    //       }
    //       break;
    //   }
    //   // }
    // };
    //
    // const onKeyUp = (e) => {
    //   switch (e.key) {
    //     case "Control":
    //       selectionModeRef.current = SelectionMode.Default;
    //       break;
    //     case "Shift":
    //       selectionModeRef.current = SelectionMode.Default;
    //       break;
    //   }
    // };

    // window.addEventListener('keydown', onKeydown);
    // window.addEventListener('keyup', onKeyUp);
    // window.addEventListener('mousedown', onMouseDown);

    return () => {
      // window.removeEventListener('keydown', onKeydown);
      // window.removeEventListener('keyup', onKeyUp);
      // window.removeEventListener('mousedown', onMouseDown);
    };
  }, []);

  console.log(
    selectedTaskIdsRef.current,
    SelectionMode[selectionModeRef.current],
  );

  const onTaskClick = (taskId: number) => {
    console.log(SelectionMode[selectionModeRef.current]);
    switch (selectionModeRef.current) {
      case SelectionMode.Default:
        if (
          selectedTaskIdsRef.current.includes(taskId) &&
          selectedTaskIdsRef.current.length == 1
        ) {
          setSelectedTaskIds([]);
        } else {
          setSelectedTaskIds([taskId]);
        }
        break;
      case SelectionMode.Ctrl:
        if (selectedTaskIdsRef.current.includes(taskId)) {
          setSelectedTaskIds(
            selectedTaskIdsRef.current.filter((id) => id != taskId),
          );
        } else {
          setSelectedTaskIds([...selectedTaskIdsRef.current, taskId]);
        }
        break;
      case SelectionMode.Shift:
        if (selectedTaskIdsRef.current.length == 0) {
          setSelectedTaskIds([taskId]);
        } else {
          const lastSelectedTaskId =
            selectedTaskIdsRef.current[selectedTaskIds.length - 1];
          const lastSelectedTaskIndex = tasks.findIndex(
            (t) => t.id == lastSelectedTaskId,
          );
          const currentTaskIndex = tasks.findIndex((t) => t.id == taskId);
          const start = Math.min(lastSelectedTaskIndex, currentTaskIndex);
          const end = Math.max(lastSelectedTaskIndex, currentTaskIndex);

          setSelectedTaskIds(tasks.slice(start, end + 1).map((t) => t.id));
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
        <div className="flex items-center gap-4">
          {thirdPartyIds.map((s) => {
            const count = tasks.filter((t) => t.thirdPartyId == s.value).length;

            return (
              <Checkbox
                // disabled={count == 0}
                key={s.value}
                isSelected={form.thirdPartyIds?.some((a) => a == s.value)}
                size={"sm"}
                onValueChange={(checked) => {
                  let thirdPartyIds = form.thirdPartyIds || [];

                  if (checked) {
                    if (thirdPartyIds.every((a) => a != s.value)) {
                      thirdPartyIds.push(s.value);
                    }
                  } else {
                    thirdPartyIds = thirdPartyIds.filter((a) => a != s.value);
                  }
                  setForm({
                    ...form,
                    thirdPartyIds,
                  });
                }}
              >
                <div className={"flex items-center gap-1"}>
                  <ThirdPartyIcon thirdPartyId={s.value} />
                  <Chip size={"sm"} variant={"light"}>
                    {s.label}
                  </Chip>
                  {count > 0 && (
                    <Chip size={"sm"} variant={"flat"}>
                      {count}
                    </Chip>
                  )}
                </div>
              </Checkbox>
            );
          })}
        </div>
        <div>{t<string>("Status")}</div>
        <div className="flex items-center gap-4">
          {downloadTaskDtoStatuses.map((s) => {
            const count = tasks.filter((t) => t.status == s.value).length;
            const color =
              DownloadTaskDtoStatusIceLabelStatusMap[
                s.value! as DownloadTaskDtoStatus
              ];

            return (
              <Checkbox
                key={s.value}
                className={"flex items-center"}
                color={color}
                isSelected={form.statuses?.some((a) => a == s.value)}
                size={"sm"}
                onValueChange={(checked) => {
                  let statuses = form.statuses || [];

                  if (checked) {
                    if (statuses.every((a) => a != s.value)) {
                      statuses.push(s.value);
                    }
                  } else {
                    statuses = statuses.filter((a) => a != s.value);
                  }
                  setForm({
                    ...form,
                    statuses,
                  });
                }}
              >
                <Chip
                  // size={'sm'}
                  classNames={{
                    base: "pl-0",
                    content: "pl-0",
                  }}
                  color={color}
                  variant={"light"}
                >
                  {t<string>(s.label)}
                </Chip>
                {count > 0 && (
                  <Chip size={"sm"} variant={"flat"}>
                    {count}
                  </Chip>
                )}
              </Checkbox>
            );
          })}
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
              createPortal(TaskDetailModal, {});
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
          <RequestStatistics />
          <Button
            size={"sm"}
            variant={"flat"}
            onPress={() => {
              BApi.downloadTask.exportAllDownloadTasks();
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
            selectionMode={'multiple'}
            variant={'flat'}
            virtualization={{
              maxListboxHeight: taskListHeight,
              itemHeight: 75,
            }}
            className={'p-0'}
            // className="max-w-xs"
            label={'Select from 1000 items'}
          >
            {filteredTasks.map((task, index) => {
              const hasErrorMessage =
                task.status == DownloadTaskDtoStatus.Failed && task.message;
              const selected = selectedTaskIds.indexOf(task.id) > -1;

              return (
                <ListboxItem key={index}>
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
                    // className={`${selected ? 'selected' : ''}`}
                    className={'flex flex-col gap-1'}
                    // style={style}
                    onClick={() => onTaskClick(task.id)}
                  >
                    <div className={"flex items-center justify-between"}>
                      <div className={"flex flex-col gap-1"}>
                        <div className={"flex items-center gap-1"}>
                          <ThirdPartyIcon thirdPartyId={task.thirdPartyId} />
                          <span className={"text-lg"}>
                            {task.name ?? task.key}
                          </span>
                        </div>
                        <div className={"flex items-center gap-1"}>
                          <span className={"opacity-60"}>
                            {task.name && task.key}
                          </span>
                          <Chip
                            color={
                              DownloadTaskDtoStatusIceLabelStatusMap[
                                task.status
                              ]
                            }
                            size={"sm"}
                            variant={"light"}
                          >
                            {t<string>(DownloadTaskDtoStatus[task.status])}
                            {task.current}
                          </Chip>
                          {task.status == DownloadTaskDtoStatus.Failed && (
                            <Button
                              isIconOnly
                              color={"danger"}
                              size={"sm"}
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
                          {task.nextStartDt && (
                            <Chip color={"default"} size={"sm"}>
                              {t<string>("Next start time")}:
                              {moment(task.nextStartDt).format(
                                "YYYY-MM-DD HH:mm:ss",
                              )}
                            </Chip>
                          )}
                        </div>
                      </div>
                      <div className={"flex items-center"}>
                        {task.availableActions?.map((a, i) => {
                          const action = parseInt(a, 10);

                          switch (action) {
                            case DownloadTaskAction.StartManually:
                            case DownloadTaskAction.Restart:
                              return (
                                <Button
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
                                    <AiOutlinePlayCircle
                                      className={"text-lg"}
                                    />
                                  )}
                                </Button>
                              );
                            case DownloadTaskAction.Disable:
                              return (
                                <Button
                                  isIconOnly
                                  size={"sm"}
                                  variant={"light"}
                                  onPress={() => {
                                    BApi.downloadTask.stopDownloadTasks([
                                      task.id,
                                    ]);
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
                            createPortal(TaskDetailModal, {
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
                                    title: t<string>(
                                      "Are you sure to delete it?",
                                    ),
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
                              startContent={
                                <AiOutlineDelete className={"text-lg"} />
                              }
                            >
                              {t<string>("Delete")}
                            </DropdownItem>
                          </DropdownMenu>
                        </Dropdown>
                      </div>
                    </div>
                    <div className="progress">
                      <Progress
                        // value={task.progress}
                        color={
                          DownloadTaskDtoStatusProgressBarColorMap[task.status]
                        }
                        size={"sm"}
                        value={task.progress}
                        // textRender={() => `${task.progress?.toFixed(2)}%`}
                        // progressive={t.status != DownloadTaskStatus.Failed}
                      />
                    </div>
                    {/* <CircularProgress */}
                    {/*   value={task.progress} */}
                    {/*   color={DownloadTaskDtoStatusProgressBarColorMap[task.status]} */}
                    {/*   size={'sm'} */}
                    {/* /> */}
                  </div>
                </ListboxItem>
              );
            })}
          </Listbox>
        )}
      </div>
      {/* {tasks?.length > 0 ? ( */}
      {/*   <div */}
      {/*     className={'tasks'} */}
      {/*   > */}
      {/*     <AutoSizer> */}
      {/*       {({ */}
      {/*         width, */}
      {/*         height, */}
      {/*       }) => ( */}
      {/*         <List */}
      {/*         // onScroll={onChildScroll} */}
      {/*         // isScrolling={isScrolling} */}
      {/*         // scrollTop={scrollTop} */}
      {/*           overscanRowCount={2} */}
      {/*         // scrollToIndex={scrollToIndex} */}
      {/*           width={width} */}
      {/*           height={height} */}
      {/*         // autoHeight */}
      {/*           rowCount={filteredTasks.length} */}
      {/*           rowHeight={75} */}
      {/*           rowRenderer={({ */}
      {/*                         index, */}
      {/*                         style, */}
      {/*                         isVisible, */}
      {/*                         isScrolling, */}
      {/*                       }) => { */}
      {/*           const task = filteredTasks[index]; */}
      {/*           const hasErrorMessage = task.status == DownloadTaskDtoStatus.Failed && task.message; */}
      {/*           const selected = selectedTaskIds.indexOf(task.id) > -1; */}
      {/*           return ( */}
      {/*             <div */}
      {/*               key={task.id} */}
      {/*               onContextMenu={e => { */}
      {/*                 console.log(`Opening context menu from ${task.id}:${task.name}`); */}
      {/*                 e.preventDefault(); */}
      {/*                 if (!selectedTaskIdsRef.current.includes(task.id)) { */}
      {/*                   setSelectedTaskIds([task.id]); */}
      {/*                 } */}
      {/*                 contextMenuAnchorPointRef.current = { */}
      {/*                   x: e.clientX, */}
      {/*                   y: e.clientY, */}
      {/*                 }; */}
      {/*                 toggleMenu(true); */}
      {/*                 forceUpdate(); */}
      {/*               }} */}
      {/*               className={`download-item ${selected ? 'selected' : ''}`} */}
      {/*               style={style} */}
      {/*               onClick={() => onTaskClick(task.id)} */}
      {/*             > */}
      {/*               <div className="icon"> */}
      {/*                 <img className={'max-w-[32px] max-h-[32px]'} src={NameIcon[task.thirdPartyId]} /> */}
      {/*               </div> */}
      {/*               <div className="content"> */}
      {/*                 <div className="name"> */}
      {/*                   <Balloon.Tooltip */}
      {/*                     trigger={( */}
      {/*                       <span onClick={() => { */}
      {/*                         setTaskId(task.id); */}
      {/*                       }} */}
      {/*                       > */}
      {/*                         {renderTaskName(task)} */}
      {/*                       </span> */}
      {/*                     )} */}
      {/*                     triggerType={'hover'} */}
      {/*                     align={'t'} */}
      {/*                   > */}
      {/*                     {task.key} */}
      {/*                   </Balloon.Tooltip> */}
      {/*                 </div> */}
      {/*                 <div className="info"> */}
      {/*                   <div className="left"> */}
      {/*                     <SimpleLabel */}
      {/*                       status={DownloadTaskDtoStatusIceLabelStatusMap[task.status]} */}
      {/*                       className={hasErrorMessage ? 'has-error-message' : ''} */}
      {/*                     > */}
      {/*                       <span */}
      {/*                         onClick={() => { */}
      {/*                             if (hasErrorMessage) { */}
      {/*                               Dialog.error({ */}
      {/*                                 v2: true, */}
      {/*                                 width: 1000, */}
      {/*                                 title: t<string>('Error'), */}
      {/*                                 content: ( */}
      {/*                                   <pre className={'select-text'}>{task.message}</pre> */}
      {/*                                 ), */}
      {/*                               }); */}
      {/*                             } */}
      {/*                           }} */}
      {/*                       > */}
      {/*                         {t<string>(DownloadTaskDtoStatus[task.status])} */}
      {/*                       </span> */}
      {/*                     </SimpleLabel> */}
      {/*                     {(task.status == DownloadTaskDtoStatus.Downloading || task.status == DownloadTaskDtoStatus.Starting || task.status == DownloadTaskDtoStatus.Stopping) && ( */}
      {/*                       <Icon type={'loading'} size={'small'} /> */}
      {/*                     )} */}
      {/*                     <span>{task.current}</span> */}
      {/*                   </div> */}
      {/*                   <div className="right"> */}
      {/*                     {task.failureTimes > 0 && ( */}
      {/*                       <SimpleLabel */}
      {/*                         status={'danger'} */}
      {/*                         className={'failure-times'} */}
      {/*                       > */}
      {/*                         {t<string>('Failure times')}: */}
      {/*                         <span>{task.failureTimes}</span> */}
      {/*                       </SimpleLabel> */}
      {/*                     )} */}
      {/*                     {task.nextStartDt && ( */}
      {/*                       <SimpleLabel */}
      {/*                         status={'info'} */}
      {/*                         className={'next-start-dt'} */}
      {/*                       > */}
      {/*                         {t<string>('Next start time')}: */}
      {/*                         <span> */}
      {/*                           {moment(task.nextStartDt) */}
      {/*                               .format('YYYY-MM-DD HH:mm:ss')} */}
      {/*                         </span> */}
      {/*                       </SimpleLabel> */}
      {/*                     )} */}
      {/*                   </div> */}
      {/*                 </div> */}
      {/*                 <div className="progress"> */}
      {/*                   <Progress */}
      {/*                     // state={t.status == DownloadTaskStatus.Failed ? 'error' : 'normal'} */}
      {/*                     className={'bar'} */}
      {/*                     percent={task.progress} */}
      {/*                     color={DownloadTaskDtoStatusProgressBarColorMap[task.status]} */}
      {/*                     size={'small'} */}
      {/*                     textRender={() => `${task.progress.toFixed(2)}%`} */}
      {/*                     // progressive={t.status != DownloadTaskStatus.Failed} */}
      {/*                   /> */}
      {/*                 </div> */}
      {/*               </div> */}
      {/*               <div className="opt"> */}
      {/*                 {task.availableActions?.map((a, i) => { */}
      {/*                   const action = parseInt(a); */}
      {/*                   switch (action) { */}
      {/*                     case DownloadTaskAction.StartManually: */}
      {/*                     case DownloadTaskAction.Restart: */}
      {/*                       return ( */}
      {/*                         <CustomIcon */}
      {/*                           key={i} */}
      {/*                           type={a == DownloadTaskAction.Restart ? 'redo' : 'play_fill'} */}
      {/*                           title={t<string>('Start now')} */}
      {/*                           onClick={() => { */}
      {/*                             startTasksManually([task.id]); */}
      {/*                           }} */}
      {/*                         /> */}
      {/*                       ); */}
      {/*                     case DownloadTaskAction.Disable: */}
      {/*                       return ( */}
      {/*                         <CustomIcon */}
      {/*                           key={i} */}
      {/*                           type={'stop'} */}
      {/*                           title={t<string>('Disable')} */}
      {/*                           onClick={() => { */}
      {/*                             BApi.downloadTask.stopDownloadTasks([task.id]); */}
      {/*                           }} */}
      {/*                         /> */}
      {/*                       ); */}
      {/*                   } */}
      {/*                   return; */}
      {/*                 })} */}
      {/*                 <CustomIcon */}
      {/*                   type={'folder-open'} */}
      {/*                   title={t<string>('Open folder')} */}
      {/*                   onClick={() => { */}
      {/*                     OpenFileOrDirectory({ */}
      {/*                       path: task.downloadPath, */}
      {/*                     }) */}
      {/*                       .invoke(); */}
      {/*                   }} */}
      {/*                 /> */}
      {/*                 <Dropdown */}
      {/*                   className={'task-operations-dropdown'} */}
      {/*                   trigger={ */}
      {/*                     <CustomIcon */}
      {/*                       type={'ellipsis'} */}
      {/*                     /> */}
      {/*                   } */}
      {/*                   triggerType={['click']} */}
      {/*                 > */}
      {/*                   <Menu> */}
      {/*                     /!* <Menu.Item title={t<string>(t.status == DownloadTaskStatus.Paused ? 'Click to enable' : 'Click to disable')}> *!/ */}
      {/*                     /!*   <div className={t.status == DownloadTaskStatus.Paused ? 'disabled' : 'enabled'}> *!/ */}
      {/*                     /!*     <CustomIcon *!/ */}
      {/*                     /!*       type={t.status == DownloadTaskStatus.Paused ? 'close-circle' : 'check-circle'} *!/ */}
      {/*                     /!*       onClick={() => { *!/ */}

      {/*                     /!*       }} *!/ */}
      {/*                     /!*     /> *!/ */}
      {/*                     /!*     {t<string>(t.status == DownloadTaskStatus.Paused ? 'Disabled' : 'Enabled')} *!/ */}
      {/*                     /!*   </div> *!/ */}
      {/*                     /!* </Menu.Item> *!/ */}
      {/*                     <Menu.Item> */}
      {/*                       <div */}
      {/*                         className={'remove'} */}
      {/*                         onClick={() => { */}
      {/*                           Dialog.confirm({ */}
      {/*                             title: t<string>('Are you sure to delete it?'), */}
      {/*                             onOk: () => BApi.downloadTask.deleteDownloadTasks({ ids: [task.id] }), */}
      {/*                           }); */}
      {/*                         }} */}
      {/*                       > */}
      {/*                         <CustomIcon type={'delete'} /> */}
      {/*                         {t<string>('Remove')} */}
      {/*                       </div> */}
      {/*                     </Menu.Item> */}
      {/*                   </Menu> */}
      {/*                 </Dropdown> */}
      {/*               </div> */}
      {/*             </div> */}
      {/*           ); */}
      {/*         }} */}
      {/*         /> */}
      {/*     )} */}
      {/*     </AutoSizer> */}
      {/*     /!* )} *!/ */}
      {/*     /!* </WindowScroller> *!/ */}
      {/*   </div> */}

      {/* ) : ( */}
      {/*   <div className={'no-task-yet'}> */}
      {/*     <Button */}
      {/*       color={'primary'} */}
      {/*       size={'large'} */}
      {/*       onPress={() => { */}
      {/*         setTaskId(0); */}
      {/*       }} */}
      {/*     > */}
      {/*       {t<string>('Create download task')} */}
      {/*     </Button> */}
      {/*   </div> */}
      {/* )} */}
    </div>
  );
};
