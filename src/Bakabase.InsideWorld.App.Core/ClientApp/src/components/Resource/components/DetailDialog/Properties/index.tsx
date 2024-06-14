import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { EyeInvisibleOutlined, SwapOutlined, SyncOutlined } from '@ant-design/icons';
import { useUpdateEffect } from 'react-use';
import type { Key } from '@react-types/shared';
import type { Resource } from '@/core/models/Resource';
import { Chip, Card, CardBody, Badge, Button, Popover, Listbox, ListboxItem } from '@/components/bakaui';
import type { PropertyValueScope } from '@/sdk/constants';
import { propertyValueScopes, ResourceProperty, ResourcePropertyType } from '@/sdk/constants';
import store from '@/store';
import BApi from '@/sdk/BApi';

type Props = {
  resource: Resource;
  className?: string;
};

export default ({ resource, className }: Props) => {
  const { t } = useTranslation();
  const cps = resource.properties;
  const resourceOptions = store.useModelState('resourceOptions');
  const [valueScopePriority, setValueScopePriority] = useState<PropertyValueScope[]>([]);

  useEffect(() => {
    const c = resourceOptions.propertyValueScopePriority;


    setValueScopePriority(c && c.length > 0 ? c : propertyValueScopes.map(s => s.value));
  }, [resourceOptions.propertyValueScopePriority]);

  if (!cps || Object.keys(cps).length == 0) {
    return (
      <div className={'opacity-60'}>
        {t('There is no property bound yet, you can bind properties to category first.')}
      </div>
    );
  }

  // console.log(Object.keys(PropertyValueScope).filter(x => !Number.isNaN(Number(x))));
  const customProperties = cps[ResourcePropertyType.Custom] || {};

  return (
    <div
      className={`grid gap-2 ${className} items-center`}
      style={{ gridTemplateColumns: 'auto minmax(0, 1fr)' }}
    >
      {
        Object.keys(customProperties).map(pIdStr => {
          const pId = parseInt(pIdStr, 10);
          const p = customProperties[pId];


          return (
            <>

            </>
          );
        })
      }
    </div>
  );
};
