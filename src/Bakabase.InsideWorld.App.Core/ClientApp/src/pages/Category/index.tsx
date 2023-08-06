import React, { useEffect, useReducer, useRef, useState } from 'react';
import { Button, Loading } from '@alifd/next';
import './index.scss';
import { history } from 'ice';
import i18n from 'i18next';
import dayjs from 'dayjs';
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
  const [categories, setCategories] = useState([]);
  const [libraries, setLibraries] = useState([]);
  const categoriesLoadedRef = useRef(false);
  // const [enhancers, setEnhancers] = useState([]);
  const resourceOptions = store.useModelState('resourceOptions');
  const [allComponents, setAllComponents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  const gotoNewCategoryPage = (noCategory: boolean) => {
    history.push(`/category/setup-wizard?noCategory=${noCategory ? 1 : 0}`);
  };

  const loadAllCategories = (cb: () => void = () => {}): Promise<any> => {
    return BApi.resourceCategory.getAllResourceCategories({ additionalItems: ResourceCategoryAdditionalItem.Validation }).then((t) => {
      categoriesLoadedRef.current = true;
      t.data.sort((a, b) => a.order - b.order);
      setCategories(t.data);
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
            {i18n.t('Add')}
          </Button>
        </div>
        <div className="right">
          <div className={'last-sync-time'}>
            {i18n.t('Last sync time')}: {resourceOptions?.lastSyncDt ? dayjs(resourceOptions.lastSyncDt).format('YYYY-MM-DD HH:mm:ss') : i18n.t('Never')}
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
            {i18n.t('QuickJump')}
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
