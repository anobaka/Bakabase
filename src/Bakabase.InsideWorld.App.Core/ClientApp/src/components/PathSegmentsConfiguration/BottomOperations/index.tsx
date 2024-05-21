import { Dialog } from '@alifd/next';
import React from 'react';
import { useTranslation } from 'react-i18next';
import type { Value } from '../models';
import { Button, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { PscValue } from '@/components/PathSegmentsConfiguration/models/PscValue';
import ValidationResult from '@/components/PathSegmentsConfiguration/ValidationResult';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Props = {
  value: Value;
  hasError: boolean;
};

export default ({ value, hasError }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const validationButton = (
    <Button
      disabled={hasError}
      color={'primary'}
      size={'small'}
      onClick={() => {
        createPortal(Modal, {
          defaultVisible: true,
          footer: false,
        });
        const dialog = Dialog.show({
          title: t('Validating'),
          footer: false,
          closeable: false,
        });

        // @ts-ignore
        BApi.mediaLibrary.validatePathConfiguration(PscValue.fromComponentValue(value))
          .then((t) => {
            ValidationResult.show({
              // @ts-ignore
              testResult: t.data,
            });
          })
          .finally(() => {
            dialog.hide();
          });
      }}
    >
      {t('Validate')}
    </Button>
  );

  return (
    <div className="">
      {validationButton}
    </div>
  );
};
