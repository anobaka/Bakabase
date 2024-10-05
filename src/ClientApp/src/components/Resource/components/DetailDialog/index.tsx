import React, { useEffect, useState } from 'react';

import './index.scss';
import { useTranslation } from 'react-i18next';
import { FolderOpenOutlined, PlayCircleOutlined } from '@ant-design/icons';
import BasicInfo from './BasicInfo';
import Properties from './Properties';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import { Button, ButtonGroup, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BApi from '@/sdk/BApi';
import { ReservedProperty, ResourceAdditionalItem, PropertyPool } from '@/sdk/constants';
import { convertFromApiValue } from '@/components/StandardValue/helpers';


interface Props extends DestroyableProps {
  id: number;
  onPlay?: () => void;
  onRemoved?: () => void;
  noPlayableFile?: boolean;
}

export default ({
                  id,
                  onPlay,
                  onRemoved,
                  noPlayableFile = false,
                  ...props
                }: Props) => {
  const { t } = useTranslation();
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
          <div className="flex gap-4 max-h-[600px]">
            <div className="min-w-[400px] w-[400px] max-w-[400px] flex flex-col gap-4">
              <div className={'h-[400px] max-h-[400px] overflow-hidden rounded flex items-center justify-center'}>
                <ResourceCover
                  resourceId={resource.id}
                  showBiggerOnHover={false}
                  coverPaths={resource.coverPaths}
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
                  <Button
                    color="primary"
                    onClick={() => !noPlayableFile && onPlay?.()}
                    disabled={noPlayableFile}
                  >
                    <PlayCircleOutlined />
                    {t('Play')}
                  </Button>
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
            <div className="overflow-auto">
              <div className={'grow'}>
                <Properties
                  resource={resource}
                  reload={loadResource}
                  restrictedPropertyPool={PropertyPool.Custom}
                  propertyClassNames={{
                    name: 'justify-end',
                  }}
                />
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
