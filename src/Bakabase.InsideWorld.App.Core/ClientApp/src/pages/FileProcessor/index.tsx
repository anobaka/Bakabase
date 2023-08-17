import React, { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import './index.scss';
import { Button, Checkbox, Dialog, Icon, Input, MenuButton, Message } from '@alifd/next';
import IceLabel from '@icedesign/label';
import { ControlledMenu, MenuItem, useMenuState } from '@szhsin/react-menu';
import { Trans, useTranslation } from 'react-i18next';
import isUncPath from 'is-unc-path';
import { useUpdateEffect } from 'react-use';
import CustomIcon from '@/components/CustomIcon';
import { IwFsType } from '@/sdk/constants';
import TreeEntry from '@/pages/FileProcessor/TreeEntry';
import WrapEntriesDialog from '@/pages/FileProcessor/WrapEntriesDialog';
import type { Entry, IEntryRef } from '@/core/models/FileExplorer/Entry';
import { IwFsEntryAction } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import FileEntryMover from '@/pages/FileProcessor/FileEntryMover';
import BusinessConstants from '@/components/BusinessConstants';
import ContentsMergeDialog from '@/pages/FileProcessor/ContentsMergeDialog';
import DeleteSameNameFilesDialog from '@/pages/FileProcessor/DeleteSameNameFilesDialog';
import ContentsExtractionDialog from '@/pages/FileProcessor/ContentsExtractionDialog';
import DeleteDialog from '@/pages/FileProcessor/DeleteDialog';
import RootEntry from '@/core/models/FileExplorer/RootEntry';
import { DecompressFiles, GetFileSystemOptions, OpenFileOrDirectory, OpenFolderSelector, OpenRecycleBin, PatchFileSystemOptions } from '@/sdk/apis';
import { buildLogger } from '@/components/utils';
import ClickableIcon from '@/components/ClickableIcon';

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
  const ctrlKeyDownRef = useRef<boolean>(false);
  const shiftKeyDownRef = useRef<boolean>(false);
  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const selectedEntriesRef = useRef<Entry[]>([]);
  const [allSelected, setAllSelected] = useState(false);
  const [keyword, setKeyword] = useState<string>();
  const keywordRef = useRef<string>();
  const lastWorkingDirectoryRef = useRef<string>();
  const [loading, setLoading] = useState(false);

  const filteredEntriesCountRef = useRef(0);

  const [selectionMode, setSelectionMode] = useState(SelectionMode.Single);

  // Context menu
  const [menuProps, toggleMenu] = useMenuState();
  const [anchorPoint, setAnchorPoint] = useState({
    x: 0,
    y: 0,
  });
  const [contextMenuTargetEntry, setContextMenuTargetEntry] = useState<Entry>();

  const onLoadFailCallback = useCallback((rsp, entry: Entry) => {
    console.log(rsp, entry, rootRef.current);
    if ((rsp.code) && entry == rootRef.current) {
      setLoading(false);
      setRoot(undefined);
    }
  }, []);

  const onContextMenuCallback = useCallback((e, entry) => {
    console.log(`Opening context menu by ${entry.path}`);
    e.preventDefault();
    setAnchorPoint({
      x: e.clientX,
      y: e.clientY,
    });
    setContextMenuTargetEntry(entry);
    toggleMenu(true);
  }, []);

  const onChildrenLoadCallback = useCallback((entry) => {
    if (entry.path == rootRef.current?.path) {
      setLoading(false);
    }
    const filteredEntries = entry.filteredChildren;
    if (filteredEntries.length != filteredEntriesCountRef.current) {
      filteredEntriesCountRef.current = filteredEntries.length;
      forceUpdate();
    }
  }, []);
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
  const onDoubleClickCallback = useCallback((evt, en) => {
    console.log(evt.target, evt.detail);
    if (en.type == IwFsType.Directory) {
      changeWorkingDirectory(en.path);
    }
  }, []);

  const onBatchWrappingEntriesCallback = useCallback((entries: Entry[]) => {
    const newSelectedEntries = selectedEntriesRef.current?.filter((e) => entries.indexOf(e) == -1) || [];
    if (newSelectedEntries.length != selectedEntriesRef.current?.length) {
      setSelectedEntries(newSelectedEntries);
    }
  }, []);


  useUpdateEffect(() => {
    if (rootRef.current) {
      rootRef.current!.dispose();
    }
    rootRef.current = root;
    console.log('Root change to', root);
  }, [root]);

  useEffect(() => {
    keywordRef.current = keyword;
    const currentCount = rootRef.current?.filteredChildren.length;
    if (currentCount != filteredEntriesCountRef.current) {
      filteredEntriesCountRef.current = currentCount;
      forceUpdate();
    }
    rootRef.current?.patchFilter({
      keyword,
    });
  }, [keyword]);

  const showDeleteConfirmDialog = (entries: Entry[]) => {
    console.log(entries);
    if (entries?.length > 0) {
      DeleteDialog.show({
        paths: entries.map(e => e.path),
      });
    } else {
      Message.notice(t('No file selected'));
    }
  };

  const workingDirectoryPathSegments = root?.path?.split(BusinessConstants.pathSeparator) || [];

  const onDeleteKeyDownCallback = useCallback((evt, en) => {
    console.log('Delete key down with selected entries', selectedEntriesRef.current);
    showDeleteConfirmDialog(selectedEntriesRef.current);
  }, []);

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
      case 'Shift':
        if (shiftKeyDownRef.current != true) {
          shiftKeyDownRef.current = true;
          setSelectionMode(SelectionMode.Range);
        }
        break;
      case 'Control':
        if (ctrlKeyDownRef.current != true) {
          ctrlKeyDownRef.current = true;
          setSelectionMode(SelectionMode.Batch);
        }
        break;
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
      case 'w':
        console.log('444444444444444', selectedEntriesRef.current);
        if (selectedEntriesRef.current?.length > 0) {
          WrapEntriesDialog.show({
            entries: selectedEntriesRef.current,
            onWrapEnd: onBatchWrappingEntriesCallback,
          });
        }
        break;
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
      case 'm': {
        moveToMediaLibraryCallback(selectedEntriesRef.current.map((e) => e.path));
        break;
      }
      default:
        break;
    }
  };

  const onKeyUp = (e) => {
    // console.log('keyup', e, e.key);
    switch (e.key) {
      case 'Shift':
        shiftKeyDownRef.current = false;
        setSelectionMode(SelectionMode.Single);
        break;
      case 'Control':
        ctrlKeyDownRef.current = false;
        setSelectionMode(SelectionMode.Single);
        break;
    }
  };

  const onKeyDownGlobally = (e) => {
    console.log('keydown globally', e, e.key, e.keyCode, e.ctrlKey);

    switch (e.key) {
      case 'Delete':
        showDeleteConfirmDialog(selectedEntriesRef.current);
        break;
    }
  };

  const moveToMediaLibraryCallback = useCallback((paths) => {
    FileEntryMover.show({ paths });
  }, []);

  useEffect(() => {
    GetFileSystemOptions()
      .invoke((a) => {
        const wd = a.data.fileProcessor?.workingDirectory;
        if (wd) {
          changeWorkingDirectory(wd.replace('/[\/\\]/g', '/'), false);
        }
      });

    window.addEventListener('keydown', onKeyDownGlobally);

    return () => {
      rootRef.current?.dispose();
      window.removeEventListener('keydown', onKeyDownGlobally);
    };
  }, []);

  const getSelectionMode = () => {
    if (ctrlKeyDownRef.current) {
      return SelectionMode.Batch;
    }
    if (shiftKeyDownRef.current) {
      return SelectionMode.Range;
    }
    return SelectionMode.Single;
  };

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

  const trySelectCallback = useCallback((en: Entry) => {
    const targetGonnaBeSelected = !en.selected;
    const mode = getSelectionMode();
    console.log(`[SelectionMode:${mode}]${en.name} will be ${targetGonnaBeSelected ? 'selected' : 'unselected'}`);

    switch (mode) {
      case SelectionMode.Batch: {
        if (targetGonnaBeSelected) {
          const firstParent = selectedEntriesRef.current[0]?.parent;
          if (firstParent && en.parent != firstParent) {
            console.log('[BatchSelection]Parent mismatch');
            Message.error('Can not select entries with different parents');
            return false;
          } else {
            console.log('[BatchSelection]Add one');
            selectedEntriesRef.current.push(en);
            setSelectedEntries([...selectedEntriesRef.current]);
            return true;
          }
        } else {
          setSelectedEntries(selectedEntriesRef.current.filter((p) => p != en));
          return true;
        }
      }
      case SelectionMode.Range: {
        const firstEn = selectedEntriesRef.current[0];
        if (firstEn) {
          const parent = firstEn.parent!;
          if (parent == en.parent) {
            const entryList = parent.filteredChildren;
            console.log('[RangeSelection]Entry list', entryList);

            const firstIndex = entryList.indexOf(firstEn);
            const lastIndex = entryList.indexOf(en);
            const totalCount = Math.abs(firstIndex - lastIndex) + 1;
            const lowerIndex = Math.min(firstIndex, lastIndex);
            const higherIndex = Math.max(firstIndex, lastIndex);
            const es = entryList.slice(lowerIndex, higherIndex + 1)
              .filter((t) => t.type != IwFsType.Invalid);
            console.log(`[RangeSelection]Parent match, ${totalCount} entries (${lowerIndex}-${higherIndex}) will be selected`);
            if (firstIndex > lastIndex) {
              es.reverse();
            }
            const invalidEs = selectedEntriesRef.current.filter((t) => es.indexOf(t) == -1);
            const newEs = es.filter((t) => selectedEntriesRef.current.indexOf(t) == -1);
            for (const e of newEs) {
              if (e != en && e != firstEn) {
                e.select(true);
                console.log(`[RangeSelection]Parent match, selecting neighbor: ${e.name}`);
              }
            }
            for (const e of invalidEs) {
              console.log(`[RangeSelection]Parent match, deselecting prev: ${e.name}`);
              e.select(false);
            }
            selectedEntriesRef.current = es;
            console.log(selectedEntriesRef.current, parent);
            setSelectedEntries(es);
            return true;
          } else {
            console.log('[RangeSelection]Parent mismatch');
            Message.error('Can not select entries with different parents');
            return false;
          }
        } else {
          console.log('[RangeSelection]Add first');
          selectedEntriesRef.current.push(en);
          setSelectedEntries([...selectedEntriesRef.current]);
          return true;
        }
      }
      case SelectionMode.Single:
      default: {
        return trySelectOne(en);
      }
    }
  }, []);

  const gotoParent = () => {
    if (workingDirectoryPathSegments.length > 1) {
      const parent = workingDirectoryPathSegments.slice(0, workingDirectoryPathSegments.length - 1)
        .join(BusinessConstants.pathSeparator);
      // console.log(chain, last);
      changeWorkingDirectory(parent);
    }
  };

  const changeWorkingDirectory = async (s: string, save = true) => {
    if (s) {
      console.log('Changing working directory: ', s);
      setLoading(true);
      const lp = rootRef.current?.path;
      if (lp != s) {
        lastWorkingDirectoryRef.current = (lp);
        if (save) {
          PatchFileSystemOptions({
            model: {
              fileProcessor: {
                workingDirectory: s,
              },
            },
          })
            .invoke();
        }

        const re = new RootEntry(s);
        setRoot(re);
      }
    }
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

  const renderDirectoryChain = () => {
    if (root) {
      const elements: JSX.Element[] = [];
      let prev = '';
      const segments = root.path?.replace(/\\/g, BusinessConstants.pathSeparator)
        .split(BusinessConstants.pathSeparator)
        .filter(a => a) || [];
      if (segments.length == 0) {
        return;
      }
      if (isUncPath(root.path)) {
        segments[0] = `${BusinessConstants.uncPathPrefix}${segments[0]}`;
      }
      segments.forEach((t, i) => {
        const currentPath = prev ? `${prev}/${t}` : t;
        elements.push(
          <span
            key={currentPath}
            className={'dir-name'}
            title={currentPath}
            onClick={() => {
              changeWorkingDirectory(currentPath);
            }}
          >{t}
          </span>,
        );
        if (i < segments.length - 1) {
          elements.push(
            <span key={`${currentPath}-separator`} className={'path-separator'}>/</span>,
          );
        }
        prev = currentPath;
      });
      return elements;
    }
    return;
  };

  const renderRoot = useCallback(() => {
    if (root) {
      console.log('rerender root', root);
      return (
        <div
          key={root.id}
          className="root"
          tabIndex={0}
          onKeyDown={onKeyDown}
          onKeyUp={onKeyUp}
          style={{
            position: 'relative',
            overflow: 'hidden',
          }}
        >
          <div
            style={{
              position: 'absolute',
              top: 0,
              left: 0,
              width: '100%',
              height: '100%',
            }}
            key={root.id}
          >
            <TreeEntry
              onContextMenu={onContextMenuCallback}
              key={root.id}
              entry={root}
              ref={(r: IEntryRef) => {
                // const prevRef = root.ref;
                root.ref = r;
                if (r) {
                  root.initialize();
                }
              }}
              onChildrenLoaded={onChildrenLoadCallback}
              onDeleteKeyDown={onDeleteKeyDownCallback}
              onDoubleClick={onDoubleClickCallback}
              trySelect={trySelectCallback}
              keyword={keywordRef.current}
              onAllSelected={onAllSelectedCallback}
              onLoadFail={onLoadFailCallback}
            />
          </div>
        </div>
      );
    }
    return undefined;
  }, [root]);

  // console.log('render all selected', allSelected);

  const canGoBack = workingDirectoryPathSegments.length > 1;

  const getCurrentPath = () => {
    const chain = rootRef.current?.directoryChain || [];
    const last = chain[chain.length - 1]?.path;
    return last;
  };

  const buildSingleContentContextMenuData = useCallback((entry: Entry) => {
    const data: ContextMenuRenderData[] = [
      // {
      //   label: t('Open in explorer'),
      //   icon: 'folder-open',
      //   onClick: () => {
      //     BApi.tool.openFileOrDirectory(
      //       {
      //         path: entry.path,
      //         openInDirectory: entry.type != IwFsType.Directory,
      //       },
      //     );
      //   },
      // },
      {
        label: t('Copy name'),
        icon: 'file-copy',
        onClick: () => {
          navigator.clipboard.writeText(entry.name)
            .then(() => {
              Message.success(t('Copied'));
            })
            .catch((e) => {
              Message.error(`${t('Failed to copy')}. ${e}`);
            });
        },
      },
    ];
    if (entry.actions.includes(IwFsEntryAction.Decompress)) {
      data.push({
        label: t('Decompress'),
        icon: 'rar',
        onClick: () => {
          BApi.file.decompressFiles({
            paths: [entry.path],
          });
        },
      });
    }
    data.push({
      label: t('Move'),
      icon: 'move',
      onClick: () => {
        moveToMediaLibraryCallback([entry.path]);
      },
    });
    // data.push({
    //   label: t('Wrap'),
    //   icon: 'package',
    //   onClick: () => {
    //     WrapEntriesDialog.show({
    //       entries: [entry],
    //       onWrapEnd: onBatchWrappingEntriesCallback,
    //     });
    //   },
    // });
    data.push({
      label: t('Merge contents'),
      icon: 'git-merge-line',
      onClick: () => {
        ContentsMergeDialog.show({
          rootPath: entry.path,
        });
      },
    });
    data.push({
      label: t('Extract'),
      icon: 'upload',
      onClick: () => {
        ContentsExtractionDialog.show({
          entry,
        });
      },
    });
    data.push({
      label: t('Delete'),
      icon: 'delete',
      onClick: () => {
        showDeleteConfirmDialog([entry]);
      },
      danger: true,
    });
    data.push({
      label: t('Delete all files with same name'),
      icon: 'deleterow',
      onClick: () => {
        DeleteSameNameFilesDialog.show({
          workingDirectory: rootRef.current!.path,
          fileName: entry.name,
          filePath: entry.path,
        });
      },
      danger: true,
    });
    return data;
  }, []);

  const buildMultiContentContextMenuData = useCallback((entries: Entry[], currentEntry?: Entry) => {
    const count = entries.length;
    const paths = entries.map(e => e.path);
    const data: ContextMenuRenderData[] = [];
    if (showDecompressionOnMultiSelection()) {
      data.push({
        label: (
          <Trans
            i18nKey={'fp.bulkOperation'}
            values={{
              count,
              operation: t('Decompress'),
            }}
          >
            Decompress<span className={'important'}>0</span>files
          </Trans>
        ),
        icon: 'rar',
        onClick: () => {
          BApi.file.decompressFiles({ paths });
        },
      });
    }
    data.push({
      label: (
        <Trans
          i18nKey={'fp.bulkOperation'}
          values={{
            count,
            operation: t('Move'),
          }}
        >
          Move <span className={'important'}>0</span>files
        </Trans>
      ),
      icon: 'move',
      onClick: () => {
        moveToMediaLibraryCallback(paths);
      },
    });
    if (currentEntry) {
      data.push({
        label: (
          <Trans
            i18nKey={'fp.bulkOperation'}
            values={{
              count,
              operation: t('Wrap'),
            }}
          >
            Wrap<span className={'important'}>0</span>files
          </Trans>
        ),
        icon: 'package',
        onClick: () => {
          WrapEntriesDialog.show({
            entries: [currentEntry, ...entries.filter(e => e != currentEntry)],
            onWrapEnd: onBatchWrappingEntriesCallback,
          });
        },
      });
    }
    if (selectedEntriesRef.current.every(e => e.type == IwFsType.Directory)) {
      data.push({
        label: (
          <Trans
            i18nKey={'fp.bulkOperation'}
            values={{
              count,
              operation: t('Extract'),
            }}
          >
            Extract<span className={'important'}>0</span>files
          </Trans>
        ),
        icon: 'upload',
        onClick: () => {
          Dialog.confirm({
            title: t('Extracting contents'),
            content: t('Are you sure to extract contents in those directories? Those directories will be removed after extraction. You can check the demo by extracting one directory.'),
            v2: true,
            width: 'auto',
            closeMode: ['esc', 'close', 'mask'],
            onOk: async () => {
              for (const e of selectedEntriesRef.current) {
                const rsp = await BApi.file.extractAndRemoveDirectory({ directory: e.path });
                if (rsp.code) {
                  Message.error(rsp.message!);
                  throw new Error(rsp.message!);
                }
              }
            },
          });
        },
      });
    }
    data.push({
      label: (
        <Trans
          i18nKey={'fp.bulkOperation'}
          values={{
            count,
            operation: t('Merge'),
          }}
        >
          Merge<span className={'important'}>0</span>files
        </Trans>
      ),
      icon: 'git-merge-line',
      danger: true,
      onClick: () => {
        ContentsMergeDialog.show({
          paths: selectedEntriesRef.current.map(s => s.path),
        });
      },
    });
    data.push({
      label: (
        <Trans
          i18nKey={'fp.bulkOperation'}
          values={{
            count,
            operation: t('Delete'),
          }}
        >
          Delete<span className={'important'}>0</span>files
        </Trans>
      ),
      icon: 'delete',
      danger: true,
      onClick: () => {
        showDeleteConfirmDialog(entries);
      },
    });
    return data;
  }, []);

  const renderContextMenuItems = useCallback((data: ContextMenuRenderData[]) => {
    return data.map((d, i) => {
      return (
        <MenuItem onClick={d.onClick} key={i}>
          <CustomIcon type={d.icon} size={'small'} className={d.danger ? 'danger' : ''} />
          <div className="label">
            {d.label}
          </div>
        </MenuItem>
      );
    });
  }, []);

  const renderContextMenu = () => {
    // console.log(contextMenuTargetEntry, 456768);

    if (selectedEntriesRef.current == undefined || selectedEntriesRef.current.length == 0) {
      return;
    }

    let components: any;
    if (selectedEntriesRef.current.length == 1) {
      if (contextMenuTargetEntry) {
        components = renderContextMenuItems(buildSingleContentContextMenuData(contextMenuTargetEntry));
      }
    } else {
      components = renderContextMenuItems(buildMultiContentContextMenuData(selectedEntriesRef.current || [], contextMenuTargetEntry));
    }

    log('Rendering context menu', menuProps, anchorPoint, components);

    return (
      <ControlledMenu
        {...menuProps}
        anchorPoint={anchorPoint}
        className={'file-processor-page-context-menu'}
        onClose={() => {
          toggleMenu(false);
          setContextMenuTargetEntry(undefined);
        }}
      >
        {components}
      </ControlledMenu>
    );
  };

  const showDecompressionOnMultiSelection = () => selectedEntriesRef.current && (
    (selectedEntriesRef.current.length == 1 && selectedEntriesRef.current[0].actions.includes(IwFsEntryAction.Decompress)) ||
    (selectedEntriesRef.current.length > 1 && selectedEntriesRef.current.every((e) => e.actions.includes(IwFsEntryAction.Decompress)))
  );

  return (
    <div className={'file-explorer-page'}>
      {/* <Loading visible={loading}> */}
      {renderContextMenu()}
      {root ? (
        <div className={'file-explorer'}>
          <div className="line1">
            <div className="left">
              <ClickableIcon
                colorType={'normal'}
                type={'return'}
                title={t('Return to previous directory')}
                className={`back ${lastWorkingDirectoryRef.current ? '' : 'disabled'} opt`}
                onClick={() => {
                  if (lastWorkingDirectoryRef.current) {
                    changeWorkingDirectory(lastWorkingDirectoryRef.current!);
                  }
                }}
              />
              <ClickableIcon
                colorType={'normal'}
                type={'arrowup'}
                title={t('Go to parent directory')}
                className={`back ${canGoBack ? '' : 'disabled'} opt`}
                onClick={() => {
                  if (canGoBack) {
                    gotoParent();
                  }
                }}
              />
              <ClickableIcon
                colorType={'normal'}
                type={'folder'}
                className={'folder'}
                title={t('Working directory')}
              />
              <div className="chain">
                {renderDirectoryChain()}
              </div>
              <ClickableIcon
                colorType={'normal'}
                className={'opt'}
                type={'edit-square'}
                title={t('Change working directory')}
                onClick={() => {
                  BApi.gui.openFolderSelector({ initialDirectory: getCurrentPath() })
                    .then(a => {
                      if (a.data && a.data != getCurrentPath()) {
                        changeWorkingDirectory(a.data);
                      }
                    });
                }}
              />
              <ClickableIcon
                colorType={'normal'}
                className={'opt'}
                type={'folder-open'}
                title={t('Open working directory in explorer')}
                onClick={() => {
                  OpenFileOrDirectory({
                    path: root?.path,
                  })
                    .invoke();
                }}
              />
            </div>
            <div className="right">
              <Button
                type={'normal'}
                size={'small'}
                onClick={() => {
                  OpenRecycleBin()
                    .invoke();
                }}
                className={'recycle-bin'}
              >
                <CustomIcon size={'small'} type={'folder-open'} />
                &nbsp;
                {t('Recycle bin')}
              </Button>
            </div>
          </div>
          <div className="line2">
            <div className="left">
              <Input
                value={keyword}
                placeholder={t('Filter files')}
                onChange={(v) => setKeyword(v)}
                innerBefore={(
                  <Icon type={'search'} style={{ margin: '0 4px' }} />
                )}
                onClick={() => {
                  selectedEntriesRef.current.forEach((e) => {
                    e.select(false);
                  });
                  setSelectedEntries([]);
                }}
              />
              <div className="counts">
                <span className={'total-count'}>{t('{{count}} items', { count: filteredEntriesCountRef.current })}</span>
                <span className={'selected-count'}>({t('{{count}} selected', { count: selectedEntries?.length })})</span>
              </div>
              <div className="selection-mode">
                <div className="label">
                  {t('Selection mode')}
                </div>
                <IceLabel className={'value'} inverse={false} status={SelectionModeIceLabelStatus[selectionMode]}>
                  {t(SelectionMode[selectionMode])}
                </IceLabel>
              </div>
            </div>
            <div className="right">
              {selectedEntries?.length > 0 && (
                <>
                  <Button
                    size={'small'}
                    onClick={() => {
                      moveToMediaLibraryCallback(selectedEntriesRef.current.map((e) => e.path));
                    }}
                  >
                    {t('Move')}(M)
                  </Button>
                  <Button
                    size={'small'}
                    type={'normal'}
                    onClick={() => WrapEntriesDialog.show({
                      entries: selectedEntriesRef.current,
                      onWrapEnd: onBatchWrappingEntriesCallback,
                    })}
                  >{t('Wrap')}(W)
                  </Button>
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
                  <Button
                    size={'small'}
                    warning
                    type={'normal'}
                    onClick={() => {
                      showDeleteConfirmDialog(selectedEntries);
                    }}
                  >{t('Remove')}(Del)
                  </Button>
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
              <Checkbox
                checked={allSelected}
                onClick={() => {
                  if (root) {
                    if (allSelected) {
                      rootRef.current?.filteredChildren.forEach((e) => e.select(false));
                      setSelectedEntries([]);
                    } else {
                      const filteredEntries = rootRef.current?.filteredChildren;
                      const invalidEntries = selectedEntries.filter((e) => filteredEntries.indexOf(e) == -1);
                      const newEntries = filteredEntries.filter((e) => selectedEntries.indexOf(e) == -1);
                      for (const ie of invalidEntries) {
                        ie.select(false);
                      }
                      for (const ne of newEntries) {
                        ne.select(true);
                      }
                      setSelectedEntries([...filteredEntries]);
                    }
                  }
                }}
              >{t('Select all')}
              </Checkbox>
            </div>
          </div>
          {renderRoot()}
        </div>
      ) : (
        <div className={'first-time-here'}>
          <Button
            type={'primary'}
            size={'large'}
            onClick={() => {
              OpenFolderSelector()
                .invoke((t) => {
                  if (t.data) {
                    changeWorkingDirectory(t.data);
                  }
                });
            }}
          >
            {t('Select a working directory')}
          </Button>
        </div>
      )}
      {/* </Loading> */}
    </div>
  );
};
