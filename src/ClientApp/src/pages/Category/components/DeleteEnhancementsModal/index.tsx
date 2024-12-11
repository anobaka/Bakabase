import { QuestionCircleOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import { Checkbox, Modal, Tooltip } from '@/components/bakaui';
import type { DestroyableProps } from '@/components/bakaui/types';

type Props = {
  title: string;
  onOk: (deleteEmptyOnly: boolean) => Promise<any>;
} & DestroyableProps;

export default ({
                  title,
                  onOk,
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();
  const [deleteEmptyOnly, setDeleteEmptyOnly] = React.useState(false);
  return (
    <Modal
      title={title}
      onDestroyed={onDestroyed}
      defaultVisible
      onOk={async () => await onOk(deleteEmptyOnly)}
    >
      <div className={'flex flex-col gap-4'}>
        <div className={'flex items-center gap-1'}>
          <Checkbox
            onValueChange={v => setDeleteEmptyOnly(v)}
            size={'sm'}
          >{t('Delete empty records only')}</Checkbox>
          <Tooltip
            color={'secondary'}
            className={'max-w-[600px]'}
            content={(
              <div>
                <div>{t('When the enhancement is successfully executed but no data available for enhancement is retrieved, an empty enhancement record is generated.')}</div>
                <div>{t('Enhancement will only be executed when there are no existing enhancement records, so in some cases, you may only need to delete the empty enhancement records.')}</div>
              </div>
            )}
          >
            <QuestionCircleOutlined className={'text-base'} />
          </Tooltip>
        </div>
        <div>{t('This operation cannot be undone. Would you like to proceed?')}</div>
      </div>
    </Modal>
  );
};
