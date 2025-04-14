import React from 'react';
import { useTranslation } from 'react-i18next';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Modal } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';
import FileSystemEntryChangeItem from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = {
  rootPath?: string;
  entries: { path: string; isDirectory: boolean }[];
} & DestroyableProps;

const log = buildLogger('DeleteConfirmationModal');

export default ({
                  entries = [],
                  onDestroyed,
                  rootPath,
                }: Props) => {
  const { t } = useTranslation();

  console.log(entries);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      title={t('Sure to delete?')}
      onDestroyed={onDestroyed}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: `${t('Delete')}(Enter)`,
          color: 'danger',
          autoFocus: true,
        },
      }}
      onOk={async () => await BApi.file.removeFiles({ paths: entries.map(p => p.path) })}
    >
      <div className={'flex flex-col gap-1'}>
        {rootPath && (
          <FileSystemEntryChangeItem type={'root'} text={rootPath} isDirectory />
        )}
        {entries.map(e => {
          return (
            <FileSystemEntryChangeItem
              type={'deleted'}
              path={e.path}
              isDirectory={e.isDirectory}
              text={rootPath ? e.path.replace(rootPath, '') : e.path}
              indent={rootPath ? 1 : 0}
            />
          );
        })}
      </div>
    </Modal>
  );
};
