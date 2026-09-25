"use client";

import type { SyntheticEvent } from "react";
import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { DownloadTask } from "@/core/models/DownloadTask";

import { memo } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineDelete,
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

import { DownloadTaskAction, DownloadTaskStatus } from "@/sdk/constants";
import {
  Button,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Progress,
  Tooltip,
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
  onStop: (id: number) => void;
  onEdit: (id: number) => void;
  onOpenFolder: (path: string) => void;
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
            <TorrentIndicator formatDateTime={formatDateTime} task={task} />
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
            {/* The estimate belongs to the progress readout, so it sits right beside the percentage
                rather than among the row's dates. The percentage stays last so it keeps the same
                column in every row whether or not an estimate is showing. */}
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
              isDisabled={!task.downloadPath}
              size="sm"
              variant="light"
              onPress={() => task.downloadPath && onOpenFolder(task.downloadPath)}
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
 * detail. Absent when there is nothing to say (a source without torrents, or a task that has never
 * run).
 */
const TorrentIndicator = ({
  task,
  formatDateTime,
}: {
  task: DownloadTask;
  formatDateTime: (value?: string | Date | null) => string;
}) => {
  const { t } = useTranslation();
  const metadata = task.metadata;

  if (!metadata) {
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
    // Only worth saying when it is a deliberate opt-out; "prefers torrents but has never been
    // probed" is the default and adds nothing to the row. Muted, unlike the probed "no torrent",
    // because nothing was found out — the user asked for images.
    indicator = {
      Icon: TbMagnetOff,
      className: "text-default-400",
      label: t<string>("downloader.label.torrentDisabled"),
      tip: t<string>("downloader.tip.torrentDisabled"),
    };
  } else {
    return null;
  }

  const { Icon, className, label, tip } = indicator;

  return (
    <Tooltip content={tip}>
      <span
        aria-label={label}
        className={`inline-flex shrink-0 items-center ${className}`}
        role="img"
      >
        <Icon aria-hidden className="text-base" />
      </span>
    </Tooltip>
  );
};

TaskRow.displayName = "TaskRow";

export default TaskRow;
