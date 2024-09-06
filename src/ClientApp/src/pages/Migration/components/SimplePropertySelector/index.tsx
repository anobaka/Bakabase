import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import PropertySelector from '@/components/PropertySelector';
import { Button } from '@/components/bakaui';
import type { CustomPropertyType, StandardValueType } from '@/sdk/constants';
import { ResourcePropertyType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

interface IProps {
  onSelected?: (property: IProperty) => any;
  valueTypes?: CustomPropertyType[];
}

export default (props: IProps) => {
  const { t } = useTranslation();

  const [property, setProperty] = useState<IProperty>();

  return (
    <Button
      size={'sm'}
      color={'primary'}
      variant={'light'}
      className={'ml-2'}
      onClick={() => {
        PropertySelector.show({
          editable: true,
          removable: true,
          addable: true,
          pool: ResourcePropertyType.Custom | ResourcePropertyType.Reserved,
          multiple: false,
          valueTypes: props.valueTypes?.map(v => v as unknown as StandardValueType),
          onSubmit: async (selected) => {
            const p = selected![0];
            setProperty(p);
            props.onSelected?.(p);
          },
        });
      }}
    >
      {property ? property.name : t('Select a property')}
    </Button>
  );
};
