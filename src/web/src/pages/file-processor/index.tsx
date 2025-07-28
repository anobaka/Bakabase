"use client";

import React, { useEffect, useReducer, useRef, useState } from "react";
import "./index.scss";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";

import RootTreeEntryPage from "./RootTreeEntry";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type RootEntry from "@/core/models/FileExplorer/RootEntry";

import { buildLogger } from "@/components/utils";
import { useFileSystemOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

const log = buildLogger("FileProcessor");
const FileProcessorPage = () => {
  const { t } = useTranslation();

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [root, setRoot] = useState<RootEntry>();
  const rootRef = useRef<RootEntry>();
  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const selectedEntriesRef = useRef<Entry[]>([]);
  const [allSelected, setAllSelected] = useState(false);

  const options = useFileSystemOptionsStore((state) => state.data);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  useUpdateEffect(() => {
    if (rootRef.current) {
      rootRef.current!.dispose();
    }
    rootRef.current = root;
    console.log("Root change to", root);
  }, [root]);

  useEffect(() => {
    if (!rootPathInitialized && options.initialized) {
      const p = options.fileProcessor?.workingDirectory;

      if (p) {
        setRootPath(p);
      }
    }
    setRootPathInitialized(true);
  }, [options.initialized, rootPathInitialized]);

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

  return (
    <div className={"file-explorer-page"}>
      <div className={"file-explorer"}>
        <div className="root relative overflow-hidden" tabIndex={0}>
          <div className={"absolute top-0 left-0 w-full h-full flex flex-col"}>
            {rootPathInitialized && (
              <RootTreeEntryPage
                expandable
                capabilities={[
                  "decompress",
                  "wrap",
                  "move",
                  "extract",
                  "delete",
                  "rename",
                  "delete-all-by-name",
                  "group",
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
                      ...options,
                      fileProcessor: {
                        ...(options.fileProcessor || {}),
                        workingDirectory: v,
                      },
                    });
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
