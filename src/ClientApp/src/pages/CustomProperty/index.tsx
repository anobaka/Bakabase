import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import PropertyDialog from '@/components/PropertyDialog';
import BApi from '@/sdk/BApi';
import type {
  PropertyType } from '@/sdk/constants';
import {
  StandardValueType,
} from '@/sdk/constants';
import {
  CustomPropertyAdditionalItem,
} from '@/sdk/constants';
import { Button, Chip, Input, Modal, Table, TableColumn, TableRow, TableCell, TableHeader, TableBody } from '@/components/bakaui';
import Property from '@/components/Property';
import type { IProperty } from '@/components/Property/models';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import TypeConversionRuleOverviewDialog from '@/pages/CustomProperty/components/TypeConversionRuleOverviewDialog';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [properties, setProperties] = useState<IProperty[]>([]);

  const [keyword, setKeyword] = useState('');

  const loadProperties = async () => {
    // @ts-ignore
    const rsp = await BApi.customProperty.getAllCustomProperties({ additionalItems: CustomPropertyAdditionalItem.Category | CustomPropertyAdditionalItem.ValueCount });
    // @ts-ignore
    setProperties((rsp.data || []));
  };

  useEffect(() => {
    loadProperties();
    // createPortal(TypeConversionOverviewDialog, {});
  }, []);

  const filteredProperties = properties.filter(p => keyword == undefined || keyword.length == 0 || p.name!.toLowerCase().includes(keyword.toLowerCase()));
  const groupedFilteredProperties: {[key in PropertyType]?: IProperty[]} = filteredProperties.reduce<{[key in PropertyType]?: IProperty[]}>((s, t) => {
    (s[t.type!] ??= []).push(t);
    return s;
  }, {});

  console.log('[CustomProperty] render', groupedFilteredProperties);

  return (
    <div>
      <div className={'flex items-center justify-between gap-2 mb-4'}>
        <div className={'flex items-center gap-2'}>
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
          <Button
            size={'sm'}
            color={'secondary'}
            variant={'light'}
            onClick={async () => {
              createPortal(TypeConversionRuleOverviewDialog, {});
            }}
          >
            {t('Check type conversion rules')}
          </Button>
        </div>
        <div />
      </div>
      <div
        className={'grid gap-2 items-center'}
        style={{ gridTemplateColumns: 'auto 1fr' }}
      >
        {Object.keys(groupedFilteredProperties).map(k => {
          const ps = groupedFilteredProperties[k];
          return (
            <>
              <div className={'mb-1 text-right'}>
                <Chip
                  radius={'sm'}
                  size={'sm'}
                >
                  {ps[0].typeName}
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
                      onDialogDestroyed={loadProperties}
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
