"use client";

import type { SyntheticEvent } from "react";
import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { DownloadTask } from "@/core/models/DownloadTask";

import { memo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineDown,
  AiOutlineEdit,
  AiOutlineEllipsis,
  AiOutlineFolderOpen,
  AiOutlineInfoCircle,
  AiOutlinePlayCircle,
  AiOutlineRedo,
  AiOutlineStop,
  AiOutlineWarning,
} from "react-icons/ai";
import { TbMagnet, TbMagnetOff } from "react-icons/tb";

import { DownloadTaskTypeIconMap } from "./TaskDetailModal/models";
import { useEstimatedRemainingLabel } from "./EstimatedRemainingTime";

import { humanFileSize } from "@/components/utils";
import { DownloadTaskAction, DownloadTaskStatus, ThirdPartyId } from "@/sdk/constants";
import {
  Button,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Progress,
  Tooltip,
  toast,
} from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";

export const DOWNLOAD_TASK_ITEM_HEIGHT = 128;
export const DOWNLOAD_TASK_ROW_HEIGHT = 116;

// React Aria keyboard events already stop by default; native React events need the boundary.
const stopPropagation = (event: SyntheticEvent) => {
  if (!("continuePropagation" in event)) {
    event.stopPropagation();
  }
};

export type TaskRowProps = {
  task: DownloadTask;
  statusColor: ChipProps["color"];
  progressColor: CircularProgressProps["color"];
  formatDateTime: (value?: string | Date | null) => string;
  onStart: (id: number) => void;
  onDownloadDirectly: (id: number) => Promise<void>;
  onStop: (id: number) => void;
  onEdit: (id: number) => void;
  onOpenFolder: (task: DownloadTask) => void;
  onDelete: (id: number) => void;
  onShowError: (task: DownloadTask) => void;
  onClick: (id: number, e: any) => void;
  onContextMenu: (id: number, e: any) => void;
};

/**
 * One row of the download task list.
 *
 * Split out of the page and memoized on purpose. The list is virtualized, but its children are
 * still built in full on every render — so with several hundred tasks the page was allocating tens
 * of thousands of elements for every pushed progress tick, whether or not anything on screen had
 * changed. As one memoized component per row, an unchanged row costs a single element and no work
 * at all. Every callback prop must therefore be referentially stable, or the memo buys nothing.
 */
const TaskRow = memo(function TaskRow({
  task,
  statusColor,
  progressColor,
  formatDateTime,
  onStart,
  onDownloadDirectly,
  onStop,
  onEdit,
  onOpenFolder,
  onDelete,
  onShowError,
  onClick,
  onContextMenu,
}: TaskRowProps) {
  const { t } = useTranslation();
  const hasErrorMessage = task.status === DownloadTaskStatus.Failed && !!task.message;
  // A task can complete and still leave notes behind (e.g. items it skipped). The message is a
  // plain-text block whose first line is the summary; the full block opens in the same dialog.
  const hasNotices = task.status === DownloadTaskStatus.Complete && !!task.message;
  const noticeSummary = task.message?.split("\n", 1)[0];
  const Icon = DownloadTaskTypeIconMap[task.thirdPartyId!]?.[task.type];
  const progress = Number.isFinite(task.progress) ? Math.min(100, Math.max(0, task.progress)) : 0;
  const downloadedSize =
    task.downloadedBytes != null &&
    Number.isFinite(task.downloadedBytes) &&
    task.downloadedBytes >= 0
      ? humanFileSize(task.downloadedBytes, false, 1)
      : undefined;
  const downloadSpeed =
    task.status === DownloadTaskStatus.Downloading &&
    task.downloadSpeedBytesPerSecond != null &&
    Number.isFinite(task.downloadSpeedBytesPerSecond) &&
    task.downloadSpeedBytesPerSecond > 0
      ? `${humanFileSize(task.downloadSpeedBytesPerSecond, false, 1)}/s`
      : undefined;
  const name = task.name || task.key;
  const createdAt = `${t<string>("downloader.label.createdAt")} ${formatDateTime(task.createdAt)}`;
  const nextStart = task.nextStartDt
    ? `${t<string>("downloader.label.nextStartTime")} ${formatDateTime(task.nextStartDt)}`
    : undefined;
  const errorLabel =
    task.failureTimes > 0
      ? t<string>("downloader.action.showError", { count: task.failureTimes })
      : t<string>("downloader.action.viewError");
  const noticesLabel = t<string>("downloader.action.viewNotices");
  const estimatedRemaining = useEstimatedRemainingLabel(task);

  return (
    <div
      aria-label={name}
      className="flex w-full min-w-0 flex-col justify-between gap-1 py-1 text-left outline-none focus-visible:rounded-lg focus-visible:ring-2 focus-visible:ring-primary"
      role="button"
      style={{ height: DOWNLOAD_TASK_ROW_HEIGHT }}
      tabIndex={0}
      onClick={(e) => {
        if (!(e.target as HTMLElement).closest("[data-task-action]")) {
          onClick(task.id, e);
        }
      }}
      onContextMenu={(e) => onContextMenu(task.id, e)}
      onKeyDown={(e) => {
        // Buttons and portalled menu items have their own keyboard interaction. Only a focused
        // row may translate Enter/Space into selection; bubbling presses must never select it.
        if (e.target === e.currentTarget && (e.key === "Enter" || e.key === " ")) {
          e.preventDefault();
          stopPropagation(e);
          onClick(task.id, e);
        }
      }}
    >
      <div className="flex h-10 min-w-0 shrink-0 items-center gap-2.5">
        <div className="relative flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-default-100">
          <ThirdPartyIcon size="md" thirdPartyId={task.thirdPartyId} />
          {Icon && (
            <span className="absolute -bottom-0.5 -right-0.5 rounded-full bg-content1 p-0.5 text-default-500">
              <Icon aria-hidden className="text-xs" />
            </span>
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex min-w-0 items-center gap-1.5">
            <div className="truncate text-sm font-semibold leading-5 text-foreground" title={name}>
              {name}
            </div>
            <TorrentIndicator
              formatDateTime={formatDateTime}
              task={task}
              onDownloadDirectly={onDownloadDirectly}
            />
          </div>
          <div className="truncate text-xs leading-4 text-default-400" title={task.key}>
            {task.name ? task.key : `#${task.id}`}
          </div>
        </div>
        <Chip className="h-6 shrink-0" color={statusColor} size="sm" variant="flat">
          {t<string>(DownloadTaskStatus[task.status])}
        </Chip>
      </div>
      <div className="flex h-8 min-w-0 shrink-0 items-center gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <div className="flex min-w-0 items-center gap-2 text-xs leading-4">
            {hasErrorMessage ? (
              <span
                data-task-action
                className="min-w-0 flex-1"
                role="presentation"
                onClick={stopPropagation}
                onContextMenu={stopPropagation}
                onKeyDown={stopPropagation}
              >
                <Button
                  aria-label={errorLabel}
                  className="flex h-4 min-h-0 w-full min-w-0 justify-start gap-1 rounded-sm px-0 text-xs"
                  color="danger"
                  size="sm"
                  title={`${errorLabel}: ${task.message}`}
                  variant="light"
                  onPress={() => onShowError(task)}
                >
                  <AiOutlineWarning aria-hidden className="shrink-0 text-sm" />
                  <span className="truncate">{task.message}</span>
                </Button>
              </span>
            ) : hasNotices ? (
              <span
                data-task-action
                className="min-w-0 flex-1"
                role="presentation"
                onClick={stopPropagation}
                onContextMenu={stopPropagation}
                onKeyDown={stopPropagation}
              >
                <Button
                  aria-label={noticesLabel}
                  className="flex h-4 min-h-0 w-full min-w-0 justify-start gap-1 rounded-sm px-0 text-xs"
                  color="warning"
                  size="sm"
                  title={`${noticesLabel}: ${noticeSummary}`}
                  variant="light"
                  onPress={() => onShowError(task)}
                >
                  <AiOutlineInfoCircle aria-hidden className="shrink-0 text-sm" />
                  <span className="truncate">{noticeSummary}</span>
                </Button>
              </span>
            ) : (
              <span className="min-w-0 flex-1 truncate text-default-500" title={task.current}>
                {task.current || t<string>("common.label.progress")}
              </span>
            )}
            {/* Keep the percentage last so it aligns across rows. Each optional value owns the
                separator before the next value, leaving no orphan when a value is unavailable. */}
            {downloadedSize && (
              <>
                <span
                  aria-label={`${t<string>("downloader.label.downloadedFileSize")}: ${downloadedSize}`}
                  className="shrink-0 tabular-nums text-default-500"
                  title={`${t<string>("downloader.label.downloadedFileSize")}: ${downloadedSize}`}
                >
                  {downloadedSize}
                </span>
                <span aria-hidden className="shrink-0 text-default-300">
                  ·
                </span>
              </>
            )}
            {downloadSpeed && (
              <>
                <span
                  aria-label={`${t<string>("downloader.label.downloadSpeed")}: ${downloadSpeed}`}
                  className="shrink-0 tabular-nums text-default-500"
                  title={`${t<string>("downloader.label.downloadSpeed")}: ${downloadSpeed}`}
                >
                  {downloadSpeed}
                </span>
                <span aria-hidden className="shrink-0 text-default-300">
                  ·
                </span>
              </>
            )}
            {estimatedRemaining && (
              <>
                <span className="shrink-0 tabular-nums text-default-500" title={estimatedRemaining}>
                  {estimatedRemaining}
                </span>
                <span aria-hidden className="shrink-0 text-default-300">
                  ·
                </span>
              </>
            )}
            <span className="shrink-0 tabular-nums text-default-500">{Math.round(progress)}%</span>
          </div>
          <Progress
            disableAnimation
            aria-label={t<string>("common.label.progress")}
            classNames={{ track: "h-1 bg-default-100" }}
            color={progressColor}
            size="sm"
            value={progress}
          />
        </div>
        <div
          data-task-action
          className="flex shrink-0 items-center gap-0.5"
          role="presentation"
          onClick={stopPropagation}
          onContextMenu={stopPropagation}
          onKeyDown={stopPropagation}
        >
          {task.availableActions?.map((action) => {
            switch (action) {
              case DownloadTaskAction.StartManually:
              case DownloadTaskAction.Restart: {
                const restarting = action === DownloadTaskAction.Restart;
                const label = t<string>(
                  restarting ? "downloader.action.restart" : "downloader.action.start",
                );

                return (
                  <Tooltip key={`start-${task.id}-${action}`} content={label}>
                    <Button
                      isIconOnly
                      aria-label={label}
                      color="primary"
                      size="sm"
                      variant="flat"
                      onPress={() => onStart(task.id)}
                    >
                      {restarting ? (
                        <AiOutlineRedo aria-hidden className="text-lg" />
                      ) : (
                        <AiOutlinePlayCircle aria-hidden className="text-lg" />
                      )}
                    </Button>
                  </Tooltip>
                );
              }
              case DownloadTaskAction.Disable:
                return (
                  <Tooltip
                    key={`stop-${task.id}-${action}`}
                    content={t<string>("downloader.action.stop")}
                  >
                    <Button
                      isIconOnly
                      aria-label={t<string>("downloader.action.stop")}
                      color="warning"
                      size="sm"
                      variant="flat"
                      onPress={() => onStop(task.id)}
                    >
                      <AiOutlineStop aria-hidden className="text-lg" />
                    </Button>
                  </Tooltip>
                );
              default:
                return null;
            }
          })}
          <Tooltip content={t<string>("downloader.action.edit")}>
            <Button
              isIconOnly
              aria-label={t<string>("downloader.action.edit")}
              size="sm"
              variant="light"
              onPress={() => onEdit(task.id)}
            >
              <AiOutlineEdit aria-hidden className="text-lg" />
            </Button>
          </Tooltip>
          <Tooltip content={t<string>("common.action.openFolder")}>
            <Button
              isIconOnly
              aria-label={t<string>("common.action.openFolder")}
              isDisabled={task.thirdPartyId !== ThirdPartyId.ExHentai && !task.downloadPath}
              size="sm"
              variant="light"
              onPress={() => onOpenFolder(task)}
            >
              <AiOutlineFolderOpen aria-hidden className="text-lg" />
            </Button>
          </Tooltip>
          <Dropdown>
            <DropdownTrigger>
              <Button
                isIconOnly
                aria-label={t<string>("common.action.more")}
                size="sm"
                variant="light"
              >
                <AiOutlineEllipsis aria-hidden className="text-lg" />
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              aria-label={t<string>("common.action.more")}
              onAction={(key) => {
                if (key === "delete") {
                  onDelete(task.id);
                }
              }}
            >
              <DropdownItem
                key="delete"
                color="danger"
                startContent={<AiOutlineDelete aria-hidden className="text-lg" />}
              >
                {t<string>("common.action.delete")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
        </div>
      </div>
      <div className="flex h-6 min-w-0 shrink-0 items-center gap-2">
        <span
          aria-label={[createdAt, nextStart].filter(Boolean).join(" · ")}
          className="min-w-0 flex-1 truncate text-xs text-default-400"
          title={[createdAt, nextStart].filter(Boolean).join(" · ")}
        >
          {nextStart || createdAt}
        </span>
      </div>
    </div>
  );
});

/**
 * What running the task has taught us about its torrent, if anything, as a small icon right after
 * the task name.
 *
 * The app already knew all of this — whether the task prefers torrents, and whether the last probe
 * found one — but only ever used it internally to order the queue, so from the list it was
 * impossible to tell a task that will download a small .torrent from one that is about to fetch a
 * few hundred images. These used to be text chips at the end of the row; most galleries have no
 * torrent, so a column of "No torrent" chips became the loudest thing in the list. An icon next to
 * the name keeps the fact visible where the eye already is, and the tooltip still carries the
 * detail. Every ExHentai task also offers its direct-image download action from this icon.
 */
const TorrentIndicator = ({
  task,
  formatDateTime,
  onDownloadDirectly,
}: {
  task: DownloadTask;
  formatDateTime: (value?: string | Date | null) => string;
  onDownloadDirectly: (id: number) => Promise<void>;
}) => {
  const { t } = useTranslation();
  const [pending, setPending] = useState(false);
  const [tooltipOpen, setTooltipOpen] = useState(false);
  const pendingRef = useRef(false);
  const metadata = task.metadata ?? {};

  if (task.thirdPartyId !== ThirdPartyId.ExHentai) {
    return null;
  }

  let indicator: { Icon: typeof TbMagnet; className: string; label: string; tip: string };

  if (metadata.torrentFoundAt) {
    indicator = {
      Icon: TbMagnet,
      className: "text-success",
      label: t<string>("downloader.label.torrentAvailable"),
      tip: t<string>("downloader.tip.torrentFoundAt", {
        time: formatDateTime(metadata.torrentFoundAt),
      }),
    };
  } else if (metadata.noTorrentCheckedAt) {
    indicator = {
      Icon: TbMagnetOff,
      className: "text-warning",
      label: t<string>("downloader.label.torrentUnavailable"),
      tip: t<string>("downloader.tip.noTorrentCheckedAt", {
        time: formatDateTime(metadata.noTorrentCheckedAt),
      }),
    };
  } else if (metadata.preferTorrent === false) {
    // A deliberate opt-out is muted; no availability verdict was learned from this choice.
    indicator = {
      Icon: TbMagnetOff,
      className: "text-default-400",
      label: t<string>("downloader.label.torrentDisabled"),
      tip: t<string>("downloader.tip.torrentDisabled"),
    };
  } else {
    indicator = {
      Icon: TbMagnet,
      className: "text-default-400",
      label: t<string>("downloader.label.torrentUnknown"),
      tip: t<string>("downloader.tip.torrentUnknown"),
    };
  }

  const { Icon, className, label, tip } = indicator;
  const downloadDirectly = async () => {
    if (pendingRef.current) return;

    pendingRef.current = true;
    setPending(true);
    try {
      await onDownloadDirectly(task.id);
    } catch (error) {
      const message =
        error instanceof Error
          ? error.message
          : error !== null &&
              typeof error === "object" &&
              "message" in error &&
              typeof error.message === "string"
            ? error.message
            : undefined;

      toast.danger(message || t<string>("downloader.toast.directDownloadFailed"));
    } finally {
      pendingRef.current = false;
      setPending(false);
    }
  };

  return (
    <span
      data-task-action
      className="inline-flex shrink-0"
      role="presentation"
      onClick={stopPropagation}
      onContextMenu={stopPropagation}
      onKeyDown={stopPropagation}
    >
      <Tooltip content={tip} isOpen={tooltipOpen} onOpenChange={setTooltipOpen}>
        <span className="inline-flex" tabIndex={-1}>
          <Dropdown isDisabled={pending}>
            <DropdownTrigger>
              <Button
                isIconOnly
                aria-description={tip}
                aria-label={label}
                className={`h-5 min-h-0 w-7 min-w-0 gap-0 rounded-sm p-0 ${className}`}
                isDisabled={pending}
                isLoading={pending}
                size="sm"
                variant="light"
                onBlur={() => setTooltipOpen(false)}
                onFocus={() => setTooltipOpen(true)}
              >
                <Icon aria-hidden className="text-base" />
                <AiOutlineDown aria-hidden className="text-[8px]" />
              </Button>
            </DropdownTrigger>
            <DropdownMenu
              aria-label={t<string>("downloader.action.torrentActions")}
              disabledKeys={pending ? ["direct-download"] : []}
              onAction={(key) => {
                if (key === "direct-download") void downloadDirectly();
              }}
            >
              <DropdownItem
                key="direct-download"
                description={t<string>("downloader.tip.directDownload")}
                startContent={<AiOutlineDownload aria-hidden className="text-lg" />}
              >
                {t<string>("downloader.action.directDownload")}
              </DropdownItem>
            </DropdownMenu>
          </Dropdown>
        </span>
      </Tooltip>
    </span>
  );
};

TaskRow.displayName = "TaskRow";

export default TaskRow;
