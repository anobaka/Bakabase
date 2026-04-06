"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerEntryProps } from "../../FileExplorerEntry";

import { FolderAddOutlined, FolderOpenOutlined, LoginOutlined, SyncOutlined, UploadOutlined } from "@ant-design/icons";
import React from "react";
import { useTranslation } from "react-i18next";

import OperationButton from "../OperationButton";

import BApi from "@/sdk/BApi";
import { IwFsType } from "@/sdk/constants";
import { Button, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ExtractModal from "../ExtractModal";
import CreateDirectoryModal from "../CreateDirectoryModal";

type Props = {
  entry: Entry;
  onEnterDirectory?: (path: string) => void;
} & Pick<FileExplorerEntryProps, "capabilities">;
const TailingOperations = (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { entry, capabilities, onEnterDirectory } = props;

  const isExtractable =
    capabilities?.includes("extract") &&
    entry.isDirectory &&
    entry.childrenCount &&
    entry.childrenCount > 0;

  return (
    <>
      <OperationButton
        isIconOnly
        onClick={(e) => {
          e.stopPropagation();
          BApi.tool.openFileOrDirectory({
            path: entry.path,
            openInDirectory: entry.type != IwFsType.Directory,
          });
        }}
      >
        <FolderOpenOutlined className={"text-base"} />
      </OperationButton>
      {entry.isDirectoryOrDrive && onEnterDirectory && (
        <Tooltip content={t<string>("fileExplorer.action.enterDirectory")} placement={"top"}>
          <Button
            isIconOnly
            className={"w-auto h-auto p-1 min-w-fit opacity-60 hover:opacity-100"}
            variant={"light"}
            onClick={(e) => {
              e.stopPropagation();
              onEnterDirectory(entry.path);
            }}
          >
            <LoginOutlined className={"text-base"} />
          </Button>
        </Tooltip>
      )}
      {isExtractable && (
        <Tooltip
          content={`(E)${t<string>("fileExplorer.tip.extractChildrenToParent")}`}
          placement={"top"}
        >
          <Button
            isIconOnly
            className={"w-auto h-auto p-1 min-w-fit opacity-60 hover:opacity-100"}
            variant={"light"}
            onClick={(e) => {
              e.stopPropagation();
              createPortal(ExtractModal, {
                entries: [entry],
              });
            }}
          >
            <UploadOutlined className={"text-base"} />
          </Button>
        </Tooltip>
      )}
      {entry.isDirectoryOrDrive && capabilities?.includes("create-directory") && (
        <Tooltip content={`(N)${t<string>("fileExplorer.contextMenu.createNewFolder")}`} placement={"top"}>
          <Button
            isIconOnly
            className={"w-auto h-auto p-1 min-w-fit opacity-60 hover:opacity-100"}
            variant={"light"}
            onClick={(e) => {
              e.stopPropagation();
              createPortal(CreateDirectoryModal, {
                parentPath: entry.path,
              });
            }}
          >
            <FolderAddOutlined className={"text-base"} />
          </Button>
        </Tooltip>
      )}
      {entry.isDirectoryOrDrive && (
        <OperationButton
          isIconOnly
          onClick={(e) => {
            entry.ref?.expand(true);
          }}
        >
          <SyncOutlined className={"text-base"} />
        </OperationButton>
      )}
    </>
  );
};

TailingOperations.displayName = "TailingOperations";

export default TailingOperations;
