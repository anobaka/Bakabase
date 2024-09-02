import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import type { Property, Resource } from '@/core/models/Resource';
import type { ResourceProperty as EnumResourceProperty } from '@/sdk/constants';
import { PropertyValueScope, propertyValueScopes, ResourcePropertyType } from '@/sdk/constants';
import store from '@/store';
import PropertyContainer from '@/components/Resource/components/DetailDialog/Properties/PropertyContainer';
import BApi from '@/sdk/BApi';
import type { IProperty } from '@/components/Property/models';
import { buildLogger } from '@/components/utils';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';
import { Button } from '@/components/bakaui';

type Props = {
  resource: Resource;
  reload: () => Promise<any>;
  className?: string;
};

type RenderContext = {
  property: IProperty;
  propertyValues: Property;
  propertyType: ResourcePropertyType;
  visible: boolean;
}[];

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
  const [showInvisibleProperties, setShowInvisibleProperties] = useState(false);

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
            options: t.options,
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

  const buildRenderContext = () => {
    const renderContext: RenderContext = [];
    Object.keys(resource.properties ?? {}).forEach(propertyTypeStr => {
      const pt = parseInt(propertyTypeStr, 10) as ResourcePropertyType;
      const propertyMap = resource.properties?.[pt] ?? {};
      Object.keys(propertyMap).forEach(idStr => {
        const pId = parseInt(idStr, 10);
        const sp = propertyMap[pId];
        let property: IProperty | undefined;
        switch (pt) {
          case ResourcePropertyType.Internal:
            break;
          case ResourcePropertyType.Reserved:
            property = reservedPropertyMap[pId];
            break;
          case ResourcePropertyType.Custom: {
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

        if (property) {
          renderContext.push({
            property,
            propertyType: pt,
            propertyValues: sp,
            visible: sp.visible ?? false,
          });
        }
      });
    });
    return renderContext.sort((a, b) => (a.visible ? 0 : 1) - (b.visible ? 0 : 1));
  };

  const renderProperties = (renderContext: RenderContext) => {
    return (
      <div
        className={`grid gap-x-4 gap-y-1 ${className} items-center overflow-hidden`}
        style={{ gridTemplateColumns: 'max(120px) minmax(0, 1fr)' }}
      >
        {renderContext.filter(x => showInvisibleProperties || x.visible).map(({
                              property,
                              propertyValues,
                              propertyType,
                            }) => {
          return (
            <PropertyContainer
              key={`${propertyType}-${property.id}`}
              property={property}
              values={propertyValues.values}
              valueScopePriority={valueScopePriority}
              onValueScopePriorityChange={reload}
              onValueChange={(sdv, sbv) => {
                const dv = deserializeStandardValue(sdv ?? null, property!.dbValueType!);
                const bv = deserializeStandardValue(sbv ?? null, property!.bizValueType!);

                log('OnValueChange', 'dv', dv, 'bv', bv, 'sdv', sdv, 'sbv', sbv);

                let manualValue = propertyValues.values?.find(v => v.scope == PropertyValueScope.Manual);
                if (!manualValue) {
                  manualValue = {
                    scope: PropertyValueScope.Manual,
                  };
                  (propertyValues.values ??= []).push(manualValue);
                }
                manualValue.bizValue = bv;
                manualValue.aliasAppliedBizValue = bv;
                manualValue.value = dv;
                forceUpdate();

                onValueChange(property.id, property!.isCustom, sdv);
              }}
            />
          );
        })}
      </div>
    );
  };

  const renderContext = buildRenderContext();

  return (
    <div>
      {renderProperties(renderContext)}
      {!showInvisibleProperties && renderContext.some(r => !r.visible) && (
        <div className={'flex items-center justify-center my-2'}>
          <Button
            onClick={() => {
              setShowInvisibleProperties(true);
            }}
            variant={'light'}
            size={'sm'}
            color={'primary'}
          >
            {t('Show attributes not bound to categories')}
          </Button>
        </div>
      )}
    </div>
  );
};
