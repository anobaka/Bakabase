import React, { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import './index.scss';
import { Button, Dialog, MenuButton, Message } from '@alifd/next';
import { ControlledMenu, MenuItem, useMenuState } from '@szhsin/react-menu';
import { Trans, useTranslation } from 'react-i18next';
import { useCookie, useUpdateEffect } from 'react-use';
import RootTreeEntry from './RootTreeEntry';
import CustomIcon from '@/components/CustomIcon';
import { IwFsType } from '@/sdk/constants';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import { IwFsEntryAction } from '@/core/models/FileExplorer/Entry';
import type RootEntry from '@/core/models/FileExplorer/RootEntry';
import { DecompressFiles } from '@/sdk/apis';
import { buildLogger } from '@/components/utils';

enum SelectionMode {
  Single = 1,
  Batch = 2,
  Range = 3,
}

const SelectionModeIceLabelStatus = {
  [SelectionMode.Single]: 'default',
  [SelectionMode.Batch]: 'info',
  [SelectionMode.Range]: 'success',
};

interface ContextMenuRenderData {
  label: any;
  icon: string;
  onClick: () => any;
  danger?: boolean;
}

const log = buildLogger('FileProcessor');

// todo: optimize
export default () => {
  const { t } = useTranslation();

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [root, setRoot] = useState<RootEntry>();
  const rootRef = useRef<RootEntry>();
  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const selectedEntriesRef = useRef<Entry[]>([]);
  const [allSelected, setAllSelected] = useState(false);

  const [cookieRootPath, setCookieRootPath] = useCookie('fileProcessorRootPath');

  const onAllSelectedCallback = useCallback((en: Entry) => {
    const entries = en.ref?.filteredChildren || [];
    selectedEntriesRef.current.forEach((e) => {
      if (entries.indexOf(e) == -1) {
        console.log(`[Ctrl+A]Deselecting ${e.path}`);
        e.select(false);
      }
    });

    for (const e of entries) {
      if (!selectedEntriesRef.current.includes(e)) {
        console.log(`[Ctrl+A]Selecting ${e.path}`);
        e.select(true);
      }
    }

    const nes = [...entries];
    console.log('[Ctrl+A]Set selectedEntries to', nes);
    setSelectedEntries(nes);
  }, []);

  useUpdateEffect(() => {
    if (rootRef.current) {
      rootRef.current!.dispose();
    }
    rootRef.current = root;
    console.log('Root change to', root);
  }, [root]);

  const findNextEntry = (e, findInChildren) => {
    // Children
    if (e.expanded && findInChildren) {
      if (e.entries.length > 0) {
        return e.entries[0];
      }
    }

    // Neighbors
    const { parent } = e;
    const neighbors = parent?.entries || [];
    const idx = neighbors.indexOf(e);
    const nextIdx = idx + 1;
    if (nextIdx < neighbors.length) {
      return neighbors[nextIdx];
    }

    return findNextEntry(parent, false);
  };

  const findPrevNext = (e) => {
    const { parent } = e;
    const neighbors = parent.entries || [];
    const idx = neighbors.indexOf(e);
    const nextIdx = idx - 1;
    if (nextIdx > -1) {
      let neighbor = neighbors[nextIdx];
      while (neighbor.expanded && neighbor.entries.length > 0) {
        neighbor = neighbor.entries[neighbor.entries.length - 1];
      }
      return neighbor;
    }
    return parent;
  };

  const onKeyDown = (e) => {
    console.log('keydown', e, e.key, e.keyCode, e.ctrlKey);
    switch (e.key) {
      case 'ArrowUp':
      case 'ArrowDown': {
        const goUp = e.key == 'ArrowUp';
        const currentEntry = selectedEntriesRef.current[selectedEntriesRef.current.length - 1];
        let nextEntry;
        if (!currentEntry) {
          nextEntry = (rootRef.current?.children || [])[0];
        } else {
          nextEntry = goUp ? findPrevNext(currentEntry) : findNextEntry(currentEntry, true);
        }
        if (nextEntry) {
          if (trySelectOne(nextEntry)) {
            if (nextEntry.ref) {
              nextEntry.ref.select(true);
            } else {
              nextEntry.selected = true;
            }
          }
        }
        e.stopPropagation();
        e.preventDefault();
        break;
      }
      case 'd': {
        if (showDecompressionOnMultiSelection()) {
          // return alert('Decompress');
          DecompressFiles({
            model: {
              paths: selectedEntriesRef.current.map((t) => t.path),
            },
          })
            .invoke();
        }
        break;
      }
      default:
        break;
    }
  };

  useEffect(() => {
    return () => {
      rootRef.current?.dispose();
    };
  }, []);

  const trySelectOne = (en) => {
    const targetGonnaBeSelected = !en.selected;
    const multipleEntriesWereSelected = selectedEntriesRef.current.length > 1;
    const newEntries = multipleEntriesWereSelected ? [en] : targetGonnaBeSelected ? [en] : [];
    const otherEntries = selectedEntriesRef.current.filter((t) => newEntries.indexOf(t) == -1 && t != en);
    console.log(selectedEntriesRef.current, en, otherEntries);
    for (const oe of otherEntries) {
      console.log(`[NormalSelection]Deselecting ${oe.name}`, oe);
      oe.select(false);
    }

    setSelectedEntries(newEntries);

    // Keep current entry selected if it's multi-selection before.
    if (multipleEntriesWereSelected) {
      if (newEntries.indexOf(en) > -1 && !targetGonnaBeSelected) {
        return false;
      }
    }
    return true;
  };

  useEffect(() => {
    selectedEntriesRef.current = selectedEntries;
    console.log('SelectedEntries changed', selectedEntries);
    const filteredEntries = rootRef.current?.filteredChildren || [];
    console.log('Current selection', selectedEntries);
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
      if (selectedEntries.length > 0 && selectedEntries.every((e) => filteredEntries.some((a) => a == e))) {
        console.log('all selected');
        setAllSelected(true);
      } else {
        setAllSelected(false);
      }
    } else {
      setAllSelected(false);
    }
  }, [selectedEntries]);

  useEffect(() => {
    console.log('set all selected', allSelected);
  }, [allSelected]);

  // console.log('render all selected', allSelected);

  const showDecompressionOnMultiSelection = () => selectedEntriesRef.current && (
    (selectedEntriesRef.current.length == 1 && selectedEntriesRef.current[0].actions.includes(IwFsEntryAction.Decompress)) ||
    (selectedEntriesRef.current.length > 1 && selectedEntriesRef.current.every((e) => e.actions.includes(IwFsEntryAction.Decompress)))
  );

  return (
    <div className={'file-explorer-page'}>
      <div className={'file-explorer'}>
        <div className="line2">
          <div className="right">
            {selectedEntries?.length > 0 && (
              <>
                {showDecompressionOnMultiSelection() && (
                  <Button
                    size={'small'}
                    type={'normal'}
                    onClick={() => DecompressFiles({
                      model: {
                        paths: selectedEntries.map((e) => e.path),
                      },
                    })
                      .invoke()}
                  >{t('Decompress')}(D)
                  </Button>
                )}
                <MenuButton
                  size={'small'}
                  label={`${t('Bulk rename')}(${t('Under development')})`}
                  menuProps={{
                    className: 'file-explorer-page-rename-menu',
                  }}
                >
                  {
                    ['Add to start', 'Append to end', 'Replace', 'Delete first n chars', 'Delete last n chars', 'Sequence numbers'].map((x) => (
                      <MenuButton.Item
                        key={x}
                        style={{ width: 160 }}
                        // style={{ width: 200 }}
                        onClick={() => {
                          Message.notice(t('Under development'));
                        }}
                      >
                        <div>{t(x)}</div>
                      </MenuButton.Item>
                    ))
                  }
                </MenuButton>
                {/* <Button size={'small'} type={'normal'}>{t('Standardize')}({t('Under development')})</Button> */}
              </>
            )}
          </div>
        </div>
        <div
          className="root relative overflow-hidden"
          tabIndex={0}
          onKeyDown={onKeyDown}
        >
          <div className={'absolute top-0 left-0 w-full h-full flex flex-col'} >
            <RootTreeEntry
              selectable={'multiple'}
              expandable
              rootPath={'I:\\Test\\file processor'}
              capabilities={['decompress', 'wrap', 'move', 'extract', 'delete', 'rename', 'delete-all-by-name', 'group']}
              // rootPath={cookieRootPath}
              // onRootPathChange={setCookieRootPath}
            />
            {/* <TreeEntry */}
            {/*   onContextMenu={onContextMenuCallback} */}
            {/*   key={root.id} */}
            {/*   entry={root} */}
            {/*   ref={(r: IEntryRef) => { */}
            {/*     // const prevRef = root.ref; */}
            {/*     root.ref = r; */}
            {/*     if (r) { */}
            {/*       root.initialize(); */}
            {/*     } */}
            {/*   }} */}
            {/*   onChildrenLoaded={onChildrenLoadCallback} */}
            {/*   onDoubleClick={onDoubleClickCallback} */}
            {/*   switchSelective={trySelectCallback} */}
            {/*   keyword={keywordRef.current} */}
            {/*   onAllSelected={onAllSelectedCallback} */}
            {/*   onLoadFail={onLoadFailCallback} */}
            {/* /> */}
          </div>
        </div>
      </div>
    </div>
  );
};
