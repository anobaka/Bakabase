"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";

import PathMarkTreeView from "@/pages/path-mark-config/components/PathMarkTreeView";

import { Modal } from "@/components/bakaui";

interface PathConfigModalProps extends DestroyableProps {
  path: string;
  onMarksChanged?: () => void;
}

const PathConfigModal = ({
  path,
  onDestroyed,
  onMarksChanged,
}: PathConfigModalProps) => {
  const { t } = useTranslation();

  return (
    <Modal
      classNames={{
        base: "h-[80vh]",
        body: "p-0 overflow-hidden",
      }}
      defaultVisible
      footer={false}
      size="5xl"
      title={t("pathMarks.modal.configurePathMarksTitle")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col h-full p-4">
        <div className="text-sm text-default-500 mb-4">
          {t("pathMarks.tip.clickOnFoldersToNavigate")}
        </div>
        <div className="flex-1 min-h-0 overflow-hidden border border-default-200 rounded-lg">
          <PathMarkTreeView
            rootPath={path}
            onMarksChanged={onMarksChanged}
          />
        </div>
      </div>
    </Modal>
  );
};

PathConfigModal.displayName = "PathConfigModal";

export default PathConfigModal;
