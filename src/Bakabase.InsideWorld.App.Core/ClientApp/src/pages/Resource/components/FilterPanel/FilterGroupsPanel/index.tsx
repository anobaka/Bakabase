import React, { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type { DataPool, DataPoolCategory, DataPoolMediaLibrary, IGroup } from './models';
import { GroupCombinator } from './models';
import FilterGroup from './FilterGroup';
import type { IProperty } from '@/components/Property/models';
import BApi from '@/sdk/BApi';
import store from '@/store';
import { CustomPropertyAdditionalItem, ResourceProperty as EnumResourceProperty } from '@/sdk/constants';

interface IProps {
  group?: IGroup;
  onChange?: (group: IGroup) => any;
  portalContainer?: any;
}

export default ({ group: propsGroup, onChange, portalContainer }: IProps) => {
  const { t } = useTranslation();

  const [group, setGroup] = useState<IGroup>(propsGroup ?? { combinator: GroupCombinator.And });
  const [propertyMap, setPropertyMap] = useState<Record<number, IProperty>>({});
  const [dataPool, setDataPool] = useState<DataPool>();
  const internalOptions = store.useModelState('internalOptions');
  const initializedRef = useRef(false);

  const loadProperties = async () => {
    const arr: IProperty[] = [];
      const map = internalOptions.resource.reservedResourcePropertyAndValueTypeMap || {};
      arr.push(
        ...Object.keys(map).map<IProperty>(pStr => {
          const p = parseInt(pStr, 10) as EnumResourceProperty;
          return {
            id: p,
            name: t(EnumResourceProperty[p]),
            valueType: map[p],
            isReserved: true,
          };
        }),
      );
      const rsp = await BApi.customProperty.getAllCustomPropertiesV2();
      // @ts-ignore
      arr.push(...(rsp.data || []).map(d => ({
        ...d,
        isReserved: false,
      })));
    setPropertyMap(arr.reduce((s, p) => {
      // @ts-ignore
      s[p.id!] = p;
      return s;
    }, {} as Record<number, IProperty>));
  };

  const prepareDataPool = async () => {
    const categories = (await BApi.category.getAllCategories()).data ?? [];
    const mediaLibraries = (await BApi.mediaLibrary.getAllMediaLibraries()).data ?? [];
    const categoryMap = categories.reduce<Record<number, DataPoolCategory>>((s, c) => {
      s[c.id!] = {
        id: c.id!,
        name: c.name!,
      };
      return s;
    }, {});
    const mediaLibraryMap = mediaLibraries.reduce<Record<number, DataPoolMediaLibrary>>((s, m) => {
      s[m.id!] = {
        id: m.id!,
        name: m.name!,
        categoryId: m.categoryId,
        resourceCount: m.resourceCount ?? 0,
      };
      return s;
    }, {});
    const dataPool: DataPool = { categoryMap, mediaLibraryMap };
    setDataPool(dataPool);
  };

  useEffect(() => {
    if (internalOptions.initialized && !initializedRef.current) {
      initializedRef.current = true;
      loadProperties();
      prepareDataPool();
    }
  }, [internalOptions]);

  useEffect(() => {
  }, []);

  useUpdateEffect(() => {
    setGroup(propsGroup ?? { combinator: GroupCombinator.And });
  }, [propsGroup]);

  return (
    <div className={'group flex flex-wrap gap-2 item-center mt-2'}>
      <FilterGroup
        dataPool={dataPool}
        propertyMap={propertyMap}
        group={group}
        isRoot
        portalContainer={portalContainer}
        onChange={group => {
          setGroup(group);
          onChange?.(group);
        }}
      />
    </div>
  );
};
