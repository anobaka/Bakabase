import { useTranslation } from "react-i18next";
import type { TFunction } from "i18next";
import { Accordion, AccordionItem, Button } from "@/components/bakaui";
import PlanSummary from "@/components/ProcessingPlanRevisions/PlanSummary";
import type { PlanTaskDetail } from "./models";
import DetailExpansionControls from "@/components/Workflow/DetailExpansionControls";
import { useDetailAccordion, useDetailExpansion } from "@/hooks/useDetailExpansion";
import SourceRevisionEvidence from "./SourceRevisionEvidence";

/** Only known machine-generated reasons are translated; user explanations remain verbatim. */
export function planRevisionReason(reason: string, t: TFunction): string {
  return ["sourceSelection.preStartFallback", "sourceSelection.userRevision"].includes(reason)
    ? t(`acquisition.plan.history.reason.${reason}`)
    : reason;
}

export default function PlanRevisionHistory({
  history,
  onRun,
}: {
  history: PlanTaskDetail["planHistory"];
  onRun: (runId: number) => void;
}) {
  const { t } = useTranslation();
  const expansion = useDetailExpansion();
  const accordion = useDetailAccordion(["plan-history"], expansion.request);
  if (!history?.length) return null;
  return (
    <section className="space-y-1">
      <div className="flex justify-end">
        <DetailExpansionControls onChange={expansion.setAll} />
      </div>
      <Accordion isCompact className="px-0" {...accordion}>
        <AccordionItem
          key="plan-history"
          title={t("acquisition.plan.history.title", { count: history.length })}
        >
          <ol className="space-y-3">
            {[...history].reverse().map((entry) => (
              <li key={entry.revision} className="space-y-2 rounded-lg bg-default-50 p-3 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <span>{t("acquisition.plan.revision", { revision: entry.revision })}</span>
                  <time className="text-xs text-default-500" dateTime={entry.createdAt}>
                    {new Date(entry.createdAt).toLocaleString()}
                  </time>
                  {!!entry.previousRunId && (
                    <Button size="sm" variant="light" onPress={() => onRun(entry.previousRunId!)}>
                      {t("acquisition.plan.history.previousRun", { id: entry.previousRunId })}
                    </Button>
                  )}
                </div>
                {entry.reason && (
                  <p className="whitespace-pre-wrap break-words">
                    {planRevisionReason(entry.reason, t)}
                  </p>
                )}
                {entry.sourceRevision?.evidence && (
                  <div className="space-y-1 text-xs text-default-500">
                    <p>{t("acquisition.sourceRevision.evidence")}</p>
                    <SourceRevisionEvidence evidence={entry.sourceRevision.evidence} />
                  </div>
                )}
                <p className="text-xs text-default-500">
                  {t("acquisition.plan.history.snapshotHint")}
                </p>
                {entry.plan.adaptiveFileProcessing && (
                  <div className="space-y-1 text-xs text-default-500">
                    <p>
                      {t("acquisition.timeline.iterationLimit", {
                        count: entry.plan.adaptiveFileProcessingMaxIterations,
                      })}
                    </p>
                    {entry.plan.adaptiveFileProcessingContext && (
                      <p className="whitespace-pre-wrap break-words">
                        {t("acquisition.plan.continueCompleted.adaptiveContext")} ·{" "}
                        {entry.plan.adaptiveFileProcessingContext}
                      </p>
                    )}
                  </div>
                )}
                {entry.plan.deliveryLayout?.taskDirectory && (
                  <p className="break-all text-xs text-default-500">
                    {t("acquisition.timeline.stage.delivery")} ·{" "}
                    <code>{entry.plan.deliveryLayout.taskDirectory}</code>
                  </p>
                )}
                {entry.plan.parts.map((part) => (
                  <div key={part.id} className="space-y-1">
                    <h4 className="font-medium">{part.title || part.id}</h4>
                    <PlanSummary plan={part.processing} />
                  </div>
                ))}
                {!!entry.artifactEvidence?.length && (
                  <div className="space-y-1 text-xs text-default-500">
                    <p>{t("acquisition.plan.replan.evidence")}</p>
                    {entry.artifactEvidence.map((artifact) => (
                      <p className="break-all" key={`${artifact.partId}:${artifact.artifactId}`}>
                        {artifact.rootDirectory} ·{" "}
                        {t("acquisition.plan.replan.fileCount", {
                          count: artifact.manifest.entries.length,
                        })}
                      </p>
                    ))}
                  </div>
                )}
              </li>
            ))}
          </ol>
        </AccordionItem>
      </Accordion>
    </section>
  );
}
