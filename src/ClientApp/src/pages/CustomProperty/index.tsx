import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import PropertyDialog from '@/components/PropertyDialog';
import BApi from '@/sdk/BApi';
import type {
  StandardValueType,
} from '@/sdk/constants';
import {
  CustomPropertyAdditionalItem,
  CustomPropertyType,
  ResourcePropertyType,
} from '@/sdk/constants';
import { Button, Chip, Input } from '@/components/bakaui';
import Property from '@/components/Property';
import type { IProperty } from '@/components/Property/models';

export default () => {
  const { t } = useTranslation();
  const [properties, setProperties] = useState<IProperty[]>([]);

  const [keyword, setKeyword] = useState('');

  const loadProperties = async () => {
    // @ts-ignore
    const rsp = await BApi.customProperty.getAllCustomProperties({ additionalItems: CustomPropertyAdditionalItem.Category | CustomPropertyAdditionalItem.ValueCount });
    // @ts-ignore
    setProperties((rsp.data || []).map(x => (
      {
        ...x,
        type: ResourcePropertyType.Custom,
      }
    )));
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const filteredProperties = properties.filter(p => keyword == undefined || keyword.length == 0 || p.name!.toLowerCase().includes(keyword.toLowerCase()));
  const groupedFilteredProperties = filteredProperties.reduce<{[key in StandardValueType]?: IProperty[]}>((s, t) => {
    (s[t.customPropertyType!] ??= []).push(t);
    return s;
  }, {});

  console.log('[CustomProperty] render', groupedFilteredProperties);

  return (
    <div>
      <div className={'flex items-center gap-2 mb-4'}>
        <Button
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
        <div>
          <Input
            size={'sm'}
            startContent={(
              <SearchOutlined className={'text-small'} />
            )}
            value={keyword}
            onValueChange={v => setKeyword(v)}
          />
        </div>
      </div>
      <div
        className={'grid gap-2 items-center'}
        style={{ gridTemplateColumns: 'auto 1fr' }}
      >
        {Object.keys(groupedFilteredProperties).map(k => {
          const type = parseInt(k, 10);
          const ps = groupedFilteredProperties[k];
          return (
            <>
              <div className={'mb-1'}>
                <Chip radius={'sm'}>
                  {t(`CustomPropertyType.${CustomPropertyType[type]}`)}
                </Chip>
              </div>
              <div className={'flex items-start gap-2 flex-wrap'}>
                {ps.map(p => {
                  return (
                    <Property
                      property={p}
                      onSaved={loadProperties}
                      editablePortal={'click'}
                      key={p.id}
                      editable
                      removable
                      onRemoved={() => {
                        loadProperties();
                      }}
                    />
                  );
                })}
              </div>
            </>
          );
        })}
      </div>
    </div>
  );
};
