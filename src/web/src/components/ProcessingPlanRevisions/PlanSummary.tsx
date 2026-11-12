import { useTranslation } from "react-i18next";
import type { BakabaseModulesWorkflowArtifactsFileProcessingPlan as ProcessingPlan } from "@/sdk/Api";
import { processingStepInputs } from "@/components/Workflow/processingStepInputs";

/** A readable projection of declared instructions. Passwords remain in the saved plan. */
export default function PlanSummary({ plan }: { plan?: ProcessingPlan | null }) {
  const { t } = useTranslation();
  if (!plan?.steps.length)
    return (
      <p className="text-sm text-default-500">
        {t(
          plan?.requirement === "notRequired"
            ? "processingRevision.plan.notRequired"
            : plan?.requirement === "required"
              ? "processingRevision.plan.missingSteps"
              : "processingRevision.plan.unknown",
        )}
      </p>
    );
  return (
    <ol className="space-y-2 text-sm">
      {plan.steps.map((step, index) => (
        <li key={step.id} className="space-y-1">
          <p className="font-medium">
            {index + 1}.{" "}
            {t(`acquisition.plan.processing.${step.op}`, {
              defaultValue: t("processingRevision.plan.unsupported"),
            })}
          </p>
          <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-default-500">
            {(
              [
                "input",
                "selector",
                "targetName",
                "targetDirectory",
                "extension",
                "findText",
                "replaceText",
              ] as const
            ).map((field) =>
              field === "input" ||
              step[field] ||
              (field === "replaceText" && step[field] === "") ? (
                <div key={field} className="flex min-w-0 gap-1">
                  <dt>{t(`acquisition.plan.step.${field}`)}:</dt>
                  <dd className="whitespace-pre-wrap break-all">
                    {field === "input"
                      ? processingStepInputs(step).join(" + ")
                      : field === "replaceText" && step[field] === ""
                        ? t("acquisition.plan.step.emptyReplacement")
                        : step[field]}
                  </dd>
                </div>
              ) : null,
            )}
          </dl>
          {step.password && (
            <p className="text-xs text-default-500">{t("processingRevision.plan.passwordSaved")}</p>
          )}
        </li>
      ))}
    </ol>
  );
}
