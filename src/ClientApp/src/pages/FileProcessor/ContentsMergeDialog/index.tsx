import React, { useCallback, useEffect, useState } from 'react';
import { Message, Tree } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import AnimatedArrow from '@/components/AnimatedArrow';
import './index.scss';
import { createPortalOfComponent } from '@/components/utils';
import SimpleOneStepDialog from '@/components/SimpleOneStepDialog';

interface IProps {
  paths?: string[];
  rootPath?: string;
  afterClose?: () => void;
}

class Props implements IProps {
  paths?: string[];
  rootPath?: string;
  afterClose?: () => void;

  constructor(init?: Partial<IProps>) {
    Object.assign(this, init);
  }

  get isValid(): boolean {
    return (this.paths != undefined && this.paths.length > 0) || this.rootPath != undefined;
  }
}

function ContentsMergeDialog(props: IProps) {
  const { t } = useTranslation();
  const {
    paths,
    rootPath,
    isValid,
    afterClose,
  } = new Props(props);

  const [result, setResult] = useState<{
    rootPath: string;
    currentNames: string[];
    mergeResult: { [dir: string]: string[] };
  }>();

  useEffect(() => {
    if (isValid) {
      if (rootPath != undefined) {
        BApi.file.previewFileEntriesMergeResultInRootPath(rootPath)
          .then(a => {
            // @ts-ignore
            setResult(a.data);
          });
      } else {
        BApi.file.previewFileEntriesMergeResult(paths!)
          .then(a => {
            // @ts-ignore
            setResult(a.data);
          });
      }
    } else {
      Message.error(t('Bad data'));
    }
  }, []);

  const renderPreview = () => {
    if (result == undefined) {
      return;
    }

    const currentTreeData = [
      {
        label: result.rootPath,
        key: '0',
        children: [
          {
            label: '...',
            key: '1',
          },
          ...result.currentNames.map(n => {
            return {
              label: n,
              key: `x-${n}`,
            };
          }),
          {
            label: '...',
            key: '2',
          },
        ],
      },
    ];

    const mergedTreeData = [
      {
        label: result.rootPath,
        key: '0',
        children: [
          {
            label: '...',
            key: '1',
          },
          ...Object.keys(result.mergeResult)
            .map(dir => {
              return {
                label: dir,
                key: `y-${dir}`,
                children: result.mergeResult[dir].map(f => {
                  return {
                    label: f,
                    key: `x-${f}`,
                  };
                }),
              };
            }),
          {
            label: '...',
            key: '2',
          },
        ],
      },
    ];

    return (
      <>
        <Tree
          defaultExpandAll
          showLine
          labelRender={renderLabel}
          dataSource={currentTreeData}
        />
        <AnimatedArrow />
        <Tree
          defaultExpandAll
          showLine
          labelRender={renderLabel}
          dataSource={mergedTreeData}
        />
      </>
    );
  };

  const renderLabel = useCallback((node: any) => {
    if (node.key.startsWith('x') || node.key.startsWith('y')) {
      return (
        <div className={node.key.startsWith('x') ? 'file' : 'dir'}>
          {node.label}
        </div>
      );
    }
    return node.label;
  }, []);

  return (
    isValid ? (
      <SimpleOneStepDialog
        afterClose={afterClose}
        title={t('Merge contents')}
        className={'fp-cmd'}
        onOk={() => (rootPath != undefined ? BApi.file.mergeFileEntriesInRootPath(rootPath) : BApi.file.mergeFileEntries(paths!))}
        okProps={{
          children: `${t('Merge')}(Enter)`,
        }}
      >
        <div className={'fp-cmd-body'}>
          {renderPreview()}
        </div>
      </SimpleOneStepDialog>
      )
      : null
  );
}

ContentsMergeDialog.show = (props: IProps) => createPortalOfComponent(ContentsMergeDialog, props);

export default ContentsMergeDialog;
