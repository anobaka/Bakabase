import AcquisitionAttentionTags from "@/components/Acquire/AcquisitionAttentionTags";
import { useTranslation } from "react-i18next";
import { Button, Chip } from "@/components/bakaui";
import { DeleteOutlined } from "@ant-design/icons";
import type { PlanTask } from "./models";
import { AcquisitionStatus } from "@/sdk/constants";
import AcquisitionTiming from "../AcquisitionTiming";
import AcquisitionCreationOrigin from "../AcquisitionCreationOrigin";
import { acquisitionTaskStage, acquisitionStageLabel, acquisitionDownloadActivity } from "../taskStage";
import RuntimeOverviewSummary from "./RuntimeOverviewSummary";
import type { RuntimeOverview } from "./runtimeOverview";
import AcquisitionSizeEstimate, { type AcquisitionSizeEstimateValue } from "../AcquisitionSizeEstimate";

export default function PlanTaskRow({
  task,
  onOpen,
  overview,
  sizeEstimate,
  facts,
  onDelete,
}: {
  task: PlanTask;
  onOpen: () => void;
  overview?: RuntimeOverview;
  sizeEstimate?: AcquisitionSizeEstimateValue;
  facts?: string[];
  onDelete?: () => void;
}) {
  const { t } = useTranslation();
  const currentOverview = task.workflowRunId && overview?.taskId === task.id &&
    overview.workflowRunId === task.workflowRunId ? overview : undefined;
  const stage = acquisitionTaskStage(task, currentOverview);
  const downloadActivity = acquisitionDownloadActivity(task, currentOverview);
  return (
    <article className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-default-200 p-3">
      <div className="min-w-0 flex-1 space-y-2">
        <div className="flex flex-wrap items-center gap-2">
          <Chip
            size="sm"
            variant="flat"
            color={
              stage === "completed"
                ? "success"
                : task.status === AcquisitionStatus.Failed
                  ? "danger"
                  : stage === "attention"
                    ? "secondary"
                    : stage === "running" ? "primary" : "default"
            }
          >
            {acquisitionStageLabel(t, task, currentOverview)}
          </Chip>
          <span className="break-words font-medium">
            {task.title || task.resourceName || t("acquisition.tasks.untitled")}
          </span>
          <span className="text-xs text-default-400">#{task.id}</span>
          <AcquisitionSizeEstimate estimate={sizeEstimate} />
        </div>
        <p className="text-sm text-default-500">
          {downloadActivity
            ? t(`acquisition.downloadActivity.${downloadActivity}Hint`)
            : task.builderId === "draft" && stage === "preparing"
            ? t("acquisition.draft.incomplete")
            : t(`acquisition.plan.phase.${task.phase}`, {
                defaultValue: t("acquisition.plan.phase.preparing"),
              })}{" "}
          · {t("acquisition.plan.revision", { revision: task.planRevision })}
        </p>
        <AcquisitionAttentionTags tags={currentOverview?.attentionTags ?? task.attentionTags} facts={facts} />
        <AcquisitionCreationOrigin origins={task.creationOrigins} compact />
        <RuntimeOverviewSummary overview={currentOverview} />
        <AcquisitionTiming task={task} />
      </div>
      <div className="flex flex-wrap items-center gap-2"><Button
        size="sm"
        color={stage === "attention" ? "secondary" : "default"}
        variant="flat"
        onPress={onOpen}
      >
        {t(
          stage === "preparing"
            ? "acquisition.draft.complete"
            : stage === "attention"
              ? "acquisition.plan.resolve"
              : "acquisition.plan.open",
        )}
      </Button>
      {onDelete && <Button size="sm" variant="light" color="danger" startContent={<DeleteOutlined aria-hidden />}
        aria-label={`${t("acquisition.actionbar.delete")} #${task.id}`} onPress={onDelete}>{t("acquisition.actionbar.delete")}</Button>}</div>
    </article>
  );
}
