import type { TFunction } from "i18next";
import type { AcquisitionRecipeVm } from ".";

import { activityDisplayName } from "@/components/Workflow/displayNames";
import { workflowLabel } from "@/components/Workflow/builtinLabels";

export const recipeLabel = (recipe: AcquisitionRecipeVm, t: TFunction): string =>
  workflowLabel(recipe, t);

export const stepLabel = (kind: string, t: TFunction): string => activityDisplayName(t, kind);
