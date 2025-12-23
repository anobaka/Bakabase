"use client";

import type { FileExplorerEntryProps } from "./FileExplorerEntry";
import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { Capability } from "./models";

import React, {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from "react";
import { useDebounce, useUpdate, useUpdateEffect } from "react-use";
import {
  ArrowLeftOutlined,
  ArrowUpOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  SearchOutlined,
} from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { ControlledMenu, useMenuState } from "@szhsin/react-menu";
import _ from "lodash";

import EventListener, { SelectionMode } from "./components/EventListener";
import ContextMenu from "./components/ContextMenu";
import { FileSystemTreeEntryCapabilityMap } from "./models";
import Shortcuts from "./components/Shortcuts";

import FileExplorerEntry from "./FileExplorerEntry";
import BApi from "@/sdk/BApi";
import { buildLogger, getStandardParentPath, standardizePath } from "@/components/utils";
import { useFileExplorerClipboardStore } from "@/stores/fileExplorerClipboard";
import RootEntry from "@/core/models/FileExplorer/RootEntry";
import { Button, Chip, Input, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DeleteConfirmationModal from "./components/DeleteConfirmationModal";
import WrapModal from "./components/WrapModal";
import ExtractModal from "./components/ExtractModal";
import FolderSelector from "@/components/FolderSelector";

export type FileExplorerProps = {
  rootPath?: string;
  onSelected?: (entries: Entry[]) => any;
  selectable: "disabled" | "single" | "multiple";
  defaultSelectedPath?: string;
  onInitialized?: (path?: string) => any;
  onDoubleClick?: (event: React.MouseEvent<any>, entry: Entry) => boolean;
  afterPlayedFirstFile?: (entry: Entry) => any;
  renderExtraContextMenuItems?: (entries: Entry[]) => React.ReactNode;
} & Pick<
  FileExplorerEntryProps,
  "capabilities" | "expandable" | "filter" | "renderAfterName" | "renderBeforeRightOperations"
>;

const log = buildLogger("FileExplorer");

export type FileExplorerRef = { root?: Entry };

const FileExplorer = forwardRef<FileExplorerRef, FileExplorerProps>(
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
      afterPlayedFirstFile,
      renderAfterName,
      renderBeforeRightOperations,
      renderExtraContextMenuItems,
    },
    ref,
  ) => {
    const { t } = useTranslation();
    const forceUpdate = useUpdate();
    const { createPortal } = useBakabaseContext();

    const initializedRootPathRef = useRef<string>();

    const inputBlurHandlerRef = useRef<ReturnType<typeof setTimeout>>();

    const [root, setRoot] = useState<RootEntry>();
    const rootRef = useRef(root);
    const [inputValue, setInputValue] = useState(rootPath);

    const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
    const selectedEntriesRef = useRef<Entry[]>(selectedEntries);

    const selectionModeRef = useRef<SelectionMode>(SelectionMode.Normal);
    const shiftSelectionStartRef = useRef<Entry>();

    // Use global store for clipboard state
    const clipboardStore = useFileExplorerClipboardStore();

    const defaultSelectedPathInitializedRef = useRef(false);

    const [historyRootPaths, setHistoryRootPaths] = useState<(string | undefined)[]>([]);
    const historyRootPathsRef = useRef(historyRootPaths);

    const [filterInputValue, setFilterInputValue] = useState<string>();
    const [debouncedFilterValue, setDebouncedFilterValue] = useState<string>();

    useDebounce(
      () => {
        setDebouncedFilterValue(filterInputValue);
      },
      300,
      [filterInputValue]
    );

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

    const initialize = useCallback(async (path?: string, addToHistory: boolean = true) => {
      let finalPath = standardizePath(path);

      if (finalPath != undefined && finalPath.length > 0) {
        const isFile = (await BApi.file.checkPathIsFile({ path: finalPath })).data;

        if (isFile) {
          finalPath = getStandardParentPath(finalPath)!;
        }
      }
      shiftSelectionStartRef.current = undefined;

      if (addToHistory && rootRef.current) {
        const history = historyRootPathsRef.current;

        if (history.length == 0 || history[history.length - 1] != rootRef.current.path) {
          setHistoryRootPaths([...history, rootRef.current.path]);
        }
      }

      log("initialize", finalPath, historyRootPathsRef.current);

      setRoot(new RootEntry(finalPath));
    }, []);

    useEffect(() => {
      initialize(rootPath);

      return () => {
        log("Disposing", rootRef);
        rootRef?.current?.dispose();
        clearTimeout(inputBlurHandlerRef.current);
      };
    }, []);

    // Listen for rootPath changes from parent
    useUpdateEffect(() => {
      initialize(rootPath);
    }, [rootPath]);

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

    useImperativeHandle(ref, (): FileExplorerRef => {
      return {
        root,
      };
    }, [root]);

    const ignoreClickEvent = useCallback((target: HTMLElement | null) => {
      let e = target;

      while (e) {
        if (e.role == "dialog" || e.role == "menuitem") {
          return true;
        }
        e = e.parentElement;
      }

      return false;
    }, []);

    // Memoize filter object to prevent unnecessary re-renders
    // Must be before early return to maintain hooks order
    const filterObj = useMemo(() => ({ keyword: debouncedFilterValue }), [debouncedFilterValue]);

    // Memoize switchSelective to prevent unnecessary re-renders
    // Must be before early return to maintain hooks order
    const switchSelective = useCallback((e: Entry): boolean => {
      if (selectable == "disabled") {
        return false;
      }

      if (selectable == "single" || selectionModeRef.current == SelectionMode.Normal) {
        shiftSelectionStartRef.current = e;
        if (selectedEntriesRef.current.includes(e)) {
          if (selectedEntriesRef.current.length > 1) {
            for (const se of selectedEntriesRef.current.filter((x) => x != e)) {
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
          const newSelectedEntries = selectedEntriesRef.current.includes(e)
            ? selectedEntriesRef.current.filter((x) => x != e)
            : [...selectedEntriesRef.current, e];
          setSelectedEntries(newSelectedEntries);

          return true;
        }
        case SelectionMode.Shift: {
          shiftSelectionStartRef.current ??= e.parent!.filteredChildren![0]!;
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

          const buildPreorderTraversal = (entry: Entry, result: Entry[]) => {
            result.push(entry);
            if (entry.expanded && entry.filteredChildren) {
              for (const child of entry.filteredChildren) {
                buildPreorderTraversal(child, result);
              }
            }
          };
          const preorderTraversal: Entry[] = [];

          buildPreorderTraversal(endParent!, preorderTraversal);
          const startIdx = preorderTraversal.indexOf(shiftSelectionStartRef.current);
          const endIdx = preorderTraversal.indexOf(e);
          const minIdx = Math.min(startIdx, endIdx);
          const maxIdx = Math.max(startIdx, endIdx);
          const newSelectedEntries = preorderTraversal.slice(minIdx, maxIdx + 1);

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
    }, [selectable]);

    if (!root) {
      return null;
    }

    const filteredChildrenCount = root.filteredChildren.length ?? 0;
    const childrenCount = root.childrenCount ?? 0;

    return (
      <div className={"flex flex-col gap-1 max-h-full min-h-0 grow"}>
        <ControlledMenu
          {...menuProps}
          anchorPoint={anchorPoint}
          className={"file-explorer-context-menu"}
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
            root={root}
            selectedEntries={selectedEntries}
            renderExtraContextMenuItems={renderExtraContextMenuItems}
            onChangeWorkingDirectory={initialize}
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
            if (selectedEntriesRef.current.length > 0 && capabilities?.includes("delete")) {
              createPortal(DeleteConfirmationModal, {
                entries: selectedEntriesRef.current,
                rootPath: rootRef.current?.path,
              });
            }
          }}
          onKeyDown={(key, evt) => {
            log("event listener", "key down", key, evt);
            const c = _.keys(FileSystemTreeEntryCapabilityMap).find(
              (k) => FileSystemTreeEntryCapabilityMap[k as Capability].shortcut?.key == key,
            ) as Capability | undefined;

            if (c) {
              evt.stopPropagation();
              evt.preventDefault();
              if (capabilities?.includes(c)) {
                switch (c) {
                  case "wrap":
                    if (selectedEntriesRef.current.length > 0) {
                      createPortal(WrapModal, {
                        entries: selectedEntriesRef.current,
                      });
                    }
                    break;
                  case "extract":
                    if (selectedEntriesRef.current.length > 0) {
                      createPortal(ExtractModal, {
                        entries: selectedEntriesRef.current,
                      });
                    }
                    break;
                  case "move":
                    if (selectedEntriesRef.current.length > 0) {
                      createPortal(FolderSelector, {
                        onSelect: (path: string) => {
                          return BApi.file.moveEntries({
                            destDir: path,
                            entryPaths: selectedEntriesRef.current.map((e) => e.path),
                          });
                        },
                        sources: ["media library", "custom"],
                      });
                    }
                    break;
                  case "delete":
                    break;
                  case "rename":
                    break;
                  case "decompress":
                    if (selectedEntriesRef.current.length > 0) {
                      BApi.file.decompressFiles({
                        paths: selectedEntriesRef.current.map((e) => e.path),
                      });
                    }
                    break;
                  case "delete-all-same-name":
                    break;
                  case "group":
                    break;
                  case "play":
                    break;
                  case "play-first-file":
                    if (selectedEntriesRef.current.length == 1) {
                      selectedEntriesRef.current[0].ref?.playFirstFile();
                    }
                    break;
                }
              }
            } else {
              switch (key) {
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
                  break;
                }
                case "ArrowUp":
                case "ArrowDown": {
                  evt.preventDefault();
                  const lastSelected = selectedEntriesRef.current[selectedEntriesRef.current.length - 1];
                  const parent = lastSelected?.parent ?? rootRef.current;

                  if (parent && parent.filteredChildren.length > 0) {
                    let currentIndex = lastSelected
                      ? parent.filteredChildren.indexOf(lastSelected)
                      : -1;

                    let newIndex: number;
                    if (key === "ArrowUp") {
                      newIndex = currentIndex <= 0 ? parent.filteredChildren.length - 1 : currentIndex - 1;
                    } else {
                      newIndex = currentIndex >= parent.filteredChildren.length - 1 ? 0 : currentIndex + 1;
                    }

                    const newEntry = parent.filteredChildren[newIndex];
                    if (newEntry) {
                      for (const se of selectedEntriesRef.current) {
                        se.select(false);
                      }
                      newEntry.select(true);
                      setSelectedEntries([newEntry]);
                      parent.ref?.scrollTo(newEntry.path);
                    }
                  }
                  break;
                }
                case "Enter": {
                  if (selectedEntriesRef.current.length === 1) {
                    const entry = selectedEntriesRef.current[0];
                    if (entry.isDirectoryOrDrive) {
                      initialize(entry.path);
                    }
                  }
                  break;
                }
                case "c": {
                  // Support both Ctrl (Windows/Linux) and Command (Mac)
                  if ((evt.ctrlKey || evt.metaKey) && selectedEntriesRef.current.length > 0) {
                    evt.preventDefault();
                    const paths = selectedEntriesRef.current.map((e) => e.path);
                    clipboardStore.copy(paths);
                    toast.success(t<string>("Copied {{count}} items", { count: paths.length }));
                  }
                  break;
                }
                case "x": {
                  // Support both Ctrl (Windows/Linux) and Command (Mac)
                  if ((evt.ctrlKey || evt.metaKey) && selectedEntriesRef.current.length > 0) {
                    evt.preventDefault();
                    const paths = selectedEntriesRef.current.map((e) => e.path);
                    clipboardStore.cut(paths);
                    toast.success(t<string>("Cut {{count}} items", { count: paths.length }));
                  }
                  break;
                }
                case "v": {
                  // Support both Ctrl (Windows/Linux) and Command (Mac)
                  if ((evt.ctrlKey || evt.metaKey) && clipboardStore.paths.length > 0) {
                    evt.preventDefault();
                    const { paths, mode } = clipboardStore;

                    // Determine destination directory based on selection
                    let destDir: string | undefined;
                    if (selectedEntriesRef.current.length === 0) {
                      // No selection -> paste to working directory
                      destDir = rootRef.current?.path;
                    } else if (selectedEntriesRef.current.length === 1 && selectedEntriesRef.current[0].isDirectoryOrDrive) {
                      // Single directory selected -> paste into it
                      destDir = selectedEntriesRef.current[0].path;
                    }
                    // If file(s) selected or multiple items selected -> don't paste

                    if (destDir) {
                      // Skip if all items are already in the destination directory (only for cut mode)
                      if (mode === "cut") {
                        const allItemsAlreadyInDest = paths.every(
                          (p) => getStandardParentPath(p) === destDir,
                        );
                        if (allItemsAlreadyInDest) {
                          break;
                        }
                      }

                      const apiCall = mode === "copy"
                        ? BApi.file.copyEntries({ destDir, entryPaths: paths })
                        : BApi.file.moveEntries({ destDir, entryPaths: paths });

                      apiCall.then(() => {
                        const message = mode === "copy"
                          ? t<string>("Copied {{count}} items", { count: paths.length })
                          : t<string>("Moved {{count}} items", { count: paths.length });
                        toast.success(message);
                        if (mode === "cut") {
                          clipboardStore.clear();
                        }
                      });
                    }
                  }
                  break;
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
            onPress={() => {
              if (root) {
                const newRootPath = getStandardParentPath(root.path);

                if (newRootPath) {
                  initialize(newRootPath);
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
                onPress={() => {
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
              clearTimeout(inputBlurHandlerRef.current);
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
          <Shortcuts capabilities={capabilities} />
        </div>
        <div className={"grow min-h-0"}>
          <FileExplorerEntry
            afterPlayedFirstFile={afterPlayedFirstFile}
            capabilities={capabilities}
            entry={root}
            expandable={expandable}
            filter={filterObj}
            renderAfterName={renderAfterName}
            renderBeforeRightOperations={renderBeforeRightOperations}
            switchSelective={switchSelective}
            onChildrenLoaded={(e) => {
              if (!defaultSelectedPathInitializedRef.current) {
                defaultSelectedPathInitializedRef.current = true;
                const standardDefaultSelectedPath = standardizePath(defaultSelectedPath);

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
                    const scrollToRow = Math.min(e.filteredChildren.length - 1, selectedRow + 2);

                    e.ref?.scrollTo(e.filteredChildren[scrollToRow].path);
                    setSelectedEntries([selected]);
                  }
                }
              }
              if (initializedRootPathRef.current != e.path) {
                initializedRootPathRef.current = e.path;
                onInitialized?.(e?.path);
              }
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
                if (en.expandable) {
                  if (en.expanded) {
                    en.collapse();
                  } else {
                    en.expand();
                  }
                }
              }
            }}
          />
        </div>
      </div>
    );
  },
);

FileExplorer.displayName = "FileExplorer";

export default FileExplorer;
