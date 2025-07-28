"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { Trans, useTranslation } from "react-i18next";
import { useState } from "react";

import { Button, Input, Modal } from "@/components/bakaui";
import BuiltinTemplateSelector from "@/pages/media-library-template/components/PresetTemplateBuilder";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {} & DestroyableProps;
const AddModal = ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [name, setName] = useState<string>();
  const [visible, setVisible] = useState<boolean>(true);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          isDisabled: name == undefined || name.length == 0,
        },
      }}
      size={"md"}
      title={t<string>("Add a media library template")}
      visible={visible}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const r = await BApi.mediaLibraryTemplate.addMediaLibraryTemplate({
          name: name!,
        });

        if (r.code) {
          throw new Error(r.message);
        }
        setVisible(false);
      }}
    >
      <div className={"flex flex-col gap-1"}>
        <div>
          <Input
            isRequired
            label={t<string>("Template name")}
            placeholder={t<string>("Enter template name")}
            value={name}
            onValueChange={setName}
          />
        </div>
        <div className={""}>
          <Trans i18nKey={"media-library-template.use-preset-template-builder"}>
            We recommend using our
            <Button
              color={"primary"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                setVisible(false);
                createPortal(BuiltinTemplateSelector, {
                  onSubmitted: onDestroyed,
                });
              }}
            >
              {t<string>("Preset template builder")}
            </Button>
            the first time you create a media library template.
          </Trans>
        </div>
      </div>
    </Modal>
  );
};

AddModal.displayName = "AddModal";

export default AddModal;
