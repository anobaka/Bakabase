import React, { useCallback, useEffect, useState } from 'react';
import { Dialog, Input, Message, Tree } from '@alifd/next';
import './index.scss';
import { useTranslation } from 'react-i18next';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import AnimatedArrow from '@/components/AnimatedArrow';
import { createPortalOfComponent, standardizePath } from '@/components/utils';
import BApi from '@/sdk/BApi';

interface Props {
  onWrapEnd?: (entries: Entry[]) => void;
  onClose?: () => void;
  entries: Entry[];
  afterClose?: () => any;
}

const pathSeparator = '/';

function WrapEntriesDialog({
                             entries = [],
                             onWrapEnd = (es) => {
                             },
                             afterClose,
                           }: Props) {
  const { t } = useTranslation();
  const [newParentName, setNewParentName] = useState(entries[0]?.meaningfulName);
  const [visible, setVisible] = useState(true);
  useEffect(() => {

  }, []);

  const { parent } = entries[0];

  const listData = entries.map((e, i) => ({
    label: e.name,
    key: `x-${i}`,
  }));

  const treeBefore = [{
    label: parent?.path ?? '.',
    key: '0',
    children: [
      {
        label: '...',
        key: '1',
      },
      ...listData,
      {
        label: '...',
        key: '3',
      },
    ],
  }];

  const treeAfter = [{
    label: parent?.path ?? '.',
    key: '0',
    children: [
      {
        label: '...',
        key: '1',
      },
      {
        label: newParentName,
        key: 'parent',
        children: listData,
      },
      {
        label: '...',
        key: '4',
      },
    ],
  }];


  const renderLabel = useCallback((node: any) => {
    if (node.key.startsWith('x')) {
      return (
        <div className={'entry'}>
          {node.label}
        </div>
      );
    } else {
      if (node.key == 'parent') {
        return (
          <Input
            size={'small'}
            defaultValue={newParentName}
            onChange={v => {
              setNewParentName(v);
            }}
          />
        );
      }
    }
    return node.label;
  }, []);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  return (
    <Dialog
      v2
      closeMode={['close', 'esc', 'mask']}
      visible={visible}
      className={'we-dialog'}
      width={'auto'}
      afterClose={afterClose}
      title={t('Wrapping {{count}} file entries', { count: entries.length })}
      onCancel={close}
      onClose={close}
      onOk={async () => {
        console.log(newParentName);
        if (newParentName && newParentName?.length > 0) {
          const d = standardizePath(`${parent?.path}${pathSeparator}${newParentName}`);
          const rsp = await BApi.file.moveEntries({
            destDir: d,
            entryPaths: entries.map((e) => e.path),

          });
          if (!rsp.code) {
            onWrapEnd(entries);
          }
          close();
          return rsp;
        } else {
          Message.error(t('Bad name'));
          throw new Error(t('Bad name'));
        }
      }}
      okProps={{
        children: `${t('Wrap')}(Enter)`,
      }}
    >
      <div className={'we'}>
        <Tree
          defaultExpandAll
          dataSource={treeBefore}
          showLine
          labelRender={renderLabel}
        />
        <AnimatedArrow />
        <Tree
          defaultExpandAll
          dataSource={treeAfter}
          showLine
          labelRender={renderLabel}
        />
      </div>
    </Dialog>
  );
}

WrapEntriesDialog.show = (props: Props) => createPortalOfComponent(WrapEntriesDialog, props);

export default WrapEntriesDialog;
