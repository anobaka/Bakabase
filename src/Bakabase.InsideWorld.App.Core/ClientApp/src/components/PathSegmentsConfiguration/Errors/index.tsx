import { WarningOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import type { OnDeleteMatcherValue } from '../models';
import type { PscContext } from '../models/PscContext';
import type { IPscPropertyMatcherValue } from '../models/PscPropertyMatcherValue';
import { PscMatcherValue } from '../models/PscMatcherValue';
import { Chip } from '@/components/bakaui';

type Props = {
  errors?: PscContext.SimpleGlobalError[];
  value?: IPscPropertyMatcherValue[];
  onDeleteMatcherValue: OnDeleteMatcherValue;
};

export default ({ errors, value, onDeleteMatcherValue }: Props) => {
  const { t } = useTranslation();
  if (errors && errors.length > 0) {
    return (
      <div className={'px-2 py-1 border-small rounded-small border-default-200 flex flex-col gap-1 text-sm relative'}>
        {errors.map(e => {
          const v = value?.filter(v => v.property.equals(e.property))?.[e.valueIndex ?? 0]?.value;
          return (
            <div className={'flex items-center gap-1'}>
              <Chip
                radius={'sm'}
                size={'sm'}
                color={'danger'}
                startContent={(
                  <WarningOutlined className={'text-sm'} />
                )}
              >
                {e.label}
              </Chip>
              <Chip
                radius={'sm'}
                size={'sm'}
                color={'danger'}
                variant={'light'}
                onClose={(
                  e.deletable ? () => {
                    onDeleteMatcherValue(e.property, e.valueIndex ?? 0);
                  } : undefined
                )}
              >
                {v && (
                  <span className={'font-bold mr-2'}>{PscMatcherValue.ToString(t, v)}</span>
                )}
                {e.message}
              </Chip>
            </div>
          );
        })}
      </div>
    );
  }
  return null;
};
