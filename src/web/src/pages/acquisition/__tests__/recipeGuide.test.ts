import type { AcquisitionRecipeVm } from "..";

import { describe, expect, it } from "vitest";

import { acceptsSharedPage, sharedPageDefaultRecipe } from "../recipeGuide";

import { AcquisitionLeadKind } from "@/sdk/constants";

const recipe = (
  definitionId: number,
  name: string,
  applicableLeadKinds: AcquisitionRecipeVm["applicableLeadKinds"] = [],
): AcquisitionRecipeVm => ({
  definitionId,
  name,
  isBuiltin: false,
  stepKinds: [],
  validation: { isValid: true, diagnostics: [] },
  applicableLeadKinds,
});
const shared = [AcquisitionLeadKind.SharedPage];

describe("sharing-page workflow input metadata", () => {
  it("uses declared inputs even when kinds and prose suggest a different behavior", () => {
    expect(
      acceptsSharedPage({ ...recipe(1, "Custom", shared), stepKinds: ["custom.action"] }),
    ).toBe(true);
    expect(
      acceptsSharedPage({
        ...recipe(2, "Forum post + cloud drive"),
        description: "Accepts sharing pages",
        stepKinds: ["acquisition.resolveSharedContent", "acquisition.materialize"],
      }),
    ).toBe(false);
  });

  it("does not infer compatibility when input metadata is missing", () => {
    const unknown = { ...recipe(1, "Custom", shared), applicableLeadKinds: undefined };

    expect(acceptsSharedPage(unknown)).toBe(false);
  });

  it.each(["SharedPage", "2"])(
    "does not skip an incompatible lowest-id default configured through %s",
    (key) => {
      const recipes = [
        recipe(1, "Forum post + cloud drive", shared),
        recipe(8, "My intake", shared),
        recipe(4, "My intake", shared),
        recipe(2, "My intake"),
      ];

      expect(sharedPageDefaultRecipe(recipes, { [key]: "My intake" })).toBeUndefined();
      expect(recipes.map((item) => item.definitionId)).toEqual([1, 8, 4, 2]);
    },
  );

  it("selects the lowest-id named definition when that definition is compatible", () => {
    const recipes = [
      recipe(1, "Forum post + cloud drive", shared),
      recipe(8, "My intake", shared),
      recipe(4, "My intake", shared),
    ];

    expect(sharedPageDefaultRecipe(recipes, { SharedPage: "My intake" })?.definitionId).toBe(4);
    expect(recipes.map((item) => item.definitionId)).toEqual([1, 8, 4]);
  });

  it.each(["Missing workflow", "Direct download", ""])(
    "does not silently replace a configured %j default",
    (name) => {
      const recipes = [recipe(1, "Forum post + cloud drive", shared), recipe(2, "Direct download")];

      expect(sharedPageDefaultRecipe(recipes, { SharedPage: name })).toBeUndefined();
    },
  );

  it("uses the built-in system default only when no per-kind default is configured", () => {
    const builtin = recipe(31, "Forum post + cloud drive", shared);

    expect(sharedPageDefaultRecipe([builtin], {})).toBe(builtin);
  });
});
