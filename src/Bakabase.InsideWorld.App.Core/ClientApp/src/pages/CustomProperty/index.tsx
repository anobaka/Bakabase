import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import PropertyDialog from './components/PropertyDialog';
import CustomProperty from '@/components/Property/CustomProperty';
import BApi from '@/sdk/BApi';
import { CustomPropertyAdditionalItem } from '@/sdk/constants';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import { Button } from '@/components/bakaui';

export default () => {
  const { t } = useTranslation();
  const [properties, setProperties] = useState<ICustomProperty[]>([]);

  const loadProperties = async () => {
    const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
    // @ts-ignore
    setProperties(rsp.data || []);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  return (
    <div>
      <div><Button
        size={'sm'}
        color={'primary'}
        onClick={() => {
          PropertyDialog.show({
            onSaved: loadProperties,
          });
        }}
      >
        {t('Add')}
      </Button>
      </div>
      <div className={'mt-2 flex items-start gap-2 flex-wrap'}>
        {properties.map(p => {
          return (
            <CustomProperty
              property={p}
              onSaved={loadProperties}
              key={p.id}
              onRemoved={() => {
                loadProperties();
              }}
            />
          );
        })}
      </div>
    </div>
  );
};
