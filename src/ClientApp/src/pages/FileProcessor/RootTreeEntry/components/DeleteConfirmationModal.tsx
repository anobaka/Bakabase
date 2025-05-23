import React from 'react';
import { useTranslation } from 'react-i18next';
import _ from 'lodash';
import FileSystemEntryChangeItem from './FileSystemEntryChangeExampleItem';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Modal } from '@/components/bakaui';
import { buildLogger, splitPathIntoSegments } from '@/components/utils';
import BApi from '@/sdk/BApi';
import FileSystemEntryChangeExampleMiscellaneousItem
  from '@/pages/FileProcessor/RootTreeEntry/components/FileSystemEntryChangeExampleMiscellaneousItem';

type Props = {
  rootPath?: string;
  entries: SimpleEntry[];
} & DestroyableProps;

const log = buildLogger('DeleteConfirmationModal');

type SimpleEntry = { path: string; isDirectory: boolean };

type Item = {
  path: string;
  pathSegment: string;
  children?: Item[];
  isDirectory: boolean;
  willBeDeleted: boolean;
};

const _mergeItems = (entries: SimpleEntry[],
                     entryRestSegmentsMap: Map<string, string[]>, path?: string): Item[] => {
  const prefixes: string[] = [];
  const ret: Item[] = [];
  const groups = _.groupBy(entries, e => entryRestSegmentsMap.get(e.path)!.shift()!);
  const keys = _.sortBy(_.keys(groups), x => x);
  for (const key of keys) {
    const childrenEntries = groups[key]!.filter(x => entryRestSegmentsMap.get(x.path)!.length > 0);
    const pathSegment = [...prefixes, key].join('/');
    const newPath = [path, pathSegment].filter(x => x != undefined).join('/');
    if (childrenEntries.length == 1) {
      ret.push({
        pathSegment,
        isDirectory: childrenEntries[0]!.isDirectory,
        path: newPath,
        willBeDeleted: entryRestSegmentsMap.has(newPath),
      });
    } else {
      ret.push({
        pathSegment,
        children: _mergeItems(childrenEntries, entryRestSegmentsMap, newPath),
        isDirectory: true,
        path: newPath,
        willBeDeleted: entryRestSegmentsMap.has(newPath),
      });
    }
  }
  return ret;
};

const mergeItems = (entries: SimpleEntry[], rootPath?: string): Item[] => {
  const entryRestSegmentsMap = entries.reduce((s, t) => {
    s.set(t.path, splitPathIntoSegments(rootPath == undefined ? t.path : t.path.replace(rootPath, '')));
    return s;
  }, new Map<string, string[]>());

  const list = _mergeItems(entries, entryRestSegmentsMap, rootPath);

  if (rootPath == undefined) {
    return list;
  } else {
    return [{
      pathSegment: rootPath,
      children: list,
      isDirectory: true,
      path: rootPath,
      willBeDeleted: false,
    }];
  }
};

const renderItem = (item: Item, layer: number) => {
  return (
    <>
      <FileSystemEntryChangeItem
        type={item.willBeDeleted ? 'deleted' : 'default'}
        text={item.pathSegment}
        isDirectory={item.isDirectory}
        layer={layer}
      />
      {item.children && item.children.map(x => renderItem(x, layer + 1))}
    </>
  );
};

export default ({
                  entries = [],
                  onDestroyed,
                  rootPath,
                }: Props) => {
  const { t } = useTranslation();
  const items = mergeItems(entries, rootPath);

  log(rootPath, entries, items);

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
        {items.map(item => renderItem(item, 0))}
        {rootPath && (
          <FileSystemEntryChangeExampleMiscellaneousItem parent={rootPath} indent={1} />
        )}
      </div>
    </Modal>
  );
};
