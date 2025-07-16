'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { NumberInput } from '@/components/bakaui';

type Props = {
  start?: number;
  end?: number;
  onChange?: (start?: number, end?: number) => void;
};

export default ({ start, end, onChange }: Props) => {
  const { t } = useTranslation();
  return (
    <>
      <div>{t<string>('Page range')}</div>
      <div className={'flex items-start gap-2'}>
        <NumberInput
          size={'sm'}
          label={t<string>('Start page')}
          min={1}
          step={1}
          value={start}
          onChange={(v) => {
            onChange?.(v, end);
          }}
          description={(
            <div>
              <div>{t<string>('Set a page range if you don\'t want to download them all.')}</div>
              <div>{t<string>('The minimal page number is 1.')}</div>
            </div>
          )}
        />
        <NumberInput
          size={'sm'}
          label={t<string>('End page')}
          min={end ?? 1}
          step={1}
          value={end}
          onChange={(v) => {
            onChange?.(start, v);
          }}
          description={t<string>('Set a page range if you don\'t want to download them all.')}
        />
      </div>
    </>
  );
};
