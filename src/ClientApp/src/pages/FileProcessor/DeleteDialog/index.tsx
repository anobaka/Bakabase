import React from 'react';
import { useTranslation } from 'react-i18next';
import SimpleOneStepDialog from '@/components/SimpleOneStepDialog';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';

interface IProps {
  paths: string[];
  afterClose?: () => any;
}

function DeleteDialog({
                  paths = [],
                        afterClose,
                }: IProps) {
  const { t } = useTranslation();

  return (
    <SimpleOneStepDialog
      title={t('Sure to delete?')}
      afterClose={afterClose}
      okProps={{
        children: `${t('Delete')}(Enter)`,
        warning: true,
      }}
      onOk={async () => {
        const rsp = await BApi.file.removeFiles({ paths });
        return rsp;
      }}
    >
      <div>
        {paths.map((e) => (
          <div key={e}>{e}</div>
        ))}
      </div>
    </SimpleOneStepDialog>
  );
}

DeleteDialog.show = (props: IProps) => createPortalOfComponent(DeleteDialog, props);

export default DeleteDialog;
