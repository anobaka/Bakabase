"use client";

import type { AcquisitionRecipeVm } from "../..";
import type { DestroyableProps } from "@/components/bakaui/types";

import React from "react";
import { useTranslation } from "react-i18next";

import { recipeLabel } from "../../recipeLabels";

import BApi from "@/sdk/BApi";
import { Input, Modal, Select, toast } from "@/components/bakaui";

interface Props extends DestroyableProps {
  recipes: AcquisitionRecipeVm[];
  onStarted?: () => void;
}

/**
 * Match or create a resource from a pasted link and start an acquisition task. This endpoint
 * uses the shared-page default unless the user explicitly chooses a different workflow.
 */
const StartAcquisitionModal = ({ recipes, onStarted, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [url, setUrl] = React.useState("");
  const [recipeId, setRecipeId] = React.useState<number | null>(null);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>("acquisition.startFromUrl.createTask"),
          isDisabled: !url.trim(),
        },
      }}
      size="lg"
      title={t<string>("acquisition.start")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const rsp = await BApi.acquisition.createAcquisitionFromUrl({
          url: url.trim(),
          recipeDefinitionId: recipeId ?? undefined,
        });

        if (!rsp.code) {
          toast.success(t<string>("acquisition.started"));
          onStarted?.();
        }
      }}
    >
      <div className="flex flex-col gap-3">
        <ol className="list-decimal space-y-1 pl-5 text-sm text-default-600">
          <li>{t<string>("acquisition.startFromUrl.steps.link")}</li>
          <li>{t<string>("acquisition.startFromUrl.steps.workflow")}</li>
          <li>{t<string>("acquisition.startFromUrl.steps.track")}</li>
        </ol>
        <Input
          isRequired
          description={t<string>("acquisition.startFromUrl.description")}
          label={t<string>("acquisition.startFromUrl.label")}
          value={url}
          onValueChange={setUrl}
        />
        <Select
          dataSource={recipes.map((recipe) => {
            const label = recipeLabel(recipe, t);

            return {
              value: String(recipe.definitionId),
              label,
              textValue: label,
            };
          })}
          description={t<string>("acquisition.recipe.description")}
          label={t<string>("acquisition.recipe.label")}
          selectedKeys={recipeId == null ? [] : [String(recipeId)]}
          selectionMode="single"
          onSelectionChange={(keys) => {
            const first = Array.from(keys)[0] as string | undefined;

            setRecipeId(first ? Number(first) : null);
          }}
        />
        <p className="rounded-lg bg-default-100 p-3 text-sm text-default-600">
          {t<string>("acquisition.startFromUrl.manualHelp")}
        </p>
      </div>
    </Modal>
  );
};

export default StartAcquisitionModal;
