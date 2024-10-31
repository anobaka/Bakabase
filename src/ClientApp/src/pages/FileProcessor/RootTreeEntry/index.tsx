import React, { forwardRef, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import { useUpdate, useUpdateEffect } from 'react-use';
import {
  ArrowLeftOutlined,
  ArrowUpOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { ControlledMenu, useMenuState } from '@szhsin/react-menu';
import EventListener, { SelectionMode } from './components/EventListener';
import ContextMenu from './components/ContextMenu';
import type { TreeEntryProps } from '@/pages/FileProcessor/TreeEntry';
import TreeEntry from '@/pages/FileProcessor/TreeEntry';
import BApi from '@/sdk/BApi';
import { buildLogger, splitPathIntoSegments, standardizePath } from '@/components/utils';
import BusinessConstants from '@/components/BusinessConstants';
import RootEntry from '@/core/models/FileExplorer/RootEntry';
import { Button, Chip, Input } from '@/components/bakaui';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import DeleteConfirmationModal from '@/pages/FileProcessor/RootTreeEntry/components/DeleteConfirmationModal';
import WrapModal from '@/pages/FileProcessor/RootTreeEntry/components/WrapModal';
import MediaLibraryPathSelectorV2 from '@/components/MediaLibraryPathSelectorV2';
import store from '@/store';
import { UiTheme } from '@/sdk/constants';
import ExtractModal from '@/pages/FileProcessor/RootTreeEntry/components/ExtractModal';


type Props = {
  rootPath?: string;
  onSelected?: (entries: Entry[]) => any;
  selectable: 'disabled' | 'single' | 'multiple';
  defaultSelectedPath?: string;
  onInitialized?: () => any;
} & Pick<TreeEntryProps, 'capabilities' | 'expandable' | 'filter' | 'onDoubleClick'>;

const log = buildLogger('RootTreeEntry');

export type RootTreeEntryRef = { root?: Entry };

const RootTreeEntry = forwardRef<RootTreeEntryRef, Props>(({
                                                             rootPath,
                                                             onDoubleClick,
                                                             filter,
                                                             onSelected,
                                                             selectable,
                                                             onInitialized,
                                                             defaultSelectedPath,
                                                             expandable = false,
                                                             capabilities,
                                                           }, ref) => {
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

  const [historyRootPaths, setHistoryRootPaths] = useState<(string | undefined)[]>([]);
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
    log('Trigger onSelected', selectedEntriesRef.current);
    onSelectedRef.current?.(selectedEntriesRef.current);
  }, [selectedEntries]);

  const initialize = useCallback(async (path?: string, addToHistory: boolean = true) => {
    let finalPath = standardizePath(path);
    if (finalPath != undefined && finalPath.length > 0) {
      const isFile = (await BApi.file.checkPathIsFile({ path: finalPath })).data;
      if (isFile) {
        finalPath = splitPathIntoSegments(finalPath).slice(0, -1).join(BusinessConstants.pathSeparator);
      }
    }
    shiftSelectionStartRef.current = undefined;

    if (addToHistory && rootRef.current) {
      const history = historyRootPathsRef.current;
      if (history.length == 0 || history[history.length - 1] != rootRef.current.path) {
        setHistoryRootPaths([...history, rootRef.current.path]);
      }
    }

    log('initialize', finalPath, historyRootPathsRef.current);

    setRoot(new RootEntry(finalPath));
  }, []);

  useEffect(() => {
    initialize(rootPath);

    return () => {
      log('Disposing', rootRef);
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
    log('root changed', root);
  }, [root]);

  useUpdateEffect(() => {
    historyRootPathsRef.current = historyRootPaths;
  }, [historyRootPaths]);

  useImperativeHandle(ref, (): RootTreeEntryRef => {
    return {
      root,
    };
  }, [root]);

  if (!root) {
    return null;
  }

  const filteredChildrenCount = root?.filteredChildren.length ?? 0;
  const childrenCount = root?.childrenCount ?? 0;

  return (
    <div
      className={'flex flex-col gap-1 max-h-full min-h-0 grow'}
      onClick={() => {
        for (const se of selectedEntriesRef.current) {
          se.select(false);
        }
        setSelectedEntries([]);
      }}
    >
      <ControlledMenu
        {...menuProps}
        anchorPoint={anchorPoint}
        className={'file-processor-page-context-menu'}
        onClose={() => {
          contextMenuEntryRef.current = undefined;
          toggleMenu(false);
        }}
        onContextMenu={e => {
          e.preventDefault();
          e.stopPropagation();
        }}
      >
        <ContextMenu
          contextMenuEntry={contextMenuEntryRef.current}
          selectedEntries={selectedEntries}
          capabilities={capabilities}
        />
      </ControlledMenu>
      <EventListener
        onSelectionModeChange={m => {
          selectionModeRef.current = m;
          forceUpdate();
        }}
        onClick={() => {
          for (const se of selectedEntriesRef.current) {
            se.select(false);
          }
          setSelectedEntries([]);
        }}
        onDelete={() => {
          if (selectedEntriesRef.current.length > 0) {
            createPortal(DeleteConfirmationModal, {
              paths: selectedEntriesRef.current.map(e => e.path),
            });
          }
        }}
        onKeyDown={(key, evt) => {
          switch (key) {
            case 'w': {
              if (selectedEntriesRef.current.length > 0) {
                createPortal(WrapModal, {
                  entries: selectedEntriesRef.current,
                });
              }
              break;
            }
            case 'm': {
              if (selectedEntriesRef.current.length > 0) {
                createPortal(MediaLibraryPathSelectorV2, {
                  onSelect: (id, path) => {
                    return BApi.file.moveEntries({
                      destDir: path,
                      entryPaths: selectedEntriesRef.current.map(e => e.path),
                    });
                  },
                });
              }
              break;
            }
            case 'd': {
              if (selectedEntriesRef.current.length > 0) {
                BApi.file.decompressFiles({ paths: selectedEntriesRef.current.map(e => e.path) });
              }
              break;
            }
            case 'e': {
              if (selectedEntriesRef.current.length > 0) {
                createPortal(ExtractModal, { entries: selectedEntriesRef.current });
              }
              break;
            }
            case 'a': {
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

                log('Select all filtered children of entry', parent);

                if (parent) {
                  const newSelectedEntries: Entry[] = [];
                  for (const c of parent.filteredChildren) {
                    newSelectedEntries.push(c);
                    c.select(true);
                  }
                  const others = selectedEntriesRef.current.filter(s => !newSelectedEntries.includes(s));
                  for (const o of others) {
                    o.select(false);
                  }
                  setSelectedEntries(newSelectedEntries);
                }
              }
            }
          }
        }}
      />
      <div className="flex items-center">
        <Button
          size={'sm'}
          radius={'none'}
          isIconOnly
          isDisabled={historyRootPaths.length == 0}
          variant={'light'}
          onClick={() => {
            const newRoot = historyRootPathsRef.current.pop();
            setHistoryRootPaths([...historyRootPathsRef.current]);
            initialize(newRoot, false);
          }}
        >
          <ArrowLeftOutlined className={'text-base'} />
        </Button>
        <Button
          size={'sm'}
          radius={'none'}
          isIconOnly
          variant={'light'}
          isDisabled={!(root?.path)}
          onClick={() => {
            if (root) {
              const segments = splitPathIntoSegments(root.path);
              const isUncPath = root.path.startsWith(BusinessConstants.uncPathPrefix);
              // console.log(root.path, segments, isUncPath);
              if (segments.length > 0 && !isUncPath) {
                const newRoot = segments.slice(0, segments.length - 1).join(BusinessConstants.pathSeparator);
                initialize(newRoot);
              }
            }
          }}
        >
          <ArrowUpOutlined className={'text-base'} />
        </Button>
        <Input
          startContent={(
            <Button
              size={'sm'}
              radius={'none'}
              isDisabled={!(root?.path)}
              isIconOnly
              onClick={() => {
                BApi.tool.openFileOrDirectory({ path: root?.path });
              }}
            >
              <FolderOutlined className={'text-base'} />
            </Button>
          )}
          endContent={
            <Chip
              size={'sm'}
              variant={'light'}
            >
              {selectedEntries.length} / {filteredChildrenCount == childrenCount ? childrenCount : `${filteredChildrenCount} / ${childrenCount}`}
            </Chip>
          }
          size={'sm'}
          radius={'none'}
          placeholder={t('You can type a path here')}
          value={inputValue}
          className={'grow'}
          classNames={{
            inputWrapper: 'pl-0',
          }}
          onValueChange={v => {
            const path = standardizePath(v)!;
            setInputValue(path);
            clearInterval(inputBlurHandlerRef.current);
            inputBlurHandlerRef.current = setTimeout(() => {
              initialize(path);
            }, 1000);
          }}
        />
        <Input
          startContent={<SearchOutlined className={'text-base'} />}
          size={'sm'}
          radius={'none'}
          placeholder={t('Filter')}
          className={'w-1/4'}
          value={filterInputValue}
          onValueChange={v => setFilterInputValue(v)}
        />
        <Button
          size={'sm'}
          radius={'none'}
          className={'px-6'}
          onClick={() => {
            BApi.file.openRecycleBin();
          }}
        >
          <FolderOpenOutlined className={'text-base'} />
          {t('Recycle bin')}
        </Button>
      </div>
      <div className={'grow min-h-0'}>
        <TreeEntry
          entry={root}
          filter={{
            keyword: filterInputValue,
          }}
          capabilities={capabilities}
          onContextMenu={(evt, entry) => {
            evt.preventDefault();
            setAnchorPoint({
              x: evt.clientX,
              y: evt.clientY,
            });
            contextMenuEntryRef.current = entry;
            toggleMenu(true);
          }}
          switchSelective={e => {
            if (selectable == 'disabled') {
              return false;
            }

            if (selectable == 'single' || selectionModeRef.current == SelectionMode.Normal) {
              shiftSelectionStartRef.current = e;
              if (selectedEntriesRef.current.includes(e)) {
                if (selectedEntriesRef.current.length > 1) {
                  for (const se of selectedEntriesRef.current.filter(x => x != e)) {
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
                  selectedEntriesRef.current = selectedEntriesRef.current.filter(x => x != e);
                } else {
                  selectedEntriesRef.current.push(e);
                }
                setSelectedEntries([...selectedEntriesRef.current]);
                return true;
              }
              case SelectionMode.Shift: {
                if (!shiftSelectionStartRef.current || shiftSelectionStartRef.current.parent != e.parent) {
                  shiftSelectionStartRef.current = e.parent!.filteredChildren![0];
                }

                const newSelectedEntries: Entry[] = [];
                const currentIdx = e.parent!.filteredChildren!.indexOf(e);
                const shiftStartIdx = e.parent!.filteredChildren!.indexOf(shiftSelectionStartRef.current);
                const startIdx = Math.min(currentIdx, shiftStartIdx);
                const endIdx = Math.max(currentIdx, shiftStartIdx);
                for (let i = startIdx; i <= endIdx; i++) {
                  const en = e.parent!.filteredChildren![i];
                  newSelectedEntries.push(en);
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
          onChildrenLoaded={e => {
            if (!defaultSelectedPathInitializedRef.current) {
              defaultSelectedPathInitializedRef.current = true;
              const standardDefaultSelectedPath = standardizePath(defaultSelectedPath);
              if (standardDefaultSelectedPath != undefined && standardDefaultSelectedPath.length > 0) {
                const selectedRow = e.filteredChildren?.findIndex(c => c.path == standardDefaultSelectedPath);
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
            onInitialized?.();
            forceUpdate();
          }}
          expandable={expandable}
          onDoubleClick={(evt, en) => {
            if (onDoubleClick) {
              onDoubleClick(evt, en);
            } else {
              if (en.isDirectoryOrDrive) {
                initialize(en.path);
              }
            }
          }}
        />
      </div>
    </div>
  );
});

export default RootTreeEntry;
