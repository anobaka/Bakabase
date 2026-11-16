import type { DownloadTask } from "@/core/models/DownloadTask";

import { useTranslation } from "react-i18next";

import { DownloadTaskStatus } from "@/sdk/constants";
import { estimatedRemainingLabel } from "@/utils/estimatedRemainingTime";

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
  return task.status === DownloadTaskStatus.Downloading
    ? estimatedRemainingLabel(task.estimatedRemainingSeconds, t)
    : undefined;
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
