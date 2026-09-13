import type { TFunction } from "i18next";
import type { AcquisitionRecipeVm } from ".";

const BUILTIN_NAMES: Record<string, string> = {
  "Forum post + cloud drive": "acquisition.recipe.sharedContent",
  "Direct download": "acquisition.recipe.directDownload",
  Magnet: "acquisition.recipe.magnet",
  "Platform fetch": "acquisition.recipe.platform",
  "Local directory": "acquisition.recipe.localDirectory",
};

export const recipeLabel = (recipe: AcquisitionRecipeVm, t: TFunction): string =>
  recipe.isBuiltin && BUILTIN_NAMES[recipe.name]
    ? t<string>(BUILTIN_NAMES[recipe.name])
    : recipe.name;

export const stepLabel = (kind: string, t: TFunction): string => {
  const key = `workflow.acquisition.step.${kind.replace(/^acquisition\./, "")}`;

  return t<string>(key, { defaultValue: kind });
};
