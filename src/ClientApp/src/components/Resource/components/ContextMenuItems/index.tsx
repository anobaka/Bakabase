import { MenuItem } from '@szhsin/react-menu';
import React from 'react';
import { useTranslation } from 'react-i18next';
import MediaLibraryPathSelectorV2 from '@/components/MediaLibraryPathSelectorV2';
import MediaLibrarySelectorV2 from '@/components/MediaLibrarySelectorV2';
import { ResourceAdditionalItem } from '@/sdk/constants';
import ResourceTransferModal from '@/components/ResourceTransferModal';
import { Checkbox, Modal } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import BApi from '@/sdk/BApi';

const log = buildLogger('ResourceContextMenuItems');

type Props = {
  selectedResourceIds: number[];
  onSelectedResourcesChanged?: (ids: number[]) => any;
};

export default ({ selectedResourceIds, onSelectedResourcesChanged }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  return (
    <>
      <MenuItem
        onClick={() => {
          log('inner', 'click');
          createPortal(MediaLibraryPathSelectorV2, {
            onSelect: (id, path) => {
              if (selectedResourceIds.length > 0) {
                BApi.resource.moveResources({
                  ids: selectedResourceIds,
                  path,
                  mediaLibraryId: id,
                }).then(r => {
                  // todo: moving files is a asynchronized operation, we need some way to update other resources after moving
                  // onSelectedResourcesChanged?.(selectedResourceIds);
                });
              }
            },
          });
        }}
        onClickCapture={() => {
          log('inner', 'click capture');
        }}
      >{selectedResourceIds.length > 1 ? t('Move {{count}} resources to media library (Including file system entries)', { count: selectedResourceIds.length }) : t('Move to media library (Including file system entries)')}</MenuItem>
      <MenuItem
        onClick={() => {
          log('inner', 'click');
          createPortal(MediaLibrarySelectorV2, {
            onSelect: async (id) => {
              await BApi.resource.moveResources({
                ids: selectedResourceIds,
                mediaLibraryId: id,
              }).then(r => {
                onSelectedResourcesChanged?.(selectedResourceIds);
              });
            },
          });
        }}
        onClickCapture={() => {
          log('inner', 'click capture');
        }}
      >{selectedResourceIds.length > 1 ? t('Move {{count}} resources to media library (Data only)', { count: selectedResourceIds.length }) : t('Move to media library (Data only)')}</MenuItem>
      <MenuItem
        onClick={() => {
          log('inner', 'click');
          BApi.resource.getResourcesByKeys({
            ids: selectedResourceIds,
            additionalItems: ResourceAdditionalItem.All,
          }).then(r => {
            const resources = r.data || [];
            createPortal(ResourceTransferModal, {
              fromResources: resources,
            });
          });
        }}
        onClickCapture={() => {
          log('inner', 'click capture');
        }}
      >{selectedResourceIds.length > 1 ? t('Transfer {{count}} resources', { count: selectedResourceIds.length }) : t('Transfer resource')}</MenuItem>
      <MenuItem
        onClick={() => {
          log('inner', 'click');
          let deleteFiles = false;
          createPortal(
            Modal, {
              defaultVisible: true,
              title: t('Delete {{count}} resources', { count: selectedResourceIds.length }),
              children: (
                <div>
                  <div>
                    <Checkbox
                      // color={'warning'}
                      // size={'sm'}
                      onValueChange={s => deleteFiles = s}
                    >{t('Delete files also')}</Checkbox>
                  </div>
                  <div className={'opacity-80'}>
                    {t('Resource will be shown again after next synchronization if you leave files undeleted.')}
                  </div>
                  <div className={'mt-6 font-bold'}>
                    {t('Are you sure you want to delete {{count}} resources?', { count: selectedResourceIds.length })}
                  </div>
                </div>
              ),
              onOk: async () => {
                await BApi.resource.deleteResourcesByKeys({ ids: selectedResourceIds, deleteFiles });
              },
            },
          );
        }}
        onClickCapture={() => {
          log('inner', 'click capture');
        }}
      >{selectedResourceIds.length > 1 ? t('Delete {{count}} resources', { count: selectedResourceIds.length }) : t('Delete resource')}</MenuItem>
    </>
  );
};
