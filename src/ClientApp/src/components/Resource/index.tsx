import { Dialog, Message, Tag } from '@alifd/next';
import React, { Profiler, useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import type Queue from 'queue';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import { PlayCircleOutlined } from '@ant-design/icons';
import styles from './index.module.scss';
import CustomIcon from '@/components/CustomIcon';
import { OpenResourceDirectory } from '@/sdk/apis';
import { buildLogger, splitPathIntoSegments, useTraceUpdate } from '@/components/utils';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';
import BApi from '@/sdk/BApi';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type SimpleSearchEngine from '@/core/models/SimpleSearchEngine';
import type { RequestParams } from '@/sdk/Api';
import Operations from '@/components/Resource/components/Operations';
import TaskCover from '@/components/Resource/components/TaskCover';
import type { Property, Resource as ResourceModel } from '@/core/models/Resource';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Link, Tooltip } from '@/components/bakaui';
import { ResourceAdditionalItem, ResourcePropertyType, StandardValueType } from '@/sdk/constants';
import type { TagValue } from '@/components/StandardValue/models';

export interface IResourceHandler {
  id: number;
  reload: (ct?: AbortSignal) => Promise<any>;
}


type TooltipPlacement =
  | 'top'
  | 'bottom'
  | 'right'
  | 'left'
  | 'top-start'
  | 'top-end'
  | 'bottom-start'
  | 'bottom-end'
  | 'left-start'
  | 'left-end'
  | 'right-start'
  | 'right-end';

type Props = {
  resource: ResourceModel;
  coverHash?: string;
  queue?: Queue;
  onRemove?: (id: number) => any;
  showBiggerCoverOnHover?: boolean;
  biggerCoverPlacement?: TooltipPlacement;
  searchEngines?: SimpleSearchEngine[] | null;
  ct: AbortSignal;
  onTagClick?: (propertyId: number, value: string) => any;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
  style?: any;
  className?: string;
};

const Resource = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onRemove = (id) => {
    },
    showBiggerCoverOnHover = true,
    onTagClick = (propertyId: number, value: string) => {
    },
    queue,
    ct = new AbortController().signal,
    disableCache = false,
    disableMediaPreviewer = false,
    biggerCoverPlacement,
    style,
  } = props;

  // console.log(`showBiggerCoverOnHover: ${showBiggerCoverOnHover}, disableMediaPreviewer: ${disableMediaPreviewer}, disableCache: ${disableCache}`);

  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();
  const log = buildLogger(`Resource:${resource.id}|${resource.path}`);

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

  useTraceUpdate(props, `[${resource.fileName}]`);

  // log('Rendering');

  const loadPlayableFiles = useCallback(async (ct?: AbortSignal) => {
    const rp: RequestParams = { signal: ct };
    if (disableCacheRef.current) {
      rp.cache = 'no-cache';
    }
    const pRsp = await BApi.resource.getResourcePlayableFiles(resource.id, rp);
    setPlayableFiles(pRsp.data || []);
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
    BApi.resource.playResourceFile(resource.categoryId, {
      file,
    }).then((a) => {
      if (!a.code) {
        Message.success(t('Opened'));
      }
    });

  const reload = useCallback(async (ct?: AbortSignal) => {
    const newResourceRsp = await BApi.resource.getResourcesByKeys({ ids: [resource.id], additionalItems: ResourceAdditionalItem.All });
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
          <div className={'flex flex-wrap gap-2'}>
            {playableFiles.map((a) => {
              const segments = splitPathIntoSegments(a);
              return (
                <Button
                  size={'sm'}
                  onClick={() => {
                    play(a);
                  }}
                >
                  <PlayCircleOutlined className={'text-base'} />
                  <span className={'break-all overflow-hidden text-ellipsis'}>{segments[segments.length - 1]}</span>
                </Button>
              );
            })}
          </div>
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

  function onRenderCallback(
    id, // the "id" prop of the Profiler tree that has just committed
    phase, // either "mount" (if the tree just mounted) or "update" (if it re-rendered)
    actualDuration, // time spent rendering the committed update
    baseDuration, // estimated time to render the entire subtree without memoization
    startTime, // when React began rendering this update
    commitTime, // when React committed this update
    interactions, // the Set of interactions belonging to this update
  ) {
    console.log({ id, phase, actualDuration, baseDuration, startTime, commitTime, interactions });
  }

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
            biggerCoverPlacement={biggerCoverPlacement}
            disableCache={disableCache}
            disableMediaPreviewer={disableMediaPreviewer}
            onClick={() => {
                createPortal(ResourceDetailDialog, {
                  id: resource.id,
                  onPlay: clickPlayButton,
                  noPlayableFile: !(playableFiles?.length > 0),
                  onDestroyed: () => {
                    reload();
                  },
                });
              }}
            resourceId={resource.id}
            coverPaths={resource.coverPaths}
            ref={coverRef}
            showBiggerOnHover={showBiggerCoverOnHover}
          />
        </div>
        {playable && (
          <div className={styles.play}>
            <Tooltip
              content={t('Use player to play')}
            >
              <PlayCircleOutlined
                className={'text-2xl'}
                onClick={clickPlayButton}
              />
            </Tooltip>
          </div>
        )}
      </div>
    );
  };

  let firstTagsValue: (TagValue & {value: string})[] | undefined;
  let firstTagsValuePropertyId: number | undefined;
  {
    const customPropertyValues = resource.properties?.[ResourcePropertyType.Custom] || {};
    Object.keys(customPropertyValues).find(x => {
      const p: Property = customPropertyValues[x];
      if (p.bizValueType == StandardValueType.ListTag) {
        const values = p.values?.find(v => (v.value as string[])?.length > 0);
        if (values) {
          firstTagsValue = (values.value as string[]).map((id, i) => {
            const bvs = values.aliasAppliedBizValue as TagValue[];
            const bv = bvs?.[i];
            return {
              value: id,
              ...bv,
            };
          });
          firstTagsValuePropertyId = parseInt(x, 10);
          return true;
        }
      }
      return false;
    });
  }

  return (
    <div
      className={`flex flex-col p-1 rounded relative border-1 border-default-200 group ${styles.resource} ${props.className}`}
      key={resource.id}
      style={style}
      role={'resource'}
      data-id={resource.id}
    >
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
        <div
          className={`select-text ${styles.limitedContent}`}
        >
          {resource.displayName}
        </div>
      </div>
      {firstTagsValue && firstTagsValue.length > 0 && (
        <div className={styles.info}>
          <div
            className={`select-text ${styles.limitedContent} flex gap-1 flex-wrap opacity-70 leading-3`}
          >
            {firstTagsValue.map(v => {
              return (
                <Link
                  color={'foreground'}
                  onClick={(e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    onTagClick?.(firstTagsValuePropertyId!, v.value);
                  }}
                  className={'text-xs cursor-pointer'}
                  underline={'none'}
                  size={'sm'}
                  // variant={'light'}
                >#{v.group == undefined ? '' : `${v.group}:`}{v.name}</Link>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
});


export default React.memo(Resource);
