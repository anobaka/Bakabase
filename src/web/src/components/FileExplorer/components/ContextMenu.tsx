"use client";

"use strict";
import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerEntryProps } from "../FileExplorerEntry";

import { MenuItem } from "@szhsin/react-menu";
import React from "react";
import {
  CopyOutlined,
  DeleteColumnOutlined,
  DeleteOutlined,
  DownOutlined,
  FolderAddOutlined,
  FolderOpenOutlined,
  GroupOutlined,
  LoginOutlined,
  MergeOutlined,
  SendOutlined,
  UpOutlined,
  UploadOutlined,
} from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { MdDriveFileRenameOutline } from "react-icons/md";
import { AiOutlineFileZip } from "react-icons/ai";

import { IwFsEntryAction } from "@/core/models/FileExplorer/Entry";
import BApi from "@/sdk/BApi";
import { IwFsType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ExtractModal from "./ExtractModal";
import WrapModal from "./WrapModal";
import DeleteConfirmationModal from "./DeleteConfirmationModal";
import DeleteItemsWithSameNamesModal from "./DeleteItemsWithSameNamesModal";
import GroupModal from "./GroupModal";
import FileNameModifierModal from "@/components/FileNameModifierModal";
import { toast } from "@/components/bakaui";
import FolderSelector from "@/components/FolderSelector";
import BulkDecompressionToolModal from "@/components/BulkDecompressionToolModal";
import CreateDirectoryModal from "./CreateDirectoryModal";

type Props = {
  selectedEntries: Entry[];
  root?: Entry;
  renderExtraContextMenuItems?: (entries: Entry[]) => React.ReactNode;
  onChangeWorkingDirectory?: (path: string) => void | Promise<void>;
} & Pick<FileExplorerEntryProps, "capabilities">;

type Item = {
  icon: any;
  label: string;
  onClick: () => any;
};
const ContextMenu = ({ selectedEntries, capabilities, root, renderExtraContextMenuItems, onChangeWorkingDirectory }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const items: Item[] = [];

  if (selectedEntries.length > 0) {
    const decompressableEntries = selectedEntries.filter((x) =>
      x.actions.includes(IwFsEntryAction.Decompress),
    );
    const expandableEntries = selectedEntries.filter((x) => x.expandable && !x.expanded);
    const collapsableEntries = selectedEntries.filter((x) => x.expandable && x.expanded);
    const directoryEntries = selectedEntries.filter((e) => e.type == IwFsType.Directory);

    // Open in system file manager (single entry only)
    if (selectedEntries.length === 1) {
      const entry = selectedEntries[0];
      items.push({
        icon: <FolderOpenOutlined className={"text-base"} />,
        label: t<string>("Open in system file manager"),
        onClick: () => {
          BApi.tool.openFileOrDirectory({
            path: entry.path,
            openInDirectory: entry.type != IwFsType.Directory,
          });
        },
      });
    }

    // Enter directory (change working directory) - only for single directory selection
    if (selectedEntries.length === 1 && selectedEntries[0].isDirectoryOrDrive && onChangeWorkingDirectory) {
      items.push({
        icon: <LoginOutlined className={"text-base"} />,
        label: t<string>("Enter directory"),
        onClick: () => {
          onChangeWorkingDirectory(selectedEntries[0].path);
        },
      });
    }

    // Create new folder - only for single directory selection
    if (capabilities?.includes("create-directory") && selectedEntries.length === 1 && selectedEntries[0].isDirectoryOrDrive) {
      items.push({
        icon: <FolderAddOutlined className={"text-base"} />,
        label: t<string>("Create new folder"),
        onClick: () => {
          createPortal(CreateDirectoryModal, {
            parentPath: selectedEntries[0].path,
          });
        },
      });
    }

    if (expandableEntries.length > 0) {
      items.push({
        icon: <DownOutlined className={"text-base"} />,
        label: t<string>(selectedEntries.length == 1 ? "Expand" : "Expand selected"),
        onClick: () => {
          for (const entry of expandableEntries) {
            entry.expand(false);
          }
        },
      });
    }

    if (collapsableEntries.length > 0) {
      items.push({
        icon: <UpOutlined className={"text-base"} />,
        label: t<string>(selectedEntries.length == 1 ? "Collapse" : "Collapse selected"),
        onClick: () => {
          for (const entry of collapsableEntries) {
            entry.collapse();
          }
        },
      });
    }

    if (capabilities?.includes("decompress") && decompressableEntries.length > 0) {
      items.push({
        icon: <CopyOutlined className={"text-base"} />,
        label: t<string>("Decompress {{count}} files", {
          count: decompressableEntries.length,
        }),
        onClick: () => {
          toast.default(t<string>("Start decompressing"));
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

    if (capabilities?.includes("delete-all-same-name")) {
      items.push({
        icon: <DeleteColumnOutlined className={"text-base"} />,
        label: t<string>("Delete items with the same names"),
        onClick: () => {
          createPortal(DeleteItemsWithSameNamesModal, {
            entries: selectedEntries,
            workingDirectory: selectedEntries[0]?.root.path ?? "",
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
          createPortal(FolderSelector, {
            onSelect: (path: string) => {
              return BApi.file.moveEntries({
                destDir: path,
                entryPaths: selectedEntries.map((e) => e.path),
              });
            },
            sources: ["media library", "custom"],
          });
        },
      });
    }

    if (capabilities?.includes("group")) {
      const fileEntries = selectedEntries.filter((x) => !x.isDirectoryOrDrive);

      if (fileEntries.length > 1) {
        items.push({
          icon: <GroupOutlined className={"text-base"} />,
          label: t<string>("Auto group {{count}} selected files", {
            count: fileEntries.length,
          }),
          onClick: () => {
            createPortal(GroupModal, {
              entries: fileEntries,
              groupInternal: false,
            });
          },
        });
      }

      const directoryEntries = selectedEntries.filter((e) => e.isDirectory);

      if (directoryEntries.length > 0) {
        items.push({
          icon: <GroupOutlined className={"text-base"} />,
          label: t<string>("Auto group internal items in {{count}} selected directories", {
            count: directoryEntries.length,
          }),
          onClick: () => {
            createPortal(GroupModal, {
              entries: selectedEntries,
              groupInternal: true,
            });
          },
        });
      }
    }

    if (capabilities?.includes("decompress")) {
      // Detect compressed files
      items.push({
        icon: <AiOutlineFileZip className={"text-base"} />,
        label: t<string>("Use bulk decompression tool"),
        onClick: () => {
          createPortal(BulkDecompressionToolModal, { paths: selectedEntries.map((e) => e.path) });
        },
      });
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

  // Create new folder in current working directory when no entries selected
  if (selectedEntries.length === 0 && capabilities?.includes("create-directory") && root?.path) {
    items.push({
      icon: <FolderAddOutlined className={"text-base"} />,
      label: t<string>("Create new folder"),
      onClick: () => {
        createPortal(CreateDirectoryModal, {
          parentPath: root.path,
        });
      },
    });
  }

  return (
    <>
      {items.map((i) => {
        return (
          <MenuItem key={i.label} onClick={i.onClick}>
            <div className={"flex items-center gap-2"}>
              {i.icon}
              {i.label}
            </div>
          </MenuItem>
        );
      })}
      {renderExtraContextMenuItems?.(selectedEntries)}
    </>
  );
};

ContextMenu.displayName = "ContextMenu";

export default ContextMenu;
