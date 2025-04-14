import React from 'react';
import { ExclamationCircleOutlined } from '@ant-design/icons';
import { Chip, Input } from '@/components/bakaui';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import { IconType } from '@/sdk/constants';

type Props = {
  type: 'others' | 'added' | 'deleted' | 'root' | 'error';
  indent?: 0 | 1 | 2;
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
    indent = 0,
    text = '',
    path,
    onChange,
    isDirectory = false,
    hideIcon,
    className,
  } = props;

  let indentClassName = '';
  switch (indent) {
    case 0:
      break;
    case 1:
      indentClassName = 'pl-6';
      break;
    case 2:
      indentClassName = 'pl-12';
      break;
  }

  switch (type) {
    case 'error':
      return (
        <div className={`${indentClassName} ${className}`}>
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
        </div>
      );
    case 'others':
      return (
        <div className={`${indentClassName} flex items-center gap-2 ${className}`}>
          <FileSystemEntryIcon
            type={IconType.UnknownFile}
            size={20}
          />
          <Chip
            size={'sm'}
            variant={'light'}
            className={'px-0'}
            classNames={{ content: 'px-0' }}
          >
            {text}
          </Chip>
        </div>
      );
    case 'added': {
      if (editable) {
        return (
          <div className={`${indentClassName} flex items-center gap-2 ${className}`}>
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
          </div>

        );
      } else {
        return (
          <div className={`${indentClassName} ${className}`}>
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
          </div>
        );
      }
    }
    case 'deleted':
      return (
        <div className={`${indentClassName} ${className}`}>
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
        </div>
      );
    case 'root':
      return (
        <div className={'flex items-center gap-2 ${className}'}>
          <FileSystemEntryIcon
            type={IconType.Directory}
            size={20}
          />
          {text}
        </div>
      );
  }
};
