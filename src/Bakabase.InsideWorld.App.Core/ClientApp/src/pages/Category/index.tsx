import React, { useEffect, useReducer, useRef, useState } from 'react';
import { Button, Dropdown, Loading, Menu } from '@alifd/next';
import './index.scss';
import { history } from 'ice';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { GetComponentDescriptors } from '@/sdk/apis';
import SortableCategoryList from '@/pages/Category/components/SortableCategoryList';
import MediaLibrarySynchronization from '@/pages/Category/components/MediaLibrarySynchronization';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { MediaLibraryAdditionalItem, ResourceCategoryAdditionalItem } from '@/sdk/constants';
import SimpleOneStepDialog from '@/components/SimpleOneStepDialog';

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
    history!.push(`/category/setupWizard?noCategory=${noCategory ? 1 : 0}`);
  };

  const loadAllCategories = (cb: () => void = () => {
  }): Promise<any> => {
    return BApi.resourceCategory.getAllResourceCategories({ additionalItems: ResourceCategoryAdditionalItem.Validation | ResourceCategoryAdditionalItem.CustomProperties }).then((rsp) => {
      categoriesLoadedRef.current = true;
      rsp.data?.sort((a, b) => a.order! - b.order!);
      setCategories(rsp.data || []);
      cb && cb();
    });
  };

  const loadAllMediaLibraries = (cb: () => void = () => {
  }): Promise<any> => {
    return BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category | MediaLibraryAdditionalItem.FileSystemInfo | MediaLibraryAdditionalItem.FixedTags }).then((x) => {
      x.data.sort((a, b) => a.order - b.order);
      setLibraries(x.data);
    });
  };

  async function init() {
    setLoading(true);
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

  useEffect(() => {
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
    <div className={'category-page'}>
      <div className="header">
        <div className="left">
          <Button
            type={'primary'}
            size={'small'}
            onClick={() => gotoNewCategoryPage(false)}
          >
            {t('Add')}
          </Button>
          {categories.length > 1 && (
            <Dropdown
              trigger={(
                <Button size={'small'} className={'elevator'}>
                  {t('QuickJump')}
                </Button>
              )}
              triggerType={'click'}
            >
              <Menu>
                {categories?.map((c) => (
                  <Menu.Item
                    key={c.id}
                    onClick={() => {
                      document.getElementById(`category-${c.id}`)?.scrollIntoView();
                    }}
                  >
                    {c.name}
                  </Menu.Item>
                ))}
              </Menu>
            </Dropdown>
          )}
        </div>
        <div className="right">
          <Button
            type={'secondary'}
            size={'small'}
            onClick={() => {
              SimpleOneStepDialog.show({
                onOk: async () => {
                  const orderedCategoryIds = categories.slice().sort((a, b) => a.name.localeCompare(b.name)).map(a => a.id);
                  const rsp = await BApi.resourceCategory.sortCategories({
                    ids: orderedCategoryIds,
                  });
                  if (!rsp.code) {
                    loadAllCategories();
                    return true;
                  }
                  return false;
                },
                title: t('Sort by name'),
              });
            }}
          >{t('Sort by name')}</Button>
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
    </div>
  );
};
