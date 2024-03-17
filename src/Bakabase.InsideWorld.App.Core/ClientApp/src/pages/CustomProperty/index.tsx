import './index.scss';
import { Button } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import PropertyDialog from './components/PropertyDialog';
import BApi from '@/sdk/BApi';
import { CustomPropertyAdditionalItem, CustomPropertyType } from '@/sdk/constants';
import type { ICustomProperty } from '@/pages/CustomProperty/models';
import { PropertyTypeIconMap } from '@/pages/CustomProperty/models';
import SimpleLabel from '@/components/SimpleLabel';
import ClickableIcon from '@/components/ClickableIcon';
import CustomIcon from '@/components/CustomIcon';
import SingleProperty from '@/pages/CustomProperty/components/SingleProperty';

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
    <div id={'custom-property-page'}>
      <div className="opts">
        <Button
          size={'small'}
          type={'primary'}
          onClick={() => {
            PropertyDialog.show({
              onSaved: loadProperties,
            });
          }}
        >
          {t('Add')}
        </Button>
      </div>
      <div className="properties">
        {properties.map(p => {
          return (
            <SingleProperty
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
