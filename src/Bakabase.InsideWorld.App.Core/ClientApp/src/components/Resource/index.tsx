import { Balloon, Button, Dialog, Message, Tag } from '@alifd/next';
import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import type Queue from 'queue';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { PlayCircleOutlined } from '@ant-design/icons';
import styles from './index.module.scss';
import CustomIcon from '@/components/CustomIcon';
import { OpenResourceDirectory, PlayResourceFile, RemoveResource } from '@/sdk/apis';
import { buildLogger, splitPathIntoSegments, useTraceUpdate } from '@/components/utils';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';
import store from '@/store';
import { Tag as TagDto } from '@/core/models/Tag';
import BApi from '@/sdk/BApi';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type SimpleSearchEngine from '@/core/models/SimpleSearchEngine';
import type { RequestParams } from '@/sdk/Api';
import Operations from '@/components/Resource/components/Operations';
import TaskCover from '@/components/Resource/components/TaskCover';

export interface IResourceHandler {
  id: number;
  reload: (ct?: AbortSignal) => Promise<any>;
}

interface Props {
  resource: any;
  coverHash?: string;
  queue?: Queue;
  onRemove?: (id: number) => any;
  showBiggerCoverOnHover?: boolean;
  searchEngines?: SimpleSearchEngine[] | null;
  ct: AbortSignal;
  onTagSearch?: (tagId: number, append: boolean) => any;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
  style?: any;
}

const Resource = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onRemove = (id) => {
    },
    showBiggerCoverOnHover = true,
    onTagSearch = (tagId: number, append: boolean) => {
    },
    queue,
    ct = new AbortController().signal,
    disableCache = false,
    disableMediaPreviewer = false,
    style,
  } = props;

  const { t } = useTranslation();
  const log = buildLogger(`Resource:${resource.id}|${resource.rawFullname}`);

  const forceUpdate = useUpdate();
  const [playableFiles, setPlayableFiles] = useState<string[]>([]);

  const disableCacheRef = useRef(disableCache);

  useEffect(() => {
    disableCacheRef.current = disableCache;
  }, [disableCache]);

  useImperativeHandle(ref, (): IResourceHandler => {
    return {
      id: resource.id,
      reload: reload,
    };
  }, []);

  useTraceUpdate(props, `[${resource.rawName}]`);

  // log('Rendering');

  const loadPlayableFiles = useCallback(async (ct?: AbortSignal) => {
    const rp: RequestParams = { signal: ct };
    if (disableCacheRef.current) {
      rp.cache = 'no-cache';
    }
    const pRsp = await BApi.resource.getResourcePlayableFiles(resource.id, rp);
    setPlayableFiles(pRsp.data || []);
    // setPlayableFiles([]);
  }, [disableCache]);

  const initialize = useCallback(async (ct: AbortSignal) => {
    await loadPlayableFiles(ct);
    // log('Initialized');
  }, []);

  useEffect(() => {
    if (queue) {
      queue.push(async () => await initialize(ct));
    } else {
      initialize(ct);
    }
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

  const play = (file) =>
    BApi.tool.playResourceFile(resource.categoryId, {
      file,
    }).then((a) => {
      if (!a.code) {
        Message.success(t('Opened'));
      }
    });

  const reload = useCallback(async (ct?: AbortSignal) => {
    const newResourceRsp = await BApi.resource.getResourcesByKeys({ ids: [resource.id] });
    if (!newResourceRsp.code) {
      const nr = (newResourceRsp.data || [])[0];
      if (nr) {
        Object.keys(nr)
          .forEach((k) => {
            resource[k] = nr[k];
          });
        coverRef.current?.load(true);
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

  const open = () => {
    openFile(resource.id);
  };

  const coverRef = useRef<IResourceCoverRef>();

  const renderCover = () => {
    const elementId = `resource-${resource.id}`;
    const playable = playableFiles.length > 0;
    return (
      <div
        className={styles.coverRectangle}
        id={elementId}
      >
        <div className={styles.absoluteRectangle}>
          <ResourceCover
            disableCache={disableCache}
            disableMediaPreviewer={disableMediaPreviewer}
            onClick={() => {
              ResourceDetailDialog.show({
                onTagSearch,
                onReloaded: () => reload(new AbortController().signal),
                resource,
                onPlay: clickPlayButton,
                onOpen: open,
                noPlayableFile: !(playableFiles?.length > 0),
                ct,
              });
            }}
            resourceId={resource.id}
            ref={coverRef}
            showBiggerOnHover={showBiggerCoverOnHover}
          />
        </div>
        {playable && (
          <div className={styles.play}>
            <Balloon.Tooltip
              trigger={
                <PlayCircleOutlined
                  className={'text-2xl'}
                  onClick={clickPlayButton}
                />
              }
              triggerType={['hover']}
              align={'t'}
            >
              {t('Use player to play')}
            </Balloon.Tooltip>
          </div>
        )}
      </div>
    );
  };

  return (
    <div className={styles.resource} key={resource.id} style={style}>
      <Operations
        resource={resource}
        coverRef={coverRef.current}
        reload={reload}
      />
      <TaskCover
        resource={resource}
      />
      {renderCover()}
      <div className={styles.info}>
        <div className={`${styles.title} ${styles.limitedContent}`}>
          {resource.displayName}
        </div>
        <div className={`${styles.tags} ${styles.limitedContent}`}>
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
              >#{tag.displayName}&nbsp;
              </Button>
            );
          })}
        </div>
      </div>
    </div>
  );
});


export default React.memo(Resource);
