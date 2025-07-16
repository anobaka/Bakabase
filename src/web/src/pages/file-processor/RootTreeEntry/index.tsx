"use client";

import type { TreeEntryProps } from "@/pages/file-processor/TreeEntry";
import type { Entry } from "@/core/models/FileExplorer/Entry";

import React, {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import {
  ArrowLeftOutlined,
  ArrowUpOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  SearchOutlined,
} from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { ControlledMenu, useMenuState } from "@szhsin/react-menu";

import EventListener, { SelectionMode } from "./components/EventListener";
import ContextMenu from "./components/ContextMenu";

import TreeEntry from "@/pages/file-processor/TreeEntry";
import BApi from "@/sdk/BApi";
import {
  buildLogger,
  splitPathIntoSegments,
  standardizePath,
} from "@/components/utils";
import BusinessConstants from "@/components/BusinessConstants";
import RootEntry from "@/core/models/FileExplorer/RootEntry";
import { Button, Chip, Input } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DeleteConfirmationModal from "@/pages/file-processor/RootTreeEntry/components/DeleteConfirmationModal";
import WrapModal from "@/pages/file-processor/RootTreeEntry/components/WrapModal";
import MediaLibraryPathSelectorV2 from "@/components/MediaLibraryPathSelectorV2";
import ExtractModal from "@/pages/file-processor/RootTreeEntry/components/ExtractModal";

type Props = {
  rootPath?: string;
  onSelected?: (entries: Entry[]) => any;
  selectable: "disabled" | "single" | "multiple";
  defaultSelectedPath?: string;
  onInitialized?: (path?: string) => any;
  onDoubleClick?: (event, entry: Entry) => boolean;
} & Pick<TreeEntryProps, "capabilities" | "expandable" | "filter">;

const log = buildLogger("RootTreeEntry");

export type RootTreeEntryRef = { root?: Entry };

const RootTreeEntry = forwardRef<RootTreeEntryRef, Props>(
  (
    {
      rootPath,
      onDoubleClick,
      filter,
      onSelected,
      selectable,
      onInitialized,
      defaultSelectedPath,
      expandable = false,
      capabilities,
    },
    ref,
  ) => {
    const { t } = useTranslation();
    const forceUpdate = useUpdate();
    const { createPortal } = useBakabaseContext();

    const inputBlurHandlerRef = useRef<any>();

    const [root, setRoot] = useState<RootEntry>();
    const rootRef = useRef(root);
    const [inputValue, setInputValue] = useState(rootPath);

    const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
    const selectedEntriesRef = useRef<Entry[]>(selectedEntries);

    const selectionModeRef = useRef<SelectionMode>(SelectionMode.Normal);
    const shiftSelectionStartRef = useRef<Entry>();

    const defaultSelectedPathInitializedRef = useRef(false);

    const [historyRootPaths, setHistoryRootPaths] = useState<
      (string | undefined)[]
    >([]);
    const historyRootPathsRef = useRef(historyRootPaths);

    const [filterInputValue, setFilterInputValue] = useState<string>();

    // Context menu
    const [menuProps, toggleMenu] = useMenuState();
    const [anchorPoint, setAnchorPoint] = useState({
      x: 0,
      y: 0,
    });

    const contextMenuEntryRef = useRef<Entry>();

    const onSelectedRef = useRef(onSelected);

    useUpdateEffect(() => {
      onSelectedRef.current = onSelected;
    }, [onSelected]);

    useUpdateEffect(() => {
      selectedEntriesRef.current = selectedEntries;
      log("Trigger onSelected", selectedEntriesRef.current);
      onSelectedRef.current?.(selectedEntriesRef.current);
    }, [selectedEntries]);

    const initialize = useCallback(
      async (path?: string, addToHistory: boolean = true) => {
        let finalPath = standardizePath(path);

        if (finalPath != undefined && finalPath.length > 0) {
          const isFile = (await BApi.file.checkPathIsFile({ path: finalPath }))
            .data;

          if (isFile) {
            finalPath = splitPathIntoSegments(finalPath)
              .slice(0, -1)
              .join(BusinessConstants.pathSeparator);
          }
        }
        shiftSelectionStartRef.current = undefined;

        if (addToHistory && rootRef.current) {
          const history = historyRootPathsRef.current;

          if (
            history.length == 0 ||
            history[history.length - 1] != rootRef.current.path
          ) {
            setHistoryRootPaths([...history, rootRef.current.path]);
          }
        }

        log("initialize", finalPath, historyRootPathsRef.current);

        setRoot(new RootEntry(finalPath));
      },
      [],
    );

    useEffect(() => {
      initialize(rootPath);

      return () => {
        log("Disposing", rootRef);
        rootRef?.current?.dispose();
      };
    }, []);

    useUpdateEffect(() => {
      rootRef.current?.patchFilter(filter);
    }, [filter]);

    useUpdateEffect(() => {
      setInputValue(root?.path);
      rootRef.current = root;
      rootRef.current?.patchFilter(filter);
      log("root changed", root);
    }, [root]);

    useUpdateEffect(() => {
      historyRootPathsRef.current = historyRootPaths;
    }, [historyRootPaths]);

    useImperativeHandle(ref, (): RootTreeEntryRef => {
      return {
        root,
      };
    }, [root]);

    const ignoreClickEvent = useCallback((target: HTMLElement | null) => {
      let e = target;

      while (e) {
        // console.log(e);
        if (e.role == "dialog" || e.role == "menuitem") {
          console.log("return");

          return true;
        }
        e = e.parentElement;
      }

      return false;
    }, []);

    if (!root) {
      return null;
    }

    const filteredChildrenCount = root?.filteredChildren.length ?? 0;
    const childrenCount = root?.childrenCount ?? 0;

    return (
      <div
        className={"flex flex-col gap-1 max-h-full min-h-0 grow"}
        onClick={(evt) => {
          if (ignoreClickEvent(evt.target as HTMLElement)) {
            return;
          }
          for (const se of selectedEntriesRef.current) {
            se.select(false);
          }
          setSelectedEntries([]);
        }}
      >
        <ControlledMenu
          {...menuProps}
          anchorPoint={anchorPoint}
          className={"file-processor-page-context-menu"}
          onClose={() => {
            contextMenuEntryRef.current = undefined;
            toggleMenu(false);
          }}
          onContextMenu={(e) => {
            e.preventDefault();
            e.stopPropagation();
          }}
        >
          <ContextMenu
            capabilities={capabilities}
            contextMenuEntry={contextMenuEntryRef.current}
            root={root}
            selectedEntries={selectedEntries}
          />
        </ControlledMenu>
        <EventListener
          onClick={(evt) => {
            if (ignoreClickEvent(evt.target as HTMLElement)) {
              return;
            }
            for (const se of selectedEntriesRef.current) {
              se.select(false);
            }
            setSelectedEntries([]);
          }}
          onDelete={() => {
            if (selectedEntriesRef.current.length > 0) {
              createPortal(DeleteConfirmationModal, {
                entries: selectedEntriesRef.current,
                rootPath: rootRef.current?.path,
              });
            }
          }}
          onKeyDown={(key, evt) => {
            switch (key) {
              case "w": {
                if (selectedEntriesRef.current.length > 0) {
                  createPortal(WrapModal, {
                    entries: selectedEntriesRef.current,
                  });
                }
                break;
              }
              case "m": {
                if (selectedEntriesRef.current.length > 0) {
                  createPortal(MediaLibraryPathSelectorV2, {
                    onSelect: (id, path, isLegacyMediaLibrary) => {
                      return BApi.file.moveEntries({
                        destDir: path,
                        entryPaths: selectedEntriesRef.current.map(
                          (e) => e.path,
                        ),
                      });
                    },
                  });
                }
                break;
              }
              case "d": {
                if (selectedEntriesRef.current.length > 0) {
                  BApi.file.decompressFiles({
                    paths: selectedEntriesRef.current.map((e) => e.path),
                  });
                }
                break;
              }
              case "e": {
                if (selectedEntriesRef.current.length > 0) {
                  createPortal(ExtractModal, {
                    entries: selectedEntriesRef.current,
                  });
                }
                break;
              }
              case "a": {
                if (evt.ctrlKey) {
                  let parent: Entry | undefined;

                  for (const se of selectedEntriesRef.current) {
                    if (se.parent) {
                      if (!parent || parent.path.startsWith(se.parent.path)) {
                        parent = se.parent;
                      }
                    }
                  }
                  parent ??= rootRef.current;

                  log("Select all filtered children of entry", parent);

                  if (parent) {
                    const newSelectedEntries: Entry[] = [];

                    for (const c of parent.filteredChildren) {
                      newSelectedEntries.push(c);
                      c.select(true);
                    }
                    const others = selectedEntriesRef.current.filter(
                      (s) => !newSelectedEntries.includes(s),
                    );

                    for (const o of others) {
                      o.select(false);
                    }
                    setSelectedEntries(newSelectedEntries);
                  }
                }
              }
            }
          }}
          onSelectionModeChange={(m) => {
            selectionModeRef.current = m;
            forceUpdate();
          }}
        />
        <div className="flex items-center">
          <Button
            isIconOnly
            isDisabled={historyRootPaths.length == 0}
            radius={"none"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              const newRoot = historyRootPathsRef.current.pop();

              setHistoryRootPaths([...historyRootPathsRef.current]);
              initialize(newRoot, false);
            }}
          >
            <ArrowLeftOutlined className={"text-base"} />
          </Button>
          <Button
            isIconOnly
            isDisabled={!root?.path}
            radius={"none"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              if (root) {
                const segments = splitPathIntoSegments(root.path);
                const isUncPath = root.path.startsWith(
                  BusinessConstants.uncPathPrefix,
                );

                // console.log(root.path, segments, isUncPath);
                if (segments.length > 0 && !isUncPath) {
                  const newRoot = segments
                    .slice(0, segments.length - 1)
                    .join(BusinessConstants.pathSeparator);

                  initialize(newRoot);
                }
              }
            }}
          >
            <ArrowUpOutlined className={"text-base"} />
          </Button>
          <Input
            className={"grow"}
            classNames={{
              inputWrapper: "pl-0",
            }}
            endContent={
              <Chip size={"sm"} variant={"light"}>
                {selectedEntries.length} /{" "}
                {filteredChildrenCount == childrenCount
                  ? childrenCount
                  : `${filteredChildrenCount} / ${childrenCount}`}
              </Chip>
            }
            placeholder={t<string>("You can type a path here")}
            radius={"none"}
            size={"sm"}
            startContent={
              <Button
                isIconOnly
                isDisabled={!root?.path}
                radius={"none"}
                size={"sm"}
                onClick={() => {
                  BApi.tool.openFileOrDirectory({ path: root?.path });
                }}
              >
                <FolderOutlined className={"text-base"} />
              </Button>
            }
            value={inputValue}
            onKeyDown={(evt) => {
              evt.stopPropagation();
            }}
            onValueChange={(v) => {
              const path = standardizePath(v)!;

              setInputValue(path);
              clearInterval(inputBlurHandlerRef.current);
              inputBlurHandlerRef.current = setTimeout(() => {
                initialize(path);
              }, 1000);
            }}
          />
          <Input
            className={"w-1/4"}
            placeholder={t<string>("Filter")}
            radius={"none"}
            size={"sm"}
            startContent={<SearchOutlined className={"text-base"} />}
            value={filterInputValue}
            onValueChange={(v) => setFilterInputValue(v)}
          />
          <Button
            className={"px-6"}
            radius={"none"}
            size={"sm"}
            onClick={() => {
              BApi.file.openRecycleBin();
            }}
          >
            <FolderOpenOutlined className={"text-base"} />
            {t<string>("Recycle bin")}
          </Button>
        </div>
        <div className={"grow min-h-0"}>
          <TreeEntry
            capabilities={capabilities}
            entry={root}
            expandable={expandable}
            filter={{
              keyword: filterInputValue,
            }}
            switchSelective={(e) => {
              if (selectable == "disabled") {
                return false;
              }

              if (
                selectable == "single" ||
                selectionModeRef.current == SelectionMode.Normal
              ) {
                shiftSelectionStartRef.current = e;
                if (selectedEntriesRef.current.includes(e)) {
                  if (selectedEntriesRef.current.length > 1) {
                    for (const se of selectedEntriesRef.current.filter(
                      (x) => x != e,
                    )) {
                      se.select(false);
                    }
                    setSelectedEntries([e]);

                    return false;
                  } else {
                    setSelectedEntries([]);

                    return true;
                  }
                } else {
                  for (const se of selectedEntriesRef.current) {
                    se.select(false);
                  }
                  setSelectedEntries([e]);

                  return true;
                }
              }

              switch (selectionModeRef.current) {
                case SelectionMode.Ctrl: {
                  shiftSelectionStartRef.current = e;
                  if (selectedEntriesRef.current.includes(e)) {
                    selectedEntriesRef.current =
                      selectedEntriesRef.current.filter((x) => x != e);
                  } else {
                    selectedEntriesRef.current.push(e);
                  }
                  setSelectedEntries([...selectedEntriesRef.current]);

                  return true;
                }
                case SelectionMode.Shift: {
                  shiftSelectionStartRef.current ??=
                    e.parent!.filteredChildren![0]!;
                  let startParent = shiftSelectionStartRef.current.parent;
                  const startParentSet = new Set<Entry>();

                  while (startParent) {
                    startParentSet.add(startParent);
                    startParent = startParent.parent;
                  }

                  let endParent = e.parent;

                  while (endParent) {
                    if (startParentSet.has(endParent)) {
                      break;
                    }
                    endParent = endParent.parent;
                  }

                  const buildPreorderTraversal = (
                    entry: Entry,
                    result: Entry[],
                  ) => {
                    result.push(entry);
                    if (entry.expanded && entry.filteredChildren) {
                      for (const child of entry.filteredChildren) {
                        buildPreorderTraversal(child, result);
                      }
                    }
                  };
                  const preorderTraversal: Entry[] = [];

                  buildPreorderTraversal(endParent!, preorderTraversal);
                  const startIdx = preorderTraversal.indexOf(
                    shiftSelectionStartRef.current,
                  );
                  const endIdx = preorderTraversal.indexOf(e);
                  const minIdx = Math.min(startIdx, endIdx);
                  const maxIdx = Math.max(startIdx, endIdx);
                  const newSelectedEntries = preorderTraversal.slice(
                    minIdx,
                    maxIdx + 1,
                  );

                  // console.log(minIdx, maxIdx, endParent);
                  for (let i = minIdx; i <= maxIdx; i++) {
                    const en = preorderTraversal[i]!;

                    en.select(true);
                  }

                  for (const se of selectedEntriesRef.current) {
                    if (!newSelectedEntries.includes(se)) {
                      se.select(false);
                    }
                  }
                  setSelectedEntries(newSelectedEntries);

                  return false;
                }
              }

              return true;
            }}
            onChildrenLoaded={(e) => {
              if (!defaultSelectedPathInitializedRef.current) {
                defaultSelectedPathInitializedRef.current = true;
                const standardDefaultSelectedPath =
                  standardizePath(defaultSelectedPath);

                if (
                  standardDefaultSelectedPath != undefined &&
                  standardDefaultSelectedPath.length > 0
                ) {
                  const selectedRow = e.filteredChildren?.findIndex(
                    (c) => c.path == standardDefaultSelectedPath,
                  );

                  if (selectedRow > -1) {
                    const selected = e.filteredChildren[selectedRow];

                    selected.select(true);
                    // pick a better view
                    const scrollToRow = Math.min(
                      e.filteredChildren.length - 1,
                      selectedRow + 2,
                    );

                    e.ref?.scrollTo(e.filteredChildren[scrollToRow].path);
                    setSelectedEntries([selected]);
                  }
                }
              }
              log("12312321321321321", e);
              onInitialized?.(e?.path);
              forceUpdate();
            }}
            onContextMenu={(evt, entry) => {
              evt.preventDefault();
              setAnchorPoint({
                x: evt.clientX,
                y: evt.clientY,
              });
              contextMenuEntryRef.current = entry;
              toggleMenu(true);
            }}
            onDoubleClick={(evt, en) => {
              if (!onDoubleClick || onDoubleClick(evt, en)) {
                if (en.isDirectoryOrDrive) {
                  initialize(en.path);
                }
              }
            }}
          />
        </div>
      </div>
    );
  },
);

export default RootTreeEntry;
