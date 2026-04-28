import { useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlinePlayCircle, AiOutlineStop, AiOutlineWarning } from "react-icons/ai";

import { Button, Progress, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { BTaskStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";

const SCORING_TASK_ID = "HealthScoring";

/**
 * Reflects the lifecycle of the on-demand HealthScoring BTask:
 *
 *  - no task / Completed → idle button ("Score now"); click triggers a fresh run
 *  - Running             → progress bar; hover reveals a stop button
 *  - Error               → danger variant + tooltip with the error; click retries
 *  - NotStarted          → queued (e.g. waiting on SearchIndex)
 *
 * The backend models this as a single fixed-id task; <c>BTaskManager.Start</c>
 * will reuse the slot on retry.
 */
export const ScoreNowButton = () => {
  const { t } = useTranslation();
  const task = useBTasksStore((s) => s.tasks.find((x) => x.id === SCORING_TASK_ID));
  const [hover, setHover] = useState(false);

  const trigger = async () => {
    try {
      await BApi.healthScore.runHealthScoringNow();
      toast.success(t<string>("healthScore.toast.scoringTriggered"));
    } catch (e: any) {
      toast.error(String(e?.message ?? e));
    }
  };

  const stop = async () => {
    try {
      await BApi.backgroundTask.stopBackgroundTask(SCORING_TASK_ID, { confirm: true });
    } catch (e: any) {
      toast.error(String(e?.message ?? e));
    }
  };

  // Running: show progress + process. Hover swaps in a stop button.
  if (task?.status === BTaskStatus.Running) {
    const pct = task.percentage ?? 0;
    return (
      <div
        className="min-w-[180px]"
        onMouseEnter={() => setHover(true)}
        onMouseLeave={() => setHover(false)}
      >
        {hover ? (
          <Button
            color="danger"
            size="sm"
            startContent={<AiOutlineStop className="text-lg" />}
            variant="flat"
            onPress={stop}
          >
            {t<string>("healthScore.action.stop")}
          </Button>
        ) : (
          <Tooltip content={task.process ?? ""}>
            <Progress
              aria-label={t<string>("healthScore.action.runNow")}
              color="primary"
              label={task.process ?? `${pct}%`}
              size="sm"
              value={pct}
            />
          </Tooltip>
        )}
      </div>
    );
  }

  // Error: surface message, allow retry
  if (task?.status === BTaskStatus.Error) {
    return (
      <Tooltip content={task.briefError ?? task.error ?? "Scoring failed"}>
        <Button
          color="danger"
          startContent={<AiOutlineWarning className="text-lg" />}
          variant="flat"
          onPress={trigger}
        >
          {t<string>("healthScore.action.retry")}
        </Button>
      </Tooltip>
    );
  }

  // NotStarted (e.g., queued behind SearchIndex)
  if (task?.status === BTaskStatus.NotStarted) {
    return (
      <Button isDisabled isLoading variant="flat">
        {t<string>("healthScore.action.queued")}
      </Button>
    );
  }

  // Idle (no task, or Completed/Cancelled)
  return (
    <Button startContent={<AiOutlinePlayCircle className="text-lg" />} variant="flat" onPress={trigger}>
      {t<string>("healthScore.action.runNow")}
    </Button>
  );
};
