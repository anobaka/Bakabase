import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FolderAddOutlined } from '@ant-design/icons';
import { useUpdate } from 'react-use';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import './index.scss';
import BApi from '@/sdk/BApi';
import { buildLogger } from '@/components/utils';
import { IwFsType } from '@/sdk/constants';
import { Button, Chip } from '@/components/bakaui';
import type { RootTreeEntryRef } from '@/pages/FileProcessor/RootTreeEntry';
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
  const rootRef = useRef<RootTreeEntryRef | null>(null);

  useEffect(() => {
    // return () => {
    //   log('disposing', rootRef);
    //   rootRef.current?.root?.dispose();
    // };
  }, []);


  const filter = (e: Entry, mode: 'visible' | 'select') => {
    log('filter', e, mode, targetType);
    if (!e.path) {
      return false;
    }
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
          if (e.type != IwFsType.Directory && e.type != IwFsType.Drive) {
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

  log('selected', selected);

  const trySelectRootOrClearSelection = () => {
    if (rootRef.current?.root && filter(rootRef.current.root, 'select')) {
      setSelected(rootRef.current.root);
    } else {
      setSelected(undefined);
    }
  };

  return (
    <div className={'flex flex-col gap-2 grow max-h-full'}>
      <RootTreeEntry
        rootPath={startPath}
        defaultSelectedPath={defaultSelectedPath}
        filter={{
          custom: e => filter(e, 'visible'),
        }}
        selectable={'single'}
        capabilities={['rename']}
        onSelected={es => {
          const e = es[0];
          log(rootRef.current);

          if (e) {
            if (filter(e, 'select')) {
              setSelected(e);
            } else {
              setSelected(undefined);
            }
          } else {
            trySelectRootOrClearSelection();
          }
        }}
        ref={r => {
          rootRef.current = r;
          log('ref', r);
        }}
        onInitialized={() => {
          log('onInitialized', rootRef.current?.root);
          if (rootRef.current?.root) {
            trySelectRootOrClearSelection();
            if (rootRef.current.root.isDirectory) {
              setCurrentDirPath(rootRef.current.root.path);
            }
          }
        }}
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
