import { SearchOutlined } from '@ant-design/icons';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { PropertyPool, ResourceProperty, SearchOperation } from '@/sdk/constants';
import { Button, Spinner } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { ResourceSearchFilter } from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/models';
import type { IProperty } from '@/components/Property/models';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const quickFilterMap = {
  [ResourceProperty.MediaLibrary]: SearchOperation.In,
  [ResourceProperty.Filename]: SearchOperation.Contains,
  [ResourceProperty.MediaLibraryV2]: SearchOperation.In,
};

type Props = {
  onAdded: (filter: ResourceSearchFilter) => any;
};

export default ({ onAdded }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [loading, setLoading] = useState(true);

  const [properties, setProperties] = useState<IProperty[]>([]);

  useEffect(() => {
    BApi.property.getPropertiesByPool(PropertyPool.Internal).then(r => {
      setProperties((r.data || []).filter(x => x.id in quickFilterMap).sort((a, b) => {
        return b.id - a.id;
      }));
      setLoading(false);
    });
  }, []);

  return (
    <>
      <div>{t('Quick filter')}</div>
      <div className={'flex items-center gap-2 flex-wrap'}>
        {loading ? (
          <Spinner size="sm" />
        ) : properties.map(p => {
          const operation = quickFilterMap[p.id];
          const pool = PropertyPool.Internal;
          return (
            <Button
              size={'sm'}
              onClick={async () => {
                const vp = (await BApi.resource.getFilterValueProperty({
                  propertyPool: pool,
                  propertyId: p.id,
                  operation: operation,
                })).data;
                const availableOperations = (await BApi.resource.getSearchOperationsForProperty({
                    propertyPool: pool,
                    propertyId: p.id,
                  })
                ).data ?? [];
                const newFilter: ResourceSearchFilter = {
                  propertyId: p.id,
                  operation: operation,
                  propertyPool: p.pool,
                  property: p,
                  valueProperty: vp,
                  availableOperations,
                };
                onAdded(newFilter);
              }}
            >
              <SearchOutlined className={'text-base'} />
              {p.name}
            </Button>
          );
        })}
      </div>
    </>
  );
};
