"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { ControlledMenu, MenuItem, useMenuState } from "@szhsin/react-menu";
import {
  AiOutlineAim,
  AiOutlineClose,
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineEdit,
  AiOutlineEllipsis,
  AiOutlineExport,
  AiOutlinePlayCircle,
  AiOutlinePlusCircle,
  AiOutlineReload,
  AiOutlineSetting,
  AiOutlineStop,
} from "react-icons/ai";
import "@szhsin/react-menu/dist/index.css";
import "@szhsin/react-menu/dist/transitions/slide.css";

import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Listbox,
  ListboxItem,
  Modal,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import { useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { DownloadTaskActionOnConflict, DownloadTaskStatus, ResponseCode } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import {
  isPrimaryModifierPressed,
  matchesPrimaryShortcut,
  shouldIgnoreGlobalShortcut,
} from "@/core/keyboard";
import { toAbsoluteBackendUrl } from "@/config/env";
import { openExternalUrl } from "@/utils/openExternalUrl";
import { extractErrorMessage } from "@/components/utils";
import FolderLocationModal from "@/components/Resource/components/FolderLocationModal";
import WorkflowIntegrationHint from "@/components/Workflow/WorkflowIntegrationHint";
import DownloadTaskDetailModal from "./components/TaskDetailModal";
import Configurations from "./components/Configurations";
import BatchEditModal from "./components/BatchEditModal";
import TaskErrorMessage from "./components/TaskErrorMessage";
import RequestStatistics from "./components/RequestStatistics";
import DownloadOrderPrompt from "./components/DownloadOrderPrompt";
import TaskRow, { DOWNLOAD_TASK_ITEM_HEIGHT } from "./components/TaskRow";
import GeneralTaskRow from "./components/GeneralTaskRow";
import GeneralTaskDetailModal from "./components/GeneralTaskDetailModal";
import AddGeneralDownloadModal from "./components/AddGeneralDownloadModal";
import DownloadCenterFilters from "./components/DownloadCenterFilters";
import useDownloadCenter from "./useDownloadCenter";
import {
  actionTargetIds,
  buildCenterRows,
  centerStatusColor,
  readCenterFilter,
  type CenterFilter,
  type CenterTask,
  type TaskAction,
} from "./downloadCenter";
import { downloadTaskDirectly } from "./directDownload";
import { downloadTaskWithBt } from "./btDownload";
import { downloadTaskTorrentFile } from "./torrentFileDownload";
import { DownloadFolderUnavailableError, openDownloadTaskFolder } from "./openDownloadTaskFolder";

const formatTaskDateTime = (value?: string | Date | null) =>
  value ? new Date(value).toLocaleString() : "";
const findScrollContainer = (root: HTMLElement | null) =>
  root &&
  [root, ...Array.from(root.querySelectorAll<HTMLElement>("*"))].find(
    (element) =>
      ["auto", "scroll"].includes(getComputedStyle(element).overflowY) &&
      element.scrollHeight > element.clientHeight,
  );

export default function DownloaderPage() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const sourceTasks = useDownloadTasksStore((state) => state.tasks);
  const { tasks, loading, loadFailed, refresh } = useDownloadCenter();
  const [params, setParams] = useSearchParams();
  const [filter, setFilter] = useState<CenterFilter>(() => readCenterFilter(params));
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [actionErrors, setActionErrors] = useState<string[]>([]);
  const [menuProps, toggleMenu] = useMenuState();
  const contextAnchor = useRef({ x: 0, y: 0 });
  const listRef = useRef<HTMLDivElement | null>(null);
  const [height, setHeight] = useState(0);
  const userSideActionsRunHere = useUserSideActionsRunHere();
  const sourceById = useMemo(
    () => new Map(sourceTasks.map((task) => [task.id, task])),
    [sourceTasks],
  );
  const sourceKeys = useMemo(
    () => new Map(sourceTasks.map((task) => [task.id, task.key])),
    [sourceTasks],
  );
  const taskById = useMemo(() => new Map(tasks.map((task) => [task.id, task])), [tasks]);
  const model = useMemo(
    () => buildCenterRows(tasks, filter, expanded, sourceKeys),
    [tasks, filter, expanded, sourceKeys],
  );
  const selected = useMemo(() => new Set(selectedIds), [selectedIds]);
  const visibleSelected = model.rows
    .filter((row) => selected.has(row.task.id))
    .map((row) => row.task.id);
  const selectedSites = visibleSelected.flatMap((id) => {
    const task = taskById.get(id);
    const source =
      task?.category === "site" && task.siteTaskId != null
        ? sourceById.get(task.siteTaskId)
        : undefined;
    return source ? [source] : [];
  });

  // Deep links can change while this page remains mounted (including the old BT route alias).
  const queryKey = params.toString();
  useEffect(() => {
    setFilter((current) => ({ ...current, ...readCenterFilter(new URLSearchParams(queryKey)) }));
    setSelectedIds([]);
  }, [queryKey]);
  useEffect(() => {
    const element = listRef.current;
    if (!element) return;
    const measure = () => setHeight(element.clientHeight);
    measure();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  }, []);
  const changeFilter = (next: CenterFilter) => {
    setFilter(next);
    setSelectedIds([]);
    const search = new URLSearchParams();
    if (next.category !== "all") search.set("category", next.category);
    if (next.provider) search.set("provider", next.provider);
    if (next.id) search.set("id", next.id);
    setParams(search, { replace: true });
  };

  const chooseDownloadOrder = (message?: string | null) =>
    new Promise<DownloadTaskActionOnConflict>((resolve) => {
      let decided = false;
      createPortal(Modal, {
        defaultVisible: true,
        size: "lg",
        title: t("downloader.downloadOrder.title"),
        children: <DownloadOrderPrompt message={message ?? undefined} />,
        footer: {
          actions: ["ok", "cancel"],
          okProps: {
            children: t("downloader.action.downloadSelectedFirst"),
            color: "default",
            variant: "bordered",
          },
          cancelProps: {
            children: t("downloader.action.addToQueue"),
            color: "primary",
            variant: "solid",
            autoFocus: true,
          },
        },
        onOk: () => {
          decided = true;
          resolve(DownloadTaskActionOnConflict.StopOthers);
        },
        onClose: () => {
          if (!decided) resolve(DownloadTaskActionOnConflict.Ignore);
        },
      });
    });

  const execute = async (
    ids: string[],
    action: TaskAction,
    actionOnConflict = DownloadTaskActionOnConflict.NotSet,
  ) => {
    const targets = actionTargetIds(tasks, ids, action);
    if (!targets.length || busy) return;
    setBusy(true);
    setActionErrors([]);
    try {
      const response = await BApi.downloadCenter.batchDownloadCenterTasks({
        ids: targets,
        action,
        actionOnConflict,
      });
      if (response.code) throw new Error(response.message || t("downloadCenter.error.action"));
      const results = response.data ?? [];
      const conflicts = results.filter(
        (result) => !result.success && result.code === ResponseCode.Conflict,
      );
      const failures = results
        .filter((result) => !result.success && result.code !== ResponseCode.Conflict)
        .map(
          (result) =>
            `${taskById.get(result.id)?.name || result.id}: ${result.error || t("downloadCenter.error.action")}`,
        );
      if (conflicts.length) {
        const choice = await chooseDownloadOrder(conflicts[0].error);
        const retry = await BApi.downloadCenter.batchDownloadCenterTasks({
          ids: conflicts.map((item) => item.id),
          action,
          actionOnConflict: choice,
        });
        if (retry.code) throw new Error(retry.message || t("downloadCenter.error.action"));
        failures.push(
          ...(retry.data ?? [])
            .filter((item) => !item.success)
            .map(
              (item) =>
                `${taskById.get(item.id)?.name || item.id}: ${item.error || t("downloadCenter.error.action")}`,
            ),
        );
      }
      setActionErrors(failures);
      if (action === "delete")
        setSelectedIds((current) =>
          current.filter((id) => !results.some((result) => result.id === id && result.success)),
        );
    } catch (error) {
      setActionErrors([extractErrorMessage(error)]);
    } finally {
      await refresh();
      setBusy(false);
    }
  };
  const confirmDelete = (ids: string[]) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: t("downloader.confirm.deleteTasks", {
        count: actionTargetIds(tasks, ids, "delete").length,
      }),
      children: (
        <p className="text-sm text-default-500">{t("downloadCenter.delete.description")}</p>
      ),
      onOk: () => execute(ids, "delete"),
    });
  const createTask = () => {
    const picker = createPortal(Modal, {
      defaultVisible: true,
      title: t("downloader.action.createTask"),
      footer: false,
      classNames: { body: "pb-6" },
      children: (
        <div className="grid gap-3 sm:grid-cols-2">
          <Button
            className="h-auto flex-col items-start whitespace-normal p-4 text-left"
            variant="flat"
            onPress={() => {
              picker.destroy();
              createPortal(AddGeneralDownloadModal, { onCreated: () => void refresh() });
            }}
          >
            <span className="font-medium">{t("downloadCenter.category.general")}</span>
            <span className="text-xs font-normal text-default-500">
              {t("downloadCenter.add.generalDescription")}
            </span>
          </Button>
          <Button
            className="h-auto flex-col items-start whitespace-normal p-4 text-left"
            variant="flat"
            onPress={() => {
              picker.destroy();
              createPortal(DownloadTaskDetailModal, {});
            }}
          >
            <span className="font-medium">{t("downloadCenter.category.site")}</span>
            <span className="text-xs font-normal text-default-500">
              {t("downloadCenter.add.siteDescription")}
            </span>
          </Button>
        </div>
      ),
    });
  };
  const showSite = (id: number) => createPortal(DownloadTaskDetailModal, { id });
  const showError = (task: { message?: string; status?: DownloadTaskStatus }) => {
    const failed = task.status === DownloadTaskStatus.Failed;
    createPortal(Modal, {
      defaultVisible: true,
      size: "xl",
      title: t(failed ? "common.label.error" : "downloader.label.notices"),
      footer: { actions: ["cancel"] },
      children: (
        <TaskErrorMessage
          message={task.message ?? ""}
          copyTip={failed ? undefined : t("downloader.tip.clickToCopyNotices")}
        />
      ),
    });
  };
  const openFolder = (task: Parameters<typeof openDownloadTaskFolder>[0]) => {
    void openDownloadTaskFolder(task, {
      userSideActionsRunHere,
      showLocation: (path) => createPortal(FolderLocationModal, { path }),
    }).catch((error) =>
      toast.danger(
        error instanceof DownloadFolderUnavailableError
          ? t("downloader.error.noFolderAvailable")
          : {
              title: t("downloader.error.openFolderFailed"),
              description: extractErrorMessage(error),
            },
      ),
    );
  };
  const directDownload = async (id: number) => {
    let response = await downloadTaskDirectly(id);
    if (response.code === ResponseCode.Conflict)
      response = await downloadTaskDirectly(id, await chooseDownloadOrder(response.message));
    if (response.code !== ResponseCode.Success)
      throw new Error(response.message || t("downloader.toast.directDownloadFailed"));
    await refresh();
  };
  const btDownload = async (id: number) => {
    let response = await downloadTaskWithBt(id);
    if (response.code === ResponseCode.Conflict)
      response = await downloadTaskWithBt(id, await chooseDownloadOrder(response.message));
    if (response.code !== ResponseCode.Success)
      throw new Error(response.message || t("downloader.toast.btDownloadFailed"));
    await refresh();
  };
  const torrentFileDownload = async (id: number) => {
    let response = await downloadTaskTorrentFile(id);
    if (response.code === ResponseCode.Conflict)
      response = await downloadTaskTorrentFile(id, await chooseDownloadOrder(response.message));
    if (response.code !== ResponseCode.Success)
      throw new Error(response.message || t("downloader.toast.torrentFileDownloadFailed"));
    await refresh();
  };
  const clearCheckpoints = (ids: number[]) =>
    createPortal(Modal, {
      defaultVisible: true,
      title: t("downloader.confirm.clearCheckpoints", { count: ids.length }),
      onOk: async () => {
        await BApi.request<void, any>({
          path: "/download-task/checkpoint",
          method: "DELETE",
          body: ids,
          type: "application/json",
          format: "json",
        } as any);
        await refresh();
      },
    });

  // Stable callbacks keep progress updates from rerendering every offscreen site row.
  const env = useRef({
    execute,
    confirmDelete,
    showSite,
    showError,
    openFolder,
    directDownload,
    btDownload,
    torrentFileDownload,
    model,
    selectedIds,
    taskById,
    createPortal,
  });
  env.current = {
    execute,
    confirmDelete,
    showSite,
    showError,
    openFolder,
    directDownload,
    btDownload,
    torrentFileDownload,
    model,
    selectedIds,
    taskById,
    createPortal,
  };
  const selectTask = useCallback(
    (id: string, event?: { shiftKey?: boolean; ctrlKey?: boolean; metaKey?: boolean }) => {
      const current = env.current.selectedIds;
      if (event?.shiftKey && current.length) {
        const rows = env.current.model.rows;
        const start = rows.findIndex((row) => row.task.id === current[current.length - 1]);
        const end = rows.findIndex((row) => row.task.id === id);
        setSelectedIds(
          start < 0
            ? [id]
            : rows.slice(Math.min(start, end), Math.max(start, end) + 1).map((row) => row.task.id),
        );
      } else if (
        event &&
        isPrimaryModifierPressed({ ctrlKey: !!event.ctrlKey, metaKey: !!event.metaKey })
      )
        setSelectedIds(
          current.includes(id) ? current.filter((item) => item !== id) : [...current, id],
        );
      else setSelectedIds(current.length === 1 && current[0] === id ? [] : [id]);
    },
    [],
  );
  const rowAction = useCallback((id: string, action: TaskAction) => {
    if (action === "delete") env.current.confirmDelete([id]);
    else void env.current.execute([id], action);
  }, []);
  const generalFolder = useCallback((id: string) => {
    const task = env.current.taskById.get(id);
    if (task?.outputDirectory)
      env.current.openFolder({ downloadPath: task.outputDirectory } as Parameters<
        typeof openDownloadTaskFolder
      >[0]);
  }, []);
  const siteClick = useCallback(
    (id: number, event: any) => selectTask(`site:${id}`, event),
    [selectTask],
  );
  const siteContext = useCallback((id: number, event: any) => {
    event.preventDefault();
    if (!env.current.selectedIds.includes(`site:${id}`)) setSelectedIds([`site:${id}`]);
    contextAnchor.current = { x: event.clientX, y: event.clientY };
    toggleMenu(true);
  }, []);
  const siteStart = useCallback(
    (id: number) => {
      const task = env.current.taskById.get(`site:${id}`);
      rowAction(`site:${id}`, task?.availableActions?.includes("retry") ? "retry" : "start");
    },
    [rowAction],
  );
  const siteStop = useCallback((id: number) => rowAction(`site:${id}`, "stop"), [rowAction]);
  const siteDelete = useCallback((id: number) => rowAction(`site:${id}`, "delete"), [rowAction]);
  const siteEdit = useCallback((id: number) => env.current.showSite(id), []);
  const siteError = useCallback(
    (task: Parameters<typeof showError>[0]) => env.current.showError(task),
    [],
  );
  const siteFolder = useCallback(
    (task: Parameters<typeof openFolder>[0]) => env.current.openFolder(task),
    [],
  );
  const siteDirect = useCallback((id: number) => env.current.directDownload(id), []);
  const siteBt = useCallback((id: number) => env.current.btDownload(id), []);
  const siteTorrentFile = useCallback((id: number) => env.current.torrentFileDownload(id), []);
  const toggleChildren = useCallback(
    (id: number) =>
      setExpanded((current) => {
        const next = new Set(current);
        const key = `site:${id}`;
        if (next.has(key)) next.delete(key);
        else next.add(key);
        return next;
      }),
    [],
  );
  const showDetail = useCallback(
    (id: string) => {
      const task = env.current.taskById.get(id);
      if (task)
        env.current.createPortal(GeneralTaskDetailModal, {
          task,
          onAction: rowAction,
          onSource: siteEdit,
          onFolder: generalFolder,
        });
    },
    [rowAction, siteEdit, generalFolder],
  );
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (
        !shouldIgnoreGlobalShortcut(event) &&
        matchesPrimaryShortcut(event, "a") &&
        listRef.current?.contains(document.activeElement)
      ) {
        event.preventDefault();
        setSelectedIds(env.current.model.rows.map((row) => row.task.id));
      }
    };
    document.addEventListener("keydown", onKeyDown);
    return () => document.removeEventListener("keydown", onKeyDown);
  }, []);
  const locate = () => {
    const index = model.rows.findIndex((row) =>
      ["running", "processing", "queued", "stopping"].includes(row.task.status),
    );
    if (index < 0) {
      toast.warning(
        t(
          tasks.some((task) => ["running", "processing", "queued"].includes(task.status))
            ? "downloader.toast.activeTaskFilteredOut"
            : "downloader.toast.noActiveTask",
        ),
      );
      return;
    }
    const container = findScrollContainer(listRef.current);
    container?.scrollTo({
      top: Math.max(
        0,
        index * DOWNLOAD_TASK_ITEM_HEIGHT -
          (container.clientHeight - DOWNLOAD_TASK_ITEM_HEIGHT) / 2,
      ),
      behavior: "smooth",
    });
    setSelectedIds([model.rows[index].task.id]);
  };
  const hasFilters =
    filter.category !== "all" ||
    !!filter.provider ||
    !!filter.keyword ||
    !!filter.status ||
    !!filter.id;
  const allVisibleIds = model.filtered.map((task) => task.id);
  const batchIds = visibleSelected.length ? visibleSelected : allVisibleIds;

  return (
    <div className="flex h-full min-h-0 min-w-0 flex-col gap-4 p-3 sm:p-5">
      <ControlledMenu
        {...menuProps}
        anchorPoint={contextAnchor.current}
        onClose={() => toggleMenu(false)}
      >
        {(["start", "stop", "retry"] as const).map((action) => (
          <MenuItem
            key={action}
            disabled={busy || !actionTargetIds(tasks, visibleSelected, action).length}
            onClick={() => void execute(visibleSelected, action)}
          >
            {t(`downloadCenter.action.${action}`)}
          </MenuItem>
        ))}
        {selectedSites.length > 1 && (
          <MenuItem onClick={() => createPortal(BatchEditModal, { tasks: selectedSites })}>
            {t("downloader.action.editSelected")}
          </MenuItem>
        )}
        {selectedSites.length > 0 && (
          <MenuItem onClick={() => clearCheckpoints(selectedSites.map((task) => task.id))}>
            {t("downloader.action.clearCheckpoints")}
          </MenuItem>
        )}
        <MenuItem
          disabled={busy || !actionTargetIds(tasks, visibleSelected, "delete").length}
          onClick={() => confirmDelete(visibleSelected)}
        >
          {t("common.action.delete")}
        </MenuItem>
      </ControlledMenu>
      <header className="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <AiOutlineDownload size={22} />
          </div>
          <div>
            <h1 className="text-xl font-semibold tracking-tight">{t("downloadCenter.title")}</h1>
            <p className="mt-0.5 text-xs text-default-500">{t("downloadCenter.description")}</p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <WorkflowIntegrationHint surface="downloader" />
          <Button size="sm" color="primary" onPress={createTask}>
            <AiOutlinePlusCircle />
            {t("downloader.action.createTask")}
          </Button>
          <Button size="sm" variant="flat" onPress={() => createPortal(Configurations, {})}>
            <AiOutlineSetting />
            {t("downloader.label.configurations")}
          </Button>
        </div>
      </header>
      <div className="max-h-[42%] shrink-0 overflow-y-auto overscroll-contain">
        <DownloadCenterFilters
          value={filter}
          onChange={changeFilter}
          tasks={tasks}
          roots={model.roots}
        />
      </div>
      {loadFailed && (
        <div role="alert" className="flex items-center gap-2 text-sm text-danger">
          {t("downloadCenter.error.load")}
          <Button size="sm" variant="light" onPress={() => void refresh()}>
            {t("common.action.refresh")}
          </Button>
        </div>
      )}
      {actionErrors.length > 0 && (
        <div
          role="alert"
          className="max-h-28 shrink-0 overflow-y-auto rounded-lg bg-danger-50 p-3 text-sm text-danger"
        >
          {actionErrors.map((error, index) => (
            <p key={index}>{error}</p>
          ))}
        </div>
      )}
      <section
        aria-label={t("downloader.page.taskList")}
        className="flex min-h-0 min-w-0 flex-1 flex-col gap-2"
      >
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 px-1">
          <div className="flex items-center gap-3">
            <h2 className="text-sm font-medium">{t("downloader.page.taskList")}</h2>
            <span aria-live="polite" className="text-xs tabular-nums text-default-500">
              {visibleSelected.length
                ? t("downloader.batchEdit.selectedCount", { count: visibleSelected.length })
                : t("downloader.page.showing", {
                    count: model.filtered.length,
                    total: model.total,
                  })}
            </span>
          </div>
          <div className="flex flex-wrap items-center gap-1">
            <Button
              isDisabled={busy || !actionTargetIds(tasks, batchIds, "start").length}
              size="sm"
              variant="flat"
              onPress={() =>
                void execute(
                  batchIds,
                  "start",
                  visibleSelected.length
                    ? DownloadTaskActionOnConflict.NotSet
                    : DownloadTaskActionOnConflict.Ignore,
                )
              }
            >
              <AiOutlinePlayCircle />
              {t(
                visibleSelected.length
                  ? "downloader.action.startSelected"
                  : "downloadCenter.action.startVisible",
              )}
            </Button>
            <Button
              isDisabled={busy || !actionTargetIds(tasks, batchIds, "stop").length}
              size="sm"
              variant="light"
              onPress={() => void execute(batchIds, "stop")}
            >
              <AiOutlineStop />
              {t(
                visibleSelected.length
                  ? "downloader.action.stopSelected"
                  : "downloadCenter.action.stopVisible",
              )}
            </Button>
            {visibleSelected.length > 0 && (
              <>
                <Button
                  isDisabled={busy || !actionTargetIds(tasks, batchIds, "retry").length}
                  size="sm"
                  variant="light"
                  onPress={() => void execute(batchIds, "retry")}
                >
                  <AiOutlineReload />
                  {t("downloadCenter.action.retrySelected")}
                </Button>
                {selectedSites.length > 1 && (
                  <Button
                    size="sm"
                    variant="light"
                    onPress={() => createPortal(BatchEditModal, { tasks: selectedSites })}
                  >
                    <AiOutlineEdit />
                    {t("downloader.action.editSelected")}
                  </Button>
                )}
                <Button
                  isIconOnly
                  aria-label={t("downloader.action.clearSelection")}
                  size="sm"
                  variant="light"
                  onPress={() => setSelectedIds([])}
                >
                  <AiOutlineClose />
                </Button>
              </>
            )}
            <Tooltip content={t("downloader.action.locateActive.tip")}>
              <Button
                isIconOnly
                aria-label={t("downloader.action.locateActive")}
                size="sm"
                variant="light"
                onPress={locate}
              >
                <AiOutlineAim />
              </Button>
            </Tooltip>
            <Button
              isIconOnly
              aria-label={t("common.action.refresh")}
              size="sm"
              variant="light"
              onPress={() => void refresh()}
            >
              <AiOutlineReload />
            </Button>
            <RequestStatistics compact />
            <Dropdown>
              <DropdownTrigger>
                <Button
                  isIconOnly
                  aria-label={t("downloader.action.moreTasks")}
                  size="sm"
                  variant="light"
                >
                  <AiOutlineEllipsis />
                </Button>
              </DropdownTrigger>
              <DropdownMenu
                onAction={(key) => {
                  if (key === "export")
                    openExternalUrl(toAbsoluteBackendUrl("/download-task/xlsx"));
                  else if (key === "checkpoint")
                    clearCheckpoints(selectedSites.map((task) => task.id));
                  else if (key === "selected") confirmDelete(visibleSelected);
                  else {
                    const status = key === "completed" ? "completed" : "failed";
                    const ids = model.filtered
                      .filter(
                        (task) =>
                          task.status === status && task.availableActions?.includes("delete"),
                      )
                      .map((task) => task.id);
                    if (ids.length) confirmDelete(ids);
                    else
                      toast.warning(
                        t(
                          status === "completed"
                            ? "downloader.toast.noCompletedTasks"
                            : "downloader.toast.noFailedTasks",
                        ),
                      );
                  }
                }}
                disabledKeys={[
                  ...(selectedSites.length ? [] : ["checkpoint"]),
                  ...(visibleSelected.length &&
                  actionTargetIds(tasks, visibleSelected, "delete").length
                    ? []
                    : ["selected"]),
                ]}
              >
                <DropdownItem key="export" startContent={<AiOutlineExport />}>
                  {t("downloadCenter.action.exportSites")}
                </DropdownItem>
                <DropdownItem key="checkpoint">
                  {t("downloader.action.clearCheckpoints")}
                </DropdownItem>
                <DropdownItem key="selected" startContent={<AiOutlineDelete />} color="danger">
                  {t("downloadCenter.action.deleteSelected")}
                </DropdownItem>
                <DropdownItem key="completed">
                  {t("downloader.action.deleteCompleted")}
                </DropdownItem>
                <DropdownItem key="failed" color="danger">
                  {t("downloader.action.deleteFailed")}
                </DropdownItem>
              </DropdownMenu>
            </Dropdown>
          </div>
        </div>
        <div ref={listRef} className="min-h-0 flex-1 overflow-hidden">
          {loading ? (
            <div className="flex h-full items-center justify-center">
              <Spinner />
            </div>
          ) : !model.rows.length ? (
            <div className="flex h-full min-h-40 flex-col items-center justify-center gap-3 rounded-2xl bg-default-50/40 p-6 text-center">
              <AiOutlineDownload className="text-default-400" size={28} />
              <h3 className="text-sm font-medium">
                {t(hasFilters ? "downloader.empty.filteredTitle" : "downloader.empty.initialTitle")}
              </h3>
              <p className="max-w-sm text-xs text-default-500">
                {t(hasFilters ? "downloader.empty.filteredDescription" : "downloadCenter.empty")}
              </p>
              <Button
                size="sm"
                color="primary"
                variant="flat"
                onPress={() => (hasFilters ? changeFilter({ category: "all" }) : createTask())}
              >
                {t(hasFilters ? "downloader.filter.reset" : "downloader.action.createTask")}
              </Button>
            </div>
          ) : (
            height > 0 && (
              <Listbox
                isVirtualized
                aria-label={t("downloader.page.taskList")}
                className="p-0"
                variant="flat"
                virtualization={{ maxListboxHeight: height, itemHeight: DOWNLOAD_TASK_ITEM_HEIGHT }}
              >
                {model.rows.map(({ task, nested }) => {
                  const source =
                    task.category === "site" && task.siteTaskId != null
                      ? sourceById.get(task.siteTaskId)
                      : undefined;
                  const color = centerStatusColor(task.status);
                  return (
                    <ListboxItem
                      key={task.id}
                      className={`rounded-xl py-1.5 ${nested ? "border-l-2 border-primary/20 pl-9 pr-3" : "px-3"} ${selected.has(task.id) ? "bg-primary-50 dark:bg-primary-900/20" : ""}`}
                      classNames={{ wrapper: "min-w-0", title: "h-full min-w-0 w-full" }}
                      style={{ height: DOWNLOAD_TASK_ITEM_HEIGHT }}
                      textValue={task.name || task.id}
                    >
                      {source ? (
                        <TaskRow
                          task={source}
                          centerTask={task}
                          execution={task.execution ?? undefined}
                          childCount={model.children.get(task.id)?.length ?? 0}
                          childrenExpanded={expanded.has(task.id)}
                          onToggleChildren={toggleChildren}
                          formatDateTime={formatTaskDateTime}
                          statusColor={color}
                          progressColor={color}
                          openFolderLabel={t(
                            userSideActionsRunHere
                              ? "common.action.openFolder"
                              : "resource.folderLocation.title",
                          )}
                          onClick={siteClick}
                          onContextMenu={siteContext}
                          onStart={siteStart}
                          onStop={siteStop}
                          onEdit={siteEdit}
                          onDelete={siteDelete}
                          onDownloadDirectly={siteDirect}
                          onDownloadWithBt={siteBt}
                          onDownloadTorrentFile={siteTorrentFile}
                          onShowError={siteError}
                          onOpenFolder={siteFolder}
                        />
                      ) : (
                        <GeneralTaskRow
                          task={task}
                          busy={busy}
                          onSelect={selectTask}
                          onAction={rowAction}
                          onDetail={showDetail}
                          onSource={siteEdit}
                          onFolder={generalFolder}
                        />
                      )}
                    </ListboxItem>
                  );
                })}
              </Listbox>
            )
          )}
        </div>
      </section>
    </div>
  );
}
