import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import _ from 'lodash';
import { Card, CardBody, Checkbox, CardHeader, Divider } from '@/components/bakaui';
import Item from '@/pages/SynchronizationOptions/components/Item';
import Group from '@/pages/SynchronizationOptions/components/Group';
import BApi from '@/sdk/BApi';
import { CategoryAdditionalItem, enhancerIds, MediaLibraryAdditionalItem } from '@/sdk/constants';

type Library = {
  id: number;
  name: string;
};

type Category = {
  id: number;
  name: string;
  order: number;
  enhancerIds: number[];
};

export default () => {
  const { t } = useTranslation();

  const [categories, setCategories] = useState<Category[]>([]);
  const [categoryLibrariesMap, setCategoryLibrariesMap] = useState<Record<number, Library[]>>({});

  useEffect(() => {
    BApi.mediaLibrary.getAllMediaLibraries().then(r => {
      const data = r.data || [];
      const clm = _.groupBy(data, d => d.categoryId);
      setCategoryLibrariesMap(clm);
    });
    BApi.category.getAllCategories({ additionalItems: CategoryAdditionalItem.EnhancerOptions }).then(r => {
      const data = r.data || [];
      const categories = data.map(c => {
        return {
          id: c.id,
          name: c.name,
          order: c.order,
          enhancerIds: c.enhancerOptions?.filter(x => x.active).map(x => x.enhancerId) ?? [],
        };
      });
      setCategories(categories);
    });
  }, []);

  return (
    <div className={'flex flex-col gap-2'}>
      <Group
        header={t('Global')}
        enhancers={enhancerIds}
      />
      {categories.map(c => {
        const libraries = categoryLibrariesMap[c.id] ?? [];
        const enhancers = enhancerIds.filter(x => c.enhancerIds.some(a => a == x.value));
        return (
          <Group
            selectors={libraries.map(l => l.name)}
            header={c.name}
            enhancers={enhancers}
          />
        );
      })}
    </div>
  );
};
