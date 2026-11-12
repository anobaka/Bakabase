import AcquisitionAttentionTags from "@/components/Acquire/AcquisitionAttentionTags";
("use client");

import type { AcquisitionRecipeVm, AcquisitionTaskVm } from "../..";

import React from "react";
import { DeleteOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { recipeLabel } from "../../recipeLabels";

import BApi from "@/sdk/BApi";
import { Button, Chip, Progress, toast } from "@/components/bakaui";
import { getWorkflowActivityUI } from "@/components/Workflow/Activities";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import { acquisitionStageLabel, acquisitionDownloadActivity } from "@/components/Acquire/taskStage";
import { AcquisitionStatus } from "@/sdk/constants";
import AcquisitionTiming from "@/components/Acquire/AcquisitionTiming";
import AcquisitionCreationOrigin from "@/components/Acquire/AcquisitionCreationOrigin";
import DownloadProblemMessage from "@/components/CloudDownload/DownloadProblemMessage";
import RuntimeOverviewSummary from "@/components/Acquire/PlanTask/RuntimeOverviewSummary";
import type { RuntimeOverview } from "@/components/Acquire/PlanTask/runtimeOverview";
import AcquisitionSizeEstimate, { type AcquisitionSizeEstimateValue } from "@/components/Acquire/AcquisitionSizeEstimate";

interface Props {
  task: AcquisitionTaskVm;
  recipes: AcquisitionRecipeVm[];
  onChanged: () => void;
  onEditRecipe: (definitionId: number) => void;
  inDetail?: boolean;
  onOpen?: () => void;
  onOriginNavigate?: () => void;
  overview?: RuntimeOverview;
  sizeEstimate?: AcquisitionSizeEstimateValue;
  onDelete?: () => void;
}

const StatusColor: Record<
  number,
  "default" | "primary" | "success" | "danger" | "warning" | "secondary"
> = {
  [AcquisitionStatus.Pending]: "default",
  [AcquisitionStatus.Running]: "primary",
  [AcquisitionStatus.Waiting]: "secondary",
  [AcquisitionStatus.Completed]: "success",
  [AcquisitionStatus.Failed]: "danger",
  [AcquisitionStatus.Cancelled]: "warning",
};

function waitingFor(since?: string | null): string | null {
  if (!since) return null;
  try {
    const ms = Date.now() - new Date(since).getTime();

    if (isNaN(ms) || ms < 0) return null;
    if (ms < 60_000) return `${Math.floor(ms / 1000)}s`;
    if (ms < 3_600_000) return `${Math.floor(ms / 60_000)}m`;
    if (ms < 86_400_000) return `${Math.floor(ms / 3_600_000)}h`;

    return `${Math.floor(ms / 86_400_000)}d`;
  } catch {
    return null;
  }
}

/**
 * One acquisition. A waiting one answers itself in place: the step that stopped ships the form for
 * its own question, and going somewhere else to answer it would be the difference between a page
 * worth leaving open and one worth ignoring.
 */
const AcquisitionRow: React.FC<Props> = ({
  inDetail,
  task,
  recipes,
  onChanged,
  onEditRecipe,
  onOpen,
  onOriginNavigate,
  overview,
  sizeEstimate,
  onDelete,
}) => {
  const { t } = useTranslation();
  const [submitting, setSubmitting] = React.useState(false);
  const readOnly = !!(task.isDeleted || task.deletedAt);

  const recipe = recipes.find((r) => r.definitionId === task.recipeDefinitionId);
  const stepKind =
    task.currentStepIndex != null ? recipe?.stepKinds?.[task.currentStepIndex] : undefined;
  const ResumeForm = stepKind ? getWorkflowActivityUI(stepKind)?.ResumeForm : undefined;
  const currentOverview = task.workflowRunId && overview?.taskId === task.id &&
    overview.workflowRunId === task.workflowRunId ? overview : undefined;
  const downloadActivity = acquisitionDownloadActivity(task, currentOverview);

  const act = async (fn: () => Promise<{ code?: number }>, successKey: string) => {
    if (readOnly || submitting) return;
    setSubmitting(true);
    try {
      const rsp = await fn();

      if (!rsp.code) {
        toast.success(t<string>(successKey));
        onChanged();
      }
    } finally {
      setSubmitting(false);
    }
  };

  const isLive =
    task.status === AcquisitionStatus.Waiting ||
    task.status === AcquisitionStatus.Running ||
    task.status === AcquisitionStatus.Pending;

  return (
    <div className="border border-default-200 rounded-lg p-3 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <Chip color={downloadActivity ? "primary" : StatusColor[task.status] ?? "default"} size="sm" variant="flat">
          {acquisitionStageLabel(t, task, currentOverview)}
        </Chip>
        {!inDetail && <span className="font-medium truncate">
          {task.resourceName ?? t<string>("acquisition.unnamed", { id: task.resourceId })}
        </span>}
        {!downloadActivity && task.status === AcquisitionStatus.Waiting && waitingFor(task.waitingSince) && (
          <span className="text-xs text-secondary-600">· {waitingFor(task.waitingSince)}</span>
        )}

        <div className="ml-auto flex items-center gap-1">
          {onDelete && <Button size="sm" variant="light" color="danger" startContent={<DeleteOutlined aria-hidden />}
            aria-label={`${t("acquisition.actionbar.delete")} #${task.id}`} onPress={onDelete}>{t("acquisition.actionbar.delete")}</Button>}
          {onOpen && (
            <Button size="sm" variant="flat" onPress={onOpen}>
              {t("acquisition.plan.open")}
            </Button>
          )}
          {recipe && !readOnly && (
            <Button size="sm" variant="light" onPress={() => onEditRecipe(recipe.definitionId)}>
              {recipeLabel(recipe, t)}
            </Button>
          )}
          {!isLive && !readOnly && (
            <Button
              isDisabled={submitting}
              size="sm"
              variant="light"
              onPress={() =>
                act(() => BApi.acquisition.retryAcquisition(task.id), "acquisition.retried")
              }
            >
              {t<string>("acquisition.retry")}
            </Button>
          )}
        </div>
      </div>

      <AcquisitionAttentionTags tags={currentOverview?.attentionTags ?? task.attentionTags} />
      <AcquisitionSizeEstimate estimate={sizeEstimate} />
      <AcquisitionCreationOrigin
        inspectPosts={inDetail}
        origins={task.creationOrigins}
        compact
        onNavigate={onOriginNavigate}
      />
      <AcquisitionTiming task={task} />
      {currentOverview && <RuntimeOverviewSummary overview={currentOverview} />}
      {!currentOverview && recipe && task.currentStepIndex != null && (
        <div className="flex items-center gap-2">
          <Progress
            aria-label="progress"
            className="max-w-[240px]"
            maxValue={Math.max(1, recipe.stepKinds?.length ?? 1)}
            size="sm"
            value={task.currentStepIndex}
          />
          <span className="text-xs text-default-500">
            {stepKind ? activityDisplayName(t, stepKind) : ""}
          </span>
        </div>
      )}

      {!currentOverview && (!downloadActivity || !!task.attentionTags?.length) && task.error && (
        <DownloadProblemMessage
          className="space-y-1 text-xs text-danger break-words"
          message={task.error}
        />
      )}

      {task.targetDirectory && (
        <div className="text-xs text-default-400 truncate">{task.targetDirectory}</div>
      )}

      {!readOnly && task.status === AcquisitionStatus.Waiting && (
        <div className="border-t border-default-100 pt-2">
          {ResumeForm ? (
            <ResumeForm
              promptJson={task.waitPromptJson ?? null}
              submitting={submitting}
              onSubmit={(signalJson) =>
                act(
                  () => BApi.acquisition.resumeAcquisition(task.id, { signalJson }),
                  "acquisition.resumed",
                )
              }
            />
          ) : (
            <Button
              color="primary"
              isDisabled={submitting}
              size="sm"
              onPress={() =>
                act(
                  () => BApi.acquisition.resumeAcquisition(task.id, { signalJson: "{}" }),
                  "acquisition.resumed",
                )
              }
            >
              {t<string>("acquisition.continue")}
            </Button>
          )}
        </div>
      )}
    </div>
  );
};

export default AcquisitionRow;
