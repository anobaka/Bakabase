import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Checkbox, Tooltip } from '@/components/bakaui';

interface IProps {
  autoMatch?: boolean;
  onChange?: (integrateWithAlias: boolean) => void;
}

export default ({ autoMatch, onChange }: IProps) => {
  const { t } = useTranslation();

  return (
    <>
      <Tooltip
        content={(
          <div>
            <div>{t('By default, an empty layer will be created if we meet an empty value in multilevel data.')}</div>
            <div>{t('If checked, this kind of data will be matched with the most similar multilevel value in the database.')}</div>
            <div>{t('For example, assume we already have a multilevel data: a->b->c, then for the incoming data: ->->c, the values of 1st and 2nd layers of which are empty.')}</div>
            <div>{t('If this option is checked, we\'ll save ->->c as a->b->c, otherwise the incoming value ->->c will be saved.')}</div>
            <div>{t('This option may cause unexpected behaviors, make sure you have enough confidence to merge the produced data into data in db before check it.')}</div>
          </div>
        )}
      >
        <Checkbox
          size={'sm'}
          isSelected={autoMatch}
          onValueChange={c => {
            onChange?.(c);
          }}
        >
          {t('Auto match on empty values')}
        </Checkbox>
      </Tooltip>
    </>
  );
};
