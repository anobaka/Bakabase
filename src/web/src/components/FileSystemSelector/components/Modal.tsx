"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";
import type { ModalProps } from "@/components/bakaui";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal } from "@/components/bakaui";
import { FileSystemSelectorPanel } from "@/components/FileSystemSelector";

interface IProps extends FileSystemSelectorProps, DestroyableProps {
  /** Explicitly opt a nested picker into its caller's overlay layer. */
  modalClassNames?: ModalProps["classNames"];
}
const FileSystemSelectorModal = (props: IProps) => {
  const { t } = useTranslation();
  const { modalClassNames, ...fsProps } = props;

  const [visible, setVisible] = useState(true);

  const close = () => {
    setVisible(false);
  };

  let titleKey = "fileSystemSelector.title.selectEntries";

  if (props.targetType != undefined) {
    switch (props.targetType) {
      case "file":
        titleKey = "fileSystemSelector.title.selectFile";
        break;
      case "folder":
        titleKey = "fileSystemSelector.title.selectFolder";
        break;
    }
  }

  return (
    <Modal
      className={"h-full"}
      classNames={modalClassNames}
      footer={false}
      size={"xl"}
      title={t(titleKey)}
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
        onMultipleSelected={(entries) => {
          close();
          if (fsProps.onMultipleSelected) {
            fsProps.onMultipleSelected(entries);
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
