import { memo } from "react";
import { useTranslation } from "react-i18next";
import {
  FiDownload,
  FiExternalLink,
  FiFileText,
  FiFolder,
  FiPlay,
  FiRefreshCw,
  FiSquare,
  FiTrash2,
} from "react-icons/fi";
import { Button, Chip, Progress, Tooltip } from "@/components/bakaui";
import { formatBtBytes } from "@/pages/bt-downloads/format";
import { centerStatusColor, type CenterTask, type TaskAction } from "../downloadCenter";
import { DOWNLOAD_TASK_ROW_HEIGHT } from "./TaskRow";

type Props = {
  task: CenterTask;
  busy?: boolean;
  onSelect: (id: string, event: React.MouseEvent | React.KeyboardEvent) => void;
  onAction: (id: string, action: TaskAction) => void;
  onDetail?: (id: string) => void;
  onSource: (id: number) => void;
  onFolder: (id: string) => void;
};
const GeneralTaskRow = memo(function GeneralTaskRow({
  task,
  busy,
  onSelect,
  onAction,
  onDetail,
  onSource,
  onFolder,
}: Props) {
  const { t } = useTranslation();
  const color = centerStatusColor(task.status);
  const icons = { start: FiPlay, stop: FiSquare, retry: FiRefreshCw, delete: FiTrash2 };
  return (
    <div
      role="button"
      aria-label={task.name || task.id}
      tabIndex={0}
      className="flex w-full min-w-0 flex-col justify-between gap-1 py-1 text-left outline-none focus-visible:ring-2 focus-visible:ring-primary"
      style={{ height: DOWNLOAD_TASK_ROW_HEIGHT }}
      onClick={(event) => {
        if (!(event.target as HTMLElement).closest("[data-task-action]")) onSelect(task.id, event);
      }}
      onKeyDown={(event) => {
        if (event.target === event.currentTarget && ["Enter", " "].includes(event.key)) {
          event.preventDefault();
          event.stopPropagation();
          onSelect(task.id, event);
        }
      }}
    >
      <div className="flex h-10 min-w-0 shrink-0 items-center gap-2.5">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-default-100">
          <FiDownload className="text-lg text-primary" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-semibold" title={task.name || task.id}>
            {task.name || task.id}
          </div>
          <div className="truncate text-xs text-default-400">
            {t(`downloadCenter.provider.${task.provider}`)} · {task.outputDirectory}
          </div>
        </div>
        <Chip size="sm" variant="flat" color={color}>
          {t(`downloadCenter.status.${task.status}`)}
        </Chip>
      </div>
      <div className="flex h-8 min-w-0 shrink-0 items-center gap-3">
        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
          <div className="flex min-w-0 items-center gap-2 text-xs">
            <span
              className={`min-w-0 flex-1 truncate ${task.error ? "text-danger" : "text-default-500"}`}
              title={task.error || undefined}
            >
              {task.error ||
                (task.stage &&
                  t(`downloadCenter.stage.${task.stage}`, {
                    defaultValue: t(`bt.stage.${task.stage}`, { defaultValue: task.stage }),
                  }))}
            </span>
            <span className="shrink-0 tabular-nums text-default-500">
              {formatBtBytes(task.completedBytes)} / {formatBtBytes(task.totalBytes)}
              {task.speedBytesPerSecond
                ? ` · ${formatBtBytes(task.speedBytesPerSecond)}/s`
                : ""} · {task.progress == null ? "—" : `${Math.round(task.progress)}%`}
            </span>
          </div>
          <Progress
            disableAnimation
            aria-label={t("common.label.progress")}
            classNames={{ track: "h-1 bg-default-100" }}
            color={color}
            size="sm"
            value={task.progress ?? 0}
            isIndeterminate={task.progress == null && task.status === "running"}
          />
        </div>
        <div
          data-task-action
          className="flex shrink-0 gap-0.5"
          onClick={(event) => event.stopPropagation()}
          onKeyDown={(event) => event.stopPropagation()}
        >
          {(task.availableActions ?? [])
            .filter((action) => action in icons)
            .map((action) => {
              const Icon = icons[action as TaskAction];
              return (
                <Tooltip key={action} content={t(`downloadCenter.action.${action}`)}>
                  <Button
                    isIconOnly
                    aria-label={t(`downloadCenter.action.${action}`)}
                    isDisabled={busy}
                    size="sm"
                    variant={action === "delete" ? "light" : "flat"}
                    color={
                      action === "stop" ? "warning" : action === "delete" ? "default" : "primary"
                    }
                    onPress={() => onAction(task.id, action as TaskAction)}
                  >
                    <Icon />
                  </Button>
                </Tooltip>
              );
            })}
          {onDetail && (
            <Tooltip content={t("downloadCenter.action.detail")}>
              <Button
                isIconOnly
                aria-label={t("downloadCenter.action.detail")}
                size="sm"
                variant="light"
                onPress={() => onDetail?.(task.id)}
              >
                <FiFileText />
              </Button>
            </Tooltip>
          )}
          <Tooltip content={t("common.action.openFolder")}>
            <Button
              isIconOnly
              aria-label={t("common.action.openFolder")}
              size="sm"
              variant="light"
              isDisabled={!task.outputDirectory}
              onPress={() => onFolder(task.id)}
            >
              <FiFolder />
            </Button>
          </Tooltip>
        </div>
      </div>
      <div className="flex h-6 min-w-0 items-center justify-between gap-2 text-xs text-default-400">
        <span className="truncate">
          {task.createdAt && new Date(task.createdAt).toLocaleString()}
        </span>
        {task.siteTaskId != null && (
          <Button
            data-task-action
            className="h-6 min-h-0 px-1 text-xs"
            size="sm"
            variant="light"
            onPress={() => onSource(task.siteTaskId!)}
          >
            <FiExternalLink />
            {t("bt.action.source")}
          </Button>
        )}
      </div>
    </div>
  );
});
export default GeneralTaskRow;
