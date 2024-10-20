import { Icon, Message } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Modal, Spinner } from '@/components/bakaui';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import FileSystemEntryChangeExampleItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = {
  entries: Entry[];
  workingDirectory: string;
  onDeleted?: (paths: string[]) => void;
} & DestroyableProps;

type Item = { name: string; isDirectory: boolean };

export default ({
                  entries,
                  workingDirectory,
                  onDestroyed,
                  ...dialogProps
                }: Props) => {
  const [deletingAllPaths, setDeletingAllPaths] = useState<Item[]>();

  const { t } = useTranslation();

  useEffect(() => {
    BApi.file.getSameNameEntriesInWorkingDirectory({
      workingDir: workingDirectory,
      entryPaths: entries.map(e => e.path),
    })
      .then(t => {
        setDeletingAllPaths(t.data || []);
      });

    return () => {
      // console.log(19282, 'exiting');
    };
  }, []);

  return (
    <Modal
      size={'xl'}
      onDestroyed={onDestroyed}
      defaultVisible
      title={t('Delete items with the same names')}
      onOk={async () => {
        // console.log(deletingAllPaths);
        if (!deletingAllPaths || deletingAllPaths.length == 0) {
          Message.error('Nothing to delete');
          return false;
        } else {
          const rsp = await BApi.file.removeSameNameEntryInWorkingDirectory({
            workingDir: workingDirectory,
            entryPaths: entries.map(e => e.path),
          });
          return rsp;
        }
      }}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: `${t('Delete')}(Enter)`,
          color: 'danger',
          autoFocus: true,
          disabled: !deletingAllPaths || deletingAllPaths.length == 0,
        },
      }}
    >
      <div
        className={'mb-2'}
      >{t('Removing all filesystem entries in {{workingDirectory}} that have the same names as the {{count}} selected filesystem entries', {
        count: entries.length,
        workingDirectory,
      })}</div>
      {deletingAllPaths ? (
        <>
          <div className={'flex flex-col gap-1'}>
            <FileSystemEntryChangeExampleItem type={'root'} text={workingDirectory} />
            {deletingAllPaths.map((d, i) => {
              return (
                <FileSystemEntryChangeExampleItem
                  type={'deleted'}
                  text={d.name}
                  isDirectory={d.isDirectory}
                  indent={1}
                />
              );
            })}
          </div>
        </>
      ) : (
        <div className={'flex items-center gap-2'}>
          <Spinner />
          {t('Discovering files with same name')}
        </div>
      )}
    </Modal>
  );
};
