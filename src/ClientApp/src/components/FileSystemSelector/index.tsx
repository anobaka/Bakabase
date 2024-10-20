import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowUpOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import { useUpdate, useUpdateEffect } from 'react-use';
import TreeEntry from '@/pages/FileProcessor/TreeEntry';
import type { IEntryRef } from '@/core/models/FileExplorer/Entry';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import RootEntry from '@/core/models/FileExplorer/RootEntry';
import './index.scss';
import BApi from '@/sdk/BApi';
import { buildLogger, splitPathIntoSegments, standardizePath } from '@/components/utils';
import BusinessConstants from '@/components/BusinessConstants';
import { IwFsType } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import DeleteDialog from '@/pages/FileProcessor/DeleteDialog';
import { Button, Chip, Input } from '@/components/bakaui';
import RootTreeEntry from '@/pages/FileProcessor/RootTreeEntry';

export interface IFileSystemSelectorProps {
  startPath?: string;
  targetType?: 'file' | 'folder';
  onSelected?: (entry: Entry) => any;
  onCancel?: () => any;
  filter?: (entry: Entry) => boolean;
  defaultSelectedPath?: string;
}

const log = buildLogger('FileSystemSelector');

export default (props: IFileSystemSelectorProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    startPath,
    targetType,
    onSelected,
    onCancel,
    filter: propsFilter = e => true,
    defaultSelectedPath,
  } = props;

  const [selected, setSelected] = useState<Entry>();
  const [currentDirPath, setCurrentDirPath] = useState<string>();

  useEffect(() => {
  }, []);

  const filter = (e: Entry, mode: 'visible' | 'select') => {
    if (targetType) {
      switch (targetType) {
        case 'file':
          if (![IwFsType.Audio, IwFsType.CompressedFilePart, IwFsType.CompressedFileEntry, IwFsType.Image, IwFsType.Unknown, IwFsType.Video].includes(e.type)) {
            if (mode == 'select') {
              return false;
            } else {
              if (e.type != IwFsType.Directory) {
                return false;
              }
            }
          }
          break;
        case 'folder':
          if (e.type != IwFsType.Directory) {
            return false;
          }
          break;
      }
    }
    if (propsFilter && !propsFilter(e)) {
      return false;
    }
    return true;
  };

  return (
    <div className={'flex flex-col gap-2 grow max-h-full'} >
      <RootTreeEntry
        rootPath={startPath}
        defaultSelectedPath={defaultSelectedPath}
        filter={{
          custom: e => filter(e, 'visible'),
        }}
        selectable={'single'}
        onSelected={es => {
          const e = es[0];
          if (e && filter(e, 'select')) {
            setSelected(e);
          } else {
            setSelected(undefined);
          }
        }}
        onRootPathChange={p => setCurrentDirPath(p)}
      />
      {
        selected && (
          <div className="flex items-center gap-2">
            <Chip
              size={'sm'}
              radius={'sm'}
              variant={'light'}
              color={'success'}
            >
              {t('Selected')}
            </Chip>
            <Chip
              size={'sm'}
              radius={'sm'}
              variant={'light'}
              color={'success'}
            >
              {selected.path}
            </Chip>
          </div>
        )
      }
      <div className="flex items-center justify-between mb-2">
        <Button
            // size={'small'}
          isDisabled={!currentDirPath}
          onClick={() => {
              BApi.file.createDirectory({ parent: currentDirPath });
            }}
        >
          <FolderAddOutlined className={'text-base'} />
          {t('New Folder')}
        </Button>
        <div className="flex items-center gap-2">
          <Button
            color={'primary'}
            // size={'small'}
            disabled={!selected}
            onClick={() => {
              onSelected?.(selected!);
            }}
          >
            {t('OK')}
          </Button>
          <Button
            // size={'small'}
            onClick={() => {
              onCancel?.();
            }}
          >
            {t('Cancel')}
          </Button>
        </div>
      </div>
    </div>
  );
};
