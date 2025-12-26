"use client";

import type { DestroyableProps } from "../bakaui/types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Modal } from "../bakaui";
import FileNameModifier from "../FileNameModifier";
import BetaChip from "../Chips/BetaChip";

export interface FileNameModificationResult {
  originalPath: string;
  modifiedPath: string;
  originalFileName: string;
  modifiedFileName: string;
  commonPrefix: string;
  originalRelative: string;
  modifiedRelative: string;
}

interface FileNameModifierModalProps extends DestroyableProps {
  onClose?: () => void;
  initialFilePaths?: string[];
}

const FileNameModifierModal: React.FC<FileNameModifierModalProps> = ({
  onClose,
  initialFilePaths = [],
}) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={null}
      size="7xl"
      title={
        <div className="flex items-center gap-1">
          {t<string>("FileNameModifier.Title")}
          <BetaChip />
        </div>
      }
      onClose={onClose}
    >
      <FileNameModifier initialFilePaths={initialFilePaths} onClose={onClose} />
    </Modal>
  );
};

export default FileNameModifierModal;
