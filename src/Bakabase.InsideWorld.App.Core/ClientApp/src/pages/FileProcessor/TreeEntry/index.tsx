import React, { useCallback, useEffect, useImperativeHandle, useRef } from 'react';
import { Balloon, Button, Dialog, Icon, Message } from '@alifd/next';
import { List } from 'react-virtualized';
import { diff } from 'deep-diff';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import EditableFileName from './components/EditableFileName';
import { IwFsEntryTaskType, IwFsType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import '@szhsin/react-menu/dist/index.css';
import '@szhsin/react-menu/dist/transitions/slide.css';
import WrapEntriesDialog from '@/pages/FileProcessor/WrapEntriesDialog';
import type { IEntryRef } from '@/core/models/FileExplorer/Entry';
import { Entry, EntryProperty, EntryStatus, IwFsEntryAction } from '@/core/models/FileExplorer/Entry';
import { buildLogger, humanFileSize, uuidv4 } from '@/components/utils';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import MediaPlayer from '@/components/MediaPlayer';
import ContentsExtractionDialog from '@/pages/FileProcessor/ContentsExtractionDialog';
import DecompressBalloon from '@/pages/FileProcessor/TreeEntry/components/DecompressBalloon';
import BApi from '@/sdk/BApi';
import FileEntryMover from '@/pages/FileProcessor/FileEntryMover';
import './index.scss';
import ClickableIcon from '@/components/ClickableIcon';

interface ITreeEntryProps {
  entry: Entry;
  trySelect?: (e: Entry) => boolean;
  onDoubleClick?: (event, entry: Entry) => void;
  onDeleteKeyDown?: (event, en: Entry) => void;
  keyword?: string;
  onAllSelected?: (en) => void;
  style?: any;
  onChildrenLoaded?: (entry) => void;
  onContextMenu?: (e, entry: Entry) => void;
  onLoadFail?: (rsp, entry: Entry) => void;
  refreshInterval?: number | undefined;
  basicMode?: boolean;
  onClick?: (e: Entry) => any;
}

// todo: split this component into base components: simple, advance
const TreeEntry = React.forwardRef((props: ITreeEntryProps, ref) => {
  const {
    style: propsStyle,
    entry,
    trySelect = (e) => false,
    onDoubleClick = (event, en) => {
    },
    onDeleteKeyDown,
    keyword,
    onChildrenLoaded,
    onAllSelected = (en) => {
    },
    onContextMenu = (e, entry) => {
    },
    onLoadFail = (rsp, entry) => {
    },
    basicMode = false,
  } = props;
  const { t } = useTranslation();
  // useTraceUpdate(props, `[${entry.path}]`);
  const log = buildLogger(entry.path);
  const forceUpdate = useUpdate();

  // const prevStyle = usePrevious(propsStyle);
  // log(prevStyle == propsStyle, prevStyle, propsStyle);


  const entryRef = useRef(entry!);

  // Infrastructures
  const loadingChildrenRef = useRef(false);

  const childrenStylesRef = useRef({});

  const domRef = useRef<HTMLElement | null>(null);

  const hashRef = useRef(uuidv4());

  // Functions
  const currentEntryDomRef = useRef<any>();

  const initialize = useCallback(async () => {
    if (!entry.isRoot) {
      await entry.initialize();
    }
    if (entry.expanded) {
      const rendered = await expand();
      if (!rendered) {
        entry.renderChildren();
      }
    }
    if (!entry.recalculateChildrenWidth()) {
      forceUpdate();
    }

    // if (entry.isRoot) {
    //   MediaPlayer.show({
    //     files: entry.children!.map(a => ({
    //       path: a.path,
    //     })),
    //     defaultActiveIndex: 0,
    //   });
    // }
  }, []);

  useEffect(() => {
    log('initializing, status: ', EntryStatus[entry.status], 'expanded:', entry.expanded, `children size:${entry.childrenWidth}x${entry.childrenHeight}`, 'filtered children', entry.filteredChildren);
    initialize();

    const resizeObserver = new ResizeObserver((c) => {
      if (domRef.current) {
        log('Size changed', domRef.current, domRef.current?.clientWidth);
        entryRef.current.childrenWidth = domRef.current!.clientWidth - (entryRef.current.isRoot ? 0 : 15);
        forceUpdate();
      }
    });
    resizeObserver.observe(domRef.current!);

    log('initialized');

    return () => {
      resizeObserver.disconnect();
    };
  }, []);

  useEffect(() => {
    entryRef.current = entry;
  }, [entry]);

  const renderFileSystemInfo = useCallback(() => {
    // log('Rendering fs info');
    const en = entryRef.current;
    if (en.type != IwFsType.Invalid) {
      const elements: any[] = [];
      if (en.childrenCount != undefined && en.type == IwFsType.Directory && en.properties.includes(EntryProperty.ChildrenCount)) {
        elements.push(
          <>
            <CustomIcon type={'file'} size={'xs'} />
            {en.childrenCount}
          </>,
        );
      }
      if (en.size != undefined && en.size > 0 && en.properties.includes(EntryProperty.Size)) {
        return (
          <div className={'file-system-info'}>
            {humanFileSize(en.size, false)}
          </div>
        );
      }

      if (elements.length > 0) {
        return (
          <div className={'file-system-info'}>
            {elements.map((e, i) => (
              <div key={i}>
                {e}
              </div>
            ))}
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
        keyword={keyword}
        onDeleteKeyDown={onDeleteKeyDown}
        onDoubleClick={onDoubleClick}
        key={e.id}
        ref={(r: IEntryRef) => {
          e.ref = r;
        }}
        entry={e}
        onAllSelected={onAllSelected}
        trySelect={trySelect}
        style={s}
        onLoadFail={onLoadFail}
        basicMode={basicMode}
      />
    );
  }, [entryRef.current.children, entryRef.current.expanded]);

// todo
  useEffect(() => {
    // console.log(123221312444, 'entries changes');
  }, [entryRef.current.children]);

  const domCallback = useCallback((node?: HTMLElement | null) => {
    domRef.current = node!;
    if (node) {
      // resize();
    }
  }, []);

  const virtualListRef = useRef<any>();

  useImperativeHandle(ref, (): IEntryRef => ({
    select: (selected) => {
      if (entry?.selected != selected) {
        entry.selected = selected;
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
  }), []);

  /**
   * Height of only one row will cause the full-re-rendering of the list, so we do not need to render a specific child.
   */
  const renderChildren = useCallback(() => {
    log('Rendering children', entry, entry.filteredChildren);

    if (domRef.current?.parentElement) {
      const newWidth = domRef.current.parentElement.clientWidth + (entry.isRoot ? 0 : -15);
      const newHeight = entryRef.current.childrenHeight;

      log(`Recalculated children size: ${newWidth}x${newHeight}`);

      for (let i = 0; i < entry.filteredChildren.length; i++) {
        virtualListRef.current?.recomputeRowHeights(i);
      }

      forceUpdate();
    }
  }, []);

  const onKeyDown = useCallback((e) => {
    log('Key down', e.key, e.ctrlKey, e.shiftKey, e.altKey, e.metaKey, e);
    switch (e.key) {
      case 'Delete':
        if (onDeleteKeyDown) {
          onDeleteKeyDown(e, entry);
          e.stopPropagation();
        }
        break;
      case 'a':
        if (e.ctrlKey && !basicMode) {
          for (const e1 of entryRef.current.filteredChildren) {
            e1.select(true);
          }
          onAllSelected(entryRef.current.parent);
          e.stopPropagation();
        }
        break;
      case 'e': {
        if (!basicMode) {
          ContentsExtractionDialog.show({
            entry: entryRef.current,
          });
          e.stopPropagation();
          break;
        }
      }
      case 'p': {
        if (!basicMode) {
          play(entryRef.current);
          e.stopPropagation();
        }
        break;
      }
      default:
        return;
    }
  }, []);

  const collapse = useCallback(() => {
    if (entryRef.current.expanded) {
      entryRef.current.expanded = false;
      entry.renderChildren();
    }
  }, []);

  /**
   *
   * @param refresh
   * @return rendered
   */
  const expand = async (refresh: boolean = false): Promise<boolean> => {
    if (refresh) {
      entry.clearChildren();
    }
    if (!entryRef.current.expanded || !entryRef.current.children) {
      if (!entryRef.current.children) {
        if (loadingChildrenRef.current) {
          return false;
        }
        loadingChildrenRef.current = true;
        const rsp = await BApi.file.getChildrenIwFsInfo({ root: entryRef.current.path });
        log(`Loaded ${rsp.data?.entries?.length} children`);
        loadingChildrenRef.current = false;
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
            parent: entry,
            properties: entry.properties,
          }));
        }
      }
      entryRef.current.expanded = true;
      // entryRef.current.root.patchFilter({ keyword });
      entry.expireFilteredChildren();
      entry.renderChildren();
      // entry.parent?.renderChild(entry);
      // onHeightChanged(entryRef.current);
      onChildrenLoaded && onChildrenLoaded(entryRef.current);
      return true;
    }
    return false;
  };

  useUpdateEffect(() => {
    entryRef.current.root.patchFilter({ keyword });
  }, [keyword]);

  const { actions } = entry;

  const optComponents: any[] = [];

  if (actions.includes(IwFsEntryAction.Decompress)) {
    optComponents.push(
      <DecompressBalloon
        key={'decompress'}
        entry={entry}
        passwords={entry.passwordsForDecompressing}
        trigger={(
          <Button size={'small'}>
            <CustomIcon type={'rar'} />
            {t('Decompress')}(D)
          </Button>
        )}
      />,
    );
  }

  optComponents.push(
    <Button
      key={'wrap'}
      type={'normal'}
      size={'small'}
      onClick={() => {
        WrapEntriesDialog.show({
          entries: [entry],
        });
      }}
    >
      <CustomIcon type={'package'} size={'small'} />
      {t('Wrap')}(W)
    </Button>,
  );

  optComponents.splice(optComponents.length, 0,
    <Button
      key={'move'}
      size={'small'}
      onClick={(e) => {
        FileEntryMover.show({ paths: [entry.path] });
      }}
    >
      <CustomIcon type={'move'} size={'small'} />
      {t('Move')}(M)
    </Button>,
  );

  const renderTaskError = () => {
    if (entryRef.current.task && entryRef.current.task.error) {
      const text = `${IwFsEntryTaskType[entryRef.current.task.type]}:${entryRef.current.task.error}`;
      return (
        <Icon
          type={'error'}
          className={'task-error info'}
          title={text}
          onClick={() => {
            Dialog.show({
              v2: true,
              width: 'auto',
              footerActions: ['cancel'],
              cancelProps: {
                children: t('Close'),
              },
              closeMode: ['close', 'mask', 'esc'],
              title: t('Error'),
              content: (
                <pre>{text}</pre>
              ),
            });
          }}
        />
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
        files: [entry.path],
        defaultActiveIndex: 0,
      });
    }
  }, []);

  const renderOperationsAfterEntryName = useCallback(() => {
    const components = [
      (
        <ClickableIcon
          key={1}
          type={'folder-open'}
          colorType={'normal'}
          size={16}
          onClick={(e) => {
            e.stopPropagation();
            BApi.tool.openFileOrDirectory({
              path: entry.path,
              openInDirectory: entry.type != IwFsType.Directory,
            });
          }}
        />
      ),
    ];

    if (entryRef.current.type == IwFsType.Directory && entryRef.current.childrenCount && entryRef.current.childrenCount > 0) {
      if (!basicMode) {
        components.push(
          <Balloon.Tooltip
            key={2}
            triggerType={'hover'}
            trigger={(
              <ClickableIcon
                type={'Arrowextractinterface'}
                colorType={'normal'}
                onClick={(e) => {
                  e.stopPropagation();
                  ContentsExtractionDialog.show({
                    entry,
                  });
                }}
              />
            )}
          >
            (E){t('Extract children to parent and delete current directory')}
          </Balloon.Tooltip>,
        );
      }
      components.push(
        <ClickableIcon
          size={16}
          key={'refresh'}
          type={'sync'}
          colorType={'normal'}
          onClick={(e) => {
            e.stopPropagation();
            expand(true);
          }}
        />,
      );
    }
    return components;
  }, []);

  const renderEntryStatus = useCallback(() => {
    switch (entryRef.current.status) {
      case EntryStatus.Loading:
        return (
          <div className={'item'}>
            <Icon type={'loading'} size={'small'} />
          </div>
        );
      case EntryStatus.Error:
        return (
          <div className={'item'}>
            <Balloon
              trigger={<CustomIcon
                onClick={e => e.stopPropagation()}
                type={'warning-circle'}
                style={{ color: 'red' }}
                size={'small'}
              />}
              triggerType={'hover'}
              align={'t'}
              v2
              closable={false}
            >
              <ul style={{ color: 'red' }}>
                {Object.keys(entryRef.current.errors)
                  .map(e => {
                    return (
                      <li key={e}>{entryRef.current.errors[e]}</li>
                    );
                  })}
              </ul>
            </Balloon>
          </div>
        );
      default:
        return;
    }
  }, []);

  log('Rendering', 'children width', entry.childrenWidth, domRef.current?.clientWidth, domRef.current, entry);

  return (
    <div
      className={'entry'}
      tabIndex={0}
      style={propsStyle}
      ref={domCallback}
      onClick={e => {
        e.stopPropagation();
      }}
      onDoubleClick={(e) => {
        // console.log('double clicked');
        e.stopPropagation();
        onDoubleClick(e, entry);
      }}
    >
      {!entry.isRoot && (
        <div className={'entry-main-container'}>
          {entryRef.current.type == IwFsType.Invalid && (
            <div className="invalid-cover" />
          )}
          {entryRef.current.task && !entryRef.current.task.error && (
            <div className="running-task-cover">
              <div
                className="progress"
              >
                <div className={'bar'} style={{ width: `${entryRef.current.task.percentage}%` }} />
                <Icon type={'loading'} size={'small'} />
                &nbsp;
                <div className="percentage">{entryRef.current.task.name}&nbsp;{entryRef.current.task.percentage}%</div>
              </div>
              <div className="stop">
                <Button
                  warning
                  type={'primary'}
                  size={'small'}
                  onClick={() => {
                    Dialog.confirm({
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
              if (!entry.selected) {
                if (trySelect(entry)) {
                  entry.selected = !entry.selected;
                  forceUpdate();
                }
              }
              onContextMenu(e, entry);
            }}
            ref={r => {
              currentEntryDomRef.current = r;
              // log('dom ref retrived', r);
            }}
            onKeyDown={onKeyDown}
            tabIndex={0}
            className={`entry-main entry-keydown-listener ${entry?.selected ? 'selected' : ''} ${entry.expanded ? 'expanded' : ''}`}
            onClick={() => {
              const r = trySelect(entry);
              log(`Trying ${entry?.selected ? 'unselect' : 'select'} ${entry?.name}, and get blocked: ${!r}`);
              if (r) {
                entry.selected = !entry?.selected;
                forceUpdate();
              }
            }}
          >
            <div className="left">
              <div className="things-before-name">
                {entry.status == EntryStatus.Default ? (
                  // Expandable
                  <div className="item">
                    {(entry.expandable) && (
                      entry.expanded ? (
                        <div
                          className={'fold'}
                          onClick={(e) => {
                            e.stopPropagation();
                            collapse();
                          }}
                        >
                          <Icon type="minus" size={'xs'} />
                        </div>
                      ) : (
                        <div
                          className={'unfold'}
                          onClick={(e) => {
                            e.stopPropagation();
                            expand();
                          }}
                        >
                          <Icon type="add" size={'xs'} />
                        </div>
                      )
                    )}
                  </div>
                ) : (
                  // Status
                  renderEntryStatus()
                )}
                {/* Icon */}
                {entry && (
                  <div className="item">
                    <FileSystemEntryIcon size={18} path={entry.path} isDirectory={entry.type == IwFsType.Directory} />
                  </div>
                )}
              </div>
              <EditableFileName
                entry={entry}
                disabled={basicMode}
              />
              <div className="things-after-name">
                {actions.includes(IwFsEntryAction.Play) && !basicMode && (
                  <CustomIcon
                    className={'preview'}
                    type={'eye'}
                    onClick={(e) => {
                      e.stopPropagation();
                      play(entry);
                    }}
                  />
                )}
                {renderOperationsAfterEntryName()}
                {renderFileSystemInfo()}
                {renderTaskError()}
              </div>
            </div>
            <div className="right">
              {!basicMode && (
                optComponents
              )}
            </div>
          </div>
        </div>
      )}
      {entryRef.current.filteredChildren?.length > 0 && entryRef.current.expanded && (
        <div
          className={`entry-children ${entry.isRoot ? 'root' : ''}`}
        >
          {entryRef.current.childrenHeight > 0 && (
            <List
              ref={virtualListRef}
              rowHeight={virtualListRowHeightCallback}
              width={entry.childrenWidth}
              height={entry.childrenHeight}
              rowCount={entryRef.current.filteredChildren.length}
              rowRenderer={virtualListRowRendererCallback}
              renderHash={hashRef.current}
              overscanRowCount={5}
            />
          )}
        </div>
      )}
    </div>
  );
});
const MemoTreeEntry = React.memo(TreeEntry);

export default MemoTreeEntry;
