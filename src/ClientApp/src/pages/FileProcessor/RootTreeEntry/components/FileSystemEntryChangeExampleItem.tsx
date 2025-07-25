import type { CSSProperties } from 'react';
import React from 'react';
import { ExclamationCircleOutlined } from '@ant-design/icons';
import { Chip, Input } from '@/components/bakaui';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import { IconType } from '@/sdk/constants';

type Props = {
  type: 'others' | 'added' | 'deleted' | 'default' | 'error';
  layer?: number;
  text?: string;
  path?: string;
  editable?: boolean;
  onChange?: (v: string) => any;
  isDirectory?: boolean;
  hideIcon?: boolean;
  className?: string;
};

export default (props: Props) => {
  const {
    type,
    editable = false,
    layer = 0,
    text = '',
    path,
    onChange,
    isDirectory = false,
    hideIcon,
    className,
  } = props;

  const style: CSSProperties = {
    paddingLeft: `${layer * 24}px`,
  };

  const renderInner = () => {
    switch (type) {
      case 'error':
        return (
          <>
            <Chip
              size={'sm'}
              variant={'light'}
              color={'danger'}
              className={'px-0 whitespace-normal h-auto'}
              classNames={{ content: 'px-0' }}
            >
              <div className={'flex items-center gap-2'}>
                {!hideIcon && (
                  <ExclamationCircleOutlined style={{ fontSize: '18px' }} />
                )}
                {text}
              </div>
            </Chip>
          </>
        );
      case 'others':
        return (
          <>
            <FileSystemEntryIcon
              type={IconType.UnknownFile}
              size={20}
            />
            <Chip
              size={'sm'}
              variant={'light'}
              className={'px-0 whitespace-break-spaces'}
              classNames={{ content: 'px-0' }}
            >
              {text}
            </Chip>
          </>
        );
      case 'added': {
        if (editable) {
          return (
            <>
              <Chip
                size={'sm'}
                variant={'light'}
                color={'success'}
                className={'px-0'}
                classNames={{ content: 'px-0' }}
              >
                <FileSystemEntryIcon
                  type={IconType.Directory}
                  size={20}
                />
              </Chip>
              <Input
                radius={'none'}
                // classNames={{
                //   inputWrapper: 'px-0',
                // }}
                size={'sm'}
                defaultValue={text}
                onValueChange={v => {
                  onChange?.(v);
                }}
              />
            </>
          );
        } else {
          return (
            <>
              <Chip
                size={'sm'}
                variant={'light'}
                color={'success'}
                className={'px-0'}
                classNames={{ content: 'px-0' }}
              >
                <div className={'flex items-center gap-2'}>
                  {!hideIcon && (
                    <FileSystemEntryIcon
                      path={path}
                      type={isDirectory ? IconType.Directory : path ? IconType.Dynamic : IconType.UnknownFile}
                      size={20}
                    />
                  )}
                  {text}
                </div>
              </Chip>
            </>
          );
        }
      }
      case 'deleted':
        return (
          <>
            <Chip
              size={'sm'}
              variant={'light'}
              color={'danger'}
              className={'line-through px-0 whitespace-normal h-auto'}
              classNames={{ content: 'px-0' }}
            >
              <div className={'flex items-center gap-2'}>
                {!hideIcon && (
                  <FileSystemEntryIcon
                    path={path}
                    type={isDirectory ? IconType.Directory : path ? IconType.Dynamic : IconType.UnknownFile}
                    size={20}
                  />
                )}
                {text}
              </div>
            </Chip>
          </>
        );
      case 'default':
        return (
          <>
            <FileSystemEntryIcon
              type={IconType.Directory}
              size={20}
            />
            {text}
          </>
        );
    }
  };

  return (
    <div className={`${className} flex items-center gap-2`} style={style}>
      {renderInner()}
    </div>
  );
};
