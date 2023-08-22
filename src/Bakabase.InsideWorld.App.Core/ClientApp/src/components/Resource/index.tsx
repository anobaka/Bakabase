import { Balloon, Button, Dialog, Dropdown, Menu, Message, Overlay, Rating, Tag } from '@alifd/next';
import React, { useCallback, useEffect, useImperativeHandle, useReducer, useRef, useState } from 'react';
import { diff } from 'deep-diff';
import type Queue from 'queue';
import { Trans, useTranslation } from 'react-i18next';
import CustomIcon from '@/components/CustomIcon';
import {
  ClearResourceTask,
  OpenFileOrDirectory,
  OpenResourceDirectory,
  OpenUrlInDefaultBrowser,
  PatchResource,
  PlayResourceFile,
  RemoveResource,
} from '@/sdk/apis';
import { buildLogger, splitPathIntoSegments, useTraceUpdate, uuidv4 } from '@/components/utils';
import './index.scss';
import { PlaylistItemType, ResourceLanguage, ResourceTaskOperationOnComplete, ResourceTaskType } from '@/sdk/constants';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';
import store from '@/store';
import { PlaylistCollection } from '@/components/Playlist';
import { Tag as TagDto } from '@/core/models/Tag';
import BApi from '@/sdk/BApi';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import FavoritesSelector from '@/pages/Resource/components/FavoritesSelector';
import ShowResourceMediaPlayer from '@/components/Resource/components/ShowResourceMediaPlayer';
import ClickableIcon from '@/components/ClickableIcon';
import type ResourceTask from '@/core/models/ResourceTask';
import ResourceEnhancementsDialog from '@/components/Resource/components/ResourceEnhancementsDialog';
import type SimpleSearchEngine from '@/core/models/SimpleSearchEngine';
import MediaLibraryPathSelector from '@/components/MediaLibraryPathSelector';
import TagSelector from '@/components/TagSelector';
import MediaPreviewer from '@/components/MediaPreviewer';

const { Popup } = Overlay;

const languageIconMapping = {
  [ResourceLanguage.Chinese]: 'china',
};

export interface IResourceHandler {
  id: number;
  reload: (ct: AbortSignal) => Promise<any>;
}

interface Props {
  showBiggerOnHover?: boolean;
  resource: any;
  coverHash?: string;
  queue?: Queue;
  onRemove?: (id: number) => void;
  showBiggerCoverOnHover?: boolean;
  searchEngines?: SimpleSearchEngine[] | null;
  ct: AbortSignal;
  onTagSearch?: (tagId: number, append: boolean) => any;
}

const displayModes = ['limited', 'full'];

const Resource = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onRemove = (id) => {
    },
    showBiggerCoverOnHover = true,
    searchEngines = [],
    onTagSearch = (tagId: number, append: boolean) => {
    },
    queue,
    ct = new AbortController().signal,
  } = props;

  const [previewerVisible, setPreviewerVisible] = useState(false);
  const previewerHoverTimerRef = useRef<any>();

  const { t } = useTranslation();
  const log = buildLogger(`Resource:${resource.id}|${resource.rawFullname}`);

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const [playableFiles, setPlayableFiles] = useState<string[]>([]);

  useImperativeHandle(ref, (): IResourceHandler => {
    return {
      id: resource.id,
      reload: reload,
    };
  }, []);

  const taskRef = useRef<ResourceTask>();

  useTraceUpdate(props, `[${resource.rawName}]`);

  log('Rendering');

  const loadPlayableFiles = useCallback(async (ct: AbortSignal) => {
    const pRsp = await BApi.resource.getResourcePlayableFiles(resource.id, { signal: ct });
    setPlayableFiles(pRsp.data || []);
  }, []);

  const initialize = useCallback(async (ct: AbortSignal) => {
    if (coverRef.current) {
      await coverRef.current?.reload(ct);
    }
    await loadPlayableFiles(ct);
    log('Initialized');
  }, []);

  useEffect(() => {
    if (queue) {
      queue.push(async () => await initialize(ct));
    } else {
      initialize(ct);
    }

    const unsubscribe = store.subscribe(() => {
      const task = store.getState()
        .resourceTasks
        ?.find((t) => t.id == resource.id);
      if (task || taskRef.current) {
        const differences = diff(task, taskRef.current);
        if (differences) {
          if (task?.percentage == 100 && task.operationOnComplete == ResourceTaskOperationOnComplete.RemoveOnResourceView) {
            onRemove(resource.id);
          } else {
            const prevTask = taskRef.current;
            taskRef.current = task;
            log('TaskChanged', differences, 'current: ', task, 'previous: ', prevTask);
            forceUpdate();
          }
        }
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  const openFile = (id: number) => {
    OpenResourceDirectory({
      id,
    })
      .invoke((t) => {
        if (!t.code) {
          Message.success(t('Opened'));
        }
      });
  };

  const play = (file) => {
    PlayResourceFile({
      id: resource.categoryId,
      file,
    })
      .invoke((a) => {
        if (!a.code) {
          Message.success(t('Opened'));
        }
      });
  };

  const reload = useCallback(async (ct: AbortSignal) => {
    const newResourceRsp = await BApi.resource.getResourcesByKeys({ ids: [resource.id] });
    if (!newResourceRsp.code) {
      const nr = (newResourceRsp.data || [])[0];
      if (nr) {
        Object.keys(nr)
          .forEach((k) => {
            resource[k] = nr[k];
          });
        if (coverRef.current) {
          await coverRef.current!.reload(ct);
        }
        await loadPlayableFiles(ct);
      }
    } else {
      throw new Error(newResourceRsp.message!);
    }
  }, []);

  const clickPlayButton = useCallback(() => {
    console.log(playableFiles);
    if (playableFiles.length == 1) {
      play(playableFiles[0]);
    } else {
      Dialog.show({
        v2: true,
        content: (
          <Tag.Group>
            {playableFiles.map((a) => {
              const segments = splitPathIntoSegments(a);
              return (
                <Balloon.Tooltip
                  key={a}
                  trigger={(
                    <Tag
                      title={a}
                    >
                      <div
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          cursor: 'pointer',
                        }}
                        onClick={() => {
                          play(a);
                        }}
                      >
                        <CustomIcon type="play-circle" />
                        {segments[segments.length - 1]}
                      </div>
                    </Tag>
                  )}
                  align={'t'}
                  triggerType={'hover'}
                >
                  {t('Use player to play')}
                </Balloon.Tooltip>
              );
            })}
          </Tag.Group>
        ),
        footer: false,
        closeMode: ['esc', 'mask', 'close'],
      });
    }
  }, [playableFiles]);

  const remove = useCallback(() => {
    if (confirm(t('Are you sure to delete this resource?(files on your disk will also be deleted)'))) {
      RemoveResource({
        id: resource.id,
      })
        .invoke((a) => {
          if (!a.code) {
            if (onRemove) {
              onRemove(resource.id);
            }
          }
        });
    }
  }, []);

  const open = () => {
    openFile(resource.id);
  };

  const coverRef = useRef<IResourceCoverRef>();

  const renderCover = () => {
    const elementId = `resource-${resource.id}`;
    const languageIconName = languageIconMapping[resource.language];
    let languageIcon;
    if (languageIconName) {
      languageIcon = (<img className={'language'} src={require(`@/assets/${languageIconName}.svg`)} />);
    }
    return (
      <div
        className={'cover-rectangle'}
        id={elementId}
      >
        <div
          className="cover-container"
          onMouseOver={() => {
            if (!previewerHoverTimerRef.current) {
              previewerHoverTimerRef.current = setTimeout(() => {
                setPreviewerVisible(true);
              }, 1000);
            }
          }}
          onMouseLeave={() => {
            clearTimeout(previewerHoverTimerRef.current);
            previewerHoverTimerRef.current = undefined;
            if (previewerVisible) {
              setPreviewerVisible(false);
            }
          }}
        >
          {previewerVisible && (
            <MediaPreviewer resourceId={resource.id} />
          )}
          <ResourceCover
            loadImmediately={false}
            resourceId={resource.id}
            onClick={() => {
              ResourceDetailDialog.show({
                onTagSearch,
                onReloaded: () => reload(new AbortController().signal),
                resource,
                onPlay: clickPlayButton,
                onOpen: open,
                onRemove: remove,
                noPlayableFile: !(playableFiles?.length > 0),
                ct,
              });
            }}
            ref={coverRef}
            showBiggerOnHover={showBiggerCoverOnHover}
          />
        </div>
        {resource.id > 0 && (
          <div className="rating">
            <Rating
              size={'medium'}
              allowClear
              onChange={(v) => {
                PatchResource({
                  id: resource.id,
                  model: {
                    rate: v,
                  },
                })
                  .invoke((a) => {
                    if (!a.code) {
                      resource.rate = v;
                      forceUpdate();
                    }
                  });
              }}
              value={resource.rate}
            />
          </div>
        )}
        <div className="play">
          <Balloon.Tooltip
            // popupContainer={elementId}
            trigger={
              <Button
                disabled={playableFiles.length == 0}
                type="normal"
                onClick={clickPlayButton}
              >
                <CustomIcon type="play-circle" size={'xl'} />
              </Button>
            }
            triggerType={['hover']}
            align={'t'}
          >
            {playableFiles.length == 0 ? t('No playable file') : playableFiles.length == 1 ? t('Use player to play') : t('Select one file to play')}
          </Balloon.Tooltip>
        </div>
        <div className="icons">
          {languageIcon}
          {resource.hasChildren && (
            <CustomIcon
              type={'package'}
              size={'small'}
              title={t('This is a parent resource')}
            />
          )}
          {
            resource.parentId > 0 && (
              <CustomIcon
                type={'node'}
                size={'small'}
                title={t('This is a child resource')}
              />
            )
          }
          <CustomIcon
            type={resource.isSingleFile ? 'file' : 'folder'}
            title={t(`This is a ${resource.isSingleFile ? 'file' : 'folder'}`)}
            size={'small'}
          />
        </div>
        <div className="opts">
          {resource.id > 0 ? (
            <>
              <div className="opt" title={t('Enhancements')}>
                <CustomIcon
                  type={'flashlight'}
                  onClick={() => {
                    BApi.resource.getResourceEnhancementRecords(resource.id)
                      .then((t) => {
                        ResourceEnhancementsDialog.show({
                          resourceId: resource.id,
                          enhancements: t.data || [],
                        });
                      });
                  }}
                />
              </div>
              <div className={'opt'} title={t('Preview')}>
                <CustomIcon
                  type={'eye'}
                  onClick={() => {
                    ShowResourceMediaPlayer(resource.id, resource.rawFullname, base64String => {
                      coverRef.current?.save(base64String, false);
                    }, t);
                    // resourceMediaPlayerRef.current.show();
                  }}
                />
              </div>
              <div className="opt" title={t('Open folder')}>
                <CustomIcon
                  type={'folder-open'}
                  onClick={() => open()}
                />
              </div>
              <div className="opt" title={t('Remove')}>
                <ClickableIcon
                  type={'delete'}
                  colorType={'danger'}
                  onClick={remove}
                />
              </div>
              <div
                className="opt"
                title={t('Move')}
                onClick={() => {
                  MediaLibraryPathSelector.show({
                    onSelect: path => BApi.resource.moveResources({
                      ids: [resource.id],
                      path,
                    }),
                  });
                }}
              >
                <CustomIcon type={'move'} size={'small'} />
              </div>
              <div className={'opt'} title={t('Search')}>
                <Dropdown
                  autoFocus={false}
                  trigger={
                    <CustomIcon type={'search'} />
                  }
                  triggerType={'click'}
                >
                  <Menu className={'resource-component-search-dropdown-menu'}>
                    {searchEngines?.filter(e => e.urlTemplate)
                      .map((e, i) => {
                        return (
                          <Menu.Item
                            key={i}
                            onClick={() => {
                              OpenUrlInDefaultBrowser({
                                url: e.urlTemplate!.replace('{keyword}', encodeURIComponent(resource.rawName)),
                              })
                                .invoke();
                            }}
                          >
                            <Trans i18nKey={'resource.search-engine.tip'}>
                              Use <span>{{ name: e.name } as any}</span> to
                              search <span>{{ keyword: resource.rawName } as any}</span>
                            </Trans>
                          </Menu.Item>
                        );
                      })}
                  </Menu>
                </Dropdown>
              </div>
              <div
                className="opt"
                title={t('Add to favorites')}
                onClick={() => {
                  FavoritesSelector.show({
                    resourceIds: [resource.id],
                  });
                }}
              >
                <CustomIcon type={'star'} size={'small'} />
              </div>
              <div
                className="opt"
                title={t('Add to playlist')}
                onClick={() => {
                  Dialog.show({
                    title: t('Add to playlist'),
                    content: (
                      <PlaylistCollection defaultNewItem={{
                        resourceId: resource.id,
                        type: PlaylistItemType.Resource,
                      }}
                      />
                    ),
                    style: { minWidth: 600 },
                    v2: true,
                    closeMode: ['close', 'mask', 'esc'],
                  });
                }}
              >
                <CustomIcon type={'playlistadd'} size={'small'} />
              </div>
              <div
                className="opt"
                title={t('Set tags')}
                onClick={() => {
                  let tagIds = (resource.tags || []).map(t => t.id);
                  Dialog.show({
                    title: t('Setting tags'),
                    width: 'auto',
                    content: (
                      <TagSelector defaultValue={{ tagIds }} onChange={value => tagIds = value.tagIds} />
                    ),
                    v2: true,
                    closeMode: ['close', 'mask', 'esc'],
                    onOk: () => BApi.resource.updateResourceTags({
                      resourceTagIds: {
                        [resource.id]: tagIds,
                      },
                    })
                      .then(t => {
                        if (!t.code) {
                          reload(new AbortController().signal);
                        }
                      }),
                  });
                }}
              >
                <CustomIcon type={'tags'} size={'small'} />
              </div>

              <div
                className="opt"
                title={t('Remove cover cache')}
                onClick={async () => {
                  await BApi.resource.removeCoverCache(resource.id);
                  await coverRef.current?.reload(new AbortController().signal);
                }}
              >
                <CustomIcon type={'image-redo'} size={'small'} />
              </div>
            </>
          ) : (
            <div className="opt" title={t('Open folder')}>
              <CustomIcon
                type={'folder-open'}
                onClick={() => OpenFileOrDirectory({
                  path: resource.rawFullname,
                  openInDirectory: true,
                })
                  .invoke()}
              />
            </div>
          )}
        </div>
      </div>
    );
  };

  const renderTaskCover = () => {
    if (taskRef.current) {
      return (
        <div className={'task-cover'} title={taskRef.current.summary}>
          <div className={'type'}>
            {ResourceTaskType[taskRef.current.type]}
          </div>
          <div className="percentage">
            {taskRef.current.error ? (<span
              style={{
                color: 'red',
                cursor: 'pointer',
              }}
              onClick={() => Dialog.show({
                title: t('Error'),
                content: (<pre>{taskRef.current!.error}</pre>),
                closeable: true,
              })}
            >Error
            </span>) : `${taskRef.current.percentage}%`}
          </div>
          <div className="opt">
            {taskRef.current.error ? (
              <Button
                size={'small'}
                type={'secondary'}
                onClick={() => {
                  ClearResourceTask({
                    id: resource.id,
                  })
                    .invoke((t) => {

                    });
                }}
              >
                {t('Clear')}
              </Button>
            ) : (
              <Button
                size={'small'}
                warning
                onClick={() => {
                  Dialog.confirm({
                    title: t('Sure to stop?'),
                    onOk: () => {
                      return new Promise(((resolve, reject) => {
                        BApi.backgroundTask.stopBackgroundTask(taskRef.current!.backgroundTaskId)
                          .then((x) => {
                            if (!x.code) {
                              resolve(x);
                            } else {
                              reject();
                            }
                          })
                          .catch(() => {
                            reject();
                          });
                      }));
                    },
                    closeable: true,
                  });
                }}
              >
                {t('Stop')}
              </Button>
            )}

          </div>
        </div>
      );
    }
    return;
  };

  return (
    <div className={'resource-component'} key={resource.id}>
      {renderTaskCover()}
      {renderCover()}
      <div className={'info'}>
        <div className={'title limited-content'}>
          {/* [202020] */}
          {/* [<Balloon.Tooltip */}
          {/*  align={'t'} */}
          {/*  trigger={<span style={{ cursor: 'pointer' }}>XXXClub</span>} */}
          {/* >点击搜索 XXXClub */}
          {/* </Balloon.Tooltip> */}
          {/* (<Balloon.Tooltip */}
          {/*  align={'t'} */}
          {/*  trigger={<span style={{ cursor: 'pointer' }}>深崎暮人</span>} */}
          {/* >点击搜索 深崎暮人 */}
          {/* </Balloon.Tooltip>、 */}
          {/* <Balloon.Tooltip align={'t'} trigger={<span style={{ cursor: 'pointer' }}>村上水军</span>}>点击搜索 村上水军</Balloon.Tooltip>)] */}
          {/* <Balloon.Tooltip */}
          {/*  align={'t'} */}
          {/*  trigger={<span style={{ cursor: 'pointer' }}>F-Ism</span>} */}
          {/* >点击搜索 F-Ism */}
          {/* </Balloon.Tooltip>Vol.999( */}
          {/* <Balloon.Tooltip */}
          {/*  align={'t'} */}
          {/*  trigger={<span style={{ cursor: 'pointer' }}>Fate Zero</span>} */}
          {/* >点击搜索 Fate Zero */}
          {/* </Balloon.Tooltip> */}
          {/* )[CN] */}
          {
            displayModes.map((c) => (
              <div className={c} key={c}>
                {resource.displayName}
              </div>
            ))
          }
        </div>
        <div className="tags limited-content">
          {displayModes.map((c) => (
            <div className={c} key={c}>
              {(resource.tags || []).map((t) => {
                const tag = new TagDto({ ...t });
                return (
                  <Button
                    key={t.id}
                    // className={'tag'}
                    text
                    style={{ color: t.color }}
                    size={'small'}
                    onClick={() => onTagSearch(t.id, true)}
                  >#{tag.displayName}
                  </Button>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
});


export default React.memo(Resource);
