"use client";

import type { ReactNode } from "react";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { ButtonProps } from "@/components/bakaui";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

type Props = ButtonProps &
  DestroyableProps & {
    fileSystemSelectorProps: FileSystemSelectorProps;
  };

export default ({
  fileSystemSelectorProps,
  children,
  ...buttonProps
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [path, setPath] = useState<string | undefined>(
    fileSystemSelectorProps.defaultSelectedPath,
  );

  let label: ReactNode = path;

  if (!path) {
    if (!children) {
      if (fileSystemSelectorProps.targetType === "folder") {
        label = t<string>("Select folder");
      } else if (fileSystemSelectorProps.targetType === "file") {
        label = t<string>("Select file");
      } else {
        label = t<string>("Select file system entries");
      }
    } else {
      label = children;
    }
  }

  return (
    <Button
      color='primary'
      variant="light"
      size='sm'
      {...buttonProps}
      onPress={() => {
        createPortal(FileSystemSelectorModal, {
          ...fileSystemSelectorProps,
          onSelected: (entry) => {
            setPath(entry.path);
            if (fileSystemSelectorProps.onSelected) {
              fileSystemSelectorProps.onSelected(entry);
            }
          },
        });
      }}
    >
      {label}
    </Button>
  );
};
