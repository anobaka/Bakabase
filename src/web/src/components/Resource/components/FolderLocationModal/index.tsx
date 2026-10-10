"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { CopyOutlined } from "@ant-design/icons";
import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Modal, toast } from "@/components/bakaui";
import { copyTextToClipboard } from "@/core/clipboard";

type Props = DestroyableProps & { path: string };

const FolderLocationModal = ({ path, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [copyFeedback, setCopyFeedback] = useState<string>();

  const copyPath = async () => {
    setCopyFeedback(undefined);
    try {
      await copyTextToClipboard(path);
      setCopyFeedback(t("resource.folderLocation.copied"));
      toast.success(t("resource.folderLocation.copied"));
    } catch {
      setCopyFeedback(t("resource.folderLocation.copyFailed"));
      toast.danger(t("resource.folderLocation.copyFailed"));
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"] }}
      size="md"
      title={t("resource.folderLocation.title")}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <p className="text-sm text-foreground-500">{t("resource.folderLocation.browserHint")}</p>
        <label className="flex flex-col gap-2 text-sm">
          <span className="font-medium">{t("resource.folderLocation.serverPath")}</span>
          <textarea
            readOnly
            className="w-full resize-none rounded-lg border border-default-200 bg-default-100 p-3 font-mono text-sm"
            rows={3}
            value={path}
            onFocus={(event) => event.currentTarget.select()}
          />
        </label>
        <div className="flex flex-col items-start gap-2">
          <Button color="primary" startContent={<CopyOutlined />} onPress={copyPath}>
            {t("resource.folderLocation.copyPath")}
          </Button>
          <div aria-live="polite" className="text-xs">
            {copyFeedback}
          </div>
          <p className="text-xs text-foreground-500">
            {t("resource.folderLocation.serverPathHint")}
          </p>
        </div>
        <p className="text-sm text-foreground-500">{t("resource.folderLocation.desktopHint")}</p>
      </div>
    </Modal>
  );
};

export default FolderLocationModal;
