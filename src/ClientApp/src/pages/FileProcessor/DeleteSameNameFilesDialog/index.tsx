import { Dialog, Icon, Message } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import React, { useEffect, useState } from 'react';
import './index.scss';
import IceLabel from '@icedesign/label';
import { Trans, useTranslation } from 'react-i18next';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import SimpleOneStepDialog from '@/components/SimpleOneStepDialog';

interface DeleteSameNameFilesDialogProps extends DialogProps {
  workingDirectory: string;
  fileName: string;
  filePath: string;
  onDeleted: (paths: string[]) => void;
  key?: string;
  afterClose?: () => any;
}

function DeleteSameNameFilesDialog({
                                     workingDirectory,
                                     filePath,
                                     fileName,
                                     onDeleted,
                                     afterClose,
                                     ...dialogProps
                                   }: DeleteSameNameFilesDialogProps) {
  const [deletingAllPaths, setDeletingAllPaths] = useState<string[]>([]);

  const { t } = useTranslation();

  useEffect(() => {
    BApi.file.getSameNameEntriesInWorkingDirectory({
      workingDir: workingDirectory,
      entryPath: filePath,
    })
      .then(t => {
        setDeletingAllPaths(t.data || []);
      });

    return () => {
      // console.log(19282, 'exiting');
    };
  }, []);

  return (
    <SimpleOneStepDialog
      className={'delete-same-name-files-dialog'}
      afterClose={afterClose}
      title={(
        <Trans i18nKey={'dsnf-dialog-title'} workingDirectory={workingDirectory} fileName={fileName}>
          Deleting all <span style={{ color: 'red', margin: '0 5px' }}>{{ fileName } as any}</span> in working directory: <span style={{ color: '#0394f5', margin: '0 5px' }}>{{ workingDirectory } as any}</span>
        </Trans>
      )}
      onOk={async () => {
        console.log(deletingAllPaths);
        if (!(deletingAllPaths?.length > 0)) {
          Message.error('Nothing to delete');
          return false;
        } else {
          const rsp = await BApi.file.removeSameNameEntryInWorkingDirectory({
            workingDir: workingDirectory,
            entryPath: filePath,
          });
          if (!rsp.code) {
            if (onDeleted) {
              onDeleted(rsp.data || []);
            }
          }
          return rsp;
        }
      }}
      okProps={{
        children: `${t('Delete')}(Enter)`,
        warning: true,
        disabled: !(deletingAllPaths?.length > 0),
      }}
    >
      {deletingAllPaths ? (
        <>
          <ul>
            {deletingAllPaths?.map((d, i) => {
              return (
                <li key={d}>
                  <IceLabel inverse={false} status={'info'}>{i + 1}</IceLabel>
                  <span>{d}</span>
                </li>
              );
            })}
          </ul>
        </>
      ) : (
        <div>
          {t('Discovering files with same name')}
          &nbsp;
          <Icon type={'loading'} />
        </div>
      )}
    </SimpleOneStepDialog>
  );
}

DeleteSameNameFilesDialog.show = (props: DeleteSameNameFilesDialogProps) => createPortalOfComponent(DeleteSameNameFilesDialog, props);

export default DeleteSameNameFilesDialog;
