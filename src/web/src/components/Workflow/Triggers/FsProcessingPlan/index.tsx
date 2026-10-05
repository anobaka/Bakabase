import type { WorkflowTriggerUI } from "../types";

import { useTranslation } from "react-i18next";

import { Input, Switch, Textarea } from "@/components/bakaui";
import { WorkflowItemTypes } from "@/components/Workflow/itemTypes";

type Payload = {
  directory: string;
  files?: string[];
  extractionPlanJson: string;
  alreadyProcessed?: boolean;
};

export function isProcessingPlanPayloadValid(json: string): boolean {
  try {
    const value = JSON.parse(json) as Payload;

    if (!value.directory?.trim()) return false;
    if (
      value.files &&
      (!Array.isArray(value.files) ||
        value.files.some((file) => typeof file !== "string" || !file.trim()))
    )
      return false;
    if (value.alreadyProcessed === true) return true;
    const plan = JSON.parse(value.extractionPlanJson);

    return (
      !!plan &&
      typeof plan === "object" &&
      ["required", "notRequired"].includes(plan.requirement) &&
      Array.isArray(plan.steps) &&
      (plan.requirement === "notRequired" || plan.steps.length > 0)
    );
  } catch {
    return false;
  }
}

const Explanation = () => {
  const { t } = useTranslation();

  return (
    <p className="text-sm text-default-500">{t("workflow.trigger.fsProcessingPlan.description")}</p>
  );
};

const ManualRunForm: NonNullable<WorkflowTriggerUI["ManualRunForm"]> = ({ value, onChange }) => {
  const { t } = useTranslation();
  let payload: Payload = { directory: "", extractionPlanJson: "" };

  try {
    payload = { ...payload, ...JSON.parse(value) };
  } catch {
    /* Keep drafts editable. */
  }
  const update = (patch: Partial<Payload>) => onChange(JSON.stringify({ ...payload, ...patch }));

  return (
    <div className="space-y-4">
      <Explanation />
      <Input
        label={t("workflow.processing.directory")}
        value={payload.directory}
        onValueChange={(directory) => update({ directory })}
      />
      <Textarea
        description={t("workflow.processing.filesHint")}
        label={t("workflow.processing.files")}
        value={payload.files?.join("\n") ?? ""}
        onValueChange={(text) =>
          update({
            files: text.trim()
              ? text
                  .split(/\r?\n/)
                  .map((path) => path.trim())
                  .filter(Boolean)
              : undefined,
          })
        }
      />
      <Switch
        isSelected={payload.alreadyProcessed === true}
        onValueChange={(alreadyProcessed) => update({ alreadyProcessed })}
      >
        {t("workflow.processing.alreadyProcessed")}
      </Switch>
      {!payload.alreadyProcessed && (
        <Textarea
          description={t("workflow.processing.planHint")}
          label={t("workflow.processing.plan")}
          minRows={8}
          value={payload.extractionPlanJson}
          onValueChange={(extractionPlanJson) => update({ extractionPlanJson })}
        />
      )}
    </div>
  );
};

export const FsProcessingPlanTriggerUI: WorkflowTriggerUI<Record<string, never>> = {
  kind: "fs.processingPlan",
  displayNameKey: "workflow.trigger.fsProcessingPlan.displayName",
  defaultFilter: () => ({}),
  parseFilter: () => ({}),
  serializeFilter: () => null,
  isValid: () => true,
  resolveOutputItemType: () => WorkflowItemTypes.Acquisition,
  FilterForm: Explanation,
  FilterSummary: Explanation,
  ManualRunForm,
  defaultManualPayload: () => JSON.stringify({ directory: "", extractionPlanJson: "" }),
  isManualPayloadValid: isProcessingPlanPayloadValid,
};
