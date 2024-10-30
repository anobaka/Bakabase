import React, { useEffect, useReducer, useRef, useState } from 'react';
import { Dropdown, Loading, Menu } from '@alifd/next';
import './index.scss';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import { DisconnectOutlined } from '@ant-design/icons';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import { GetComponentDescriptors } from '@/sdk/apis';
import SortableCategoryList from '@/pages/Category/components/SortableCategoryList';
import MediaLibrarySynchronization from '@/pages/Category/components/MediaLibrarySynchronization';
import store from '@/store';
import BApi from '@/sdk/BApi';
import { CategoryAdditionalItem, MediaLibraryAdditionalItem } from '@/sdk/constants';
import type { EnhancerDescriptor } from '@/components/EnhancerSelectorV2/models';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Input, Modal } from '@/components/bakaui';
import DeleteUnknownResources from '@/components/DeleteUnknownResources';

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [categories, setCategories] = useState<any[]>([]);
  const [libraries, setLibraries] = useState<any[]>([]);
  const categoriesLoadedRef = useRef(false);
  // const [enhancers, setEnhancers] = useState([]);
  const resourceOptions = store.useModelState('resourceOptions');
  const [allComponents, setAllComponents] = useState([]);

  const [enhancers, setEnhancers] = useState<EnhancerDescriptor[]>([]);

  const [loading, setLoading] = useState(true);
  const [, forceUpdate] = useReducer((x) => x + 1, 0);

  // const gotoNewCategoryPage = (noCategory: boolean) => {
  //   history!.push(`/category/setupWizard?noCategory=${noCategory ? 1 : 0}`);
  // };

  const loadAllCategories = (cb: () => void = () => {
  }): Promise<any> => {
    return BApi.category.getAllCategories({ additionalItems:
        CategoryAdditionalItem.Validation |
        CategoryAdditionalItem.CustomProperties |
        CategoryAdditionalItem.EnhancerOptions,
    }).then((rsp) => {
      categoriesLoadedRef.current = true;
      rsp.data?.sort((a, b) => a.order! - b.order!);
      setCategories(rsp.data || []);
      cb && cb();
    });
  };

  const loadAllMediaLibraries = (cb: () => void = () => {
  }): Promise<any> => {
    return BApi.mediaLibrary.getAllMediaLibraries({ additionalItems: MediaLibraryAdditionalItem.Category | MediaLibraryAdditionalItem.FileSystemInfo | MediaLibraryAdditionalItem.PathConfigurationCustomProperties }).then((x) => {
      x.data?.sort((a, b) => a.order - b.order);
      setLibraries(x.data || []);
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
      const er = await BApi.enhancer.getAllEnhancerDescriptors();
      const enhancerData = er.data || [];
      // @ts-ignore
      setEnhancers(enhancerData);
    } finally {
      setLoading(false);
    }
  }

  const reloadCategory = async (id: number) => {
    const c = (await BApi.category.getCategory(id, { additionalItems: CategoryAdditionalItem.Validation |
        CategoryAdditionalItem.CustomProperties |
        CategoryAdditionalItem.EnhancerOptions })).data ?? {};
    const idx = categories.findIndex(x => x.id == id);
    categories[idx] = c;
    setCategories([...categories]);
  };

  const reloadMediaLibrary = async (id: number) => {
    const c = (await BApi.mediaLibrary.getMediaLibrary(id, { additionalItems: MediaLibraryAdditionalItem.Category | MediaLibraryAdditionalItem.FileSystemInfo | MediaLibraryAdditionalItem.PathConfigurationCustomProperties })).data ?? {};
    const idx = libraries.findIndex(x => x.id == id);
    libraries[idx] = c;
    setLibraries({ ...libraries });
  };

  useEffect(() => {
    init();
  }, []);

  // useEffect(() => {
  //   if (categoriesLoadedRef.current) {
  //     const noCategory = !(categories?.length > 0);
  //     if (noCategory) {
  //       gotoNewCategoryPage(true);
  //     }
  //   }
  // }, [categories]);

  // console.log(categories);

  return (
    <div className={'category-page'}>
      <div className="header mb-1">
        <div className="left">
          <Button
            color={'primary'}
            size={'small'}
            onClick={() => {
              let name = '';
              createPortal(Modal, {
                defaultVisible: true,
                size: 'md',
                title: t('Create a category'),
                children: (
                  <div>
                    <Input
                      size={'md'}
                      className={'w-full'}
                      placeholder={t('Please enter the name of the category')}
                      onValueChange={(v) => {
                        name = v;
                      }}
                    />
                    <FeatureStatusTip
                      className={'mt-2'}
                      name={t('Setup wizard')}
                      status={'deprecated'}
                    />
                  </div>
                ),
                onOk: async () => {
                  if (name == undefined || name.length == 0) {
                    throw new Error('Name is required');
                  }
                  await BApi.category.addCategory({
                    name,
                  });
                  loadAllCategories();
                },
              });
            }}
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
          <DeleteUnknownResources />
          <Button
            color={'default'}
            size={'small'}
            onClick={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t('Sort by name'),
                children: t('We\'ll sort categories by name'),
                onOk: async () => {
                  const orderedCategoryIds = categories.slice().sort((a, b) => a.name.localeCompare(b.name)).map(a => a.id);
                  const rsp = await BApi.category.sortCategories({
                    ids: orderedCategoryIds,
                  });
                  if (!rsp.code) {
                    loadAllCategories();
                  }
                },
              });
            }}
          >{t('Sort by name')}</Button>
          <div className={'last-sync-time'}>
            {t('Last sync time')}: {resourceOptions?.lastSyncDt ? dayjs(resourceOptions.lastSyncDt).format('YYYY-MM-DD HH:mm:ss') : t('Never')}
          </div>
          <MediaLibrarySynchronization onComplete={() => loadAllMediaLibraries()} />
        </div>
      </div>
      <Loading visible={loading} fullScreen />
      {categories.length > 0 ? (
        <SortableCategoryList
          forceUpdate={forceUpdate}
          allComponents={allComponents}
          loadAllCategories={loadAllCategories}
          loadAllMediaLibraries={loadAllMediaLibraries}
          reloadCategory={reloadCategory}
          reloadMediaLibrary={reloadMediaLibrary}
          categories={categories}
          libraries={libraries}
          enhancers={enhancers}
        />
      ) : (
        <div className={'flex items-center gap-1 justify-center mt-10'}>
          <DisconnectOutlined className={'text-base'} />
          {t('Categories have not been created yet. To load your resources, please ensure that at least one category is created.')}
        </div>
      )}
    </div>
  );
};
