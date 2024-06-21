import React, { useEffect, useState } from 'react';

import './index.scss';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined, FolderOpenOutlined, PlayCircleOutlined } from '@ant-design/icons';
import BasicInfo from './BasicInfo';
import Properties from './Properties';
import ResourceCover from '@/components/Resource/components/ResourceCover';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import { Button, ButtonGroup, Divider, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import FileSystemEntries from '@/components/Resource/components/DetailDialog/FileSystemEntries';
import BApi from '@/sdk/BApi';
import { ResourceAdditionalItem } from '@/sdk/constants';


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
    const d = r.data || [];
    // @ts-ignore
    setResource(d[0] || {});
  };

  useEffect(() => {
    loadResource();
  }, []);

  return (
    <Modal
      size={'xl'}
      footer={{
        actions: ['ok'],
      }}
      defaultVisible
      onDestroyed={props.onDestroyed}
      title={resource?.displayName}
    >
      {resource && (
        <>
          <div className="flex gap-5">
            <div
              className="rounded flex items-center justify-center w-[400px] h-[400px] max-w-[400px] max-h-[400px] overflow-hidden"
            >
              <ResourceCover
                resourceId={resource.id}
                showBiggerOnHover={false}
              />
            </div>
            <div className="flex flex-col gap-5 grow">
              <div className={'grow'}>
                <Properties
                  resource={resource}
                  reload={loadResource}
                />
              </div>
              <div className={''}>
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
          </div>
          {/* <Divider /> */}
          {/* <FileSystemEntries isFile={resource.isFile} path={resource.path} /> */}
        </>
      )}
    </Modal>
  );
};
