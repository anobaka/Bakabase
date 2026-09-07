"use client";

import type { ChipProps, CircularProgressProps } from "@/components/bakaui";
import type { DownloadTask } from "@/core/models/DownloadTask";

import { memo } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineDelete,
  AiOutlineEdit,
  AiOutlineEllipsis,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlineRedo,
  AiOutlineStop,
  AiOutlineWarning,
} from "react-icons/ai";
import { CircularProgress } from "@heroui/react";

import { DownloadTaskTypeIconMap } from "./TaskDetailModal/models";

import { DownloadTaskAction, DownloadTaskStatus } from "@/sdk/constants";
import {
  Button,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
} from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";

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
  const hasErrorMessage = task.status == DownloadTaskStatus.Failed && task.message;
  const Icon = DownloadTaskTypeIconMap[task.thirdPartyId!]?.[task.type];

  return (
    <div
      className={"flex flex-col gap-1"}
      role="button"
      tabIndex={0}
      onClick={(e) => onClick(task.id, e)}
      onContextMenu={(e) => onContextMenu(task.id, e)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onClick(task.id, e);
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
                {t<string>("downloader.label.nextStartTime")}:{formatDateTime(task.nextStartDt)}
              </Chip>
            )}
            <Chip color="default" size="sm">
              {t("downloader.label.createdAt")}
              &nbsp;
              {formatDateTime(task.createdAt)}
            </Chip>
            <TorrentChip formatDateTime={formatDateTime} task={task} />
          </div>
        </div>
        <div className={"flex items-center"}>
          <div className={"mr-8 flex items-center gap-2"}>
            <Chip color={statusColor} variant={"light"}>
              {t<string>(DownloadTaskStatus[task.status])}
            </Chip>
            {task.current && <span className="text-xs text-default-400">{task.current}</span>}
            {task.status == DownloadTaskStatus.Failed && (
              <Button
                isIconOnly
                color={"danger"}
                variant={"light"}
                onPress={() => {
                  if (hasErrorMessage) {
                    onShowError(task);
                  }
                }}
              >
                <AiOutlineWarning className={"text-base"} />
                {task.failureTimes}
              </Button>
            )}
            <CircularProgress
              disableAnimation
              showValueLabel
              color={progressColor}
              size={"lg"}
              value={task.progress}
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
                    onPress={() => onStart(task.id)}
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
                    onPress={() => onStop(task.id)}
                  >
                    <AiOutlineStop className={"text-lg"} />
                  </Button>
                );
            }

            return;
          })}
          <Button isIconOnly size={"sm"} variant={"light"} onPress={() => onEdit(task.id)}>
            <AiOutlineEdit className={"text-lg"} />
          </Button>
          <Button
            isIconOnly
            size={"sm"}
            variant={"light"}
            onPress={() => onOpenFolder(task.downloadPath!)}
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
                if ((key as string) === "delete") {
                  onDelete(task.id);
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
    </div>
  );
});

/**
 * What running the task has taught us about its torrent, if anything.
 *
 * The app already knew all of this — whether the task prefers torrents, and whether the last probe
 * found one — but only ever used it internally to order the queue, so from the list it was
 * impossible to tell a task that will download a small .torrent from one that is about to fetch a
 * few hundred images. Absent when there is nothing to say (a source without torrents, or a task
 * that has never run).
 */
const TorrentChip = ({
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

  if (metadata.torrentFoundAt) {
    return (
      <Tooltip
        content={t<string>("downloader.tip.torrentFoundAt", {
          time: formatDateTime(metadata.torrentFoundAt),
        })}
      >
        <Chip color="success" size="sm" variant="flat">
          {t<string>("downloader.label.torrentAvailable")}
        </Chip>
      </Tooltip>
    );
  }

  if (metadata.noTorrentCheckedAt) {
    return (
      <Tooltip
        content={t<string>("downloader.tip.noTorrentCheckedAt", {
          time: formatDateTime(metadata.noTorrentCheckedAt),
        })}
      >
        <Chip color="warning" size="sm" variant="flat">
          {t<string>("downloader.label.torrentUnavailable")}
        </Chip>
      </Tooltip>
    );
  }

  // Only worth saying when it is a deliberate opt-out; "prefers torrents but has never been probed"
  // is the default and adds nothing to the row.
  if (metadata.preferTorrent === false) {
    return (
      <Tooltip content={t<string>("downloader.tip.torrentDisabled")}>
        <Chip color="default" size="sm" variant="flat">
          {t<string>("downloader.label.torrentDisabled")}
        </Chip>
      </Tooltip>
    );
  }

  return null;
};

TaskRow.displayName = "TaskRow";

export default TaskRow;
