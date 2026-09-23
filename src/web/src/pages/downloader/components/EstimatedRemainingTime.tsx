import type { DownloadTask } from "@/core/models/DownloadTask";

import { useTranslation } from "react-i18next";

import { DownloadTaskStatus } from "@/sdk/constants";

type Props = {
  task: Pick<DownloadTask, "status" | "estimatedRemainingSeconds">;
};

/**
 * The localized "estimated remaining …" text, or undefined when there is nothing trustworthy to
 * show. Exposed as a hook so the task row can place it inline with the progress readout (and
 * decide on its own separators) instead of rendering a standalone element.
 */
export const useEstimatedRemainingLabel = (task: Props["task"]): string | undefined => {
  const { t } = useTranslation();
  const seconds = task.estimatedRemainingSeconds;

  if (
    task.status !== DownloadTaskStatus.Downloading ||
    seconds == null ||
    !Number.isFinite(seconds) ||
    seconds < 0
  ) {
    return undefined;
  }

  let remaining = Math.ceil(seconds);
  const units = [
    [86400, t<string>("datetime.duration.day")],
    [3600, t<string>("datetime.duration.hour")],
    [60, t<string>("datetime.duration.minute")],
    [1, t<string>("datetime.duration.second")],
  ] as const;
  const parts = units.flatMap(([size, label]) => {
    const value = Math.floor(remaining / size);

    remaining %= size;

    return value > 0 ? [`${value}${label}`] : [];
  });
  const duration = parts.join(" ") || `0${t<string>("datetime.duration.second")}`;

  return `${t<string>("downloader.label.estimatedRemaining")} ${duration}`;
};

const EstimatedRemainingTime = ({ task }: Props) => {
  const label = useEstimatedRemainingLabel(task);

  if (!label) {
    return null;
  }

  return (
    <span className="shrink-0 text-xs tabular-nums text-default-500" title={label}>
      {label}
    </span>
  );
};

export default EstimatedRemainingTime;
