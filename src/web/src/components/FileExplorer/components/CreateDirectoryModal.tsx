"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Modal } from "@/components/bakaui";

type Props = {
  parentPath: string;
} & DestroyableProps;

const CreateDirectoryModal = ({ parentPath, onDestroyed }: Props) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          children: `${t<string>("Create")}(Enter)`,
          autoFocus: true,
        },
      }}
      size={"sm"}
      title={t<string>("Create new folder")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await BApi.file.createDirectory({ parent: parentPath });
      }}
    >
      <div className="text-sm">
        {t<string>("Create a new folder in")}: <span className="font-medium">{parentPath}</span>
      </div>
    </Modal>
  );
};

CreateDirectoryModal.displayName = "CreateDirectoryModal";

export default CreateDirectoryModal;
