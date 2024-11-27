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
  convertToBizValue?: (value?: string) => string | undefined;
  property: IProperty;
};

export default ({
                  valueType = BulkModificationProcessorValueType.BizValue,
                  variables,
                  value,
                  property,
                  convertToBizValue,
                }: Props) => {
  const { t } = useTranslation();

  const renderValue = () => {
    switch (valueType!) {
      case BulkModificationProcessorValueType.BizValue:
        return (
          <PropertyValueRenderer
            bizValue={value}
            property={property}
            variant={'light'}
          />
        );
      case BulkModificationProcessorValueType.DbValue:
        return (
          <PropertyValueRenderer
            bizValue={convertToBizValue ? convertToBizValue?.(value) : value}
            property={property}
            variant={'light'}
          />
        );
      case BulkModificationProcessorValueType.VariableKey:
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
      >
        {t(`BulkModificationProcessorValueType.${BulkModificationProcessorValueType[valueType]}`)}
      </Chip>
      {renderValue()}
    </div>
  );
};
