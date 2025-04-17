import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Message } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { EllipsisOutlined, FileOutlined, FolderAddOutlined, FolderOutlined } from '@ant-design/icons';
import _ from 'lodash';
import FileSystemEntryChangeExampleMiscellaneousItem from './FileSystemEntryChangeExampleMiscellaneousItem';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import BApi from '@/sdk/BApi';
import { Chip, Input, Modal } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemEntryChangeItem from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';
import FileSystemEntryChangeExampleItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleItem';

type Props = { entries: Entry[] } & DestroyableProps;

export default ({
                  entries = [],
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const groupsRef = useRef(_.groupBy(entries, e => e.parent?.path));
  const [newParentNames, setNewParentNames] = useState(_.mapValues(groupsRef.current, g => g[0]!.meaningfulName));
  useEffect(() => {

  }, []);

  console.log(newParentNames);

  return (
    <Modal
      defaultVisible
      size={'xl'}
      onDestroyed={onDestroyed}
      title={t('Wrapping {{count}} file entries', { count: entries.length })}
      onOk={async () => {
        await Promise.all(_.keys(groupsRef.current).map(async p => {
          const innerEntries = groupsRef.current[p]!;
          const parentEntry = innerEntries[0]!.parent!;
          const d = [parentEntry.path, newParentNames[p]].join(BusinessConstants.pathSeparator);
          await BApi.file.moveEntries({
            destDir: d,
            entryPaths: innerEntries.map((e) => e.path),
          });
        }));
      }}
      footer={{
        actions: ['ok', 'cancel'],
        okProps: {
          children: `${t('Wrap')}(Enter)`,
          autoFocus: true,
          disabled: _.values(newParentNames).some(x => !x || x.length == 0),
        },
      }}
    >
      <div className={'flex flex-col gap-1'}>
        {Object.keys(groupsRef.current).map((parent) => {
          const innerEntries = groupsRef.current[parent] ?? [];
          const newParentName = newParentNames[parent] ?? '';
          return (
            <>
              <FileSystemEntryChangeExampleItem type={'default'} text={parent ?? '.'} isDirectory />
              <FileSystemEntryChangeItem
                type={'added'}
                editable
                text={newParentName}
                onChange={v => {
                  setNewParentNames((old) => ({ ...old, [parent]: v }));
                }}
                layer={1}
                isDirectory
              />
              {innerEntries.map((e, i) => {
                return (
                  <FileSystemEntryChangeExampleItem
                    type={'added'}
                    text={e.name}
                    layer={2}
                    isDirectory={e.isDirectory}
                    path={e.path}
                  />
                );
              })}
              {innerEntries.map((e, i) => {
                return (
                  <FileSystemEntryChangeExampleItem
                    type={'deleted'}
                    text={e.name}
                    layer={1}
                    isDirectory={e.isDirectory}
                    path={e.path}
                  />
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
