import { useTranslation } from 'react-i18next';
import { DeleteOutlined, FileAddOutlined, LoadingOutlined, UploadOutlined } from '@ant-design/icons';
import { Img } from 'react-image';
import React from 'react';
import type { ValueRendererProps } from '../models';
import serverConfig from '@/serverConfig';
import BApi from '@/sdk/BApi';
import NotSet from '@/components/StandardValue/ValueRenderer/Renderers/components/NotSet';
import CustomIcon from '@/components/CustomIcon';
import { Button, Snippet, Tooltip } from '@/components/bakaui';
import { splitPathIntoSegments } from '@/components/utils';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FileSystemSelectorDialog from '@/components/FileSystemSelector/Dialog';

type AttachmentValueRendererProps = Omit<ValueRendererProps<string[]>, 'variant'> & {
  variant: ValueRendererProps<string[]>['variant'];
};

export default ({ value, variant, editor, ...props }: AttachmentValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const v = variant ?? 'default';

  const editable = !!editor;

  switch (v) {
    case 'default':
      return (
        <div className={'flex items-center gap-2 flex-wrap'}>
          {value?.map(v => {
            const pathSegments = splitPathIntoSegments(v);
            return (
              <div className={'flex flex-col gap-1 max-w-[100px] relative group'}>
                <Img
                  src={[`${serverConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(v)}`]}
                  loader={(
                    <LoadingOutlined className={'text-2xl'} />
                  )}
                  unloader={(
                    <CustomIcon type={'image-slash'} className={'text-2xl'} />
                  )}
                  alt={''}
                  style={{
                    maxWidth: 100,
                    maxHeight: 100,
                  }}
                  title={v}
                />
                <Button
                  isIconOnly
                  color={'danger'}
                  size={'sm'}
                  // variant={'light'}
                  className={'top-0 right-0 absolute hidden group-hover:block'}
                  style={{ transform: 'translate(50%, -50%)', zIndex: 1 }}
                  onClick={() => {
                    const newValue = value.filter(e => e != v);
                    editor?.onValueChange?.(newValue, newValue);
                  }}
                >
                  <DeleteOutlined className={'text-lg'} />
                </Button>
              </div>
            );
          })}
          {editable && (
            <div
              className={'flex items-center justify-center w-[80px] h-[80px] border-1 rounded'}
              style={{ borderColor: 'var(--bakaui-overlap-background)' }}
            >
              <Button
                isIconOnly
                color={'primary'}
                variant={'light'}
                onClick={() => {
                  createPortal(FileSystemSelectorDialog, {
                    targetType: 'file',
                    onSelected: entry => {
                      const newValue = (value ?? []).concat([entry.path]);
                      editor?.onValueChange?.(newValue, newValue);
                    },
                  });
                }}
              >
                <FileAddOutlined className={'text-lg'} />
              </Button>
            </div>
          )}
        </div>
      );
    case 'light':
      if (!value || value.length == 0) {
        return (
          <NotSet />
        );
      } else {
        return (
          <span className={'break-all'}>
            {(value.join(','))}
          </span>
        );
      }
  }
};
