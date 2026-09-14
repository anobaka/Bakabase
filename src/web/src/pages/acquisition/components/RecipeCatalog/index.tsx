import type { AcquisitionRecipeVm } from "../..";

import React from "react";
import { useTranslation } from "react-i18next";

import { recipeLabel } from "../../recipeLabels";

import WorkflowSummary from "@/components/Workflow/WorkflowSummary";
import { Button, Chip } from "@/components/bakaui";
import { AcquisitionLeadKind } from "@/sdk/constants";

const inputKeys: Record<number, string> = {
  [AcquisitionLeadKind.PlatformHolding]: "acquisition.input.platform",
  [AcquisitionLeadKind.SharedPage]: "acquisition.input.sharedPage",
  [AcquisitionLeadKind.SharedDocument]: "acquisition.input.sharedDocument",
  [AcquisitionLeadKind.DirectUrl]: "acquisition.input.directUrl",
  [AcquisitionLeadKind.Magnet]: "acquisition.input.magnet",
  [AcquisitionLeadKind.Manual]: "acquisition.input.localDirectory",
  [AcquisitionLeadKind.Torrent]: "acquisition.input.torrent",
};

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
              <p className="mt-2 text-xs text-default-500">
                {t<string>("acquisition.input.label")}:{" "}
                {recipe.applicableLeadKinds?.length
                  ? recipe.applicableLeadKinds
                      .map((kind) => t<string>(inputKeys[kind] ?? "acquisition.input.other"))
                      .join(" / ")
                  : t<string>("acquisition.input.none")}
              </p>
              <div className="mt-3">
                <WorkflowSummary activityKinds={recipe.stepKinds} workflow={recipe} />
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
};

export default RecipeCatalog;
