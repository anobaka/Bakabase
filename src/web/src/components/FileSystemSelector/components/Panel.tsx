"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { FileExplorerRef } from "@/components/FileExplorer";
import type { FileSystemSelectorProps } from "@/components/FileSystemSelector/models";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { FolderAddOutlined } from "@ant-design/icons";
import { useUpdate } from "react-use";

import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import { IwFsType } from "@/sdk/constants";
import { Button, Chip } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";

const log = buildLogger("FileSystemSelector");
const Panel = (props: FileSystemSelectorProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

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
  const [currentDirPath, setCurrentDirPath] = useState<string>();
  const rootRef = useRef<FileExplorerRef | null>(null);

  useEffect(() => {
    // return () => {
    //   log('disposing', rootRef);
    //   rootRef.current?.root?.dispose();
    // };
  }, []);

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

  return (
    <div className={"flex flex-col gap-2 grow max-h-full"}>
      <FileExplorer
        ref={(r) => {
          rootRef.current = r;
          log("ref", r);
        }}
        capabilities={["rename", "select"]}
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
            if (rootRef.current.root.isDirectory) {
              setCurrentDirPath(rootRef.current.root.path);
            }
          }
        }}
        onSelected={(es) => {
          log(rootRef.current);

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
      <div className="flex items-center justify-between mb-2">
        <Button
          // size={'small'}
          isDisabled={!currentDirPath}
          onClick={() => {
            BApi.file.createDirectory({ parent: currentDirPath });
          }}
        >
          <FolderAddOutlined className={"text-base"} />
          {t<string>("New Folder")}
        </Button>
        <div className="flex items-center gap-2">
          <Button
            color={"primary"}
            // size={'small'}
            disabled={!hasSelection}
            onClick={() => {
              if (multiple) {
                onMultipleSelected?.(selectedMany);
              } else {
                onSelected?.(selected!);
              }
            }}
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
