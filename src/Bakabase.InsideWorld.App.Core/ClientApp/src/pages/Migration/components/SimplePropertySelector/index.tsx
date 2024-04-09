import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import PropertySelector from '@/components/PropertySelector';
import { Button } from '@/components/bakaui';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import type { CustomPropertyType, StandardValueType } from '@/sdk/constants';

interface IProps {
  onSelected?: (property: ICustomProperty) => any;
  valueTypes?: CustomPropertyType[];
}

export default (props: IProps) => {
  const { t } = useTranslation();

  const [property, setProperty] = useState<ICustomProperty>();

  return (
    <Button
      size={'sm'}
      color={'primary'}
      variant={'light'}
      className={'ml-2'}
      onClick={() => {
        PropertySelector.show({
          addable: true,
          pool: 'custom',
          multiple: false,
          valueTypes: props.valueTypes?.map(v => v as unknown as StandardValueType),
          onSubmit: async (selected) => {
            const p = selected.customProperties![0];
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
