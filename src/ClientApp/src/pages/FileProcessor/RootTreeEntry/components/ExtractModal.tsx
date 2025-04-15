import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import FileSystemEntryChangeExampleItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';
import FileSystemEntryChangeExampleMiscellaneousItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleMiscellaneousItem';

type Props = { entries: Entry[] } & DestroyableProps;

export default ({
                  entries = [],
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();

  useEffect(() => {
  }, []);

  // const { parent } = entries[0]!;
  const groups = _.groupBy(entries, e => e.parent?.path);

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
        {Object.keys(groups).map((parent) => {
          const innerEntries = groups[parent] ?? [];
          return (
            <>
              <FileSystemEntryChangeExampleItem type={'root'} text={parent ?? '.'} isDirectory />
              {innerEntries.map((e, i) => {
                return (
                  <>
                    <FileSystemEntryChangeExampleItem
                      path={e.path}
                      type={'deleted'}
                      indent={1}
                      text={e.name}
                      isDirectory={e.isDirectory}
                    />
                    <FileSystemEntryChangeExampleItem
                      type={'deleted'}
                      indent={2}
                      text={`${t('Other files')}...`}
                      isDirectory={false}
                    />
                    <FileSystemEntryChangeExampleItem
                      type={'added'}
                      indent={1}
                      text={`${t('Other files in {{parent}}', { parent: e.name })}...`}
                      isDirectory={false}
                    />
                  </>
                );
              })}
              <FileSystemEntryChangeExampleMiscellaneousItem parent={parent} indent={1} />
            </>
          );
        })}
      </div>
    </Modal>
  );
};
