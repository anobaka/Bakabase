"use client";

import React, { useEffect, useReducer, useRef, useState } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import RootTreeEntry from "./RootTreeEntry";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type RootEntry from "@/core/models/FileExplorer/RootEntry";

import { buildLogger } from "@/components/utils";
import { useFileSystemOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { Checkbox } from "@/components/bakaui/components/Checkbox";
import FolderSelector from "@/components/FolderSelector";
import AfterFirstPlayOperationsModal from "@/pages/file-processor/components/AfterFirstPlayOperationsModal";
import { MdUnarchive } from "react-icons/md";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Tooltip } from "@/components/bakaui";
import BulkDecompressionToolModal from "@/components/BulkDecompressionToolModal";

const log = buildLogger("FileProcessor");
const FileProcessorPage = () => {
  const { t } = useTranslation();

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [root, setRoot] = useState<RootEntry>();
  const rootRef = useRef<RootEntry>();
  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const selectedEntriesRef = useRef<Entry[]>([]);
  const [allSelected, setAllSelected] = useState(false);

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);

  const { createPortal } = useBakabaseContext();

  useUpdateEffect(() => {
    if (rootRef.current) {
      rootRef.current!.dispose();
    }
    rootRef.current = root;
    console.log("Root change to", root);
  }, [root]);

  useEffect(() => {
    if (!rootPathInitialized && optionsStore.initialized) {
      const p = optionsStore.data.fileProcessor?.workingDirectory;

      // console.log(123, p, optionsStore);

      if (p) {
        setRootPath(p);
      }
      setRootPathInitialized(true);
    }
  }, [rootPathInitialized, optionsStore.initialized]);

  useEffect(() => {
    fpOptionsRef.current = optionsStore.data?.fileProcessor;
  }, [optionsStore]);

  useEffect(() => {
    return () => {
      rootRef.current?.dispose();
    };
  }, []);

  useEffect(() => {
    selectedEntriesRef.current = selectedEntries;
    console.log("SelectedEntries changed", selectedEntries);
    const filteredEntries = rootRef.current?.filteredChildren || [];

    console.log("Current selection", selectedEntries);
    // console.log(selectedEntries, filteredEntries);
    if (selectedEntries.length == filteredEntries.length) {
      for (const se of selectedEntries) {
        let exist = false;

        for (const re of filteredEntries) {
          if (se == re) {
            exist = true;
          }
        }
        if (!exist) {
          console.log(se);
        }
      }
      if (
        selectedEntries.length > 0 &&
        selectedEntries.every((e) => filteredEntries.some((a) => a == e))
      ) {
        console.log("all selected");
        setAllSelected(true);
      } else {
        setAllSelected(false);
      }
    } else {
      setAllSelected(false);
    }
  }, [selectedEntries]);

  useEffect(() => {
    console.log("set all selected", allSelected);
  }, [allSelected]);

  // console.log('render all selected', allSelected);

  console.log('root path', rootPath, 'root ref', rootRef);

  return (
    <div className={"file-explorer-page"}>
      <div className={"file-explorer flex flex-col gap-0"}>
        <div className="flex items-center justify-between">
          <div />
          <div className="flex items-center gap-2">
            {rootPath && (
              <Button size="sm" variant="flat" onPress={() => {
                createPortal(BulkDecompressionToolModal, { paths: [rootPath] });
              }}>
                <MdUnarchive className="text-lg" />
                {t("Bulk decompression all decompressed files in current folder")}
              </Button>
            )}
            <Tooltip
              content={t("Usually this will work fine if you are categorizing files.")}
              placement="bottom"
            >
              <Checkbox
                size="sm"
                isSelected={optionsStore.data?.fileProcessor?.showOperationsAfterPlayingFirstFile}
                onValueChange={(v) => {
                  BApi.options.patchFileSystemOptions({
                    ...optionsStore.data,
                    fileProcessor: {
                      ...optionsStore.data?.fileProcessor,
                      showOperationsAfterPlayingFirstFile: v,
                      workingDirectory: optionsStore.data?.fileProcessor?.workingDirectory ?? "",
                    },
                  });
                }}
              >
                {t("Show operations after playing first file")}
              </Checkbox>
            </Tooltip>
          </div>
        </div>
        <div className="root relative overflow-hidden min-h-0 grow" tabIndex={0}>
          <div className={"absolute top-0 left-0 w-full h-full flex flex-col"}>
            {rootPathInitialized && (
              <RootTreeEntry
                expandable
                afterPlayedFirstFile={(entry) => {
                  // console.log(
                  //   "afterPlayedFirstFile",
                  //   optionsStore.data?.fileProcessor?.showOperationsAfterPlayingFirstFile,
                  // );
                  if (fpOptionsRef.current?.showOperationsAfterPlayingFirstFile) {
                    createPortal(AfterFirstPlayOperationsModal, {
                      entry,
                    });
                  }
                }}
                capabilities={[
                  "select",
                  "multi-select",
                  "range-select",
                  "decompress",
                  "wrap",
                  "move",
                  "extract",
                  "delete",
                  "rename",
                  "delete-all-same-name",
                  "group",
                  "play-first-file",
                ]}
                rootPath={rootPath}
                selectable={"multiple"}
                onDoubleClick={(evt, en) => {
                  if (!en.isDirectoryOrDrive) {
                    BApi.tool.openFile({ path: en.path });

                    return false;
                  }

                  return true;
                }}
                onInitialized={(v) => {
                  if (v != undefined) {
                    BApi.options.patchFileSystemOptions({
                      fileProcessor: {
                        ...(fpOptionsRef.current ?? { showOperationsAfterPlayingFirstFile: false }),
                        workingDirectory: v,
                      },
                    });
                    setRootPath(v);
                  }
                }}
              />
            )}
          </div>
        </div>
      </div>
    </div>
  );
};

FileProcessorPage.displayName = "FileProcessorPage";

// todo: optimize
export default FileProcessorPage;
