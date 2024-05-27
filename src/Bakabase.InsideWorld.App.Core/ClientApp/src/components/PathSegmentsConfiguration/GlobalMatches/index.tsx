import React from 'react';
import { useTranslation } from 'react-i18next';
import type { OnDeleteMatcherValue } from '../models';
import type { IPscPropertyMatcherValue } from '../models/PscPropertyMatcherValue';
import type { PscContext } from '../models/PscContext';
import { PscMatcherValue } from '../models/PscMatcherValue';
import CustomIcon from '@/components/CustomIcon';
import SimpleLabel from '@/components/SimpleLabel';

type Props = {
  matches: PscContext.SimpleGlobalMatchResult[];
  value: IPscPropertyMatcherValue[];
  onDeleteMatcherValue: OnDeleteMatcherValue;
};

export default ({ matches, value, onDeleteMatcherValue }: Props) => {
  const { t } = useTranslation();
  if (matches.length > 0) {
    return (
      <div className={'global-matches psc-block'}>
        <div className="title">{t('Global matches')}</div>
        {matches.map(gm => {
          const v = value?.filter(v => v.property.equals(gm.property))?.[gm.valueIndex ?? 0]?.value;
          return (
            <div className={'global-match'}>
              <SimpleLabel status={'default'}>{gm.label}</SimpleLabel>
              {v && (
                <span>{PscMatcherValue.ToString(t, v)}</span>
              )}
              {t('Matched {{count}} results', { count: gm.matches.length })}
              {gm.matches.map(m => (
                <SimpleLabel status={'default'}>{m}</SimpleLabel>
              ))}
              <CustomIcon
                type={'delete'}
                className={'text-sm'}
                onClick={() => {
                  onDeleteMatcherValue(gm.property, gm.valueIndex);
                }}
              />
            </div>
          );
        })}
      </div>
    );
  }
  return null;
};
