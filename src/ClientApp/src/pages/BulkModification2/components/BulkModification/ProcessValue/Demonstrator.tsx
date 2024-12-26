'use strict';
import { useTranslation } from 'react-i18next';
import React from 'react';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import type {
  BulkModificationProcessValue,
  BulkModificationVariable,
} from '@/pages/BulkModification2/components/BulkModification/models';
import { Chip } from '@/components/bakaui';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';

type Props = {
  variables?: BulkModificationVariable[];
  value?: BulkModificationProcessValue;
};

export default ({
                  variables,
                  value,
                }: Props) => {
  const { t } = useTranslation();

  if (!value) {
    return (
      <Chip
        size={'sm'}
        // color={'danger'}
        radius={'sm'}
        isDisabled
      >
        {t('No set')}
      </Chip>
    );
  }

  switch (value.type) {
    case BulkModificationProcessorValueType.ManuallyInput:
      if (!value.property) {
        return (
          <Chip
            size={'sm'}
            color={'danger'}
            radius={'sm'}
            isDisabled
          >
            {t('Unable to get property information')}
          </Chip>
        );
      }
      return (
        <PropertyValueRenderer
          bizValue={value.followPropertyChanges ? undefined : value.value}
          dbValue={value.followPropertyChanges ? value.value : undefined}
          property={value.property}
          // variant={'light'}
        />
      );
    case BulkModificationProcessorValueType.Variable:
      return (
        <div className={'flex items-center gap-1'}>
          <Chip
            size={'sm'}
            radius={'sm'}
            color={'secondary'}
            variant={'flat'}
          >
            {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[value.type]}`)}
          </Chip>
          <Chip
            size={'sm'}
            radius={'sm'}
          >
            {variables?.find(v => v.key === value.value)?.name}
          </Chip>
        </div>
      );
  }
};
