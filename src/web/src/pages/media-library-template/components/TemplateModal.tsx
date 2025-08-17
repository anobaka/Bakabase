"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { MediaLibraryTemplatePage } from "@/pages/media-library-template/models";

import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";
import { useEffect, useState } from "react";

import { Chip, Modal } from "@/components/bakaui";
import Template from "@/pages/media-library-template/components/Template";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Props = {
  id: number;
} & DestroyableProps;
const TemplateModal = ({ id, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [template, setTemplate] = useState<MediaLibraryTemplatePage>();

  useEffect(() => {
    BApi.mediaLibraryTemplate.getMediaLibraryTemplate(id).then((r) => {
      if (!r.code) {
        setTemplate(r.data!);
      }
    });
  }, []);

  return (
    <Modal
      defaultVisible
      footer={false}
      size={"full"}
      title={
        <div>
          {t<string>("Editing media library template")}
          &nbsp;
          <Chip
            className={"font-bold"}
            color={"success"}
            size={"lg"}
            variant={"light"}
          >
            {template?.name}
          </Chip>
        </div>
      }
      onDestroyed={onDestroyed}
    >
      {template && <Template template={template} />}
    </Modal>
  );
};

TemplateModal.displayName = "TemplateModal";

export default TemplateModal;
