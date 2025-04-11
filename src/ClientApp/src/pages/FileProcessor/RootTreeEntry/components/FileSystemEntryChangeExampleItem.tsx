import { EllipsisOutlined, FileOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import React from 'react';
import { Chip, Input } from '@/components/bakaui';

type Props = {
  type: 'others' | 'added' | 'deleted' | 'root';
  indent?: 0 | 1 | 2;
  text?: string;
  editable?: boolean;
  onChange?: (v: string) => any;
  isDirectory?: boolean;
  hideIcon?: boolean;
};

export default (props: Props) => {
  const {
    type,
    editable = false,
    indent = 0,
    text = '',
    onChange,
    isDirectory = false,
    hideIcon,
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
    case 'others':
      return (
        <div className={`${indentClassName}`}>
          <EllipsisOutlined className={'text-xl'} />
        </div>
      );
    case 'added': {
      if (editable) {
        return (
          <div className={`${indentClassName} flex items-center gap-2`}>
            <Chip
              size={'sm'}
              variant={'light'}
              color={'success'}
              className={'px-0'}
              classNames={{ content: 'px-0' }}
            >
              <FolderAddOutlined className={'text-xl'} />
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
          <div className={`${indentClassName}`}>
            <Chip
              size={'sm'}
              variant={'light'}
              color={'success'}
              className={'px-0'}
              classNames={{ content: 'px-0' }}
            >
              <div className={'flex items-center gap-2'}>
                {!hideIcon && (
                  isDirectory ? (
                    <FolderOutlined className={'text-xl'} />
                  ) : (
                    <FileOutlined className={'text-xl'} />
                  )
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
        <div className={`${indentClassName}`}>
          <Chip
            size={'sm'}
            variant={'light'}
            color={'danger'}
            className={'line-through px-0'}
            classNames={{ content: 'px-0' }}
          >
            <div className={'flex items-center gap-2'}>
              {!hideIcon && (
                isDirectory ? (
                  <FolderOutlined className={'text-xl'} />
                ) : (
                  <FileOutlined className={'text-xl'} />
                )
              )}
              {text}
            </div>
          </Chip>
        </div>
      );
    case 'root':
      return (
        <div className={'flex items-center gap-2'}>
          <FolderOutlined className={'text-xl'} />
          {text}
        </div>
      );
  }
};
