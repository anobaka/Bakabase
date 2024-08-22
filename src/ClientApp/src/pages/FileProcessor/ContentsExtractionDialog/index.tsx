import { Balloon, Dialog, Message, Tree } from '@alifd/next';
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Entry } from '@/core/models/FileExplorer/Entry';
import AnimatedArrow from '@/components/AnimatedArrow';
import './index.scss';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent, forceFocus, sleep } from '@/components/utils';
import SimpleOneStepDialog from '@/components/SimpleOneStepDialog';

interface Props{
  entry: Entry;
  afterClose?: () => any;
}

const MaxVisibleCount = 3;

const ContentsExtractionDialog = (props: Props) => {
  const { entry, afterClose } = props;
  const { t } = useTranslation();

  const [fileEntries, setFileEntries] = useState<string[]>([]);
  const initializedRef = useRef(false);

  useEffect(() => {
    BApi.file.getFileSystemEntriesInDirectory({ path: entry.path, maxCount: 10 }).then(a => {
      initializedRef.current = true;
      setFileEntries(a.data || []);
    });
  }, []);

  const entryChildrenData: any[] = [];

  for (let i = 0; i < Math.min(MaxVisibleCount, fileEntries.length); i++) {
    entryChildrenData.push({
      label: fileEntries[i],
      key: `x-${i}`,
    });
    if (fileEntries.length > MaxVisibleCount && i == MaxVisibleCount - 1) {
      entryChildrenData.push({
        label: '...',
        key: `x-${i + 1}`,
      });
    }
  }

  const treeBefore = [{
    label: entry?.parent?.path ?? '.',
    key: '0',
    children: [
      {
        label: '...',
        key: '1',
      },
      {
        label: entry?.name,
        key: 'x',
        children: entryChildrenData,
      },
      {
        label: '...',
        key: '3',
      },
    ],
  }];

  const treeAfter = [{
    label: entry?.parent?.path ?? '.',
    key: '0',
    children: [
      {
        label: '...',
        key: '1',
      },
      ...entryChildrenData,
      {
        label: '...',
        key: '4',
      },
    ],
  }];

  const renderLabel = useCallback((node: any) => {
    if (node.key.startsWith('x')) {
      if (node.key == 'x') {
        return (
          <Balloon.Tooltip
            trigger={(
              <div className={'entry'}>
                {node.label}
              </div>
            )}
            triggerType={'hover'}
            align={'t'}
          >
            {t('Will be deleted')}
          </Balloon.Tooltip>
        );
      }
      return (
        <div className={node.key == 'x' ? 'entry' : 'sub-entry'}>
          {node.label}
        </div>
      );
    }
    return node.label;
  }, []);

  return (
    <SimpleOneStepDialog
      title={t('Extract contents from directory')}
      className={'ce-dialog'}
      afterClose={afterClose}
      okProps={{
        children: `${t('Extract')}(Enter)`,
      }}
      onOk={() => BApi.file.extractAndRemoveDirectory({ directory: entry?.path })}
    >
      <div className="ce">
        {initializedRef.current && (
          <>
            <Tree
              defaultExpandAll
              selectable={false}
              showLine
              labelRender={renderLabel}
              dataSource={treeBefore}
            />
            <AnimatedArrow />
            <Tree
              tabIndex={1}
              defaultExpandAll
              selectable={false}
              showLine
              labelRender={renderLabel}
              dataSource={treeAfter}
            />
          </>
        )}
      </div>
    </SimpleOneStepDialog>
  );
};


ContentsExtractionDialog.show = (props: Props) => createPortalOfComponent(ContentsExtractionDialog, props);

export default ContentsExtractionDialog;
