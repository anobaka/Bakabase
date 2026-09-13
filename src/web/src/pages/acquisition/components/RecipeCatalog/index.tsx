import type { AcquisitionRecipeVm } from "../..";

import React from "react";
import { useTranslation } from "react-i18next";

import { recipeLabel, stepLabel } from "../../recipeLabels";

import { Button, Chip } from "@/components/bakaui";

type Props = {
  recipes: AcquisitionRecipeVm[];
  onOpen: (id: number) => void;
};

const RecipeCatalog = ({ recipes, onOpen }: Props) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-default-500">{t<string>("acquisition.recipes.description")}</p>
      {recipes.length === 0 ? (
        <div className="py-10 text-center text-default-500">
          {t<string>("acquisition.recipes.empty")}
        </div>
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {recipes.map((recipe) => (
            <section key={recipe.definitionId} className="rounded-xl border border-default-200 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <h3 className="font-medium">{recipeLabel(recipe, t)}</h3>
                <Chip size="sm" variant="flat">
                  {t<string>(
                    recipe.isBuiltin ? "acquisition.recipes.builtin" : "acquisition.recipes.custom",
                  )}
                </Chip>
                <Button
                  className="ml-auto"
                  size="sm"
                  variant="light"
                  onPress={() => onOpen(recipe.definitionId)}
                >
                  {t<string>("acquisition.recipes.open")}
                </Button>
              </div>
              <ol className="mt-3 flex flex-wrap items-center gap-2 text-xs text-default-600">
                {recipe.stepKinds.map((kind, index) => (
                  <li key={`${index}-${kind}`} className="rounded-md bg-default-100 px-2 py-1">
                    <span className="mr-1.5 text-default-400">{index + 1}.</span>
                    {stepLabel(kind, t)}
                  </li>
                ))}
              </ol>
            </section>
          ))}
        </div>
      )}
    </div>
  );
};

export default RecipeCatalog;
