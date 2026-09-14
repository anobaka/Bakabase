import type { AcquisitionRecipeVm } from "..";

import { describe, expect, it } from "vitest";

import { acceptsSharedPage, recipeInputKind, sharedPageDefaultRecipe } from "../recipeGuide";

const recipe = (definitionId: number, name: string, stepKinds: string[]): AcquisitionRecipeVm => ({
  definitionId,
  name,
  isBuiltin: false,
  stepKinds,
});
const materialize = "acquisition.materialize";
const resolve = "acquisition.resolveSharedContent";
const inbox = "acquisition.waitForInbox";

describe("sharing-page workflow guidance", () => {
  it("uses the first consuming step for copied and renamed workflows, skipping other steps", () => {
    const copied = recipe(12, "My renamed workflow", [
      "transform.example",
      resolve,
      inbox,
      materialize,
    ]);

    expect(recipeInputKind(copied)).toBe("sharedContent");
    expect(acceptsSharedPage(copied)).toBe(true);
    expect(recipeInputKind(recipe(13, "Manual intake", [inbox, materialize]))).toBe("inbox");
    expect(acceptsSharedPage(recipe(13, "Manual intake", [inbox, materialize]))).toBe(true);
  });

  it.each(["fetchHttp", "fetchMagnet", "fetchFromPlatform", "pickLocalDirectory"])(
    "does not offer a workflow whose first source is %s even if it later resolves a page",
    (first) => {
      expect(
        acceptsSharedPage(
          recipe(1, "Mixed inputs", [`acquisition.${first}`, resolve, materialize]),
        ),
      ).toBe(false);
    },
  );

  it("requires materialization and a recognized sharing-page input", () => {
    expect(acceptsSharedPage(recipe(1, "Resolve only", [resolve]))).toBe(false);
    expect(acceptsSharedPage(recipe(2, "Unknown", ["custom.step", materialize]))).toBe(false);
    expect(recipeInputKind(recipe(3, "Empty", []))).toBe("custom");
  });

  it.each(["SharedPage", "2"])(
    "does not skip an incompatible lowest-id default configured through %s",
    (key) => {
      const recipes = [
        recipe(1, "Forum post + cloud drive", [resolve, materialize]),
        recipe(8, "My intake", [inbox, materialize]),
        recipe(4, "My intake", [resolve, materialize]),
        recipe(2, "My intake", ["acquisition.fetchHttp", materialize]),
      ];

      expect(sharedPageDefaultRecipe(recipes, { [key]: "My intake" })).toBeUndefined();
      expect(recipes.map((item) => item.definitionId)).toEqual([1, 8, 4, 2]);
    },
  );

  it("selects the lowest-id named definition when that definition is compatible", () => {
    const recipes = [
      recipe(1, "Forum post + cloud drive", [resolve, materialize]),
      recipe(8, "My intake", [inbox, materialize]),
      recipe(4, "My intake", [resolve, materialize]),
    ];

    expect(sharedPageDefaultRecipe(recipes, { SharedPage: "My intake" })?.definitionId).toBe(4);
    expect(recipes.map((item) => item.definitionId)).toEqual([1, 8, 4]);
  });

  it.each(["Missing workflow", "Direct download", ""])(
    "does not silently replace a configured %j default",
    (name) => {
      const recipes = [
        recipe(1, "Forum post + cloud drive", [resolve, materialize]),
        recipe(2, "Direct download", ["acquisition.fetchHttp", materialize]),
      ];

      expect(sharedPageDefaultRecipe(recipes, { SharedPage: name })).toBeUndefined();
    },
  );

  it("uses the built-in system default only when no per-kind default is configured", () => {
    const builtin = recipe(31, "Forum post + cloud drive", [resolve, materialize]);

    expect(sharedPageDefaultRecipe([builtin], {})).toBe(builtin);
  });
});
