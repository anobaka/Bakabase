import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import PropertyDialog from '@/components/PropertyDialog';
import BApi from '@/sdk/BApi';
import { StandardValueType } from '@/sdk/constants';
import { CustomPropertyAdditionalItem } from '@/sdk/constants';
import { Button, Chip, Input } from '@/components/bakaui';
import Property from '@/components/Property';
import type { IProperty } from '@/components/Property/models';

export default () => {
  const { t } = useTranslation();
  const [properties, setProperties] = useState<IProperty[]>([]);

  const [keyword, setKeyword] = useState('');

  const loadProperties = async () => {
    const rsp = await BApi.customProperty.getAllCustomPropertiesV2({ additionalItems: CustomPropertyAdditionalItem.Category });
    // @ts-ignore
    setProperties(rsp.data || []);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const filteredProperties = properties.filter(p => keyword == undefined || keyword.length == 0 || p.name.toLowerCase().includes(keyword.toLowerCase()));
  const groupedFilteredProperties = filteredProperties.reduce<{[key in StandardValueType]?: IProperty[]}>((s, t) => {
    (s[t.type] ??= []).push(t);
    return s;
  }, {});

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
      {Object.keys(groupedFilteredProperties).map(k => {
        const type = parseInt(k, 10);
        const ps = groupedFilteredProperties[k];
        return (
          <div className={'flex gap-4 mt-2'}>
            <div className={'mb-1 max-w-[80px] w-[80px]'}>
              <Chip>
                {t(StandardValueType[type])}
              </Chip>
            </div>
            <div className={'flex items-start gap-2 flex-wrap'}>
              {ps.map(p => {
                return (
                  <Property
                    property={p}
                    onSaved={loadProperties}
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
          </div>
        );
      })}
    </div>
  );
};
