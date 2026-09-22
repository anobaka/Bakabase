import type { DownloadTask } from "@/core/models/DownloadTask";

import { useTranslation } from "react-i18next";

import { DownloadTaskStatus } from "@/sdk/constants";

type Props = {
  task: Pick<DownloadTask, "status" | "estimatedRemainingSeconds">;
};

const EstimatedRemainingTime = ({ task }: Props) => {
  const { t } = useTranslation();
  const seconds = task.estimatedRemainingSeconds;

  if (
    task.status !== DownloadTaskStatus.Downloading ||
    seconds == null ||
    !Number.isFinite(seconds) ||
    seconds < 0
  ) {
    return null;
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
  const label = `${t<string>("downloader.label.estimatedRemaining")} ${duration}`;

  return (
    <span className="shrink-0 text-xs tabular-nums text-default-500" title={label}>
      {label}
    </span>
  );
};

export default EstimatedRemainingTime;
