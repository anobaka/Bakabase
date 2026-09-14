"use client";

import type { AcquisitionRecipeVm } from "../..";
import type { DestroyableProps } from "@/components/bakaui/types";

import React from "react";
import { useTranslation } from "react-i18next";

import { recipeLabel, stepLabel } from "../../recipeLabels";
import { acceptsSharedPage, recipeInputKind, sharedPageDefaultRecipe } from "../../recipeGuide";

import BApi from "@/sdk/BApi";
import { Button, Input, Modal, Select, toast } from "@/components/bakaui";
import { AcquisitionLeadKind } from "@/sdk/constants";
import {
  MAX_SOURCE_LENGTH,
  validateAcquisitionSource,
} from "@/components/Resource/components/AcquisitionPanel/sourcePicker";

interface Props extends DestroyableProps {
  recipes: AcquisitionRecipeVm[];
  onStarted?: () => void;
}

/** The from-url endpoint currently consumes a sharing page, not an inferred link kind. */
const StartAcquisitionModal = ({ recipes, onStarted, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [url, setUrl] = React.useState("");
  const [recipeId, setRecipeId] = React.useState<number>();
  const [defaultId, setDefaultId] = React.useState<number>();
  const [loadingDefault, setLoadingDefault] = React.useState(true);
  const [defaultError, setDefaultError] = React.useState(false);
  const [revision, setRevision] = React.useState(0);
  const availableRecipes = recipes.filter(acceptsSharedPage);
  const selected = availableRecipes.find((recipe) => recipe.definitionId === recipeId);
  const validation = validateAcquisitionSource(AcquisitionLeadKind.SharedPage, url);

  React.useEffect(() => {
    let active = true;

    setLoadingDefault(true);
    setDefaultError(false);
    void (async () => {
      try {
        const rsp = await BApi.acquisition.getAcquisitionOptions();

        if (!active) return;
        if (rsp.code || !rsp.data) throw new Error("Unable to load the default workflow");
        const recipe = sharedPageDefaultRecipe(recipes, rsp.data.recipeByLeadKind);

        setDefaultId(recipe?.definitionId);
        setRecipeId((current) => current ?? recipe?.definitionId);
      } catch {
        if (active) setDefaultError(true);
      } finally {
        if (active) setLoadingDefault(false);
      }
    })();

    return () => {
      active = false;
    };
  }, [recipes, revision]);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>("acquisition.startFromUrl.createTask"),
          isDisabled: !!validation || !selected || loadingDefault,
        },
      }}
      size="2xl"
      title={t<string>("acquisition.startFromUrl.title")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        if (validation || !selected || loadingDefault)
          throw new Error(t("acquisition.startFromUrl.completeForm"));
        const rsp = await BApi.acquisition.createAcquisitionFromUrl({
          url: url.trim(),
          recipeDefinitionId: selected.definitionId,
        });

        if (rsp.code) throw new Error(rsp.message || t("acquisition.startFromUrl.failed"));
        toast.success(t<string>("acquisition.started"));
        onStarted?.();
      }}
    >
      <div className="flex flex-col gap-4">
        <div className="rounded-xl bg-primary/5 p-3 text-sm leading-relaxed text-default-600">
          <p className="font-medium text-default-800">
            {t<string>("acquisition.startFromUrl.scopeTitle")}
          </p>
          <p className="mt-1">{t<string>("acquisition.startFromUrl.scopeDescription")}</p>
        </div>
        <Input
          isRequired
          description={t<string>("acquisition.startFromUrl.description")}
          errorMessage={
            validation ? t<string>(`acquisition.sourcePicker.validation.${validation}`) : undefined
          }
          isInvalid={!!url.trim() && !!validation}
          label={t<string>("acquisition.startFromUrl.label")}
          maxLength={MAX_SOURCE_LENGTH}
          placeholder="https://example.com/posts/123"
          value={url}
          onValueChange={setUrl}
        />
        <p className="text-xs leading-relaxed text-default-500">
          {t<string>("acquisition.startFromUrl.preflightLimit")}
        </p>
        <Select
          disallowEmptySelection
          isRequired
          dataSource={availableRecipes.map((recipe) => {
            const name = recipeLabel(recipe, t);
            const label =
              recipe.definitionId === defaultId
                ? t<string>("acquisition.startFromUrl.defaultRecipe", { name })
                : name;

            return {
              value: String(recipe.definitionId),
              label,
              textValue: label,
            };
          })}
          description={t<string>("acquisition.recipe.description")}
          isDisabled={loadingDefault}
          isLoading={loadingDefault}
          label={t<string>("acquisition.recipe.label")}
          placeholder={t<string>("acquisition.overview.selectRecipe")}
          selectedKeys={selected ? [String(selected.definitionId)] : []}
          selectionMode="single"
          onSelectionChange={(keys) => {
            const first = Array.from(keys)[0];

            if (first != null) setRecipeId(Number(first));
          }}
        />
        {!loadingDefault && (defaultError || defaultId == null) && (
          <div className="flex flex-wrap items-center gap-2 text-xs text-warning-600" role="status">
            {t<string>(
              defaultError
                ? "acquisition.startFromUrl.defaultLoadFailed"
                : "acquisition.startFromUrl.defaultMissing",
            )}
            {defaultError && (
              <Button size="sm" variant="light" onPress={() => setRevision((value) => value + 1)}>
                {t<string>("acquisition.retry")}
              </Button>
            )}
          </div>
        )}
        {availableRecipes.length === 0 && (
          <p className="text-sm text-warning-600">
            {t<string>("acquisition.startFromUrl.noCompatibleRecipe")}
          </p>
        )}
        {selected && (
          <div className="rounded-xl bg-default-50 p-3">
            <p className="text-xs font-medium text-default-700">
              {t<string>("acquisition.startFromUrl.whatHappens")}
            </p>
            <p className="mt-1 text-xs leading-relaxed text-default-600">
              {t<string>(`acquisition.recipeGuide.${recipeInputKind(selected)}.requirements`)}
            </p>
            {recipeInputKind(selected) !== "inbox" &&
              selected.stepKinds.includes("acquisition.waitForInbox") && (
                <p className="mt-1 text-xs leading-relaxed text-default-600">
                  {t<string>("acquisition.overview.method.inbox")}
                </p>
              )}
            <ol className="mt-2 flex flex-wrap gap-1.5 text-xs text-default-500">
              {selected.stepKinds.map((kind, index) => (
                <li key={`${index}-${kind}`} className="rounded-md bg-default-100 px-2 py-1">
                  {index + 1}. {stepLabel(kind, t)}
                </li>
              ))}
            </ol>
          </div>
        )}
        <details className="text-sm text-default-600">
          <summary className="w-fit cursor-pointer py-1">
            {t<string>("acquisition.startFromUrl.otherLinks")}
          </summary>
          <div className="mt-2 space-y-2 text-xs leading-relaxed text-default-500">
            <p>{t<string>("acquisition.startFromUrl.directAndMagnet")}</p>
            <p>{t<string>("acquisition.startFromUrl.cloudAndPlatform")}</p>
          </div>
        </details>
        <p className="text-xs leading-relaxed text-default-500">
          {t<string>("acquisition.startFromUrl.steps.track")}
        </p>
      </div>
    </Modal>
  );
};

export default StartAcquisitionModal;
