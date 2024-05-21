import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import PropertySelector from '@/components/PropertySelector';
import { Button } from '@/components/bakaui';
import type { IFilter } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';

export default () => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<IFilter>({});

  return (
    <Button
      type={'primary'}
      text
      onClick={() => {
        PropertySelector.show({
          selection: { [filter.isReservedProperty ? 'reservedPropertyIds' : 'customPropertyIds']: filter.propertyId == undefined ? undefined : [filter.propertyId] },
          onSubmit: async (selectedProperties) => {
            const property = (selectedProperties.reservedProperties?.[0] ?? selectedProperties.customProperties?.[0])!;
            const cp = property as ICustomProperty;
            setFilter({
              ...filter,
              propertyId: property.id,
              propertyName: property.name,
              isReservedProperty: cp == undefined,
            });
          },
          multiple: false,
          pool: 'all',
        });
      }}
      size={'small'}
    >
      {filter.propertyId ? filter.propertyName : t('Property')}
    </Button>
  );
};
