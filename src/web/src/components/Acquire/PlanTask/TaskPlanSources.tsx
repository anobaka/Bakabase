import PostImageSourceAction, { type ReviewedImageSource } from "./PostImageSourceAction";
import { useTranslation } from "react-i18next";
import { Button, Chip } from "@/components/bakaui";
import SourceProcessingPlanPanel from "@/components/ProcessingPlanRevisions/SourceProcessingPlanPanel";
import type { SourceAvailabilityRow } from "@/components/SourceAvailability/models";
import AcquisitionSourceDetails from "../AcquisitionSourceDetails";
import type { PlanTaskDetail } from "./models";
import type { SourceCorrections } from "./useSourceCorrections";

type Part = NonNullable<PlanTaskDetail["plan"]>["parts"][number];

export default function TaskPlanSources({
  detail,
  corrections,
  sourceHealth,
  onReview,
  onApply,
  onReplace,
  busy = false,
  sourcesOnly = false,
}: {
  detail: PlanTaskDetail;
  corrections?: SourceCorrections;
  sourceHealth: SourceAvailabilityRow[];
  onReview?: () => void;
  onReplace?: (partId: string, sourceId: string, source: ReviewedImageSource) => Promise<void>;
  onApply?: (
    partId: string,
    source: NonNullable<Part["processingSource"]>,
    revision: string,
  ) => Promise<void>;
  busy?: boolean;
  sourcesOnly?: boolean;
}) {
  const { t } = useTranslation();
  const { task, plan } = detail;
  if (!plan?.parts.length) return null;
  return (
    <section
      aria-label={t("acquisition.plan.parts")}
      className="space-y-2 rounded-lg border border-default-200 p-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("acquisition.plan.parts")}</h3>
        {onReview && (
          <Button size="sm" variant="light" onPress={onReview}>
            {t("sourceAvailability.review.one")}
          </Button>
        )}
      </div>
      <p className="text-xs text-default-500">{t("acquisition.plan.partsHint")}</p>
      {plan.parts.map((part, index) => (
        <div key={part.id} className="rounded-lg bg-default-50 p-2 text-sm">
          <p>{part.title || t("acquisition.plan.part", { index: index + 1 })}</p>
          <div className="mt-2 space-y-2">
            {part.sources.map((source) => {
              const correction =
                corrections &&
                corrections.expectedRunId === task?.workflowRunId &&
                corrections?.expectedRevision === task?.planRevision
                  ? corrections.sources.find(
                      (row) => row.targetId === part.id && row.sourceId === source.id,
                    )
                  : undefined;
              const executionSource = correction?.currentSource;
              const correctionReason =
                correction && !correction.canCorrect
                  ? (correction.reasonCode ?? corrections?.reasonCode)
                  : undefined;
              return (
                <div key={source.id} className="space-y-2 rounded-lg border border-default-200 p-2">
                  <Chip
                    size="sm"
                    variant="flat"
                    color={source.id === part.selectedSourceId ? "primary" : "default"}
                  >
                    {(() => {
                      try {
                        return new URL(source.url).hostname;
                      } catch {
                        return source.providerId || source.kind;
                      }
                    })()}
                    {source.id === part.selectedSourceId
                      ? ` · ${t("acquisition.plan.selected")}`
                      : ""}
                  </Chip>
                  {task?.workflowRunId && (
                    <p className="text-xs text-default-500">
                      {t(
                        executionSource
                          ? "acquisition.sourceCorrection.currentSource"
                          : "acquisition.sourceCorrection.originalSource",
                      )}
                    </p>
                  )}
                  <AcquisitionSourceDetails
                    source={executionSource ?? source}
                    health={
                      sourceHealth.find(
                        (row) => row.partId === part.id && row.sourceId === source.id,
                      )?.health
                    }
                    disabled={
                      sourceHealth.find(
                        (row) => row.partId === part.id && row.sourceId === source.id,
                      )?.disabled
                    }
                  />
                  {onReplace && source.id === part.selectedSourceId && (
                    <PostImageSourceAction key={task.planRevision} origins={task.creationOrigins}
                      imageUrl={source.url} initialAccessCode={source.accessCode} disabled={busy} onUse={reviewed => onReplace(part.id, source.id, reviewed)} />
                  )}
                  {correctionReason && (
                    <p className="text-xs text-default-500">
                      {t(
                        `acquisition.sourceCorrection.reason.${correctionReason.replace(/^SOURCE_CORRECTION_/, "")}`,
                        { defaultValue: t("acquisition.sourceCorrection.reason.unknown") },
                      )}
                    </p>
                  )}
                </div>
              );
            })}
          </div>
          {!sourcesOnly && part.processingSource && (
            <div className="mt-2">
              <SourceProcessingPlanPanel
                source={part.processingSource}
                currentPlan={part.processing ?? null}
                appliedRevisionId={part.appliedProcessingRevisionId}
                frozen={!!task.workflowRunId || !!task.isDeleted || !!task.deletedAt}
                isDisabled={!onApply || busy}
                onApply={
                  onApply && ((revision) => onApply(part.id, part.processingSource!, revision))
                }
              />
            </div>
          )}
        </div>
      ))}
    </section>
  );
}
