import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Modal, Spinner } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import Resource from '@/components/Resource';
import { buildLogger } from '@/components/utils';
import { ResourceAdditionalItem, SearchCombinator, SearchOperation, PropertyPool, InternalProperty } from '@/sdk/constants';
import { DestroyableProps } from '@/components/bakaui/types';

const log = buildLogger('ChildrenModal');

type ChildrenModalProps ={
  resourceId: number;
} & DestroyableProps;

const ChildrenModal: React.FC<ChildrenModalProps> = ({
  resourceId,
  onDestroyed
}) => {
  const { t } = useTranslation();
  const [children, setChildren] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 搜索子资源
  const searchChildren = async () => {
    if (!resourceId) return;

    setLoading(true);
    setError(null);

    try {
      // 创建搜索表单，查找parentId为当前resourceId的资源
      const searchForm = {
        group: {
          combinator: SearchCombinator.And,
          disabled: false,
          filters: [{
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.ParentResource, // ParentResource 属性ID
            operation: SearchOperation.Equals,
            dbValue: resourceId.toString(),
            disabled: false
          }]
        },
        page: 1,
        pageSize: 1000 // 获取更多子资源
      };

      const response = await BApi.resource.searchResources(searchForm, { saveSearch: false });
      setChildren(response.data || []);
      log('Found children:', response.data?.length || 0);
    } catch (err) {
      console.error('Failed to search children:', err);
      setError(t('Failed to load children'));
    } finally {
      setLoading(false);
    }
  };

  // 当Modal显示且resourceId变化时，重新搜索
  useEffect(() => {
    if (resourceId) {
      searchChildren();
    }
  }, [ resourceId]);

  return (
    <Modal
      defaultVisible
      title={t('Resource Children')}
      onDestroyed={onDestroyed}
      size="xl"
      footer={false}
    >
      <div className="p-4">
        {loading ? (
          <div className="flex justify-center items-center h-32">
            <Spinner size="lg" />
            <span className="ml-2">{t('Loading children...')}</span>
          </div>
        ) : error ? (
          <div className="text-center text-red-500 p-4">
            {error}
          </div>
        ) : children.length === 0 ? (
          <div className="text-center text-gray-500 p-4">
            {t('No children found')}
          </div>
        ) : (
          <div className='flex flex-col gap-2'>
            <div>
              {t('Found {{count}} children', { count: children.length })}
            </div>
            <div className="grid grid-cols-6 gap-4">
              {children.map((child) => (
                <Resource
                  key={child.id}
                  resource={child}
                  mode="default"
                  selected={false}
                  onSelectedResourcesChanged={() => {}}
                  onSelected={() => {}}
                  selectedResourceIds={[]}
                />
              ))}
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

export default ChildrenModal;