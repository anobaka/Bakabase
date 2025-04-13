import React, { useCallback, useEffect, useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { EllipsisOutlined, FileOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { Chip, Input, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemEntryChangeExampleItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = { entries: Entry[] } & DestroyableProps;

type Item = {name: string; isDirectory: boolean};

const SampleCount = 3;

export default ({
                  entries = [],
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();

  const [sampleItems, setSampleItems] = useState<Item[]>([]);

  useEffect(() => {
    BApi.file.getTopLevelFileSystemEntryNames({ root: entries[0]!.path }).then(x => {
      setSampleItems(x.data ?? []);
    });
  }, []);

  const { parent } = entries[0]!;

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      title={t('Extract {{count}} directories', { count: entries.length })}
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
      <div className={'flex flex-col gap-1'}>
        <FileSystemEntryChangeExampleItem type={'root'} text={parent?.path ?? '.'} isDirectory />
        <FileSystemEntryChangeExampleItem type={'others'} indent={1} />
        {entries.map((e, i) => {
          const samples: Item[] = i == 0 ? sampleItems.slice(0, SampleCount) : [];
          return (
            <>
              <FileSystemEntryChangeExampleItem type={'deleted'} indent={1} text={e.name} isDirectory={e.isDirectory} />
              {samples.map(c => {
                return (
                  <FileSystemEntryChangeExampleItem type={'deleted'} indent={2} text={c.name} isDirectory={c.isDirectory} />
                );
              })}
              {(sampleItems.length > SampleCount || i > 0) && (
                <FileSystemEntryChangeExampleItem type={'deleted'} indent={2} text={`${t('Other files')}...`} isDirectory={false} />
              )}
              {samples.map(c => {
                return (
                  <FileSystemEntryChangeExampleItem type={'added'} indent={1} text={c.name} isDirectory={c.isDirectory} />
                );
              })}
              {(sampleItems.length > SampleCount || i > 0) && (
                <FileSystemEntryChangeExampleItem type={'added'} indent={1} text={`${t('Other files in {{parent}}', { parent: e.name })}...`} isDirectory={false} />
              )}
            </>
          );
        })}
        <FileSystemEntryChangeExampleItem type={'others'} indent={1} />
      </div>
    </Modal>
  );
};
