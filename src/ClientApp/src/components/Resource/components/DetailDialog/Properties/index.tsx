import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdate } from 'react-use';
import type { Property, Resource } from '@/core/models/Resource';
import { PropertyPool, PropertyValueScope, propertyValueScopes } from '@/sdk/constants';
import store from '@/store';
import type {
  PropertyContainerProps,
} from '@/components/Resource/components/DetailDialog/Properties/PropertyContainer';
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
  restrictedPropertyPool?: PropertyPool;
  restrictedPropertyIds?: number[];
  propertyInnerDirection?: 'hoz' | 'ver';
  hidePropertyName?: boolean;
  propertyClassNames?: PropertyContainerProps['classNames'];
  noPropertyContent?: any;
  sortable?: boolean;
};

type PropertyRenderContext = {
  property: IProperty;
  propertyValues: Property;
  propertyPool: PropertyPool;
  visible: boolean;
  order: number;
};

export type RenderContext = PropertyRenderContext[];

const log = buildLogger('Properties');

export default (props: Props) => {
  const {
    resource,
    className,
    reload,
    restrictedPropertyPool,
    restrictedPropertyIds,
    propertyInnerDirection = 'hoz',
    hidePropertyName = false,
    propertyClassNames,
    noPropertyContent,
    sortable = false,
  } = props;
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const cps = resource.properties;
  const resourceOptions = store.useModelState('resourceOptions');
  const [valueScopePriority, setValueScopePriority] = useState<PropertyValueScope[]>([]);
  const [builtinPropertyMap, setBuiltinPropertyMap] = useState<Record<number, IProperty>>({});
  const [customPropertyMap, setCustomPropertyMap] = useState<Record<number, IProperty>>({});
  const [showInvisibleProperties, setShowInvisibleProperties] = useState(false);

  useEffect(() => {
    const c = resourceOptions.propertyValueScopePriority;
    setValueScopePriority(c && c.length > 0 ? c : propertyValueScopes.map(s => s.value));
  }, [resourceOptions.propertyValueScopePriority]);

  useEffect(() => {
    // @ts-ignore
    BApi.property.getPropertiesByPool(PropertyPool.Reserved | PropertyPool.Internal).then(r => {
      const ps = r.data || [];
      setBuiltinPropertyMap(ps.reduce<Record<number, IProperty>>((s, t) => {
        // @ts-ignore
        s[t.id!] = t;
        return s;
      }, {}));
    });

    if (restrictedPropertyPool == undefined || restrictedPropertyPool == PropertyPool.Custom) {
      const customPropertyMap = resource.properties?.[PropertyPool.Custom] || {};
      const customPropertyIds = Object.keys(customPropertyMap).map(x => parseInt(x, 10));
      if (customPropertyIds.length > 0) {
        BApi.customProperty.getCustomPropertyByKeys({ ids: customPropertyIds }).then(r => {
          const ps = r.data || [];
          setCustomPropertyMap(ps.reduce<Record<number, IProperty>>((s, t) => {
            // @ts-ignore
            s[t.id!] = t;
            return s;
          }, {}));
        });
      }
    }
  }, []);

  if (!cps || Object.keys(cps).length == 0) {
    return (
      <div className={'opacity-60'}>
        {t('There is no property bound yet, you can bind properties to media library template or category(deprecated) first.')}
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

  log(props, 'reserved property map', builtinPropertyMap);

  const buildRenderContext = () => {
    const renderContext: RenderContext = [];
    Object.keys(resource.properties ?? {}).forEach(propertyPoolStr => {
      const pp = parseInt(propertyPoolStr, 10) as PropertyPool;
      if (restrictedPropertyPool != undefined && restrictedPropertyPool != pp) {
        return;
      }
      const propertyMap = resource.properties?.[pp] ?? {};
      Object.keys(propertyMap).forEach(idStr => {
        const pId = parseInt(idStr, 10);
        if (restrictedPropertyIds != undefined && !restrictedPropertyIds.includes(pId)) {
          return;
        }

        const sp = propertyMap[pId];
        let property: IProperty | undefined;
        switch (pp) {
          case PropertyPool.Internal:
            break;
          case PropertyPool.Reserved:
            property = builtinPropertyMap[pId];
            break;
          case PropertyPool.Custom: {
            const p = customPropertyMap?.[pId];
            property = p;
            break;
          }
        }

        if (property) {
          renderContext.push({
            property,
            propertyPool: pp,
            propertyValues: sp!,
            visible: sp!.visible ?? false,
            order: sp!.order,
          });
        }
      });
    });
    return renderContext.sort((a, b) => a.order - b.order);
  };

  const renderProperty = (pCtx: PropertyRenderContext) => {
    const {
      property,
      propertyValues,
      propertyPool,
    } = pCtx;
    // log(pCtx);
    return (
      <PropertyContainer
        isLinked={pCtx.visible}
        categoryId={resource.categoryId}
        classNames={propertyClassNames}
        hidePropertyName={hidePropertyName}
        key={`${propertyPool}-${property.id}`}
        property={property}
        values={propertyValues.values}
        valueScopePriority={valueScopePriority}
        onValueScopePriorityChange={reload}
        onValueChange={(sdv, sbv) => {
          const dv = deserializeStandardValue(sdv ?? null, property!.dbValueType!);
          const bv = deserializeStandardValue(sbv ?? null, property!.bizValueType!);

          log('OnValueChange', 'dv', dv, 'bv', bv, 'sdv', sdv, 'sbv', sbv, property);

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

          onValueChange(property.id, property!.pool == PropertyPool.Custom, sdv);
        }}
      />
    );
  };

  const renderContext = buildRenderContext();

  // log(renderContext);

  return (
    <div>
      {propertyInnerDirection == 'hoz' ? (
        <div
          className={`grid gap-x-4 gap-y-1 ${className} items-center overflow-visible`}
          style={{ gridTemplateColumns: 'max(120px) minmax(0, 1fr)' }}
        >
          {renderContext.filter(x => showInvisibleProperties || x.visible).map(pCtx => {
            return renderProperty(pCtx);
          })}
        </div>
      ) : (
        <div className={'flex flex-col gap-4'}>
          {renderContext.filter(x => showInvisibleProperties || x.visible).map(pCtx => {
            return (
              <div className={'flex flex-col gap-2'}>{renderProperty(pCtx)}</div>
            );
          })}
        </div>
      )}
      {renderContext.length == 0 ? (
        noPropertyContent
      ) : null}
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
            {t('Show properties not bound to media library templates or categories(deprecated)')}
          </Button>
        </div>
      )}
    </div>
  );
};
