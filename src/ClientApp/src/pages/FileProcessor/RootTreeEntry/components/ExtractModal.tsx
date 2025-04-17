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

  const [targetEntries, setTargetEntries] = useState<Entry[]>([]);

  useEffect(() => {
    const directoryEntries = _.sortBy(_.sortBy(entries.filter(entry => entry.isDirectory), e => e.path),
      x => x.path.length);
    const outerDirectoryEntries: Entry[] = [];
    const outerDirectoryPaths: Set<string> = new Set();
    for (const de of directoryEntries) {
      let skip = false;
      for (const odp of outerDirectoryPaths) {
        if (de.path.startsWith(odp)) {
          skip = true;
        }
      }
      if (!skip) {
        outerDirectoryPaths.add(`${de.path}/`);
        outerDirectoryEntries.push(de);
      }
    }
    setTargetEntries(outerDirectoryEntries);
  }, [entries]);


  // const { parent } = entries[0]!;
  const groups = _.groupBy(targetEntries, e => e.parent?.path);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      title={t('Extract {{count}} directories', { count: entries.length })}
      onOk={async () => {
        for (const e of targetEntries) {
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
              <FileSystemEntryChangeExampleItem type={'default'} text={parent ?? '.'} isDirectory />
              {innerEntries.map((e, i) => {
                if (e.isDirectoryOrDrive) {
                  return (
                    <>
                      <FileSystemEntryChangeExampleItem
                        path={e.path}
                        type={'deleted'}
                        layer={1}
                        text={e.name}
                        isDirectory={e.isDirectory}
                      />
                      <FileSystemEntryChangeExampleItem
                        type={'deleted'}
                        layer={2}
                        text={`${t('Other files')}...`}
                        isDirectory={false}
                      />
                      <FileSystemEntryChangeExampleItem
                        type={'added'}
                        layer={1}
                        text={`${t('Other files in {{parent}}', { parent: e.name })}...`}
                        isDirectory={false}
                      />
                    </>
                  );
                } else {
                  return (
                    <FileSystemEntryChangeExampleItem
                      path={e.path}
                      type={'default'}
                      layer={1}
                      text={e.name}
                      isDirectory={e.isDirectory}
                    />
                  );
                }
              })}
              <FileSystemEntryChangeExampleMiscellaneousItem parent={parent} indent={1} />
            </>
          );
        })}
      </div>
    </Modal>
  );
};
