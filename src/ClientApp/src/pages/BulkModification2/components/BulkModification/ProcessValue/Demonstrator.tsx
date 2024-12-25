'use strict';
import { useTranslation } from 'react-i18next';
import React from 'react';
import { BulkModificationProcessorValueType } from '@/sdk/constants';
import type { BulkModificationVariable } from '@/pages/BulkModification2/components/BulkModification/models';
import { Chip } from '@/components/bakaui';
import type { IProperty } from '@/components/Property/models';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';

type Props = {
  variables?: BulkModificationVariable[];
  valueType?: BulkModificationProcessorValueType;
  value?: string;
  property: IProperty;
};

export default ({
                  valueType = BulkModificationProcessorValueType.Static,
                  variables,
                  value,
                  property,
                }: Props) => {
  const { t } = useTranslation();

  const renderValue = () => {
    switch (valueType!) {
      case BulkModificationProcessorValueType.Static:
        return (
          <PropertyValueRenderer
            bizValue={value}
            property={property}
            variant={'light'}
          />
        );
      case BulkModificationProcessorValueType.Dynamic:
        return (
          <PropertyValueRenderer
            dbValue={value}
            property={property}
            variant={'light'}
          />
        );
      case BulkModificationProcessorValueType.Variable:
        return (
          <Chip
            size={'sm'}
            radius={'sm'}
          >
            {variables?.find(v => v.key === value)?.name}
          </Chip>
        );
    }
  };

  return (
    <div className={'flex items-center gap-1'}>
      <Chip
        size={'sm'}
        radius={'sm'}
        color={'secondary'}
        variant={'flat'}
      >
        {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[valueType]}`)}
      </Chip>
      {renderValue()}
    </div>
  );
};
