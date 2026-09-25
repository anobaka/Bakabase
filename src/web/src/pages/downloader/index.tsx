import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
("use client");

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
  AiOutlineExport,
  AiOutlineDownload,
  AiOutlineClose,
  AiOutlineEllipsis,
  AiOutlinePlayCircle,
  AiOutlinePlusCircle,
  AiOutlineSetting,
  AiOutlineStop,
  AiOutlineWarning,
} from "react-icons/ai";
import { MdPlayCircle, MdAccessTime, MdDelete } from "react-icons/md";

import DownloadTaskDetailModal from "./components/TaskDetailModal";
import BatchEditModal from "./components/BatchEditModal";
import TaskErrorMessage from "./components/TaskErrorMessage";
import TaskRow, { DOWNLOAD_TASK_ITEM_HEIGHT } from "./components/TaskRow";
import DownloadTaskFilters, { type DownloadTaskFilter } from "./components/DownloadTaskFilters";

import { ThirdPartyId } from "@/sdk/constants";
import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Listbox,
  ListboxItem,
  Modal,
  toast,
  Tooltip,
} from "@/components/bakaui";
import "@szhsin/react-menu/dist/index.css";
import "@szhsin/react-menu/dist/transitions/slide.css";
import { DownloadTaskActionOnConflict, DownloadTaskStatus, ResponseCode } from "@/sdk/constants";
import Configurations from "@/pages/downloader/components/Configurations";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import RequestStatistics from "@/pages/downloader/components/RequestStatistics";
import { toAbsoluteBackendUrl } from "@/config/env.ts";

/** Row height handed to the listbox virtualizer; also how "locate" computes a scroll offset. */
const TASK_ITEM_HEIGHT = DOWNLOAD_TASK_ITEM_HEIGHT;

/**
 * Formatting a timestamp with moment is not cheap, and every task row does it twice on
 * every render — of which there is one per pushed progress update. The result depends
 * only on the input string, so cache it.
 */
const formattedDateTimeCache = new Map<string, string>();

const formatTaskDateTime = (value?: string | Date | null): string => {
  if (!value) return "";

  // A Date instance is a fresh object per push, so key on its epoch instead.
  const key = value instanceof Date ? String(value.getTime()) : value;
  const cached = formattedDateTimeCache.get(key);

  if (cached !== undefined) return cached;

  // nextStartDt keeps producing new values as tasks are rescheduled, so bound the cache
  // rather than letting it grow for the life of the page.
  if (formattedDateTimeCache.size > 5000) {
    formattedDateTimeCache.clear();
  }

  const formatted = moment(value).format("YYYY-MM-DD HH:mm:ss");

  formattedDateTimeCache.set(key, formatted);

  return formatted;
};

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

const DownloaderPage = () => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const [form, setForm] = useState<DownloadTaskFilter>({});
  const [downloaderDefinitions, setDownloaderDefinitions] = useState<
    BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition[]
  >([]);

  const tasks = useDownloadTasksStore((state) => state.tasks);
  const patchTasks = useDownloadTasksStore((state) => state.patchTasks);

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

  // Keeps the virtualizer's viewport in step with the container, which is measured once when the
  // ref lands and otherwise never again.
  useEffect(() => {
    const container = taskListRef.current;

    if (!container || typeof ResizeObserver === "undefined") {
      return;
    }

    const observer = new ResizeObserver(() => {
      const next = container.clientHeight;

      // Only on a real change: the observer fires during layout, and setting state unconditionally
      // there is a render loop.
      setTaskListHeight((current) => (current === next ? current : next));
    });

    observer.observe(container);

    return () => observer.disconnect();
  }, [taskListHeight > 0]);

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

  // Every handler a row receives has to keep the same identity between renders, or memoizing the
  // rows achieves nothing: a new function per render is a changed prop on all of them. Anything
  // that varies is therefore read through a ref rather than captured.
  const onTaskClick = useCallback((taskId: number, e?: any) => {
    const filtered = filteredTasksRef.current;
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
          const lastSelectedTaskId =
            selectedTaskIdsRef.current[selectedTaskIdsRef.current.length - 1];
          const lastSelectedTaskIndex = filtered.findIndex((t) => t.id == lastSelectedTaskId);
          const currentTaskIndex = filtered.findIndex((t) => t.id == taskId);
          const start = Math.min(lastSelectedTaskIndex, currentTaskIndex);
          const end = Math.max(lastSelectedTaskIndex, currentTaskIndex);

          setSelectedTaskIds(filtered.slice(start, end + 1).map((t) => t.id));
        }
        break;
    }
  }, []);

  // Recomputed on every render before, including the many caused purely by a pushed progress tick.
  const filteredTasks = useMemo(() => {
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

    return taskFilters.length === 0 ? tasks : tasks.filter((x) => taskFilters.every((f) => f(x)));
  }, [tasks, form.thirdPartyId, form.status, form.keyword]);

  /**
   * Membership is tested once per row per render. As an array that is a linear scan each time, so
   * selecting everything (ctrl+A is a supported gesture here) made every render quadratic in the
   * number of tasks.
   */
  const selectedTaskIdSet = useMemo(() => new Set(selectedTaskIds), [selectedTaskIds]);

  /**
   * Counts for the source and status filter chips, in one pass over the tasks instead of one pass
   * per chip — there are a dozen chips, so that was a dozen full scans on every render.
   */
  const { countsByThirdParty, countsByStatus } = useMemo(() => {
    const byThirdParty = new Map<number, number>();
    const byStatus = new Map<number, number>();

    for (const task of tasks) {
      byThirdParty.set(task.thirdPartyId, (byThirdParty.get(task.thirdPartyId) ?? 0) + 1);
      byStatus.set(task.status, (byStatus.get(task.status) ?? 0) + 1);
    }

    return { countsByThirdParty: byThirdParty, countsByStatus: byStatus };
  }, [tasks]);

  // Keep the latest filtered tasks available to the (once-registered) key handler.
  const filteredTasksRef = useRef(filteredTasks);

  filteredTasksRef.current = filteredTasks;

  /**
   * Everything the row handlers need that is rebuilt on each render, kept behind one ref so the
   * handlers themselves can be created once. Without this each render hands every row a fresh set
   * of callbacks, which defeats the row memoization entirely.
   */
  const rowEnvRef = useRef({ startTasksManually, withOptimisticStatus, createPortal, t });

  rowEnvRef.current = { startTasksManually, withOptimisticStatus, createPortal, t };

  const handleRowStart = useCallback((id: number) => {
    rowEnvRef.current.startTasksManually([id]);
  }, []);

  const handleRowStop = useCallback((id: number) => {
    rowEnvRef.current.withOptimisticStatus([id], DownloadTaskStatus.Stopping, () =>
      BApi.downloadTask.stopDownloadTasks([id]),
    );
  }, []);

  const handleRowEdit = useCallback((id: number) => {
    rowEnvRef.current.createPortal(DownloadTaskDetailModal, { id });
  }, []);

  const handleRowOpenFolder = useCallback((path: string) => {
    BApi.tool.openFileOrDirectory({ path });
  }, []);

  const handleRowDelete = useCallback((id: number) => {
    const { createPortal: portal, t: translate } = rowEnvRef.current;

    portal(Modal, {
      defaultVisible: true,
      title: translate<string>("downloader.confirm.deleteTask"),
      onOk: () => BApi.downloadTask.deleteDownloadTasks({ ids: [id] }),
    });
  }, []);

  // Shows a failed task's error, or the notes a completed task left behind (skipped items etc.).
  const handleRowShowError = useCallback(
    (task: { message?: string; status?: DownloadTaskStatus }) => {
      const { createPortal: portal, t: translate } = rowEnvRef.current;
      const failed = task.status === DownloadTaskStatus.Failed;

      portal(Modal, {
        defaultVisible: true,
        size: "xl",
        title: translate<string>(failed ? "common.label.error" : "downloader.label.notices"),
        footer: { actions: ["cancel"] },
        children: (
          <TaskErrorMessage
            copyTip={failed ? undefined : translate<string>("downloader.tip.clickToCopyNotices")}
            message={task.message ?? ""}
          />
        ),
      });
    },
    [],
  );

  const handleRowClick = useCallback((id: number, e: any) => onTaskClick(id, e), [onTaskClick]);

  const handleRowContextMenu = useCallback((id: number, e: any) => {
    e.preventDefault();
    if (!selectedTaskIdsRef.current.includes(id)) {
      setSelectedTaskIds([id]);
    }
    contextMenuAnchorPointRef.current = { x: e.clientX, y: e.clientY };
    toggleMenu(true);
    forceUpdate();
  }, []);

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

  const visibleSelectedTasks = filteredTasks.filter((task) => selectedTaskIdSet.has(task.id));
  const visibleSelectedIds = visibleSelectedTasks.map((task) => task.id);
  const hasFilters = form.thirdPartyId != null || form.status != null || !!form.keyword;
  const changeFilters = (next: DownloadTaskFilter) => {
    setForm(next);
    setSelectedTaskIds([]);
  };

  return (
    <div className="flex h-full min-h-0 min-w-0 flex-col gap-4 p-3 sm:p-5">
      {renderContextMenu()}
      <header className="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <AiOutlineDownload size={22} />
          </div>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">{t("downloader.page.title")}</h1>
            <p className="mt-0.5 text-xs text-default-500">{t("downloader.page.description")}</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <WorkflowIntegrationHint surface="downloader" />
          <Button size="sm" variant="flat" onPress={() => createPortal(Configurations, {})}>
            <AiOutlineSetting size={17} />
            {t("downloader.label.configurations")}
          </Button>
          <Button
            color="primary"
            size="sm"
            onPress={() => createPortal(DownloadTaskDetailModal, {})}
          >
            <AiOutlinePlusCircle size={17} />
            {t("downloader.action.createTask")}
          </Button>
        </div>
      </header>
      <div className="max-h-[42%] shrink-0 overflow-y-auto overscroll-contain">
        <DownloadTaskFilters
          countsByStatus={countsByStatus}
          countsByThirdParty={countsByThirdParty}
          sources={sortedThirdPartyIds}
          total={tasks.length}
          value={form}
          onChange={changeFilters}
        />
      </div>
      <section
        aria-label={t("downloader.page.taskList")}
        className="flex min-h-0 min-w-0 flex-1 flex-col gap-2"
      >
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 px-1">
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <h2 className="text-sm font-medium">{t("downloader.page.taskList")}</h2>
            <span aria-live="polite" className="text-xs tabular-nums text-default-500">
              {visibleSelectedIds.length > 0
                ? t("downloader.batchEdit.selectedCount", { count: visibleSelectedIds.length })
                : t("downloader.page.showing", {
                    count: filteredTasks.length,
                    total: tasks.length,
                  })}
            </span>
          </div>
          <div className="flex flex-wrap items-center gap-1">
            {visibleSelectedIds.length > 0 ? (
              <>
                <Button
                  size="sm"
                  variant="flat"
                  onPress={() => startTasksManually(visibleSelectedIds)}
                >
                  <AiOutlinePlayCircle size={17} />
                  {t("downloader.action.startSelected")}
                </Button>
                <Button
                  size="sm"
                  variant="light"
                  onPress={() =>
                    withOptimisticStatus(visibleSelectedIds, DownloadTaskStatus.Stopping, () =>
                      BApi.downloadTask.stopDownloadTasks(visibleSelectedIds),
                    )
                  }
                >
                  <AiOutlineStop size={17} />
                  {t("downloader.action.stopSelected")}
                </Button>
                {visibleSelectedIds.length > 1 && (
                  <Button
                    size="sm"
                    variant="light"
                    onPress={() => createPortal(BatchEditModal, { tasks: visibleSelectedTasks })}
                  >
                    <AiOutlineEdit size={17} />
                    {t("downloader.action.editSelected")}
                  </Button>
                )}
                <Tooltip content={t("downloader.action.clearSelection")}>
                  <Button
                    isIconOnly
                    aria-label={t("downloader.action.clearSelection")}
                    size="sm"
                    variant="light"
                    onPress={() => setSelectedTaskIds([])}
                  >
                    <AiOutlineClose size={16} />
                  </Button>
                </Tooltip>
              </>
            ) : (
              <>
                <Button
                  isDisabled={tasks.length === 0}
                  size="sm"
                  variant="light"
                  onPress={() => {
                    toast.success(t("downloader.toast.startingAll"));
                    startTasksManually([], DownloadTaskActionOnConflict.Ignore);
                  }}
                >
                  <AiOutlinePlayCircle size={17} />
                  {t("downloader.action.startAll")}
                </Button>
                <Button
                  isDisabled={tasks.length === 0}
                  size="sm"
                  variant="light"
                  onPress={() => {
                    toast.success(t("downloader.toast.stoppingAll"));
                    BApi.downloadTask.stopDownloadTasks([]);
                  }}
                >
                  <AiOutlineStop size={17} />
                  {t("downloader.action.stopAll")}
                </Button>
              </>
            )}
            <div className="flex shrink-0 items-center gap-1">
              <span aria-hidden className="mx-1 h-4 w-px bg-divider" />
              <Tooltip content={t("downloader.action.locateActive.tip")}>
                <Button
                  isIconOnly
                  aria-label={t("downloader.action.locateActive")}
                  size="sm"
                  variant="light"
                  onPress={locateActiveTask}
                >
                  <AiOutlineAim size={18} />
                </Button>
              </Tooltip>
              <RequestStatistics compact />
              <Dropdown>
                <DropdownTrigger>
                  <Button
                    isIconOnly
                    aria-label={t("downloader.action.moreTasks")}
                    size="sm"
                    variant="light"
                  >
                    <AiOutlineEllipsis size={18} />
                  </Button>
                </DropdownTrigger>
                <DropdownMenu
                  onAction={(key) => {
                    switch (key as string) {
                      case "export":
                        BApi.gui.openUrlInDefaultBrowser({
                          url: toAbsoluteBackendUrl("/download-task/xlsx"),
                        });
                        break;
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
                    key="export"
                    showDivider
                    startContent={<AiOutlineExport size={16} />}
                  >
                    {t("downloader.action.exportAll")}
                  </DropdownItem>
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
            </div>
          </div>
        </div>
        <div
          ref={(r) => {
            taskListRef.current = r;
            if (r && taskListHeight == 0) {
              setTaskListHeight(r.clientHeight);
            }
          }}
          className="min-h-0 flex-1 overflow-hidden"
        >
          {/* The virtualizer is told the viewport height once, so resizing the window (or opening a
            panel that changes the layout) left it rendering for the old size — a short list with
            dead space below, or a long one clipped. Keep it in step. */}
          {taskListHeight > 0 &&
            (filteredTasks.length === 0 ? (
              <div className="flex h-full min-h-40 flex-col items-center justify-center gap-3 rounded-2xl bg-default-50/40 px-6 py-8 text-center">
                <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-default-100 text-default-400">
                  <AiOutlineDownload size={28} />
                </div>
                <div className="space-y-1.5">
                  <h3 className="text-sm font-medium">
                    {t(
                      hasFilters
                        ? "downloader.empty.filteredTitle"
                        : "downloader.empty.initialTitle",
                    )}
                  </h3>
                  <p className="max-w-sm text-xs leading-relaxed text-default-500">
                    {t(
                      hasFilters
                        ? "downloader.empty.filteredDescription"
                        : "downloader.empty.initialDescription",
                    )}
                  </p>
                </div>
                <Button
                  color="primary"
                  size="sm"
                  variant="flat"
                  onPress={() =>
                    hasFilters ? changeFilters({}) : createPortal(DownloadTaskDetailModal, {})
                  }
                >
                  {t(hasFilters ? "downloader.filter.reset" : "downloader.action.createTask")}
                </Button>
              </div>
            ) : (
              <Listbox
                isVirtualized
                aria-label={t("downloader.page.taskList")}
                className={"p-0"}
                emptyContent={t<string>("downloader.empty.noTasks")}
                variant={"flat"}
                virtualization={{
                  maxListboxHeight: taskListHeight,
                  itemHeight: TASK_ITEM_HEIGHT,
                }}
              >
                {filteredTasks.map((task) => (
                  <ListboxItem
                    key={task.id}
                    className={`rounded-xl px-3 py-1.5 ${selectedTaskIdSet.has(task.id) ? "bg-primary-50 dark:bg-primary-900/20" : ""}`}
                    classNames={{ wrapper: "min-w-0", title: "h-full min-w-0 w-full" }}
                    style={{ height: TASK_ITEM_HEIGHT }}
                    textValue={task.name || task.key}
                  >
                    <TaskRow
                      formatDateTime={formatTaskDateTime}
                      progressColor={DownloadTaskStatusProgressBarColorMap[task.status]}
                      statusColor={DownloadTaskStatusIceLabelStatusMap[task.status]}
                      task={task}
                      onClick={handleRowClick}
                      onContextMenu={handleRowContextMenu}
                      onDelete={handleRowDelete}
                      onEdit={handleRowEdit}
                      onOpenFolder={handleRowOpenFolder}
                      onShowError={handleRowShowError}
                      onStart={handleRowStart}
                      onStop={handleRowStop}
                    />
                  </ListboxItem>
                ))}
              </Listbox>
            ))}
        </div>
      </section>
    </div>
  );
};

DownloaderPage.displayName = "DownloaderPage";
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

export default DownloaderPage;
