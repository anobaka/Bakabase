import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import type { Resource } from '@/core/models/Resource';
import type { ResourceProperty as EnumResourceProperty } from '@/sdk/constants';
import { PropertyValueScope } from '@/sdk/constants';
import { propertyValueScopes, ResourceProperty, ResourcePropertyType, StandardValueType } from '@/sdk/constants';
import store from '@/store';
import PropertyContainer from '@/components/Resource/components/DetailDialog/Properties/PropertyContainer';
import BApi from '@/sdk/BApi';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';

type Props = {
  resource: Resource;
  reload: () => Promise<any>;
  className?: string;
};

const log = buildLogger('Properties');

export default (props: Props) => {
  const {
    resource,
    className,
    reload,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const cps = resource.properties;
  const resourceOptions = store.useModelState('resourceOptions');
  const [valueScopePriority, setValueScopePriority] = useState<PropertyValueScope[]>([]);
  const internalOptions = store.useModelState('internalOptions');
  const [reservedPropertyMap, setReservedPropertyMap] = useState<Record<number, IProperty>>({});
  const [customPropertyMap, setCustomPropertyMap] = useState<Record<number, IProperty>>({});

  useEffect(() => {
    const c = resourceOptions.propertyValueScopePriority;
    setValueScopePriority(c && c.length > 0 ? c : propertyValueScopes.map(s => s.value));
  }, [resourceOptions.propertyValueScopePriority]);

  useEffect(() => {
    if (internalOptions.initialized) {
      const map = internalOptions.resource.reservedResourcePropertyAndValueTypesMap || {};
      Object.keys(map).forEach(pStr => {
        const p = parseInt(pStr, 10) as EnumResourceProperty;
        reservedPropertyMap[p] = {
          id: p,
          dbValueType: map[p].dbValueType,
          isCustom: false,
          bizValueType: map[p].bizValueType,
        };
      });
      setReservedPropertyMap({ ...reservedPropertyMap });
    }
  }, [internalOptions.initialized]);

  useEffect(() => {
    const customPropertyMap = resource.properties?.[ResourcePropertyType.Custom] || {};
    const customPropertyIds = Object.keys(customPropertyMap).map(x => parseInt(x, 10));
    if (customPropertyIds.length > 0) {
      BApi.customProperty.getCustomPropertyByKeys({ ids: customPropertyIds }).then(r => {
        const ps = r.data || [];
        setCustomPropertyMap(ps.reduce<Record<number, IProperty>>((s, t) => {
          s[t.id!] = {
            id: t.id!,
            name: t.name!,
            bizValueType: t.bizValueType!,
            dbValueType: t.dbValueType!,
            isCustom: true,
            type: t.type!,
            subOptions: t.options,
          };
          return s;
        }, {}));
      });
    }
  }, []);

  if (!cps || Object.keys(cps).length == 0) {
    return (
      <div className={'opacity-60'}>
        {t('There is no property bound yet, you can bind properties to category first.')}
      </div>
    );
  }

  const onValueChange = async (propertyId: number, isCustomProperty: boolean, value?: string) => {
    await BApi.resource.putResourcePropertyValue(resource.id, {
      value,
      isCustomProperty,
      propertyId,
    });
    reload();
  };

  log(props, 'reserved property map', reservedPropertyMap);

  return (
    <div
      className={`grid gap-x-4 gap-y-1 ${className} items-center`}
      style={{ gridTemplateColumns: 'auto minmax(0, 1fr)' }}
    >
      {Object.keys(resource.properties ?? {}).map(propertyTypeStr => {
        const pt = parseInt(propertyTypeStr, 10) as ResourcePropertyType;
        const propertyMap = resource.properties?.[pt] ?? {};
        return Object.keys(propertyMap).map(idStr => {
          const pId = parseInt(idStr, 10);
          const sp = propertyMap[pId];
          const { values } = sp;
          let property: IProperty | undefined;
          switch (pt) {
            case ResourcePropertyType.Internal:
              break;
            case ResourcePropertyType.Reserved:
              property = reservedPropertyMap[pId];
              break;
            case ResourcePropertyType.Custom:
            {
              const p = customPropertyMap?.[pId];
              property = {
                ...p,
                id: pId,
                isCustom: true,
                dbValueType: sp.dbValueType,
                bizValueType: sp.bizValueType,
                name: sp.name,
              };
              break;
            }
          }

          if (!property) {
            return null;
          }
          return (
            <PropertyContainer
              key={`${propertyTypeStr}-${idStr}`}
              property={property}
              values={values}
              valueScopePriority={valueScopePriority}
              onValueScopePriorityChange={reload}
              onValueChange={(sdv, sbv) => {
                const dv = deserializeStandardValue(sdv ?? null, property!.dbValueType!);
                const bv = deserializeStandardValue(sbv ?? null, property!.bizValueType!);

                log('OnValueChange', 'dv', dv, 'bv', bv, 'sdv', sdv, 'sbv', sbv);

                let manualValue = sp.values?.find(v => v.scope == PropertyValueScope.Manual);
                if (!manualValue) {
                  manualValue = {
                    scope: PropertyValueScope.Manual,
                  };
                  (sp.values ??= []).push(manualValue);
                }
                manualValue.bizValue = bv;
                manualValue.aliasAppliedBizValue = bv;
                manualValue.value = dv;
                forceUpdate();

                onValueChange(pId, property!.isCustom, sdv);
              }}
            />
          );
        });
      })}
    </div>
  );
};
