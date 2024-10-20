import React, { useCallback, useEffect, useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { EllipsisOutlined, FileOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { Chip, Input, Modal, Spinner } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemEntryChangeExampleItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = {
  entries: Entry[];
  groupInternal: boolean;
} & DestroyableProps;

type Group = {directoryName: string; filenames: string[]};

export default ({
                  entries = [],
                  groupInternal,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();

  const [result, setResult] = useState<{groups: Group[]; rootPath: string}>();

  useEffect(() => {
    BApi.file.previewFileSystemEntriesGroupResult({ paths: entries.map(e => e.path), groupInternal }).then(x => {
      setResult(x.data);
    });
  }, []);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      title={t(groupInternal ? 'Group internal items' : 'Group {{count}} items', { count: entries.length })}
      onOk={async () => {
        for (const e of entries) {
          await BApi.file.extractAndRemoveDirectory({ directory: e.path });
        }
      }}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: `${t('Extract')}(Enter)`,
          autoFocus: true,
        },
      }}
    >
      {result ? (
        <div className={'flex flex-col gap-1'}>
          <FileSystemEntryChangeExampleItem type={'root'} text={result.rootPath} isDirectory />
          <FileSystemEntryChangeExampleItem type={'others'} indent={1} />
          {result.groups.map(g => {
            return (
              <>
                <FileSystemEntryChangeExampleItem type={'added'} indent={1} text={g.directoryName} isDirectory />
                {g.filenames.map(f => {
                  return (
                    <FileSystemEntryChangeExampleItem type={'added'} indent={2} text={f} />
                  );
                })}
                {g.filenames.map(f => {
                  return (
                    <FileSystemEntryChangeExampleItem type={'deleted'} indent={1} text={f} />
                  );
                })}
              </>
            );
          })}
          <FileSystemEntryChangeExampleItem type={'others'} indent={1} />
        </div>
      ) : (
        <div className={'flex items-center gap-2'}>
          <Spinner />
          {t('Calculating changes...')}
        </div>
      )}
    </Modal>
  );
};
