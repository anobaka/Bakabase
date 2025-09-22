import type { DestroyableProps } from "@/components/bakaui/types";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React from "react";
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

  return (
    <Modal
      defaultVisible
      footer={false}
      size="lg"
      title={t("We noticed that you played the first file in this path. Would you like to proceed with the following actions?")}
      style={{maxWidth: '85vw'}}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center gap-2">
          <Chip variant="flat">
            {t("Current Path")}
          </Chip>
          <Snippet symbol={false} size="sm">{entry.path}</Snippet>
        </div>
        <div className="grid gap-3" style={{gridTemplateColumns: 'auto auto'}}>
          <Card className="border border-default-200 dark:border-default-100 rounded-md">
            <CardBody>
              <div className="mb-2 font-medium">{t("You can move it to another path.")}</div>
              <FolderSelectorInner
                sources={["media library", "custom"]}
                onSelect={(path) =>
                  BApi.file.moveEntries({ destDir: path, entryPaths: [entry.path] })
                }
              />
            </CardBody>
          </Card>
          <Card className="border border-default-200 dark:border-default-100 rounded-md">
            <CardBody className="flex flex-col gap-2">
              <div className="mb-2 font-medium">{t('Or delete it?')}</div>
              <Button
                color="danger"
                onPress={async () => {
                  createPortal(Modal, {
                    title: t("Delete this file/folder?"),
                    defaultVisible: true,
                    children: (
                      <div>{t("Are you sure you want to delete this file/folder?")}</div>
                    ),
                    footer: {
                      actions: ["ok", "cancel"],
                      okProps: {
                        color: "danger",
                      }
                    },
                    onOk: async () => {
                      await BApi.file.removeFiles({ paths: [entry.path] });
                    },
                  });
                }}
              >
                {t("Delete")}
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


