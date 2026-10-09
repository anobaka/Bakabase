"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerRef } from "@/components/FileExplorer";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderAddOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import { IwFsType } from "@/sdk/constants";
import { Button, Chip } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";
import { storageError, validateUserStoragePaths } from "@/stores/userStorage";

const log = buildLogger("FileSystemSelector");
const Panel = (props: FileSystemSelectorProps) => {
  const { t } = useTranslation();

  const {
    startPath,
    targetType,
    onSelected,
    onMultipleSelected,
    onCancel,
    filter: propsFilter = (e) => true,
    defaultSelectedPath,
    multiple = false,
  } = props;

  const [selected, setSelected] = useState<Entry>();
  const [selectedMany, setSelectedMany] = useState<Entry[]>([]);
  const [explorerSelection, setExplorerSelection] = useState<Entry[]>([]);
  const [currentDirPath, setCurrentDirPath] = useState<string>();
  const rootRef = useRef<FileExplorerRef | null>(null);
  const validating = useRef(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const creating = useRef(false);
  const [creatingFolder, setCreatingFolder] = useState(false);

  const filter = (e: Entry, mode: "visible" | "select") => {
    log("filter", e, mode, targetType);
    if (!e.path) {
      return false;
    }
    if (targetType) {
      switch (targetType) {
        case "file":
          if (
            ![
              IwFsType.Audio,
              IwFsType.CompressedFilePart,
              IwFsType.CompressedFileEntry,
              IwFsType.Image,
              IwFsType.Unknown,
              IwFsType.Video,
            ].includes(e.type)
          ) {
            if (mode == "select") {
              return false;
            } else {
              if (e.type != IwFsType.Directory && e.type != IwFsType.Drive) {
                return false;
              }
            }
          }
          break;
        case "folder":
          if (e.type != IwFsType.Directory && e.type != IwFsType.Drive) {
            return false;
          }
          break;
      }
    }
    if (propsFilter && !propsFilter(e)) {
      return false;
    }

    return true;
  };

  log("selected", selected);

  const trySelectRootOrClearSelection = () => {
    if (rootRef.current?.root && filter(rootRef.current.root, "select")) {
      if (multiple) {
        setSelectedMany([rootRef.current.root]);
      } else {
        setSelected(rootRef.current.root);
      }
    } else {
      if (multiple) {
        setSelectedMany([]);
      } else {
        setSelected(undefined);
      }
    }
  };

  const hasSelection = multiple ? selectedMany.length > 0 : !!selected;
  const selectedDirectory =
    explorerSelection.length === 1 &&
    [IwFsType.Directory, IwFsType.Drive].includes(explorerSelection[0].type)
      ? explorerSelection[0]
      : undefined;
  const newFolderParentPath = selectedDirectory?.path || currentDirPath;
  const createFolder = async () => {
    if (!newFolderParentPath || creating.current) return;
    creating.current = true;
    setCreatingFolder(true);
    setError(undefined);
    try {
      const response = await BApi.file.createDirectory(
        { parent: newFolderParentPath },
        { showErrorToast: false },
      );

      if (response.code) throw storageError(response, t);
    } catch (cause) {
      setError(storageError(cause, t).message || t("fileSystemSelector.error.createFolder"));
    } finally {
      creating.current = false;
      setCreatingFolder(false);
    }
  };
  const confirmSelection = async () => {
    if (!hasSelection || validating.current) return;
    validating.current = true;
    setBusy(true);
    setError(undefined);
    const entries = multiple ? selectedMany : [selected!];

    try {
      await validateUserStoragePaths(entries.map((entry) => entry.path));
      if (multiple) onMultipleSelected?.(entries);
      else onSelected?.(entries[0]);
    } catch (cause) {
      setError(storageError(cause, t).message || t("fileExplorer.storage.pathRejected"));
    } finally {
      validating.current = false;
      setBusy(false);
    }
  };

  return (
    <div className={"flex flex-col gap-2 grow max-h-full"}>
      <FileExplorer
        ref={(r) => {
          rootRef.current = r;
          log("ref", r);
        }}
        capabilities={["rename", "delete", "create-directory", "select", "enter-directory"]}
        defaultSelectedPath={defaultSelectedPath}
        filter={{
          custom: (e) => filter(e, "visible"),
        }}
        rootPath={startPath}
        selectable={multiple ? "multiple" : "single"}
        onInitialized={() => {
          log("onInitialized", rootRef.current?.root);
          if (rootRef.current?.root) {
            trySelectRootOrClearSelection();
            if (rootRef.current.root.isDirectoryOrDrive) {
              setCurrentDirPath(rootRef.current.root.path);
            } else setCurrentDirPath(undefined);
          }
        }}
        onSelected={(es) => {
          log(rootRef.current);
          setExplorerSelection(es);

          if (multiple) {
            const valid = es.filter((e) => filter(e, "select"));

            setSelectedMany(valid);
          } else {
            const e = es[0];

            if (e) {
              if (filter(e, "select")) {
                setSelected(e);
              } else {
                setSelected(undefined);
              }
            } else {
              trySelectRootOrClearSelection();
            }
          }
        }}
      />
      {!multiple && selected && (
        <div className="flex items-center gap-2">
          <Chip color={"success"} radius={"sm"} size={"sm"} variant={"light"}>
            {t<string>("Selected")}
          </Chip>
          <Chip
            className={"whitespace-break-spaces h-auto"}
            color={"success"}
            radius={"sm"}
            size={"sm"}
            variant={"light"}
          >
            {selected.path}
          </Chip>
        </div>
      )}
      {multiple && selectedMany.length > 0 && (
        <div className="flex flex-wrap items-start gap-2 max-h-32 overflow-auto">
          <Chip color={"success"} radius={"sm"} size={"sm"} variant={"light"}>
            {t<string>("Selected")} ({selectedMany.length})
          </Chip>
          {selectedMany.map((e) => (
            <Chip
              key={e.path}
              className={"whitespace-break-spaces h-auto"}
              color={"success"}
              radius={"sm"}
              size={"sm"}
              variant={"light"}
            >
              {e.path}
            </Chip>
          ))}
        </div>
      )}
      {error && (
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      <div className="flex items-center justify-between mb-2">
        <Button
          isDisabled={!newFolderParentPath || creatingFolder}
          isLoading={creatingFolder}
          title={newFolderParentPath}
          onClick={() => void createFolder()}
        >
          <FolderAddOutlined aria-hidden className={"text-base"} />
          {t<string>("fileSystemSelector.action.newFolder")}
        </Button>
        <div className="flex items-center gap-2">
          <Button
            color={"primary"}
            // size={'small'}
            disabled={!hasSelection || busy}
            onClick={() => void confirmSelection()}
          >
            {t<string>("OK")}
          </Button>
          <Button
            // size={'small'}
            onClick={() => {
              onCancel?.();
            }}
          >
            {t<string>("Cancel")}
          </Button>
        </div>
      </div>
    </div>
  );
};

Panel.displayName = "Panel";

export default Panel;
