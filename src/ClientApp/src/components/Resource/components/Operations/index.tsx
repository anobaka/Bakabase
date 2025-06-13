import { Dialog } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import React from 'react';
import {
  FireOutlined,
  FolderOpenOutlined,
  ProductOutlined,
  PushpinOutlined,
  VideoCameraAddOutlined,
} from '@ant-design/icons';
import { AiOutlinePicture } from 'react-icons/ai';
import BApi from '@/sdk/BApi';
import ResourceEnhancementsDialog from '@/components/Resource/components/ResourceEnhancementsDialog';
import ShowResourceMediaPlayer from '@/components/Resource/components/ShowResourceMediaPlayer';
import { EnhancementAdditionalItem, PlaylistItemType } from '@/sdk/constants';
import { PlaylistCollection } from '@/components/Playlist';
import type { IResourceCoverRef } from '@/components/Resource/components/ResourceCover';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Popover } from '@/components/bakaui';
import type { Resource } from '@/core/models/Resource';

interface IProps {
  resource: Resource;
  coverRef?: IResourceCoverRef;
  reload?: (ct?: AbortSignal) => Promise<any>;
}

export default ({
                  resource,
                  coverRef,
                  reload,
                }: IProps) => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();
  // const searchEngines = store.useModelState('thirdPartyOptions').simpleSearchEngines || [];

  return (
    <Popover
      trigger={(
        <Button
          isIconOnly
          size={'sm'}
          className={'absolute top-1 right-1 z-10 opacity-0 group-hover/resource:opacity-100'}
        >
          <ProductOutlined className={'text-lg'} />
        </Button>
      )}
      style={{
        zIndex: 20,
      }}
    >
      <div className={'grid grid-cols-3 gap-1 py-1 rounded'}>
        <Button
          size={'sm'}
          color={resource.pinned ? 'warning' : 'default'}
          title={resource.pinned ? t('Unpin') : t('Pin')}
          isIconOnly
          onClick={() => {
            BApi.resource.pinResource(resource.id, { pin: !resource.pinned }).then(r => {
              reload?.();
            });
          }}
        >
          <PushpinOutlined className={'text-lg'} />
        </Button>
        <Button
          size={'sm'}
          title={t('Open folder')}
          isIconOnly
          onClick={() => BApi.tool.openFileOrDirectory({ path: resource.path, openInDirectory: resource.isFile })}

        >
          <FolderOpenOutlined className={'text-lg'} />
        </Button>
        <Button
          size={'sm'}
          title={t('Enhancements')}
          isIconOnly
          onClick={() => {
            BApi.resource.getResourceEnhancements(resource.id, { additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue })
              .then((t) => {
                createPortal(ResourceEnhancementsDialog, {
                  resourceId: resource.id,
                  // @ts-ignore
                  enhancements: t.data || [],
                });
              });
          }}
        >
          <FireOutlined className={'text-lg'} />
        </Button>
        <Button
          size={'sm'}
          title={t('Preview')}
          isIconOnly
          onClick={() => {
            ShowResourceMediaPlayer(resource.id, resource.path, (base64String: string) => {
              coverRef?.save(base64String);
              // @ts-ignore
            }, t, resource.isSingleFile);
          }}
        >
          <AiOutlinePicture className={'text-lg'} />
        </Button>
        <Button
          size={'sm'}
          title={t('Add to playlist')}
          isIconOnly
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
          <VideoCameraAddOutlined className={'text-lg'} />
        </Button>
      </div>
    </Popover>
  );
};
