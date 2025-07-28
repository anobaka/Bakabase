"use client";

import type { FC } from "react";

import { useTranslation } from "react-i18next";

import { Button, Chip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

type Props = {
  downloadPath?: string;
  onChange?: (downloadPath?: string) => void;
};

const DownloadPathSelector: FC<Props> = ({ downloadPath, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  return (
    <div className="flex items-center gap-2">
      <Chip radius="sm">{t<string>("Download path")}</Chip>
      <div>
        <Button
          color={"primary"}
          size={"sm"}
          variant={"light"}
          onPress={() => {
            createPortal(FileSystemSelectorModal, {
              onSelected: (e) => {
                onChange?.(e.path);
              },
              targetType: "folder",
              startPath: downloadPath,
              defaultSelectedPath: downloadPath,
            });
          }}
        >
          {downloadPath ?? t<string>("Select download path")}
        </Button>
      </div>
    </div>
  );
};

export default DownloadPathSelector;
