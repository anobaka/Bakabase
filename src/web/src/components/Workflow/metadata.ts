import type { TFunction } from "i18next";
import type { components } from "@/sdk/BApi2";

export type WorkflowDescription = Pick<
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowDefinitionViewModel"],
  "description" | "descriptionKey"
>;

export type WorkflowValidation =
  components["schemas"]["Bakabase.Modules.Workflow.Abstractions.Models.View.WorkflowValidationResult"];

/** Descriptions are authored metadata; they never determine what a workflow can do. */
export const workflowDescription = (source: WorkflowDescription, t: TFunction): string =>
  source.descriptionKey
    ? t<string>(source.descriptionKey, { defaultValue: source.description ?? "" })
    : (source.description ?? "");
