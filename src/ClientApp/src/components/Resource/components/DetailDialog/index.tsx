import React, { useEffect, useState } from 'react';

import './index.scss';
import { useTranslation } from 'react-i18next';
import {
  AppstoreOutlined,
  CloseCircleOutlined,
  DisconnectOutlined,
  FolderOpenOutlined,
  PlayCircleOutlined,
  ProfileOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import { history } from 'ice';
import _ from 'lodash';
import { MdCalendarMonth } from 'react-icons/md';
import BasicInfo from './BasicInfo';
import Properties from './Properties';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import { Button, ButtonGroup, Chip, Listbox, ListboxItem, Modal, Popover, Tooltip } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BApi from '@/sdk/BApi';
import { PropertyPool, ReservedProperty, ResourceAdditionalItem } from '@/sdk/constants';
import { convertFromApiValue } from '@/components/StandardValue/helpers';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertyValueScopePicker from '@/components/Resource/components/DetailDialog/PropertyValueScopePicker';
import PlayableFiles from '@/components/Resource/components/PlayableFiles';
import CategoryPropertySortModal from '@/components/Resource/components/DetailDialog/CategoryPropertySortModal';
import CustomPropertySortModal from '@/components/CustomPropertySortModal';
import store from '@/store';
import ChildrenModal from '../ChildrenModal';
import { TiFlowChildren } from 'react-icons/ti';


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
  const uiOptions = store.useModelState('uiOptions');

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

  const hideTimeInfo = !!uiOptions.resource?.hideResourceTimeInfo;

  return (
    <Modal
      size={'xl'}
      footer={false}
      defaultVisible
      onDestroyed={props.onDestroyed}
      title={
        <div className={'flex items-center justify-between'}>
          <div>{resource?.displayName}</div>
          <Popover
            trigger={(
              <Button
                size={'sm'}
                isIconOnly
                variant={'light'}
                // className={'absolute left-0 top-0'}
              >
                <SettingOutlined className={'text-base'} />
              </Button>
            )}
            shouldCloseOnBlur
            placement={'left-start'}
          >
            <Listbox
              aria-label="Actions"
              onAction={(key) => {
                switch (key) {
                  case 'AdjustPropertyScopePriority': {
                    createPortal(PropertyValueScopePicker, {
                      resource,
                    });
                    break;
                  }
                  case 'SortPropertiesInCategory': {
                    const propertyMap = resource?.properties?.[PropertyPool.Custom] ?? {};
                    const properties = _.keys(propertyMap).map(x => {
                      const id = parseInt(x, 10);
                      const p = propertyMap[id]!;
                      if (p.visible) {
                        return {
                          id,
                          name: p.name!,
                        };
                      }
                      return null;
                    }).filter(x => x != null);
                    createPortal(CategoryPropertySortModal, {
                      categoryId: resource.categoryId,
                      properties,
                      onDestroyed: loadResource,
                    });
                    break;
                  }
                  case 'SortPropertiesGlobally': {
                    BApi.customProperty.getAllCustomProperties().then(r => {
                      const properties = (r.data || []).sort((a, b) => a.order - b.order);
                      createPortal(CustomPropertySortModal, {
                        properties,
                        onDestroyed: loadResource,
                      });
                    });
                  }
                }
              }}
            >
              <ListboxItem
                key="AdjustPropertyScopePriority"
                startContent={<AppstoreOutlined className={'text-small'} />}
              >
                {t('Adjust the display priority of property scopes')}
              </ListboxItem>
              <ListboxItem key="SortPropertiesInCategory" startContent={<ProfileOutlined className={'text-small'} />}>
                {t('Adjust orders of linked properties for current category')}
              </ListboxItem>
              <ListboxItem key="SortPropertiesGlobally" startContent={<ProfileOutlined className={'text-small'} />}>
                {t('Adjust orders of properties globally')}
              </ListboxItem>
            </Listbox>
          </Popover>
        </div>
      }
    >
      {resource && (
        <>
          <div className="flex gap-4">
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
              <div className={'flex items-center justify-between relative'}>
                <div className={'absolute flex justify-center left-0 right-0 w-full '}>
                  <ButtonGroup size={'sm'}>
                    <PlayableFiles
                      PortalComponent={({ onClick }) => (
                        <Button
                          color="primary"
                          onPress={onClick}
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
                      onPress={() => {
                        BApi.resource.openResourceDirectory({
                          id: resource.id,
                        });
                      }}
                    >
                      <FolderOpenOutlined />
                      {t('Open')}
                    </Button>
                    {resource.hasChildren && (
                      <Button
                        color="default"
                        onPress={() => {
                          createPortal(ChildrenModal, {
                            resourceId: resource.id,
                          });
                        }}
                      >
                        <TiFlowChildren className='text-lg' />
                        {t('View Children')}
                      </Button>
                    )}
                    {/* <Button */}
                    {/*   color={'danger'} */}
                    {/*   onClick={() => onRemoved?.()} */}
                    {/* > */}
                    {/*   <DeleteOutlined /> */}
                    {/*   {t('Remove')} */}
                    {/* </Button> */}
                  </ButtonGroup>
                </div>
                <div />
                <div>
                  <Button
                    className={hideTimeInfo ? 'opacity-20' : undefined}
                    isIconOnly
                    size={'sm'}
                    variant={'light'}
                    color="default"
                    onPress={() => {
                      BApi.options.patchUiOptions({
                        ...uiOptions,
                        resource: {
                          ...uiOptions.resource,
                          hideResourceTimeInfo: !hideTimeInfo,
                        },
                      });
                    }}
                  >
                    <MdCalendarMonth className={'text-lg'} />
                  </Button>
                </div>
              </div>
              {!hideTimeInfo && (
                <BasicInfo resource={resource} />
              )}
            </div>
            <div className="overflow-auto relative grow">
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
                      {t('No custom property bound. You can bind them in media library template')}
                    </div>
                  </div>
                )}
              />
              <div className={'flex flex-col gap-1 mt-2'}>
                <Properties
                  resource={resource}
                  reload={loadResource}
                  restrictedPropertyPool={PropertyPool.Reserved}
                  restrictedPropertyIds={[ReservedProperty.Cover]}
                  propertyClassNames={{
                    name: 'justify-end',
                  }}
                />
                {resource.playedAt && (
                  <div
                    style={{ gridTemplateColumns: 'calc(120px) minmax(0, 1fr)' }}
                    className={'grid gap-x-4 gap-y-1 undefined items-center overflow-visible'}
                  >
                    <Chip size={'sm'} color={'default'} radius={'sm'} className={'text-right justify-self-end'}>
                      {t('Last played at')}
                    </Chip>
                    <div className={'flex items-center gap-1'}>
                      {resource.playedAt}
                      <Tooltip content={t('Mark as not played')}>
                        <Button
                          isIconOnly
                          variant={'light'}
                          size={'sm'}
                          onPress={() => {
                            BApi.resource.markResourceAsNotPlayed(resource.id).then(r => {
                              if (!r.code) {
                                loadResource();
                              }
                            });
                          }}
                        >
                          <CloseCircleOutlined className={'text-medium opacity-60'} />
                        </Button>
                      </Tooltip>
                    </div>
                  </div>
                )}
              </div>
            </div>
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
