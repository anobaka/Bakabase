import React, { useEffect, useState } from 'react';

import './index.scss';
import { useTranslation } from 'react-i18next';
import { DisconnectOutlined, FolderOpenOutlined, PlayCircleOutlined, SettingOutlined } from '@ant-design/icons';
import { history } from 'ice';
import BasicInfo from './BasicInfo';
import Properties from './Properties';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import { Button, ButtonGroup, Link, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BApi from '@/sdk/BApi';
import { PropertyPool, ReservedProperty, ResourceAdditionalItem } from '@/sdk/constants';
import { convertFromApiValue } from '@/components/StandardValue/helpers';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertyValueScopePicker from '@/components/Resource/components/DetailDialog/PropertyValueScopePicker';
import PlayableFiles from '@/components/Resource/components/PlayableFiles';


interface Props extends DestroyableProps {
  id: number;
  onRemoved?: () => void;
}

export default ({
                  id,
                  onRemoved,
                  ...props
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [resource, setResource] = useState<ResourceModel>();

  const loadResource = async () => {
    // @ts-ignore
    const r = await BApi.resource.getResourcesByKeys({
      ids: [id],
      additionalItems: ResourceAdditionalItem.All,
    });
    const d = (r.data || [])?.[0] ?? {};
    if (d.properties) {
      Object.values(d.properties).forEach(a => {
        Object.values(a).forEach(b => {
          if (b.values) {
            for (const v of b.values) {
              v.bizValue = convertFromApiValue(v.bizValue, b.bizValueType!);
              v.aliasAppliedBizValue = convertFromApiValue(v.aliasAppliedBizValue, b.bizValueType!);
              v.value = convertFromApiValue(v.value, b.dbValueType!);
            }
          }
        });
      });
    }
    // @ts-ignore
    setResource(d);
  };

  useEffect(() => {
    loadResource();
  }, []);

  console.log(resource);

  return (
    <Modal
      size={'xl'}
      footer={false}
      defaultVisible
      onDestroyed={props.onDestroyed}
      title={resource?.displayName}
    >
      {resource && (
        <>
          <div className="flex gap-4 relative">
            <div className="min-w-[400px] w-[400px] max-w-[400px] flex flex-col gap-4">
              <div
                className={'h-[400px] max-h-[400px] overflow-hidden rounded flex items-center justify-center border-1'}
                style={{ borderColor: 'var(--bakaui-overlap-background)' }}
              >
                <ResourceCover
                  resource={resource}
                  useCache={false}
                  showBiggerOnHover={false}
                />
              </div>
              <div className={'flex justify-center'}>
                <Properties
                  resource={resource}
                  reload={loadResource}
                  restrictedPropertyPool={PropertyPool.Reserved}
                  restrictedPropertyIds={[ReservedProperty.Rating]}
                  hidePropertyName
                  propertyInnerDirection={'ver'}
                />
              </div>
              <div className={'flex items-center justify-center'}>
                <ButtonGroup size={'sm'}>
                  <PlayableFiles
                    PortalComponent={({ onClick }) => (
                      <Button
                        color="primary"
                        onClick={onClick}
                      >
                        <PlayCircleOutlined />
                        {t('Play')}
                      </Button>
                    )}
                    resource={resource}
                    autoInitialize
                  />
                  <Button
                    color="default"
                    onClick={() => {
                      BApi.resource.openResourceDirectory({
                        id: resource.id,
                      });
                    }}
                  >
                    <FolderOpenOutlined />
                    {t('Open')}
                  </Button>
                  {/* <Button */}
                  {/*   color={'danger'} */}
                  {/*   onClick={() => onRemoved?.()} */}
                  {/* > */}
                  {/*   <DeleteOutlined /> */}
                  {/*   {t('Remove')} */}
                  {/* </Button> */}
                </ButtonGroup>
              </div>
              <BasicInfo resource={resource} />
            </div>
            <div className="overflow-auto grow">
              <Properties
                resource={resource}
                reload={loadResource}
                restrictedPropertyPool={PropertyPool.Custom}
                propertyClassNames={{
                  name: 'justify-end',
                }}
                noPropertyContent={(
                  <div className={'flex flex-col items-center gap-2 justify-center'}>
                    <div className={'w-4/5'}>
                      <DisconnectOutlined className={'text-base mr-1'} />
                      {t('No property available. Please bind properties to category if you need to show them.')}
                      <Button
                        onClick={() => {
                          history?.push('/category');
                        }}
                        variant={'light'}
                        size={'sm'}
                        color={'primary'}
                      >{t('Go to category page')}</Button>
                    </div>
                  </div>
                )}
              />
              <div>
                <Properties
                  resource={resource}
                  reload={loadResource}
                  restrictedPropertyPool={PropertyPool.Reserved}
                  restrictedPropertyIds={[ReservedProperty.Cover]}
                  propertyClassNames={{
                    name: 'justify-end',
                  }}
                />
                <Properties
                  resource={resource}
                  reload={loadResource}
                  restrictedPropertyPool={PropertyPool.Reserved}
                  restrictedPropertyIds={[ReservedProperty.PlayedAt]}
                  propertyClassNames={{
                    name: 'justify-end',
                  }}
                />
              </div>
            </div>
            <Button
              size={'sm'}
              isIconOnly
              variant={'light'}
              className={'absolute top-0 right-0'}
              onClick={() => {
                createPortal(PropertyValueScopePicker, {
                  resource,
                });
              }}
            >
              <SettingOutlined className={'text-base'} />
            </Button>
          </div>
          <div className={'mt-2'}>
            <Properties
              resource={resource}
              reload={loadResource}
              restrictedPropertyPool={PropertyPool.Reserved}
              restrictedPropertyIds={[ReservedProperty.Introduction]}
              propertyInnerDirection={'ver'}
            />
          </div>
          {/* <Divider /> */}
          {/* <FileSystemEntries isFile={resource.isFile} path={resource.path} /> */}
        </>
      )}
    </Modal>
  );
};
