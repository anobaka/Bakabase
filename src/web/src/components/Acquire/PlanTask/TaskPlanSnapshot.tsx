import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Chip, Input } from "@/components/bakaui";
import { activityDisplayName } from "@/components/Workflow/displayNames";
import { processingStepInputs } from "@/components/Workflow/processingStepInputs";
import BApi from "@/sdk/BApi";
import type { PlanTaskDetail } from "./models";
import PlanRevisionHistory from "./PlanRevisionHistory";
import TaskStoragePolicySettings from "./TaskStoragePolicySettings";
import TaskDeliverySettings from "./TaskDeliverySettings";

export type AcquisitionOptions = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.getAcquisitionOptions>>["data"]
>;

export default function TaskPlanSnapshot({
  detail,
  acquisitionOptions,
  onRun,
  children,
}: {
  detail: PlanTaskDetail;
  acquisitionOptions?: AcquisitionOptions;
  onRun: (id: number) => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  return (
    <div className="space-y-4">
      {children}
      {(acquisitionOptions?.downloadRootDirectory || acquisitionOptions?.processingWorkspaceDirectory || acquisitionOptions?.deliveryRootDirectory) && (
        <section
          aria-label={t("acquisition.plan.storageLocations.title")}
          className="space-y-2 rounded-lg border border-default-200 p-3"
        >
          <h3 className="text-sm font-medium">{t("acquisition.plan.storageLocations.title")}</h3>
          <dl className="grid grid-cols-1 gap-1 text-xs sm:grid-cols-[auto_1fr] sm:gap-x-3">
            {acquisitionOptions?.downloadRootDirectory && (
              <>
                <dt className="text-default-500">
                  {t("acquisition.plan.storageLocations.currentDownload")}
                </dt>
                <dd className="break-all font-mono">{acquisitionOptions.downloadRootDirectory}</dd>
              </>
            )}
            {acquisitionOptions?.processingWorkspaceDirectory && (
              <>
                <dt className="text-default-500">
                  {t("acquisition.plan.storageLocations.currentWorkspace")}
                </dt>
                <dd className="break-all font-mono">
                  {acquisitionOptions.processingWorkspaceDirectory}
                </dd>
              </>
            )}
            {acquisitionOptions?.deliveryRootDirectory && (
              <>
                <dt className="text-default-500">
                  {t("acquisition.plan.storageLocations.currentDelivery")}
                </dt>
                <dd className="break-all font-mono">{acquisitionOptions.deliveryRootDirectory}</dd>
              </>
            )}
          </dl>
          <p className="text-xs text-default-500">{t("acquisition.plan.storageLocations.hint")}</p>
        </section>
      )}
      <PlanRevisionHistory history={detail.planHistory} onRun={onRun} />
      {(detail.legacyTitle || detail.importedListRow) && (
        <Accordion isCompact className="px-0">
          <AccordionItem key="original-input" title={t("acquisition.draft.originalInput")}>
            <div className="space-y-2 text-sm">
              {(detail.legacyTitle || detail.importedListRow?.title) && (
                <p className="break-words">
                  {t("acquisition.draft.originalTitle")}:{" "}
                  {detail.legacyTitle || detail.importedListRow?.title}
                </p>
              )}
              {detail.importedListRow?.url && (
                <p className="break-all">{detail.importedListRow.url}</p>
              )}
              {detail.importedListRow?.password && (
                <Input
                  isReadOnly
                  type="password"
                  label={t("acquisition.sharedList.column.password")}
                  value={detail.importedListRow.password}
                />
              )}
            </div>
          </AccordionItem>
        </Accordion>
      )}
      {!!detail.legacyLeads?.length && (
        <Accordion isCompact className="px-0">
          <AccordionItem
            key="legacy-clues"
            title={t("acquisition.draft.legacyClues", {
              count: detail.legacyLeads.length,
            })}
          >
            <p className="mb-2 text-xs text-default-500">{t("acquisition.draft.snapshotHint")}</p>
            <ul className="space-y-2 text-sm">
              {detail.legacyLeads.map((lead) => (
                <li key={lead.id} className="break-all rounded-lg bg-default-50 p-2">
                  {lead.value}
                </li>
              ))}
            </ul>
          </AccordionItem>
        </Accordion>
      )}
      {!!detail.preparationAttempts?.length && (
        <Accordion isCompact className="px-0">
          <AccordionItem
            key="preparation"
            title={t("acquisition.plan.preparationHistory", {
              count: detail.preparationAttempts.length,
            })}
          >
            <ol className="space-y-2">
              {[...detail.preparationAttempts].reverse().map((attempt) => (
                <li
                  key={attempt.attempt}
                  className="space-y-1 rounded-lg bg-default-50 p-3 text-sm"
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <span>#{attempt.attempt}</span>
                    <Chip size="sm" variant="flat">
                      {t(`acquisition.plan.preparationStatus.${attempt.status}`)}
                    </Chip>
                    <span className="text-xs text-default-500">
                      {new Date(attempt.startedAt).toLocaleString()}
                    </span>
                  </div>
                  <p className="break-words">{attempt.query}</p>
                  <p className="text-xs text-default-500">
                    {t("acquisition.plan.candidateCount", {
                      count: attempt.candidateCount,
                    })}
                  </p>
                  {attempt.code && (
                    <p className="text-xs text-warning-600">
                      {t(`acquisition.plan.issue.${attempt.code}`, {
                        defaultValue: t("acquisition.plan.issue.unknown"),
                      })}
                    </p>
                  )}
                </li>
              ))}
            </ol>
          </AccordionItem>
        </Accordion>
      )}

      <TaskStoragePolicySettings detail={detail} />
      <TaskDeliverySettings detail={detail} />
      <section className="space-y-2 rounded-lg border border-default-200 p-3">
        <h3 className="text-sm font-medium">{t("acquisition.plan.steps")}</h3>
        <p className="text-xs text-default-500">{t("acquisition.plan.stepsHint")}</p>
        {detail.steps.length ? (
          <ol className="space-y-2">
            {detail.steps.map((step, index) => (
              <li key={step.id} className="flex items-start gap-2 text-sm">
                <span className="text-default-400">{index + 1}.</span>
                <span>
                  {step.labelKey
                    ? t(step.labelKey, {
                        defaultValue: activityDisplayName(t, step.kind),
                      })
                    : activityDisplayName(t, step.kind)}
                  {(step.operation ||
                    step.input ||
                    step.selector ||
                    step.targetName ||
                    step.targetDirectory ||
                    step.sourceDirectory ||
                    step.extension ||
                    step.findText ||
                    step.replaceText != null) && (
                    <dl className="mt-1 space-y-1 text-xs text-default-500">
                      {(
                        [
                          "input",
                          "selector",
                          "targetName",
                          "targetDirectory",
                          "sourceDirectory",
                          "extension",
                          "findText",
                          "replaceText",
                        ] as const
                      ).map((field) =>
                        (field === "input" && step.operation) ||
                        step[field] ||
                        (field === "replaceText" && step[field] === "") ||
                        (field === "targetDirectory" &&
                          step.operation === "rebaseDirectory" &&
                          step[field] === "") ? (
                          <div key={field} className="flex flex-wrap gap-x-2">
                            <dt>{t(`acquisition.plan.step.${field}`)}</dt>
                            <dd className="whitespace-pre-wrap break-all">
                              {field === "input"
                                ? processingStepInputs(step).join(" + ")
                                : field === "replaceText" && step[field] === ""
                                  ? t("acquisition.plan.step.emptyReplacement")
                                  : field === "targetDirectory" &&
                                      step.operation === "rebaseDirectory" &&
                                      (step[field] === "" || step[field] === ".")
                                    ? t("acquisition.plan.step.outputRoot")
                                    : step[field]}
                            </dd>
                          </div>
                        ) : null,
                      )}
                    </dl>
                  )}
                </span>
              </li>
            ))}
          </ol>
        ) : (
          <p className="text-sm text-default-500">{t("acquisition.plan.noSteps")}</p>
        )}
      </section>
    </div>
  );
}
