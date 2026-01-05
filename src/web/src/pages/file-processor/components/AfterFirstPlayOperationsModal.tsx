import type { DestroyableProps } from "@/components/bakaui/types";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Card, CardBody, Chip, Modal, Snippet } from "@/components/bakaui";
import FolderSelectorInner from "@/components/FolderSelector/components/FolderSelectorInner";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  entry: Entry;
} & DestroyableProps;

const AfterFirstPlayOperationsModal = ({ entry, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);

  return (
    <Modal
      visible={visible}
      footer={false}
      onClose={() => setVisible(false)}
      size="lg"
      title={t("fileProcessor.modal.afterFirstPlayTitle")}
      style={{ maxWidth: '85vw' }}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <Chip variant="flat">
            {t("fileProcessor.label.currentPath")}
          </Chip>
          <Snippet symbol={false} size="sm">{entry.path}</Snippet>
        </div>
        <div className="grid gap-3" style={{ gridTemplateColumns: 'auto auto' }}>
          <Card className="border border-default-200 dark:border-default-100 rounded-md">
            <CardBody>
              <div className="mb-2 font-medium">{t("fileProcessor.tip.moveToAnotherPath")}</div>
              <FolderSelectorInner
                sources={["media library", "custom"]}
                onSelect={async (path) => {
                  await BApi.file.moveEntries({ destDir: path, entryPaths: [entry.path] });
                  setVisible(false);
                }}
              />
            </CardBody>
          </Card>
          <Card className="border border-default-200 dark:border-default-100 rounded-md">
            <CardBody className="flex flex-col gap-2">
              <div className="mb-2 font-medium">{t("fileProcessor.tip.orDeleteIt")}</div>
              <Button
                color="danger"
                onPress={async () => {
                  createPortal(Modal, {
                    title: t("common.confirm.deleteFileTitle"),
                    defaultVisible: true,
                    children: (
                      <div>{t("common.confirm.deleteFile")}</div>
                    ),
                    footer: {
                      actions: ["ok", "cancel"],
                      okProps: {
                        color: "danger",
                      }
                    },
                    onOk: async () => {
                      await BApi.file.removeFiles({ paths: [entry.path] });
                      setVisible(false);
                    },
                  });
                }}
              >
                {t("common.action.delete")}
              </Button>
            </CardBody>
          </Card>
        </div>
      </div>
    </Modal>
  );
};

AfterFirstPlayOperationsModal.displayName = "AfterFirstPlayOperationsModal";

export default AfterFirstPlayOperationsModal;


