import { Message } from '@alifd/next';
import type { CSSProperties } from 'react';
import React, { useCallback, useEffect, useImperativeHandle, useRef, useState } from 'react';
import type Queue from 'queue';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import {
  ApartmentOutlined,
  DisconnectOutlined,
  FileUnknownOutlined,
  PlayCircleOutlined,
  PushpinOutlined,
} from '@ant-design/icons';
import { ControlledMenu } from '@szhsin/react-menu';
import styles from './index.module.scss';
import { OpenResourceDirectory } from '@/sdk/apis';
import { buildLogger, useTraceUpdate } from '@/components/utils';
import ResourceDetailDialog from '@/components/Resource/components/DetailDialog';
import BApi from '@/sdk/BApi';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type SimpleSearchEngine from '@/core/models/SimpleSearchEngine';
import Operations from '@/components/Resource/components/Operations';
import TaskCover from '@/components/Resource/components/TaskCover';
import type { Property, Resource as ResourceModel } from '@/core/models/Resource';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Chip, Link, Tooltip } from '@/components/bakaui';
import {
  BTaskStatus,
  PropertyPool,
  ResourceAdditionalItem,
  ResourceDisplayContent,
  ResourceTag,
  StandardValueType,
} from '@/sdk/constants';
import type { TagValue } from '@/components/StandardValue/models';
import store from '@/store';
import type { PlayableFilesRef } from '@/components/Resource/components/PlayableFiles';
import PlayableFiles from '@/components/Resource/components/PlayableFiles';
import ContextMenuItems from '@/components/Resource/components/ContextMenuItems';
import type { BTask } from '@/core/models/BTask';

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
  ct?: AbortSignal;
  onTagClick?: (propertyId: number, value: TagValue) => any;
  disableCache?: boolean;
  disableMediaPreviewer?: boolean;
  style?: any;
  className?: string;
  selected?: boolean;
  mode?: 'default' | 'select';
  onSelected?: () => any;
  selectedResourceIds?: number[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
};

const Resource = React.forwardRef((props: Props, ref) => {
  const {
    resource,
    onRemove = (id) => {
    },
    onTagClick = (propertyId: number, value: TagValue) => {
    },
    queue,
    ct = new AbortController().signal,
    disableCache = false,
    biggerCoverPlacement,
    style: propStyle = {},
    selected = false,
    mode = 'default',
    onSelected = () => {
    },
    selectedResourceIds: propsSelectedResourceIds,
    onSelectedResourcesChanged,
  } = props;

  // console.log(`showBiggerCoverOnHover: ${showBiggerCoverOnHover}, disableMediaPreviewer: ${disableMediaPreviewer}, disableCache: ${disableCache}`);

  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();
  const log = buildLogger(`Resource:${resource.id}|${resource.path}`);
  const appContext = store.useModelState('appContext');
  const tasksRef = useRef<BTask[] | undefined>();

  const uiOptions = store.useModelState('uiOptions');

  const forceUpdate = useUpdate();

  const playableFilesRef = useRef<PlayableFilesRef>(null);

  const [contextMenuIsOpen, setContextMenuIsOpen] = useState(false);
  const [contextMenuAnchorPoint, setContextMenuAnchorPoint] = useState({
    x: 0,
    y: 0,
  });

  const hasActiveTask = tasksRef.current?.some(x => x.status == BTaskStatus.Paused || x.status == BTaskStatus.Running) == true;

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

  const displayContents = uiOptions.resource?.displayContents ?? ResourceDisplayContent.All;
  // log('Rendering');

  const initialize = useCallback(async (ct: AbortSignal) => {
    if (playableFilesRef.current) {
      await playableFilesRef.current.initialize();
    }
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


  const reload = useCallback(async (ct?: AbortSignal) => {
    const newResourceRsp = await BApi.resource.getResourcesByKeys({
      ids: [resource.id],
      additionalItems: ResourceAdditionalItem.All,
    });
    if (!newResourceRsp.code) {
      const nr = (newResourceRsp.data || [])[0];
      if (nr) {
        Object.keys(nr)
          .forEach((k) => {
            resource[k] = nr[k];
          });
        coverRef.current?.load(true);
        playableFilesRef.current?.initialize();
        forceUpdate();
      }
    } else {
      throw new Error(newResourceRsp.message!);
    }
  }, []);

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
    console.log({
      id,
      phase,
      actualDuration,
      baseDuration,
      startTime,
      commitTime,
      interactions,
    });
  }

  const renderCover = () => {
    const elementId = `resource-${resource.id}`;
    return (
      <div
        className={styles.coverRectangle}
        id={elementId}
      >
        <div className={styles.absoluteRectangle}>
          <ResourceCover
            coverFit={uiOptions.resource?.coverFit}
            biggerCoverPlacement={biggerCoverPlacement}
            useCache={!uiOptions?.resource?.disableCache}
            disableMediaPreviewer={uiOptions?.resource?.disableMediaPreviewer}
            disableCarousel={uiOptions?.resource?.disableCoverCarousel}
            onClick={() => {
              createPortal(ResourceDetailDialog, {
                id: resource.id,
                onDestroyed: () => {
                  reload();
                },
              });
            }}
            resource={resource}
            ref={coverRef}
            showBiggerOnHover={uiOptions?.resource?.showBiggerCoverWhileHover}
          />
        </div>
        {/* lef-top */}
        <div className={'absolute top-1 left-1 flex gap-1 items-center'}>
          {resource.tags.includes(ResourceTag.Pinned) && (
            <PushpinOutlined />
          )}
          {resource.tags.includes(ResourceTag.IsParent) && (
            <Tooltip content={t('This is a parent resource')}>
              <ApartmentOutlined className={''} />
            </Tooltip>
          )}
          {resource.tags.includes(ResourceTag.PathDoesNotExist) && (
            <Tooltip content={t('File does not exist')}>
              <FileUnknownOutlined className={'text-warning'} />
            </Tooltip>
          )}
          {resource.tags.includes(ResourceTag.UnknownMediaLibrary) && (
            <Tooltip content={t('Unknown media library')}>
              <DisconnectOutlined className={'text-warning'} />
            </Tooltip>
          )}
        </div>
        <PlayableFiles
          PortalComponent={({ onClick }) => (
            <div className={styles.play}>
              <Tooltip
                content={t('Use player to play')}
              >
                <Button
                  onClick={onClick}
                  // variant={'light'}
                  isIconOnly
                >
                  <PlayCircleOutlined className={'text-2xl'} />
                </Button>
              </Tooltip>
            </div>
          )}
          resource={resource}
          ref={playableFilesRef}
        />
        <div className={'flex flex-col gap-1 absolute bottom-0 right-0 items-end'}>
          {(displayContents & ResourceDisplayContent.MediaLibrary) ? (resource.mediaLibraryName != undefined && (
            <Chip
              size={'sm'}
              variant={'flat'}
              className={'h-auto'}
              radius={'sm'}
            >{resource.mediaLibraryName}</Chip>
          )) : undefined}
          {(displayContents & ResourceDisplayContent.Category) ? (resource.category != undefined && (
            <Chip
              size={'sm'}
              variant={'flat'}
              className={'h-auto'}
              radius={'sm'}
            >{resource.category.name}</Chip>
          )) : undefined}
        </div>
      </div>
    );
  };

  let firstTagsValue: TagValue[] | undefined;
  let firstTagsValuePropertyId: number | undefined;
  {
    const customPropertyValues = resource.properties?.[PropertyPool.Custom] || {};
    Object.keys(customPropertyValues).find(x => {
      const p: Property = customPropertyValues[x];
      if (p.bizValueType == StandardValueType.ListTag) {
        const values = p.values?.find(v => (v.aliasAppliedBizValue as TagValue[])?.length > 0);
        if (values) {
          firstTagsValue = (values.aliasAppliedBizValue as TagValue[]).map((id, i) => {
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

  const style: CSSProperties = {
    ...propStyle,
  };

  if (selected) {
    style.borderWidth = 2;
    style.borderColor = 'var(--bakaui-success)';
  }

  const selectedResourceIds = (propsSelectedResourceIds ?? []).slice();
  if (!selectedResourceIds.includes(resource.id)) {
    selectedResourceIds.push(resource.id);
  }

  log('selectedResourceIds', selectedResourceIds);
  log(resource);

  return (
    <div
      className={`flex flex-col p-1 rounded relative border-1 border-default-200 group/resource ${styles.resource} ${props.className}`}
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
        reload={reload}
        onTasksChange={tasks => tasksRef.current = tasks}
      />
      <div
        onContextMenu={(e) => {
          if (typeof document.hasFocus === 'function' && !document.hasFocus()) return;

          e.preventDefault();
          setContextMenuAnchorPoint({
            x: e.clientX,
            y: e.clientY,
          });
          setContextMenuIsOpen(true);
        }}
        onClick={() => {
          log('outer', 'click');
        }}
      >
        <ControlledMenu
          key={resource.id}
          anchorPoint={contextMenuAnchorPoint}
          state={contextMenuIsOpen ? 'open' : 'closed'}
          direction="right"
          onClose={() => setContextMenuIsOpen(false)}
          onClick={e => {
            e.preventDefault();
            e.stopPropagation();
          }}
        >
          <ContextMenuItems
            selectedResourceIds={selectedResourceIds}
            onSelectedResourcesChanged={onSelectedResourcesChanged}
          />
        </ControlledMenu>
        <div onClickCapture={e => {
          log('outer', 'click capture');
          if (mode == 'select' && !hasActiveTask) {
            onSelected();
            e.preventDefault();
            e.stopPropagation();
          }
        }}
        >
          {renderCover()}
          <div className={styles.info}>
            <div
              className={`select-text ${styles.limitedContent}`}
            >
              {resource.displayName}
            </div>
          </div>
          {(displayContents & ResourceDisplayContent.Tags) ? (firstTagsValue && firstTagsValue.length > 0 && (
            <div className={styles.info}>
              <div
                className={`select-text ${styles.limitedContent} flex flex-wrap opacity-70 leading-3 gap-px`}
              >
                {firstTagsValue.map(v => {
                  return (
                    <Link
                      color={'foreground'}
                      onClick={(e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        onTagClick?.(firstTagsValuePropertyId!, v);
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
          )) : undefined}
        </div>
      </div>
    </div>
  );
});


export default React.memo(Resource);
