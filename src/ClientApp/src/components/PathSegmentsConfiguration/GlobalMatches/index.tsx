import React from 'react';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined } from '@ant-design/icons';
import type { OnDeleteMatcherValue } from '../models';
import type { IPscPropertyMatcherValue } from '../models/PscPropertyMatcherValue';
import type { PscContext } from '../models/PscContext';
import { PscMatcherValue } from '../models/PscMatcherValue';
import CustomIcon from '@/components/CustomIcon';
import SimpleLabel from '@/components/SimpleLabel';
import { Button, Chip } from '@/components/bakaui';

type Props = {
  matches: PscContext.SimpleGlobalMatchResult[];
  value: IPscPropertyMatcherValue[];
  onDeleteMatcherValue: OnDeleteMatcherValue;
};

export default ({ matches, value, onDeleteMatcherValue }: Props) => {
  const { t } = useTranslation();
  if (matches.length > 0) {
    return (
      <div className={'flex flex-col mt-2'}>
        <div className="font-bold mb-1">{t('Global matches')}</div>
        {matches.map(gm => {
          const v = value?.filter(v => v.property.equals(gm.property))?.[gm.valueIndex ?? 0]?.value;
          return (
            <div className={'flex gap-2 items-center'}>
              <Chip
                radius={'sm'}
                size={'sm'}
              >{gm.property.toString(t, gm.valueIndex)}</Chip>
              {v && (
                <span>{PscMatcherValue.ToString(t, v)}</span>
              )}
              {t('Matched {{count}} results', { count: gm.matches.length })}
              {gm.matches.map(m => (
                <Chip
                  radius={'sm'}
                  size={'sm'}
                >{m}</Chip>
              ))}
              <Button
                color={'danger'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onClick={() => {
                  onDeleteMatcherValue(gm.property, gm.valueIndex);
                }}
              >
                <DeleteOutlined className={'text-base'} />
              </Button>
            </div>
          );
        })}
      </div>
    );
  }
  return null;
};
