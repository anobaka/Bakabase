import { Button, Input, Message } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import i18n from 'i18next';
import { ValidateCookie } from '@/sdk/apis';
import { CookieValidatorTarget } from '@/sdk/constants';

export default ({ cookie, target, onChange = (v) => {} }: {cookie: string|undefined; target: CookieValidatorTarget; onChange: any}) => {
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
              title: i18n.t('Validating cookie'),
              align: 'cc cc',
              duration: 0,
              closeable: true,
              hasMask: true,
            });
            ValidateCookie({
              target,
              cookie,
            }).invoke((a) => {
              if (!a.code) {
                Message.success({
                  title: i18n.t('Success'),
                  align: 'cc cc',
                });
              } else {
                Message.error({
                  title: i18n.t(a.message),
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
        {i18n.t('Validate')}
      </Button>
    </div>
  );
};
