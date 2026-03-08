"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { MenuItem } from "@szhsin/react-menu";

import { FileExplorer } from "@/components/FileExplorer";
import type { FileExplorerRef } from "@/components/FileExplorer";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import { useFileSystemOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { Checkbox } from "@/components/bakaui/components/Checkbox";
import AfterFirstPlayOperationsModal from "@/pages/file-processor/components/AfterFirstPlayOperationsModal";
import { MdUnarchive } from "react-icons/md";
import { RiRobot2Line } from "react-icons/ri";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Tooltip } from "@/components/bakaui";
import BulkDecompressionToolModal from "@/components/BulkDecompressionToolModal";
import AiAnalysisModal from "@/pages/file-processor/components/AiAnalysisModal";

const FileProcessorPage = () => {
  const { t } = useTranslation();

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);
  const fileExplorerRef = useRef<FileExplorerRef>(null);

  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    if (!rootPathInitialized && optionsStore.initialized) {
      const p = optionsStore.data.fileProcessor?.workingDirectory;

      if (p) {
        setRootPath(p);
      }
      setRootPathInitialized(true);
    }
  }, [rootPathInitialized, optionsStore.initialized]);

  useEffect(() => {
    fpOptionsRef.current = optionsStore.data?.fileProcessor;
  }, [optionsStore]);

  const getFirstLevelPaths = useCallback((): string[] => {
    const root = fileExplorerRef.current?.root;
    if (!root?.children) return [];
    return root.children.map((c) => c.path);
  }, []);

  const openAiAnalysis = useCallback((directoryPath: string, filePaths: string[]) => {
    createPortal(AiAnalysisModal, {
      directoryPath,
      filePaths,
    });
  }, [createPortal]);

  const renderExtraContextMenuItems = useCallback(
    (entries: Entry[]) => {
      if (entries.length === 0) return null;

      return (
        <MenuItem
          onClick={() => {
            const paths = entries.map((e) => e.path);
            // Use the first directory entry's path, or the parent directory
            const dirPath = entries.find((e) => e.isDirectoryOrDrive)?.path ?? rootPath ?? "";
            openAiAnalysis(dirPath, paths);
          }}
        >
          <div className="flex items-center gap-2">
            <RiRobot2Line className="text-base" />
            {t("fileProcessor.ai.title")}
          </div>
        </MenuItem>
      );
    },
    [rootPath, openAiAnalysis, t],
  );

  return (
    <div className={"file-explorer-page"}>
      <div className={"file-explorer flex flex-col gap-0"}>
        <div className="flex items-center justify-between">
          <div />
          <div className="flex items-center gap-2">
            {rootPath && (
              <>
                <Button size="sm" variant="flat" onPress={() => {
                  openAiAnalysis(rootPath, getFirstLevelPaths());
                }}>
                  <RiRobot2Line className="text-lg" />
                  {t("fileProcessor.ai.title")}
                </Button>
                <Button size="sm" variant="flat" onPress={() => {
                  createPortal(BulkDecompressionToolModal, { paths: [rootPath] });
                }}>
                  <MdUnarchive className="text-lg" />
                  {t("fileProcessor.action.bulkDecompression")}
                </Button>
              </>
            )}
            <Tooltip
              content={t("fileProcessor.tip.operationsAfterPlay")}
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
                {t("fileProcessor.label.showOperationsAfterPlay")}
              </Checkbox>
            </Tooltip>
          </div>
        </div>
        <div className="root relative overflow-hidden min-h-0 grow" tabIndex={0}>
          <div className={"absolute top-0 left-0 w-full h-full flex flex-col"}>
            {rootPathInitialized && (
              <FileExplorer
                ref={fileExplorerRef}
                expandable
                afterPlayedFirstFile={(entry) => {
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
                  "create-directory"
                ]}
                rootPath={rootPath}
                selectable={"multiple"}
                renderExtraContextMenuItems={renderExtraContextMenuItems}
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

export default FileProcessorPage;
