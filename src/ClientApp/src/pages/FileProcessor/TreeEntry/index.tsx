import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Message } from '@alifd/next';
import { List } from 'react-virtualized';
import { diff } from 'deep-diff';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { CloseCircleOutlined, EyeOutlined, FileOutlined, InfoCircleOutlined } from '@ant-design/icons';
import EditableFileName from './components/EditableFileName';
import OperationButton from './components/OperationButton';
import RightOperations from './components/RightOperations';
import { IwFsEntryTaskType, IwFsType } from '@/sdk/constants';
import '@szhsin/react-menu/dist/index.css';
import '@szhsin/react-menu/dist/transitions/slide.css';
import type { IEntryFilter } from '@/core/models/FileExplorer/Entry';
import { Entry, EntryProperty, EntryStatus, IwFsEntryAction } from '@/core/models/FileExplorer/Entry';
import { buildLogger, humanFileSize, standardizePath, uuidv4 } from '@/components/utils';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import MediaPlayer from '@/components/MediaPlayer';
import BApi from '@/sdk/BApi';
import './index.scss';
import { Button, Chip, Modal, Spinner } from '@/components/bakaui';
import TailingOperations from './components/TailingOperations';
import LeftIcon from './components/LeftIcon';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

export type Capability =
  'wrap'
  | 'extract'
  | 'move'
  | 'delete'
  | 'rename'
  | 'decompress'
  | 'delete-all-by-name'
  | 'group'
  | 'play';

export type TreeEntryProps = {
  entry: Entry;
  onDoubleClick?: (event, entry: Entry) => void;
  /*
    * Return false will block the selection of the entry
   */
  switchSelective?: (e: Entry) => boolean;
  onChildrenLoaded?: (e: Entry) => void;
  filter?: IEntryFilter;
  expandable?: boolean;
  capabilities?: Capability[];

  style?: any;
  onContextMenu?: (e, entry: Entry) => void;
  onLoadFail?: (rsp, entry: Entry) => void;
};

// todo: split this component into base components: simple, advance
const TreeEntry = (props: TreeEntryProps) => {
  const {
    style: propsStyle,
    entry: propsEntry,
    filter,
    switchSelective,
    onDoubleClick,
    capabilities,
    onChildrenLoaded,
    onContextMenu = (e, entry) => {
    },
    onLoadFail = (rsp, entry) => {
    },
    expandable = true,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const [entry, setEntry] = useState(propsEntry);
  const entryRef = useRef(entry);
  // useTraceUpdate(props, `[${entryRef.current.path}]`);
  const log = buildLogger(entryRef.current.path);

  // Infrastructures
  const loadingChildrenRef = useRef(false);
  const childrenStylesRef = useRef({});
  const domRef = useRef<HTMLElement | null>(null);
  const hashRef = useRef(uuidv4());
  const [loading, setLoading] = useState(false);
  const pendingRenderingRef = useRef(false);

  useUpdateEffect(() => {
    entryRef.current = entry;
  }, [entry]);

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
    log('Initializing', e);

    if (entryRef.current) {
      log('Disposing previous entry', entryRef.current);
      await entryRef.current.dispose();
    }

    entryRef.current = e;

    entryRef.current.ref = {
      select: (selected) => {
        if (entryRef.current?.selected != selected) {
          entryRef.current.selected = selected;
          if (selected) {
            log('Focus');
            currentEntryDomRef.current.focus();
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
        const row = entryRef.current.filteredChildren.findIndex(e => e.path == path);
        log('scrolling to', path, row, virtualListRef.current);
        virtualListRef.current?.scrollToRow(row);
      },
      setLoading,
    };

    log('initializing, status: ', EntryStatus[entryRef.current.status],
      'expanded:', entryRef.current.expanded,
      `children size:${entryRef.current.childrenWidth}x${entryRef.current.childrenHeight}`,
      'filtered children', entryRef.current.filteredChildren);

    await entryRef.current.initialize();

    if (entryRef.current.expanded) {
      log('Expanding');
      const rendered = await expand();
      if (!rendered) {
        entryRef.current.renderChildren();
      }
    }
    if (!entryRef.current.recalculateChildrenWidth()) {
      forceUpdate();
    }
    log('Initialized');
  }, []);

  useEffect(() => {
    // log('0987654321');
    initialize(entry);

    const resizeObserver = new ResizeObserver((c) => {
      if (domRef.current) {
        log('Size changed', domRef.current, domRef.current?.clientWidth);
        entryRef.current.childrenWidth = domRef.current!.clientWidth - (entryRef.current.isRoot ? 0 : 15);
        forceUpdate();
      }
    });
    resizeObserver.observe(domRef.current!);

    return () => {
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
      if (en.childrenCount != undefined && en.isDirectoryOrDrive && en.properties.includes(EntryProperty.ChildrenCount)) {
        elements.push(
          <Chip
            size={'sm'}
            variant={'light'}
            color={'secondary'}
          >
            <FileOutlined className={'text-sm'} />
            {en.childrenCount}
          </Chip>,
        );
      }
      if (en.size != undefined && en.size > 0 && en.properties.includes(EntryProperty.Size)) {
        elements.push(
          <Chip
            size={'sm'}
            variant={'light'}
            color={'secondary'}
          >
            {humanFileSize(en.size, false)}
          </Chip>,
        );
      }

      if (elements.length > 0) {
        return (
          <div className={'flex items-center'}>
            {elements}
          </div>
        );
      }
    }
    return;
  }, []);

  const virtualListRowHeightCallback = useCallback(({ index }) => {
    const child = entryRef.current.filteredChildren[index];
    if (child) {
      const h = child.totalHeight;
      log(`[VirtualListRowHeight] Getting row height of [${child?.name}]:${h}, with children height:[${child.childrenHeight}]`);
      return h;
    } else {
      log(`[VirtualListRowHeight] Getting row height of [${index}]:Unknown`);
      return 0;
    }
  }, [entryRef.current.children, entryRef.current.expanded]);

  const virtualListRowRendererCallback = useCallback(({
                                                        index,
                                                        style,
                                                        isVisible,
                                                        isScrolling,
                                                      }) => {
    if (entryRef.current.filteredChildren.length <= index) {
      log(`[VirtualListRowRenderer] Rendering a child with overflow index: ${index} >= count of children: ${entryRef.current.filteredChildren.length}, ignoring`);
      return;
    }
    // todo: style always causes the re-rendering of the child, even if it's the same
    const e = entryRef.current.filteredChildren[index];
    log(`[VirtualListRowRenderer]Rendering children row:[${e.name}]`, `height:[${style.height}]`, `children height:[${e.childrenHeight}]`, 'status:', EntryStatus[e.status]);
    const prevStyle = childrenStylesRef.current[e.path];
    const differences = diff(prevStyle, style);
    if (differences) {
      // log(`[VirtualList]Applying new style: ${e.path}`, differences, prevStyle, style);
      childrenStylesRef.current[e.path] = style;
      style.id = uuidv4();
    }

    const s = childrenStylesRef.current[e.path];

    return (
      <MemoTreeEntry
        onContextMenu={onContextMenu}
        filter={filter}
        capabilities={capabilities}
        onDoubleClick={onDoubleClick}
        key={e.id}
        entry={e}
        switchSelective={switchSelective}
        style={s}
        onLoadFail={onLoadFail}
        expandable={expandable}
      />
    );
  }, [entryRef.current.children, entryRef.current.expanded, onDoubleClick]);

  const domCallback = useCallback((node?: HTMLElement | null) => {
    domRef.current = node!;
    if (node) {
      // resize();
    }
  }, []);

  const virtualListRef = useRef<any>();

  /**
   * Height of only one row will cause the full-re-rendering of the list, so we do not need to render a specific child.
   */
  const renderChildren = useCallback(() => {
    log('Rendering children', entryRef.current, entryRef.current.filteredChildren);
    pendingRenderingRef.current = true;

    if (domRef.current?.parentElement) {
      const newWidth = domRef.current.parentElement.clientWidth + (entryRef.current.isRoot ? 0 : -15);
      const newHeight = entryRef.current.childrenHeight;

      log(`Recalculated children size: ${newWidth}x${newHeight}`);

      for (let i = 0; i < entryRef.current.filteredChildren.length; i++) {
        virtualListRef.current?.recomputeRowHeights(i);
      }

      forceUpdate();
    }
  }, []);

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
  const expand = async (refresh: boolean = false): Promise<boolean> => {
    if (refresh) {
      entryRef.current.clearChildren();
      log('Clear children');
    }
    if (!entryRef.current.expanded || !entryRef.current.children) {
      if (!entryRef.current.children) {
        if (loadingChildrenRef.current) {
          return false;
        }
        loadingChildrenRef.current = true;
        setLoading(true);
        // @ts-ignore
        const rsp = await BApi.file.getChildrenIwFsInfo({ root: entryRef.current.path }, { ignoreError: () => true });
        log(`Loaded ${rsp.data?.entries?.length} children`);
        setLoading(false);
        if (rsp.code) {
          onLoadFail(rsp, entryRef.current);
          return false;
        }
        if (rsp.data) {
          const {
            entries = [],
          } = rsp.data || {};
          // @ts-ignore
          entryRef.current.children = entries!.map((e) => new Entry({
            ...e,
            parent: entryRef.current,
            properties: entryRef.current.properties,
          }));
        }
      }
      entryRef.current.expanded = true;
      entryRef.current.expireFilteredChildren();
      entryRef.current.renderChildren();
      return true;
    }
    return false;
  };

  const triggerChildrenLoaded = useCallback(() => {
    log('Trigger onChildrenLoaded');
    onChildrenLoaded?.(entryRef.current);
  }, []);

  useUpdateEffect(() => {
    entryRef.current.root.patchFilter(filter);
    forceUpdate();
  }, [filter]);

  const { actions } = entryRef.current;

  const renderTaskError = () => {
    if (entryRef.current.task && entryRef.current.task.error) {
      const text = `${IwFsEntryTaskType[entryRef.current.task.type]}:${entryRef.current.task.error}`;
      return (
        <Button
          size={'sm'}
          variant={'light'}
          isIconOnly
          color={'danger'}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              size: 'xl',
              title: t('Error'),
              children: (
                <pre>{text}</pre>
              ),
            });
          }}
        >
          <CloseCircleOutlined className={'text-base'} />
        </Button>
      );
    }
    return;
  };

  const play = useCallback((entry) => {
    if (entry.type == IwFsType.Directory) {
      BApi.file.getAllFiles({
        path: entry.path,
      })
        .then((x) => {
          if (!x.code) {
            if (!x.data || (x.data.length == 0)) {
              return Message.notice(t('No files to preview'));
            }
            MediaPlayer.show({
              files: x.data!.map(a => ({
                path: a,
              })),
              defaultActiveIndex: 0,
            });
          }
        });
    } else {
      MediaPlayer.show({
        files: [{ path: entry.path }],
        defaultActiveIndex: 0,
      });
    }
  }, []);

  // log('Rendering', 'children width', entryRef.current.childrenWidth, domRef.current?.clientWidth, domRef.current, entryRef.current);

  return (
    <div
      className={`tree-entry ${entryRef.current.isRoot ? 'h-full' : ''}`}
      tabIndex={0}
      style={propsStyle}
      ref={domCallback}
      onDoubleClick={e => {
        e.stopPropagation();
        log('Double clicked', entryRef.current);
        onDoubleClick?.(e, entryRef.current);
      }}
    >
      {!entryRef.current.isRoot && (
        <div
          className={'entry-main-container'}
          onClick={e => {
            e.stopPropagation();
          }}
        >
          {entryRef.current.type == IwFsType.Invalid && (
            <div className="invalid-cover" />
          )}
          {entryRef.current.task && !entryRef.current.task.error && (
            <div className="running-task-cover">
              <div
                className="progress"
              >
                <div className={'bar'} style={{ width: `${entryRef.current.task.percentage}%` }} />
                <Spinner size="sm" />
                &nbsp;
                <div className="percentage">{entryRef.current.task.name}&nbsp;{entryRef.current.task.percentage}%</div>
              </div>
              <div className="stop">
                <Button
                  color={'warning'}
                  size={'small'}
                  onClick={() => {
                    createPortal(Modal, {
                      title: t('Sure to stop?'),
                      onOk: () => new Promise((resolve, reject) => {
                        BApi.backgroundTask.stopBackgroundTask(entryRef.current!.task!.backgroundTaskId)
                          .then((t) => {
                            if (!t.code) {
                              resolve(t);
                            } else {
                              reject();
                            }
                          })
                          .catch((e) => {
                            reject();
                          });
                      }),
                    });
                  }}
                >{t('Stop')}
                </Button>
              </div>
            </div>
          )}
          <div
            onContextMenu={(e) => {
              if (!entryRef.current.selected) {
                if (!switchSelective || switchSelective(entryRef.current)) {
                  entryRef.current.selected = true;
                  forceUpdate();
                }
              }
              onContextMenu(e, entryRef.current);
            }}
            ref={r => {
              currentEntryDomRef.current = r;
              // log('dom ref retrived', r);
            }}
            tabIndex={0}
            className={`entry-main entry-keydown-listener ${entryRef.current?.selected ? 'selected' : ''} ${entryRef.current.expanded ? 'expanded' : ''}`}
            onClick={() => {
              const r = !switchSelective || switchSelective(entryRef.current);
              log(`Trying ${entryRef.current?.selected ? 'unselect' : 'select'} ${entryRef.current?.name}, and get blocked: ${!r}`);
              if (r) {
                entryRef.current.selected = !entryRef.current?.selected;
                forceUpdate();
              }
            }}
          >
            <div className="left">
              <div className="things-before-name">
                <LeftIcon
                  entry={entryRef.current}
                  expandable={expandable}
                  loading={loading}
                />
                {entryRef.current && (
                  <div className="item">
                    <FileSystemEntryIcon
                      size={18}
                      path={entryRef.current.path}
                      showAsDirectory={entry.isDirectoryOrDrive}
                    />
                  </div>
                )}
              </div>
              <EditableFileName
                isDirectory={entry.isDirectoryOrDrive}
                path={entry.path}
                name={entry.name}
                disabled={entry.isDrive || !capabilities?.includes('rename') || entry.status == EntryStatus.Error}
              />
              <div className="flex items-center">
                {actions.includes(IwFsEntryAction.Play) && capabilities?.includes('play') && (
                  <OperationButton
                    onClick={(e) => {
                      play(entryRef.current);
                    }}
                    isIconOnly
                    color={'primary'}
                  >
                    <EyeOutlined className={'text-base'} />
                  </OperationButton>
                )}
                <TailingOperations
                  entry={entry}
                  capabilities={capabilities}
                />
                {renderFileSystemInfo()}
                {renderTaskError()}
              </div>
            </div>
            <div className="right">
              <RightOperations
                entry={entryRef.current}
                capabilities={capabilities}
              />
            </div>
          </div>
        </div>
      )}
      {entryRef.current.expanded && ((entryRef.current.filteredChildren?.length > 0) ? (
        <div
          className={`entry-children ${entryRef.current.isRoot ? 'root' : ''}`}
        >
          {entryRef.current.childrenHeight > 0 && (
            <List
              ref={r => {
                virtualListRef.current = r;
                if (r) {
                  if (pendingRenderingRef.current) {
                    pendingRenderingRef.current = false;
                    triggerChildrenLoaded();
                  }
                }
              }}
              rowHeight={virtualListRowHeightCallback}
              width={entryRef.current.childrenWidth}
              height={entryRef.current.childrenHeight}
              rowCount={entryRef.current.filteredChildren.length}
              rowRenderer={virtualListRowRendererCallback}
              renderHash={hashRef.current}
              overscanRowCount={5}
            />
          )}
        </div>
      ) : loading ? (
        <div className={'flex justify-center items-center py-2'}>
          <Spinner size={'sm'} />
        </div>
      ) : (
        <div className={'flex justify-center items-center gap-2 opacity-70 py-2'}>
          <InfoCircleOutlined />
          <div>{t('No content')}</div>
        </div>
      ))}
    </div>
  );
};
const MemoTreeEntry = React.memo(TreeEntry);

export default MemoTreeEntry;
