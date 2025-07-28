"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal } from "@/components/bakaui";
import { FileSystemSelectorPanel } from "@/components/FileSystemSelector";

interface IProps extends FileSystemSelectorProps, DestroyableProps {}
const FileSystemSelectorModal = (props: IProps) => {
  const { t } = useTranslation();
  const { ...fsProps } = props;

  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  let title = "Select file system entries";

  if (props.targetType != undefined) {
    switch (props.targetType) {
      case "file":
        title = "Select file";
        break;
      case "folder":
        title = "Select folder";
        break;
    }
  }

  return (
    <Modal
      className={"h-full"}
      footer={false}
      size={"xl"}
      title={t<string>(title)}
      visible={visible}
      onClose={close}
      onDestroyed={props.onDestroyed}
    >
      <FileSystemSelectorPanel
        {...fsProps}
        onCancel={() => {
          close();
          if (fsProps.onCancel) {
            fsProps.onCancel();
          }
        }}
        onSelected={(e) => {
          close();
          if (fsProps.onSelected) {
            fsProps.onSelected(e);
          }
        }}
      />
    </Modal>
  );
};

FileSystemSelectorModal.displayName = "FileSystemSelectorModal";

export default FileSystemSelectorModal;
