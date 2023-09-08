import React, { useEffect, useReducer, useRef, useState } from 'react';
import { Button, Loading } from '@alifd/next';
import './index.scss';
import { history } from 'ice';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  GetAllMediaLibraries,
  GetAllResourceCategories,
  GetComponentDescriptors,
} from '@/sdk/apis';
import SortableCategoryList from '@/pages/Category/components/SortableCategoryList';
import MediaLibrarySynchronization from '@/pages/Category/components/MediaLibrarySynchronization';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { ResourceCategoryAdditionalItem } from '@/sdk/constants';

export default () => {
  const { t } = useTranslation();

  const [categories, setCategories] = useState<any[]>([]);
  const [libraries, setLibraries] = useState([]);
  const categoriesLoadedRef = useRef(false);
  // const [enhancers, setEnhancers] = useState([]);
  const resourceOptions = store.useModelState('resourceOptions');
  const [allComponents, setAllComponents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  const gotoNewCategoryPage = (noCategory: boolean) => {
    history!.push(`/category/setup-wizard?noCategory=${noCategory ? 1 : 0}`);
  };

  const loadAllCategories = (cb: () => void = () => {}): Promise<any> => {
    return BApi.resourceCategory.getAllResourceCategories({ additionalItems: ResourceCategoryAdditionalItem.Validation }).then((rsp) => {
      categoriesLoadedRef.current = true;
      rsp.data?.sort((a, b) => a.order! - b.order!);
      setCategories(rsp.data || []);
      cb && cb();
    });
  };

  const loadAllMediaLibraries = (cb: () => void = () => {}): Promise<any> => {
    return GetAllMediaLibraries().invoke((x) => {
      x.data.sort((a, b) => a.order - b.order);
      setLibraries(x.data);
    });
  };

  useEffect(() => {
    async function init() {
      try {
        await loadAllCategories();
        await loadAllMediaLibraries();
        await GetComponentDescriptors().invoke(a => {
          setAllComponents(a.data || []);
        });
      } finally {
        setLoading(false);
      }
    }

    init();
  }, []);

  useEffect(() => {
    if (categoriesLoadedRef.current) {
      const noCategory = !(categories?.length > 0);
      if (noCategory) {
        gotoNewCategoryPage(true);
      }
    }
  }, [categories]);

  return (
    <div className={'category-page'} >
      <div className="header">
        <div className="left">
          <Button
            type={'secondary'}
            size={'small'}
            onClick={() => gotoNewCategoryPage(false)}
          >
            {t('Add')}
          </Button>
        </div>
        <div className="right">
          <div className={'last-sync-time'}>
            {t('Last sync time')}: {resourceOptions?.lastSyncDt ? dayjs(resourceOptions.lastSyncDt).format('YYYY-MM-DD HH:mm:ss') : t('Never')}
          </div>
          <MediaLibrarySynchronization />
        </div>
      </div>
      <Loading visible={loading} fullScreen />
      <SortableCategoryList
        forceUpdate={forceUpdate}
        allComponents={allComponents}
        loadAllCategories={loadAllCategories}
        loadAllMediaLibraries={loadAllMediaLibraries}
        categories={categories}
        libraries={libraries}
      />
      {categories.length > 1 && (
        <div id="elevator">
          <div className="title">
            {t('QuickJump')}
          </div>
          <ul>
            {categories?.map((c) => (
              <li
                key={c.id}
                onClick={() => {
                document.getElementById(`category-${c.id}`)?.scrollIntoView();
                }}
              >
                {c.name}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
};
