"use client";

import type { AcquisitionRecipeVm, AcquisitionTaskVm } from "../..";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Chip, Progress, toast } from "@/components/bakaui";
import { getWorkflowActivityUI } from "@/components/Workflow/Activities";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import { AcquisitionStatus, AcquisitionStatusLabel } from "@/sdk/constants";

interface Props {
  task: AcquisitionTaskVm;
  recipes: AcquisitionRecipeVm[];
  onChanged: () => void;
  onEditRecipe: (definitionId: number) => void;
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
const AcquisitionRow: React.FC<Props> = ({ task, recipes, onChanged, onEditRecipe }) => {
  const { t } = useTranslation();
  const [submitting, setSubmitting] = React.useState(false);

  const recipe = recipes.find((r) => r.definitionId === task.recipeDefinitionId);
  const stepKind =
    task.currentStepIndex != null ? recipe?.stepKinds?.[task.currentStepIndex] : undefined;
  const ResumeForm = stepKind ? getWorkflowActivityUI(stepKind)?.ResumeForm : undefined;

  const act = async (fn: () => Promise<{ code?: number }>, successKey: string) => {
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
        <Chip color={StatusColor[task.status] ?? "default"} size="sm" variant="flat">
          {AcquisitionStatusLabel[task.status]}
        </Chip>
        <span className="font-medium truncate">
          {task.resourceName ?? t<string>("acquisition.unnamed", { id: task.resourceId })}
        </span>
        {task.status === AcquisitionStatus.Waiting && waitingFor(task.waitingSince) && (
          <span className="text-xs text-secondary-600">· {waitingFor(task.waitingSince)}</span>
        )}

        <div className="ml-auto flex items-center gap-1">
          {recipe && (
            <Button size="sm" variant="light" onPress={() => onEditRecipe(recipe.definitionId)}>
              {recipe.name}
            </Button>
          )}
          {isLive && (
            <Button
              isDisabled={submitting}
              size="sm"
              variant="light"
              onPress={() =>
                act(() => BApi.acquisition.cancelAcquisition(task.id), "acquisition.cancelled")
              }
            >
              {t<string>("acquisition.cancel")}
            </Button>
          )}
          {!isLive && (
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

      {recipe && task.currentStepIndex != null && (
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

      {task.error && <div className="text-xs text-danger break-words">{task.error}</div>}

      {task.targetDirectory && (
        <div className="text-xs text-default-400 truncate">{task.targetDirectory}</div>
      )}

      {task.status === AcquisitionStatus.Waiting && (
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
