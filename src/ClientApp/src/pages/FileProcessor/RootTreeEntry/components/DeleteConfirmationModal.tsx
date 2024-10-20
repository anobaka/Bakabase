import React, { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import type { DestroyableProps } from '@/components/bakaui/types';
import { Modal } from '@/components/bakaui';
import { buildLogger } from '@/components/utils';
import BApi from '@/sdk/BApi';

type Props = {
  paths: string[];
} & DestroyableProps;

const log = buildLogger('DeleteConfirmationModal');

export default ({
                  paths = [],
                  onDestroyed,
                }: Props) => {
  const { t } = useTranslation();

  const deleteBtnRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    log('Delete button', deleteBtnRef.current);
    deleteBtnRef.current?.focus();
  }, []);

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
          ref: deleteBtnRef,
        },
      }}
      onOk={async () => await BApi.file.removeFiles({ paths })}
    >
      <div>
        {paths.map((e) => (
          <div key={e}>{e}</div>
        ))}
      </div>
    </Modal>
  );
};
