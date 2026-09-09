"use client";

import type { AcquisitionRecipeVm } from "../..";
import type { DestroyableProps } from "@/components/bakaui/types";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Input, Modal, Select, toast } from "@/components/bakaui";

interface Props extends DestroyableProps {
  recipes: AcquisitionRecipeVm[];
  onStarted?: () => void;
}

/**
 * Paste a link, get the thing. The resource is matched or created from the link itself, so there is
 * nothing else to fill in — picking a recipe is optional and the lead's kind decides by default.
 */
const StartAcquisitionModal = ({ recipes, onStarted, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [url, setUrl] = React.useState("");
  const [recipeId, setRecipeId] = React.useState<number | null>(null);

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel", "ok"] }}
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
        <Input
          isRequired
          description={t<string>("acquisition.startFromUrl.description")}
          label={t<string>("acquisition.startFromUrl.label")}
          value={url}
          onValueChange={setUrl}
        />
        <Select
          dataSource={recipes.map((r) => ({
            value: String(r.definitionId),
            label: r.name,
            textValue: r.name,
          }))}
          description={t<string>("acquisition.recipe.description")}
          label={t<string>("acquisition.recipe.label")}
          selectedKeys={recipeId == null ? [] : [String(recipeId)]}
          selectionMode="single"
          onSelectionChange={(keys) => {
            const first = Array.from(keys)[0] as string | undefined;

            setRecipeId(first ? Number(first) : null);
          }}
        />
      </div>
    </Modal>
  );
};

export default StartAcquisitionModal;
