'use client';

import React from 'react';
import { useTranslation } from 'react-i18next';
import { Chip, Tooltip } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  return (
    <Tooltip content={t<string>('You can search values by aliases also')} color={'foreground'}>
      <Chip size={'sm'} >{t<string>('Alias applied')}</Chip>
    </Tooltip>
  );
};
