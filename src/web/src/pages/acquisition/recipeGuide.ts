import type { AcquisitionRecipeVm } from ".";

export const recipeInputKinds = {
  "acquisition.resolveSharedContent": "sharedContent",
  "acquisition.fetchHttp": "directDownload",
  "acquisition.fetchMagnet": "magnetDownload",
  "acquisition.fetchFromPlatform": "platform",
  "acquisition.pickLocalDirectory": "localDirectory",
  "acquisition.waitForInbox": "inbox",
} as const;

/** Follow the first input-consuming step, so copied and renamed workflows get the same guidance. */
export const recipeInputKind = (recipe: AcquisitionRecipeVm) => {
  const first = recipe.stepKinds.find((kind) => kind in recipeInputKinds);

  return first ? recipeInputKinds[first as keyof typeof recipeInputKinds] : "custom";
};

export const acceptsSharedPage = (recipe: AcquisitionRecipeVm) =>
  recipe.stepKinds.includes("acquisition.materialize") &&
  ["sharedContent", "inbox"].includes(recipeInputKind(recipe));

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
