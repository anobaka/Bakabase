import type { AcquisitionRecipeVm } from ".";

import { AcquisitionLeadKind } from "@/sdk/constants";

/** Input compatibility is declared by the server, never inferred from step order or prose. */
export const acceptsSharedPage = (recipe: Partial<AcquisitionRecipeVm>) =>
  recipe.applicableLeadKinds?.includes(AcquisitionLeadKind.SharedPage) === true;

export const sharedPageDefaultRecipe = (
  recipes: AcquisitionRecipeVm[],
  recipeByLeadKind?: Record<string, string>,
) => {
  // Dictionary enum keys may be represented by their name or numeric value.
  const name =
    recipeByLeadKind?.SharedPage ?? recipeByLeadKind?.["2"] ?? "Forum post + cloud drive";

  const configured = recipes
    .filter((recipe) => recipe.name === name)
    .sort((a, b) => a.definitionId - b.definitionId)[0];

  return configured && acceptsSharedPage(configured) ? configured : undefined;
};
