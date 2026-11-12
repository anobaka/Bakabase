import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";
import type { LocalFileInstructions } from "./localProcessingPlanPreview";

import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem } from "@/components/bakaui";

import { activityDisplayName } from "./displayNames";
import { getWorkflowActivityUI } from "./Activities";
import { localFileWorkflowPreview } from "./localProcessingPlanPreview";

/** A plan preview only: rendering this component never calls an execution or filesystem API. */
export default function LocalFileWorkflowPreview({
  workflow,
  instructions,
  alreadyProcessed = false,
}: {
  workflow: Definition;
  instructions?: LocalFileInstructions | null;
  alreadyProcessed?: boolean;
}) {
  const { t } = useTranslation();
  const steps = localFileWorkflowPreview(workflow, instructions, alreadyProcessed);

  return (
    <div className="space-y-3 text-xs">
      <ol className="space-y-3">
        {steps.map(({ activity, configValid, notices }, index) => {
          const ui = getWorkflowActivityUI(activity.kind);
          const Summary = ui?.Summary;

          return (
            <li key={`${activity.id}-${index}`} className="space-y-1 rounded-md bg-default-50 p-2">
              <div className="font-medium">
                {index + 1}. {activityDisplayName(t, activity.kind)}
              </div>
              {configValid && Summary && <Summary config={ui.parseConfig(activity.configJson)} />}
              {notices.map((notice) => (
                <p
                  key={notice.key}
                  className={notice.warning ? "text-warning-600" : "text-default-500"}
                >
                  {t(`workflow.processing.preview.${notice.key}`, notice.values)}
                </p>
              ))}
              {activity.notes && (
                <p className="whitespace-pre-wrap break-words text-default-500">{activity.notes}</p>
              )}
            </li>
          );
        })}
      </ol>
      {!steps.length && (
        <p className="text-warning-600">{t("workflow.processing.preview.noSteps")}</p>
      )}
      {!alreadyProcessed && instructions?.steps?.length ? (
        <Accordion variant="light">
          <AccordionItem key="instructions" title={t("workflow.processing.preview.instructions")}>
            <ol className="mt-2 space-y-1 pl-4">
              {instructions.steps.map((step, index) => (
                <li key={index} className="break-words">
                  {index + 1}.{" "}
                  {t(
                    `workflow.processing.preview.operation.${step.op === "renameLiteral" && step.replaceText === "" ? "renameLiteralRemove" : step.op}`,
                    {
                      defaultValue: step.op ?? "?",
                      extension: step.extension ?? "?",
                      name: step.targetName ?? "?",
                      directory:
                        step.op === "rebaseDirectory" &&
                        (step.targetDirectory === "" || step.targetDirectory === ".")
                          ? t("workflow.processing.preview.outputRoot")
                          : (step.targetDirectory ?? "?"),
                      sourceDirectory: step.sourceDirectory ?? "?",
                      findText: step.findText ?? "?",
                      replaceText: step.replaceText ?? "?",
                    },
                  )}
                  {step.selector && (
                    <span className="ml-1 text-default-500">({step.selector})</span>
                  )}
                </li>
              ))}
            </ol>
          </AccordionItem>
        </Accordion>
      ) : null}
    </div>
  );
}
