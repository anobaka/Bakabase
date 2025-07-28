"use client";

"use strict";
import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { TreeEntryProps } from "@/pages/file-processor/TreeEntry";

import { MenuItem } from "@szhsin/react-menu";
import React from "react";
import {
  CopyOutlined,
  DeleteColumnOutlined,
  DeleteOutlined,
  GroupOutlined,
  MergeOutlined,
  SendOutlined,
  UploadOutlined,
} from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { MdDriveFileRenameOutline } from "react-icons/md";

import { IwFsEntryAction } from "@/core/models/FileExplorer/Entry";
import BApi from "@/sdk/BApi";
import { IwFsType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ExtractModal from "@/pages/file-processor/RootTreeEntry/components/ExtractModal";
import WrapModal from "@/pages/file-processor/RootTreeEntry/components/WrapModal";
import DeleteConfirmationModal from "@/pages/file-processor/RootTreeEntry/components/DeleteConfirmationModal";
import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";
import DeleteItemsWithSameNamesModal from "@/pages/file-processor/RootTreeEntry/components/DeleteItemsWithSameNamesModal";
import GroupModal from "@/pages/file-processor/RootTreeEntry/components/GroupModal";
import FileNameModifierModal from "@/components/FileNameModifierModal";
import { toast } from "@/components/bakaui";

type Props = {
  selectedEntries: Entry[];
  contextMenuEntry?: Entry;
  root?: Entry;
} & Pick<TreeEntryProps, "capabilities">;

type Item = {
  icon: any;
  label: string;
  onClick: () => any;
};
const ContextMenu = ({ selectedEntries, capabilities, root }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const items: Item[] = [];

  console.log(selectedEntries);

  if (selectedEntries.length > 0) {
    const decompressableEntries = selectedEntries.filter((x) =>
      x.actions.includes(IwFsEntryAction.Decompress),
    );
    const expandableEntries = selectedEntries.filter(
      (x) => x.expandable && !x.expanded,
    );
    const collapsableEntries = selectedEntries.filter(
      (x) => x.expandable && x.expanded,
    );
    const directoryEntries = selectedEntries.filter(
      (e) => e.type == IwFsType.Directory,
    );

    if (expandableEntries.length > 0) {
      items.push({
        icon: <CopyOutlined className={"text-base"} />,
        label: t<string>(
          selectedEntries.length == 1 ? "Expand" : "Expand selected",
        ),
        onClick: () => {
          for (const entry of expandableEntries) {
            entry.expand(false);
          }
        },
      });
    }

    if (collapsableEntries.length > 0) {
      items.push({
        icon: <CopyOutlined className={"text-base"} />,
        label: t<string>(
          selectedEntries.length == 1 ? "Collapse" : "Collapse selected",
        ),
        onClick: () => {
          for (const entry of collapsableEntries) {
            entry.collapse();
          }
        },
      });
    }

    if (
      capabilities?.includes("decompress") &&
      decompressableEntries.length > 0
    ) {
      items.push({
        icon: <CopyOutlined className={"text-base"} />,
        label: t<string>("Decompress {{count}} files", {
          count: decompressableEntries.length,
        }),
        onClick: () => {
          BApi.file.decompressFiles({
            paths: decompressableEntries.map((e) => e.path),
          });
        },
      });
    }

    if (capabilities?.includes("extract") && directoryEntries.length > 0) {
      items.push({
        icon: <UploadOutlined className={"text-base"} />,
        label: t<string>("Extract {{count}} directories", {
          count: directoryEntries.length,
        }),
        onClick: () => {
          createPortal(ExtractModal, { entries: selectedEntries });
        },
      });
    }

    if (capabilities?.includes("wrap")) {
      items.push({
        icon: <MergeOutlined className={"text-base"} />,
        label: t<string>("Wrap {{count}} items using directory", {
          count: selectedEntries.length,
        }),
        onClick: () => {
          createPortal(WrapModal, { entries: selectedEntries });
        },
      });
    }

    if (capabilities?.includes("delete")) {
      items.push({
        icon: <DeleteOutlined className={"text-base"} />,
        label: t<string>("Delete {{count}} items", {
          count: selectedEntries.length,
        }),
        onClick: () => {
          createPortal(DeleteConfirmationModal, {
            entries: selectedEntries,
            rootPath: root?.path,
          });
        },
      });
    }

    if (capabilities?.includes("delete-all-by-name")) {
      items.push({
        icon: <DeleteColumnOutlined className={"text-base"} />,
        label: t<string>("Delete items with the same names"),
        onClick: () => {
          createPortal(DeleteItemsWithSameNamesModal, {
            entries: selectedEntries,
            workingDirectory: selectedEntries[0]!.root.path,
          });
        },
      });
    }

    if (capabilities?.includes("move")) {
      items.push({
        icon: <SendOutlined className={"text-base"} />,
        label: t<string>("Move {{count}} items", {
          count: selectedEntries.length,
        }),
        onClick: () => {
          createPortal(MediaLibraryPathSelectorV2, {
            onSelect: (id, path, isLegacyMediaLibrary) => {
              return BApi.file.moveEntries({
                destDir: path,
                entryPaths: selectedEntries.map((e) => e.path),
              });
            },
          });
        },
      });
    }

    if (capabilities?.includes("group")) {
      const targetEntries = selectedEntries;

      if (targetEntries.length > 1) {
        items.push({
          icon: <GroupOutlined className={"text-base"} />,
          label: t<string>("Auto group {{count}} selected items", {
            count: targetEntries.length,
          }),
          onClick: () => {
            createPortal(GroupModal, {
              entries: selectedEntries,
              groupInternal: false,
            });
          },
        });
      }

      const directoryEntries = selectedEntries.filter((e) => e.isDirectory);

      if (directoryEntries.length > 0) {
        items.push({
          icon: <GroupOutlined className={"text-base"} />,
          label: t<string>(
            "Auto group internal items in {{count}} selected directories",
            { count: directoryEntries.length },
          ),
          onClick: () => {
            createPortal(GroupModal, {
              entries: selectedEntries,
              groupInternal: true,
            });
          },
        });
      }
    }

    items.push({
      icon: <CopyOutlined className={"text-base"} />,
      label: t<string>("Copy {{count}} names", {
        count: selectedEntries.length,
      }),
      onClick: () => {
        navigator.clipboard
          .writeText(selectedEntries.map((e) => e.name).join("\n"))
          .then(() => {
            toast.success(t<string>("Copied"));
          })
          .catch((e) => {
            toast.danger(`${t<string>("Failed to copy")}. ${e}`);
          });
      },
    });

    items.push({
      icon: <CopyOutlined className={"text-base"} />,
      label: t<string>("Copy {{count}} paths", {
        count: selectedEntries.length,
      }),
      onClick: () => {
        navigator.clipboard
          .writeText(selectedEntries.map((e) => e.path).join("\n"))
          .then(() => {
            toast.success(t<string>("Copied"));
          })
          .catch((e) => {
            toast.danger(`${t<string>("Failed to copy")}. ${e}`);
          });
      },
    });

    // 批量修改名称操作
    items.push({
      icon: <MdDriveFileRenameOutline className={"text-base"} />, // 可换为更合适的icon
      label: t<string>("Batch rename {{count}} items", {
        count: selectedEntries.length,
      }),
      onClick: () => {
        createPortal(FileNameModifierModal, {
          initialFilePaths: selectedEntries.map((e) => e.path),
        });
      },
    });
  }

  return (
    <>
      {items.map((i) => {
        return (
          <MenuItem onClick={i.onClick}>
            <div className={"flex items-center gap-2"}>
              {i.icon}
              {i.label}
            </div>
          </MenuItem>
        );
      })}
    </>
  );
};

ContextMenu.displayName = "ContextMenu";

export default ContextMenu;
