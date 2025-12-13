"use client";

import type { DestroyableProps } from "@/components/bakaui/types.ts";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, NumberInput } from "@/components/bakaui";
import ExtensionGroupSelect from "@/components/ExtensionGroupSelect";
import ExtensionsInput from "@/components/ExtensionsInput";

type Selection = {
  extensionGroupIds?: number[];
  extensions?: string[];
  maxFileCount?: number;
};

type Props = {
  selection?: Selection;
  onSubmit?: (selection: Selection) => any;
} & DestroyableProps;
const PlayableFileSelectorModal = ({ selection: propSelection, onSubmit }: Props) => {
  const { t } = useTranslation();

  const [selection, setSelection] = useState<Selection>(propSelection ?? {});

  return (
    <Modal defaultVisible size={"xl"} onOk={() => onSubmit?.(selection)}>
      <div className={"flex flex-col gap-2"}>
        <ExtensionGroupSelect
          value={selection.extensionGroupIds}
          onSelectionChange={(ids) => {
            setSelection({
              ...selection,
              extensionGroupIds: ids,
            });
          }}
        />
        <ExtensionsInput
          defaultValue={selection.extensions}
          label={t<string>("Limit file extensions")}
          onValueChange={(v) => {
            setSelection({
              ...selection,
              extensions: v,
            });
          }}
        />
        <div className={"flex flex-col gap-1"}>
          <label className={"text-sm font-medium"}>{t<string>("Maximum file count")}</label>
          <NumberInput
            label={t<string>("Leave empty for no limit")}
            minValue={1}
            value={selection.maxFileCount}
            onValueChange={(v) => {
              setSelection({
                ...selection,
                maxFileCount: v,
              });
            }}
          />
        </div>
      </div>
    </Modal>
  );
};

PlayableFileSelectorModal.displayName = "PlayableFileSelectorModal";

export default PlayableFileSelectorModal;
