"use client";

import React, { useEffect, useRef, useState } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";

import { FileExplorer } from "@/components/FileExplorer";

import { useFileSystemOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { Checkbox } from "@/components/bakaui/components/Checkbox";
import AfterFirstPlayOperationsModal from "@/pages/file-processor/components/AfterFirstPlayOperationsModal";
import { MdUnarchive } from "react-icons/md";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Button, Tooltip } from "@/components/bakaui";
import BulkDecompressionToolModal from "@/components/BulkDecompressionToolModal";

const FileProcessorPage = () => {
  const { t } = useTranslation();

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);

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
                {t("fileProcessor.action.bulkDecompression")}
              </Button>
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
