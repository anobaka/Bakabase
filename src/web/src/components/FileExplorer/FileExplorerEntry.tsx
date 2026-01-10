"use client";
import type { IEntryFilter } from "@/core/models/FileExplorer/Entry";
import type { Capability } from "./models";
import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  CloseCircleOutlined,
  EyeOutlined,
  FileOutlined,
  InfoCircleOutlined,
} from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { useUpdate, useUpdateEffect } from "react-use";
import { List } from "react-virtualized";
import { AiOutlinePlayCircle } from "react-icons/ai";
import { BsCollectionPlayFill } from "react-icons/bs";

import OperationButton from "./components/OperationButton";
import EditableFileName from "./components/EditableFileName";
import RightOperations from "./components/RightOperations";

import { Tooltip, toast } from "@/components/bakaui";
import { IconType, IwFsType } from "@/sdk/constants";

import "@szhsin/react-menu/dist/index.css";
import "@szhsin/react-menu/dist/transitions/slide.css";

import {
  ChildrenIndent,
  Entry,
  EntryProperty,
  EntryStatus,
  IwFsEntryAction,
} from "@/core/models/FileExplorer/Entry";
import { buildLogger, humanFileSize, standardizePath, uuidv4 } from "@/components/utils";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import MediaPlayer from "@/components/MediaPlayer";
import BApi from "@/sdk/BApi";
import "./FileExplorerEntry.scss";
import { Button, Chip, Modal, Spinner } from "@/components/bakaui";

import TailingOperations from "./components/TailingOperations";
import LeftIcon from "./components/LeftIcon";
import TaskOverlay from "./components/TaskOverlay";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useFileExplorerClipboardStore } from "@/stores/fileExplorerClipboard";

// Virtual list configuration
const VIRTUAL_LIST_OVERSCAN_ROW_COUNT = 5;
const RESIZE_DEBOUNCE_MS = 100;

export type FileExplorerEntryProps = {
  entry: Entry;
  onDoubleClick?: (event: React.MouseEvent, entry: Entry) => void;
  /*
   * Return false will block the selection of the entry
   */
  switchSelective?: (e: Entry) => boolean;
  onChildrenLoaded?: (e: Entry) => void;
  filter?: IEntryFilter;
  expandable?: boolean;
  capabilities?: Capability[];

  style?: React.CSSProperties;
  onContextMenu?: (e: React.MouseEvent, entry: Entry) => void;
  onLoadFail?: (rsp: { code?: number; message?: string }, entry: Entry) => void;

  afterPlayedFirstFile?: (entry: Entry) => void;

  // Render props for custom decorations
  renderAfterName?: (entry: Entry) => React.ReactNode;
  renderBeforeRightOperations?: (entry: Entry) => React.ReactNode;
};

// todo: split this component into base components: simple, advance
const FileExplorerEntry = (props: FileExplorerEntryProps) => {
  const {
    style: propsStyle,
    entry: propsEntry,
    filter,
    switchSelective,
    onDoubleClick,
    capabilities,
    onChildrenLoaded,
    onContextMenu = (e, entry) => {},
    onLoadFail = (rsp, entry) => {},
    expandable = true,
    afterPlayedFirstFile,
    renderAfterName,
    renderBeforeRightOperations,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  // Use global clipboard store for visual indicators
  const clipboardStore = useFileExplorerClipboardStore();
  const { createPortal, createWindow } = useBakabaseContext();

  const [entry, setEntry] = useState(propsEntry);
  const entryRef = useRef(entry);

  // Memoize logger to avoid recreation on each render
  const log = useMemo(() => buildLogger(entryRef.current.path), [entry.path]);

  // Version counter to trigger useCallback updates when children/expanded change
  const [childrenVersion, setChildrenVersion] = useState(0);

  // Infrastructures
  const loadingChildrenRef = useRef(false);
  const childrenStylesRef = useRef<Record<string, any>>({});
  const domRef = useRef<HTMLElement | null>(null);
  const hashRef = useRef(uuidv4());
  const [loading, setLoading] = useState(false);
  const pendingRenderingRef = useRef(false);
  const resizeDebounceRef = useRef<ReturnType<typeof setTimeout>>();
  const [isDragOver, setIsDragOver] = useState(false);

  useUpdateEffect(() => {
    setEntry(propsEntry);
  }, [propsEntry]);

  useEffect(() => {
    loadingChildrenRef.current = loading;
  }, [loading]);

  // Functions
  const currentEntryDomRef = useRef<any>();

  const initialize = useCallback(async (e: Entry) => {
    // console.trace();

    e.path = standardizePath(e.path)!;
    log("Initializing", e);

    if (entryRef.current) {
      log("Disposing previous entry", entryRef.current);
      await entryRef.current.dispose();
    }

    entryRef.current = e;

    entryRef.current.ref = {
      select: (selected) => {
        if (entryRef.current?.selected != selected) {
          entryRef.current.selected = selected;
          if (selected) {
            log("Focus");
            currentEntryDomRef.current?.focus();
          }
          forceUpdate();
        }
      },
      get dom() {
        return domRef.current;
      },
      forceUpdate,
      renderChildren,
      get filteredChildren(): Entry[] {
        return entryRef.current.filteredChildren;
      },
      expand: expand,
      collapse: collapse,
      scrollTo: (path: string) => {
        const row = entryRef.current.filteredChildren.findIndex((e) => e.path == path);

        log("scrolling to", path, row, virtualListRef.current);
        virtualListRef.current?.scrollToRow(row);
      },
      setLoading,
      playFirstFile,
    };

    log(
      "initializing, status: ",
      EntryStatus[entryRef.current.status],
      "expanded:",
      entryRef.current.expanded,
      `children size:${entryRef.current.childrenWidth}x${entryRef.current.childrenHeight}`,
      "filtered children",
      entryRef.current.filteredChildren,
    );

    await entryRef.current.initialize();

    if (entryRef.current.expanded) {
      log("Expanding");
      const rendered = await expand();

      if (!rendered) {
        entryRef.current.renderChildren();
      }
    }
    if (!entryRef.current.recalculateChildrenWidth()) {
      forceUpdate();
    }
    log("Initialized");
  }, []);

  useEffect(() => {
    initialize(entry);

    // Debounced resize handler to prevent excessive re-renders
    const resizeObserver = new ResizeObserver(() => {
      clearTimeout(resizeDebounceRef.current);
      resizeDebounceRef.current = setTimeout(() => {
        if (domRef.current) {
          log("Size changed", domRef.current, domRef.current?.clientWidth);
          entryRef.current.childrenWidth =
            domRef.current!.clientWidth - (entryRef.current.isRoot ? 0 : ChildrenIndent);
          forceUpdate();
        }
      }, RESIZE_DEBOUNCE_MS);
    });

    resizeObserver.observe(domRef.current!);

    return () => {
      clearTimeout(resizeDebounceRef.current);
      resizeObserver.disconnect();
    };
  }, []);

  useUpdateEffect(() => {
    // log('1234567890');
    initialize(entry);
  }, [entry]);

  const renderFileSystemInfo = useCallback(() => {
    // log('Rendering fs info');
    const en = entryRef.current;

    if (en.type != IwFsType.Invalid) {
      const elements: any[] = [];

      if (
        en.childrenCount != undefined &&
        en.isDirectoryOrDrive &&
        en.properties.includes(EntryProperty.ChildrenCount)
      ) {
        elements.push(
          <Chip key="children-count" color={"secondary"} size={"sm"} variant={"light"}>
            <FileOutlined className={"text-sm"} />
            {en.childrenCount}
          </Chip>,
        );
      }
      if (en.size != undefined && en.size > 0 && en.properties.includes(EntryProperty.Size)) {
        elements.push(
          <Chip key="size" color={"secondary"} size={"sm"} variant={"light"}>
            {humanFileSize(en.size, false)}
          </Chip>,
        );
      }

      if (elements.length > 0) {
        return <div className={"flex items-center"}>{elements}</div>;
      }
    }

    return;
  }, []);

  // Shallow compare for style objects (faster than deep-diff)
  const shallowStyleEqual = useCallback((a: any, b: any): boolean => {
    if (a === b) return true;
    if (!a || !b) return false;
    const keysA = Object.keys(a).filter(k => k !== 'id');
    const keysB = Object.keys(b).filter(k => k !== 'id');
    if (keysA.length !== keysB.length) return false;
    return keysA.every(key => a[key] === b[key]);
  }, []);

  const virtualListRowHeightCallback = useCallback(
    ({ index }: { index: number }) => {
      const child = entryRef.current.filteredChildren[index];

      if (child) {
        const h = child.totalHeight;

        log(
          `[VirtualListRowHeight] Getting row height of [${child?.name}]:${h}, with children height:[${child.childrenHeight}]`,
        );

        return h;
      } else {
        log(`[VirtualListRowHeight] Getting row height of [${index}]:Unknown`);

        return 0;
      }
    },
    [childrenVersion, log],
  );

  const virtualListRowRendererCallback = useCallback(
    ({ index, style }: { index: number; style: any; isVisible?: boolean; isScrolling?: boolean }) => {
      if (entryRef.current.filteredChildren.length <= index) {
        log(
          `[VirtualListRowRenderer] Rendering a child with overflow index: ${index} >= count of children: ${entryRef.current.filteredChildren.length}, ignoring`,
        );

        return;
      }
      const e = entryRef.current.filteredChildren[index];

      log(
        `[VirtualListRowRenderer]Rendering children row:[${e.name}]`,
        `height:[${style.height}]`,
        `children height:[${e.childrenHeight}]`,
        "status:",
        EntryStatus[e.status],
      );

      // Use shallow compare instead of deep-diff for better performance
      const prevStyle = childrenStylesRef.current[e.path];
      if (!shallowStyleEqual(prevStyle, style)) {
        childrenStylesRef.current[e.path] = { ...style, id: uuidv4() };
      }

      const s = childrenStylesRef.current[e.path];

      return (
        <MemoFileExplorerEntry
          key={e.id}
          afterPlayedFirstFile={afterPlayedFirstFile}
          capabilities={capabilities}
          entry={e}
          expandable={e.expandable}
          filter={filter}
          style={s}
          switchSelective={switchSelective}
          onContextMenu={onContextMenu}
          onDoubleClick={onDoubleClick}
          onLoadFail={onLoadFail}
          renderAfterName={renderAfterName}
          renderBeforeRightOperations={renderBeforeRightOperations}
        />
      );
    },
    [childrenVersion, onDoubleClick, renderAfterName, renderBeforeRightOperations, shallowStyleEqual, log, afterPlayedFirstFile, capabilities, filter, switchSelective, onContextMenu, onLoadFail],
  );

  const domCallback = useCallback((node?: HTMLElement | null) => {
    domRef.current = node!;
    if (node) {
      // resize();
    }
  }, []);

  const virtualListRef = useRef<List | null>(null);

  /**
   * Height of only one row will cause the full-re-rendering of the list, so we do not need to render a specific child.
   */
  const renderChildren = useCallback(() => {
    log("Rendering children", entryRef.current, entryRef.current.filteredChildren);

    pendingRenderingRef.current = true;

    // Increment version to trigger useCallback updates
    setChildrenVersion(v => v + 1);

    // Clean up style cache for entries that are no longer in the children list
    const currentPaths = new Set(entryRef.current.filteredChildren.map(c => c.path));
    for (const path of Object.keys(childrenStylesRef.current)) {
      if (!currentPaths.has(path)) {
        delete childrenStylesRef.current[path];
      }
    }

    if (domRef.current?.parentElement) {
      const newWidth =
        domRef.current.parentElement.clientWidth - (entryRef.current.isRoot ? 0 : ChildrenIndent);
      const newHeight = entryRef.current.childrenHeight;

      log(`Recalculated children size: ${newWidth}x${newHeight}`);

      for (let i = 0; i < entryRef.current.filteredChildren.length; i++) {
        virtualListRef.current?.recomputeRowHeights(i);
      }

      forceUpdate();
    }

    if (entryRef.current.filteredChildren.length == 0) {
      triggerChildrenLoaded();
    }
  }, [log]);

  const collapse = useCallback(() => {
    if (entryRef.current.expanded) {
      entryRef.current.expanded = false;
      entryRef.current.renderChildren();
    }
  }, []);

  /**
   *
   * @param refresh
   * @return rendered
   */
  const expand = useCallback(async (refresh: boolean = false): Promise<boolean> => {
    if (!entryRef.current.expandable) {
      return false;
    }
    if (refresh) {
      entryRef.current.clearChildren();
      log("Clear children");
    }
    if (!entryRef.current.expanded || !entryRef.current.children) {
      if (!entryRef.current.children) {
        if (loadingChildrenRef.current) {
          return false;
        }
        loadingChildrenRef.current = true;
        setLoading(true);
        // @ts-ignore
        const rsp = await BApi.file.getChildrenIwFsInfo(
          { root: entryRef.current.path },
          { showErrorToast: () => false },
        );

        log(`Loaded ${rsp.data?.entries?.length} children`);
        setLoading(false);
        if (rsp.code) {
          onLoadFail(rsp, entryRef.current);

          return false;
        }
        if (rsp.data) {
          const { entries = [] } = rsp.data || {};

          // @ts-ignore
          entryRef.current.children = entries!.map(
            (e) =>
              new Entry({
                ...e,
                parent: entryRef.current,
                properties: entryRef.current.properties,
              }),
          );
        }
      }
      entryRef.current.expanded = true;
      entryRef.current.expireFilteredChildren();
      entryRef.current.renderChildren();

      return true;
    }

    return false;
  }, [log, onLoadFail]);

  const triggerChildrenLoaded = useCallback(() => {
    log("Trigger onChildrenLoaded", entryRef.current);
    onChildrenLoaded?.(entryRef.current);
  }, []);

  useUpdateEffect(() => {
    entryRef.current.root.patchFilter(filter);
    forceUpdate();
  }, [filter]);

  const { actions } = entryRef.current;

  const renderTaskError = useCallback(() => {
    if (entryRef.current.task && entryRef.current.task.error) {
      const text = `${entryRef.current.task.name}:${entryRef.current.task.error}`;

      return (
        <Button
          color={"danger"}
          isIconOnly={!entryRef.current.task.briefError}
          size={"sm"}
          variant={"light"}
          onPress={() => {
            createPortal(Modal, {
              defaultVisible: true,
              size: "xl",
              title: t<string>("common.label.error"),
              children: <pre>{text}</pre>,
            });
          }}
        >
          <CloseCircleOutlined className={"text-base"} />
          {entryRef.current.task.briefError}
        </Button>
      );
    }

    return;
  }, [createPortal, t]);

  const play = useCallback((entry: Entry) => {
    // MediaPlayer now handles directories and compressed files internally
    // by auto-expanding them when there's a single entry of that type
    const mediaEntry: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry = {
      path: entry.path,
      name: entry.name,
      meaningfulName: entry.meaningfulName || entry.name,
      ext: entry.ext,
      type: entry.type,
      passwordsForDecompressing: entry.passwordsForDecompressing || [],
    };

    log("Play", mediaEntry);

    createWindow(
      MediaPlayer,
      {
        entries: [mediaEntry],
        defaultActiveIndex: 0,
      },
      {
        title: entry.meaningfulName || entry.name,
        persistent: true,
      },
    );
  }, []);

  const playFirstFile = useCallback(async () => {
    if (
      !(
        ((entryRef.current.childrenCount && entryRef.current.childrenCount > 0) ||
          !entryRef.current.isDirectoryOrDrive) &&
        capabilities?.includes("play-first-file")
      )
    ) {
      return;
    }

    const rsp = await BApi.file.getFirstFileByExtension({
      path: entryRef.current.path,
    });
    const files = rsp.data ?? [];

    if (files.length == 0) {
      toast.danger(t<string>("fileExplorer.error.noFilesUnderPath"));
    } else {
      if (files.length == 1) {
        await BApi.tool.openFile({ path: files[0] });
        afterPlayedFirstFile?.(entryRef.current);
      } else {
        const modal = createPortal(Modal, {
          size: "lg",
          defaultVisible: true,
          title: t("fileExplorer.modal.multipleFileTypesFound"),
          children: (
            <div className="flex flex-col gap-2">
              <div>{t<string>("fileExplorer.tip.pleaseSelectOneToOpen")}</div>
              <div className="flex flex-wrap gap-1">
                {files.map((x) => {
                  const segments = x.split(".");

                  return (
                    <Tooltip className="max-w-[600px]" content={x}>
                      <Button
                        onPress={async () => {
                          await BApi.tool.openFile({ path: x });
                          afterPlayedFirstFile?.(entryRef.current);
                          modal.destroy();
                        }}
                      >
                        {segments[segments.length - 1]}
                      </Button>
                    </Tooltip>
                  );
                })}
              </div>
            </div>
          ),
          footer: {
            actions: ["cancel"],
          },
        });
      }
    }
  }, [capabilities, t, createPortal, afterPlayedFirstFile]);

  // log('Rendering', 'children width', entryRef.current.childrenWidth, domRef.current?.clientWidth, domRef.current, entryRef.current);
  // log(132132321, entryRef.current.task);
  // {entryRef.current.expanded &&
  //   (entryRef.current.filteredChildren?.length > 0 ? (
  //     <div className={`entry-children ${entryRef.current.isRoot ? "root" : ""}`}>
  //       {entryRef.current.childrenHeight > 0 && (
  // log('entryRef.current.expanded', entryRef.current.expanded);
  // log('entryRef.current.filteredChildren?.length', entryRef.current.filteredChildren?.length);
  // log('entryRef.current.childrenHeight', entryRef.current.childrenHeight);
  // log('entryRef.current.children', entryRef.current.children);
  // log('entryRef.current.childrenWidth', entryRef.current.childrenWidth);
  // log('entryRef.current.isRoot', entryRef.current.isRoot);
  // log('domRef.current.clientWidth', domRef.current?.clientWidth);

  return (
    <div
      ref={domCallback}
      className={`tree-entry flex flex-col select-none outline-none ${entryRef.current.isRoot ? "h-full" : ""}`}
      style={propsStyle}
      tabIndex={0}
      onDoubleClick={(e) => {
        e.stopPropagation();
        log("Double clicked", entryRef.current);
        onDoubleClick?.(e, entryRef.current);
      }}
    >
      {!entryRef.current.isRoot && (
        <div
          className="entry-main-container relative w-full h-9 min-h-9 max-h-9 border-b border-[var(--theme-file-processor-entry-divider)]"
          onClick={(e) => {
            e.stopPropagation();
          }}
        >
          {entryRef.current.type == IwFsType.Invalid && (
            <div className="absolute inset-0 z-[2] cursor-not-allowed bg-red-700/10 backdrop-blur-[1px]" />
          )}
          {entryRef.current.task && <TaskOverlay task={entryRef.current.task} />}
          <div
            ref={(r) => {
              currentEntryDomRef.current = r;
            }}
            className={`entry-main box-content flex items-center justify-between h-6 py-[5px] pl-1 pr-2 relative rounded-md mx-1 border-l-[3px] border-l-transparent transition-all duration-150 ${entryRef.current?.selected ? "selected" : ""} ${entryRef.current.expanded ? "border-b-[var(--theme-border-color)]" : ""} ${isDragOver ? "bg-primary/20 border-l-primary" : ""} ${clipboardStore.paths.includes(entryRef.current.path) ? (clipboardStore.mode === "cut" ? "opacity-50 border-l-warning" : "border-l-success") : ""}`}
            tabIndex={0}
            draggable={!entryRef.current.isDrive}
            onDragStart={(e) => {
              e.dataTransfer.setData("text/plain", JSON.stringify([entryRef.current.path]));
              e.dataTransfer.effectAllowed = "move";
            }}
            onDragOver={(e) => {
              if (entryRef.current.isDirectoryOrDrive) {
                e.preventDefault();
                e.dataTransfer.dropEffect = "move";
                setIsDragOver(true);
              }
            }}
            onDragLeave={() => {
              setIsDragOver(false);
            }}
            onDrop={(e) => {
              e.preventDefault();
              setIsDragOver(false);
              try {
                const paths = JSON.parse(e.dataTransfer.getData("text/plain")) as string[];
                if (paths.length > 0 && entryRef.current.isDirectoryOrDrive) {
                  BApi.file.moveEntries({
                    destDir: entryRef.current.path,
                    entryPaths: paths,
                  }).then(() => {
                    toast.success(t<string>("fileExplorer.success.movedItems", { count: paths.length }));
                  }).catch((err) => {
                    toast.danger(t<string>("fileExplorer.error.failedToMoveItems"));
                    log("Move entries failed", err);
                  });
                }
              } catch (err) {
                // Invalid drag data - log for debugging
                log("Invalid drag data", err);
              }
            }}
            onClick={() => {
              const r = !switchSelective || switchSelective(entryRef.current);

              log(
                `Trying ${entryRef.current?.selected ? "unselect" : "select"} ${entryRef.current?.name}, and get blocked: ${!r}`,
              );
              if (r) {
                entryRef.current.selected = !entryRef.current?.selected;
                forceUpdate();
              }
            }}
            onContextMenu={(e) => {
              if (!entryRef.current.selected) {
                if (!switchSelective || switchSelective(entryRef.current)) {
                  entryRef.current.selected = true;
                  forceUpdate();
                }
              }
              onContextMenu(e, entryRef.current);
            }}
          >
            <div className={`flex items-center flex-1 overflow-hidden text-sm gap-0.5 ${entry.isDirectoryOrDrive ? "font-medium" : "font-normal opacity-90"}`}>
              <div className="things-before-name flex items-center">
                <LeftIcon entry={entryRef.current} expandable={expandable} loading={loading} />
                {entryRef.current && (
                  <div className={`flex items-center justify-center mr-1.5 ${entry.isDirectoryOrDrive ? "w-5 h-5 min-w-5 max-w-5 min-h-5 max-h-5" : "w-4 h-4 min-w-4 max-w-4 min-h-4 max-h-4"}`}>
                    <FileSystemEntryIcon
                      path={entryRef.current.path}
                      size={entry.isDirectoryOrDrive ? 18 : 16}
                      type={entry.isDirectoryOrDrive ? IconType.Directory : IconType.Dynamic}
                    />
                  </div>
                )}
              </div>
              <EditableFileName
                disabled={
                  entry.isDrive ||
                  !capabilities?.includes("rename") ||
                  entry.status == EntryStatus.Error
                }
                isDirectory={entry.isDirectoryOrDrive}
                name={entry.name}
                path={entry.path}
              />
              <div className="flex items-center [&>*]:whitespace-nowrap [&>*]:flex [&>*]:items-center [&>*]:ml-1.5">
                {actions.includes(IwFsEntryAction.Play) && capabilities?.includes("play") && (
                  <OperationButton
                    isIconOnly
                    color={"primary"}
                    onClick={(e) => {
                      play(entryRef.current);
                    }}
                  >
                    <EyeOutlined className={"text-base"} />
                  </OperationButton>
                )}
                &nbsp;
                {((entry.childrenCount && entry.childrenCount > 0) || !entry.isDirectoryOrDrive) &&
                  capabilities?.includes("play-first-file") && (
                    <Tooltip content={t<string>("fileExplorer.tip.quickPreviewUsingDefaultApp")}>
                      <OperationButton
                        isIconOnly
                        onClick={async (e) => {
                          e.stopPropagation();
                          playFirstFile();
                        }}
                      >
                        <AiOutlinePlayCircle className={"text-base"} />
                      </OperationButton>
                    </Tooltip>
                  )}
                <Tooltip content={t<string>("fileExplorer.tip.openInMediaPlayer")}>
                  <OperationButton
                    isIconOnly
                    color={"success"}
                    onPress={(e) => {
                      play(entryRef.current);
                    }}
                  >
                    <BsCollectionPlayFill className={"text-base"} />
                  </OperationButton>
                </Tooltip>
                <TailingOperations capabilities={capabilities} entry={entry} />
                {renderFileSystemInfo()}
                {renderTaskError()}
                {renderAfterName && (
                  <div
                    className="ml-1.5"
                    onClick={(e) => e.stopPropagation()}
                    onDoubleClick={(e) => e.stopPropagation()}
                    onMouseDown={(e) => e.stopPropagation()}
                    onKeyDown={(e) => e.stopPropagation()}
                    onContextMenu={(e) => e.stopPropagation()}
                  >
                    {renderAfterName(entry)}
                  </div>
                )}
              </div>
            </div>
            <div className="flex items-center justify-end ml-3 gap-1">
              {renderBeforeRightOperations && (
                <div
                  onClick={(e) => e.stopPropagation()}
                  onDoubleClick={(e) => e.stopPropagation()}
                  onMouseDown={(e) => e.stopPropagation()}
                  onKeyDown={(e) => e.stopPropagation()}
                >
                  {renderBeforeRightOperations(entry)}
                </div>
              )}
              <RightOperations capabilities={capabilities} entry={entryRef.current} />
            </div>
          </div>
        </div>
      )}
      {entryRef.current.expanded &&
        (entryRef.current.filteredChildren?.length > 0 ? (
          <div
            className={`children-container flex-1 overflow-hidden ${
              entryRef.current.isRoot
                ? ""
                : "pl-[8px] ml-[18px] border-l border-[var(--theme-file-processor-entry-divider)] hover:border-l-primary/50 transition-colors"
            }`}
          >
            {entryRef.current.childrenHeight > 0 && (
              <List
                ref={(r) => {
                  log("VirtualList ref", r);
                  virtualListRef.current = r;
                  if (r) {
                    if (pendingRenderingRef.current) {
                      pendingRenderingRef.current = false;
                      triggerChildrenLoaded();
                    }
                  }
                }}
                height={entryRef.current.childrenHeight}
                overscanRowCount={VIRTUAL_LIST_OVERSCAN_ROW_COUNT}
                renderHash={hashRef.current}
                rowCount={entryRef.current.filteredChildren.length}
                rowHeight={virtualListRowHeightCallback}
                rowRenderer={virtualListRowRendererCallback}
                width={entryRef.current.childrenWidth}
              />
            )}
          </div>
        ) : loading ? (
          <div className="flex justify-center items-center py-2">
            <Spinner size={"sm"} />
          </div>
        ) : (
          <div className="flex justify-center items-center gap-2 opacity-70 py-2">
            <InfoCircleOutlined />
            <div>{t<string>("fileExplorer.empty.noContent")}</div>
          </div>
        ))}
    </div>
  );
};
const MemoFileExplorerEntry = React.memo(FileExplorerEntry);

export default MemoFileExplorerEntry;
