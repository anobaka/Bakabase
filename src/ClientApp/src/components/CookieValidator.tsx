import { Button, Input } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import i18n from 'i18next';
import { useTranslation } from 'react-i18next';
import { ValidateCookie } from '@/sdk/apis';
import type { CookieValidatorTarget } from '@/sdk/constants';
import { Message } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

export default ({ cookie, target, onChange = (v) => {} }: {cookie: string | undefined; target: CookieValidatorTarget; onChange: any}) => {
  const { t } = useTranslation();

  return (
    <div className={'cookie-validator'}>
      <Input.TextArea
        style={{ width: '100%' }}
        value={cookie}
        onChange={(v) => {
          onChange(v);
        }}
        autoHeight
      />
      <Button
        type={'primary'}
        text
        disabled={!cookie}
        size={'small'}
        onClick={() => {
          if (cookie?.length > 0) {
            Message.loading({
              title: t('Validating cookie'),
              align: 'cc cc',
              duration: 0,
              closeable: true,
              hasMask: true,
            });
            BApi.tool.validateCookie({
              target,
              cookie,
            }).then((a) => {
              if (!a.code) {
                Message.success({
                  title: t('Success'),
                  align: 'cc cc',
                });
              } else {
                Message.error({
                  title: a.message,
                  align: 'cc cc',
                  closeable: true,
                  duration: 0,
                  hasMask: true,
                });
              }
            });
          }
        }}
      >
        {t('Validate')}
      </Button>
    </div>
  );
};
