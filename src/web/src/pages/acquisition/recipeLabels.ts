import type { TFunction } from "i18next";
import type { AcquisitionRecipeVm } from ".";

import { workflowLabel } from "@/components/Workflow/builtinLabels";

export const recipeLabel = (recipe: AcquisitionRecipeVm, t: TFunction): string =>
  workflowLabel(recipe, t);

export const stepLabel = (kind: string, t: TFunction): string => {
  const key = `workflow.acquisition.step.${kind.replace(/^acquisition\./, "")}`;

  return t<string>(key, { defaultValue: kind });
};
