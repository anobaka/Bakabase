"use client";

import type { ReactNode } from "react";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { ButtonProps } from "@/components/bakaui";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = ButtonProps &
  DestroyableProps & {
    fileSystemSelectorProps: FileSystemSelectorProps;
    wrapPath?: boolean;
  };
const FileSystemSelectorButton = ({ fileSystemSelectorProps, children, wrapPath, ...buttonProps }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [path, setPath] = useState<string | undefined>(fileSystemSelectorProps.defaultSelectedPath);
  const [opening, setOpening] = useState(false);

  let label: ReactNode = path;

  if (!path) {
    if (!children) {
      if (fileSystemSelectorProps.targetType === "folder") {
        label = t<string>("fileSystemSelector.title.selectFolder");
      } else if (fileSystemSelectorProps.targetType === "file") {
        label = t<string>("fileSystemSelector.title.selectFile");
      } else {
        label = t<string>("fileSystemSelector.title.selectEntries");
      }
    } else {
      label = children;
    }
  }

  return (
    <Button
      color="primary"
      size="sm"
      variant="light"
      {...buttonProps}
      isLoading={opening || buttonProps.isLoading}
      onPress={async () => {
        if (opening) return;
        setOpening(true);
        try {
          // The explorer contains workflow actions. Load it on demand so node editors can
          // reuse this picker without a workflow-registry initialization cycle.
          const { default: FileSystemSelectorModal } = await import("./Modal");
          createPortal(FileSystemSelectorModal, {
            ...fileSystemSelectorProps,
            onSelected: (entry) => {
              setPath(entry.path);
              if (fileSystemSelectorProps.onSelected) {
                fileSystemSelectorProps.onSelected(entry);
              }
            },
          });
        } catch {
          toast.danger(t<string>("fileSystemSelector.error.loadPicker"));
        } finally {
          setOpening(false);
        }
      }}
    >
      {wrapPath && path ? <span className="block min-w-0 max-w-full whitespace-normal break-all text-left">{label}</span> : label}
    </Button>
  );
};

FileSystemSelectorButton.displayName = "FileSystemSelectorButton";

export default FileSystemSelectorButton;
