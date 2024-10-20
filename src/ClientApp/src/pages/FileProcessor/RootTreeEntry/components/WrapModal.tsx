import React, { useCallback, useEffect, useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { EllipsisOutlined, FileOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { Chip, Input, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemEntryChangeItem from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = { entries: Entry[] } & DestroyableProps;

export default ({
                  entries = [],
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const [newParentName, setNewParentName] = useState(entries[0]?.meaningfulName);
  useEffect(() => {

  }, []);

  const { parent } = entries[0];

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      title={t('Wrapping {{count}} file entries', { count: entries.length })}
      onOk={async () => {
        // console.log(newParentName);
        if (newParentName && newParentName?.length > 0) {
          const d = [parent!.path, newParentName].join(BusinessConstants.pathSeparator);
          const rsp = await BApi.file.moveEntries({
            destDir: d,
            entryPaths: entries.map((e) => e.path),
          });
          return rsp;
        } else {
          Message.error(t('Bad name'));
          throw new Error(t('Bad name'));
        }
      }}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: `${t('Wrap')}(Enter)`,
          autoFocus: true,
        },
      }}
    >
      <div className={'flex flex-col gap-1'}>
        <FileSystemEntryChangeItem type={'root'} text={parent?.path ?? '.'} isDirectory />
        <FileSystemEntryChangeItem type={'others'} indent={1} />
        <FileSystemEntryChangeItem
          type={'added'}
          editable
          text={newParentName}
          onChange={setNewParentName}
          indent={1}
          isDirectory
        />
        {entries.map(e => {
          return (
            <FileSystemEntryChangeItem type={'added'} text={e.name} indent={2} isDirectory={e.isDirectory} />
          );
        })}
        {entries.map(e => {
          return (
            <FileSystemEntryChangeItem type={'deleted'} text={e.name} indent={1} isDirectory={e.isDirectory} />
          );
        })}
        <FileSystemEntryChangeItem type={'others'} indent={1} />
      </div>
    </Modal>
  );
};
