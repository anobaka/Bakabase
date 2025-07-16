"use client";

import React, { useEffect } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

type Options = {
  downloader?: {
    defaultPath?: string;
  };
};

type Props = {
  options: Options;
  downloadPath?: string;
  onChange?: (downloadPath?: string) => void;
};

export default ({
  options,
  downloadPath: propsDownloadPath,
  onChange,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const downloadPath = propsDownloadPath ?? options.downloader?.defaultPath;

  useEffect(() => {
    if (
      !propsDownloadPath &&
      options.downloader?.defaultPath
    ) {
      onChange?.(options.downloader.defaultPath);
    }
  }, [options]);

  return (
    <>
      <div>{t<string>("Download path")}</div>
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
    </>
  );
};
