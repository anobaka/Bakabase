"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerEntryProps } from "../../FileExplorerEntry";

import {
  ApartmentOutlined,
  FileZipOutlined,
  SendOutlined,
} from "@ant-design/icons";
import React from "react";
import { useTranslation } from "react-i18next";

import DecompressBalloon from "../DecompressBalloon";

import { IwFsEntryAction } from "@/core/models/FileExplorer/Entry";
import { Button, Kbd } from "@/components/bakaui";
import WrapModal from "../WrapModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";
import BApi from "@/sdk/BApi";
import FolderSelector from "@/components/FolderSelector";

type Props = {
  entry: Entry;
} & Pick<FileExplorerEntryProps, "capabilities">;
const RightOperations = ({ entry, capabilities }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { actions } = entry;
  const isDecompressible =
    capabilities?.includes("decompress") &&
    actions.includes(IwFsEntryAction.Decompress);
  const isWrappable = capabilities?.includes("wrap") && entry.isDirectory;
  const isMovable = capabilities?.includes("move");

  return (
    <>
      {isDecompressible && (
        <DecompressBalloon
          key={"decompress"}
          entry={entry}
          passwords={entry.passwordsForDecompressing}
          trigger={
            <Button size={"sm"} variant={"ghost"}>
              <FileZipOutlined className={"text-sm"} />
              {t<string>("Decompress")}
            </Button>
          }
        />
      )}
      {isWrappable && (
        <Button
          key={"wrap"}
          size={"sm"}
          variant={"ghost"}
          onClick={() => {
            createPortal(WrapModal, {
              entries: [entry],
            });
          }}
        >
          <ApartmentOutlined className={"text-sm"} />
          {t<string>("Wrap")}
        </Button>
      )}
      {isMovable && (
        <Button
          key={"move"}
          size={"sm"}
          variant={"ghost"}
          onClick={(e) => {
            createPortal(FolderSelector, {
              sources: ['media library', 'custom'],
              onSelect: (path: string) => {
                return BApi.file.moveEntries({
                  destDir: path,
                  entryPaths: [entry.path],
                });
              },
            });
          }}
        >
          <SendOutlined className={"text-sm"} />
          {t<string>("Move")}
        </Button>
      )}
    </>
  );
};

RightOperations.displayName = "RightOperations";

export default RightOperations;
