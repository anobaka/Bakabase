import { Trans, useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { history } from 'ice';
import ErrorModal from '../Modal';
import { Accordion, AccordionItem, Divider, Link, Modal, Snippet, Spacer } from '@/components/bakaui';
import BApi from '@/sdk/BApi';

interface IProps {
  error?: string;
}

export default ({ error }: IProps) => {
  const { t } = useTranslation();

  useEffect(() => {
  }, []);

  return (
    <div className={'flex gap-2 items-center'}>
      <span>{error ?? t('We have encountered some problems.')}</span>
      <span
        style={{ color: 'var(--bakaui-primary)' }}
        className={'cursor-pointer'}
        onClick={() => ErrorModal.show({})}
      >{t('how should I handle this problem?')}</span>
    </div>
  );
};
