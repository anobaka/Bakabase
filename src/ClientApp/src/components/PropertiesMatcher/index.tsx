import { useTranslation } from 'react-i18next';
import { useCallback } from 'react';
import { IoLocate } from 'react-icons/io5';
import type { IProperty } from '../Property/models';
import { Button, Tooltip } from '@/components/bakaui';
import type { PropertyType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Property = {
  name: string;
  type: PropertyType;
  options?: any;
};

type Props = {
  properties: Property[];
  onValueChanged?: (properties: (IProperty | undefined)[]) => any;
};

export default ({
                  properties,
                  onValueChanged: propsOnValueChanged,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const onValueChange = useCallback((properties: (IProperty | undefined)[]) => {
    console.log(properties);
    propsOnValueChanged?.(properties);
  }, [propsOnValueChanged]);

  return (
    <Tooltip content={t('Automatically match property')}>
      <Button
        size={'sm'}
        isIconOnly
        color={'primary'}
        variant={'light'}
        onPress={async () => {
          const ret: (IProperty | undefined)[] = [];
          for (const property of properties) {
            const candidate = await BApi.property.findBestMatchingProperty({
              type: property.type,
              name: property.name,
            });
            ret.push(candidate.data);
          }
          onValueChange(ret);
        }}
      >
        <IoLocate className={'text-base'} />
      </Button>
    </Tooltip>
  );
};
