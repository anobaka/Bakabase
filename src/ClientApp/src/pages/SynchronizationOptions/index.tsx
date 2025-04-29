import { useTranslation } from 'react-i18next';
import { useEffect, useState } from 'react';
import _ from 'lodash';
import type { IdName } from './models';
import { Alert, Button, Card, CardBody, CardHeader, Chip, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { CategoryAdditionalItem, enhancerIds } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import store from '@/store';
import type {
  BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel,
} from '@/sdk/Api';
import GlobalOptions from '@/pages/SynchronizationOptions/components/GlobalOptions';
import CategoryOptions from '@/pages/SynchronizationOptions/components/CategoryOptions';

type Options = BakabaseInsideWorldBusinessConfigurationsModelsDomainResourceOptionsSynchronizationOptionsModel;


type Category = {
  order: number;
  mediaLibraries?: IdName[];
  enhancers?: IdName[];
} & IdName;

export default () => {
  const { t } = useTranslation();

  const { createPortal } = useBakabaseContext();

  const [categories, setCategories] = useState<Category[]>([]);
  const synchronizationOptions = store.useModelState('resourceOptions')?.synchronizationOptions;

  const [options, setOptions] = useState<Options>();

  useEffect(() => {
    setOptions(synchronizationOptions ? JSON.parse(JSON.stringify(synchronizationOptions)) : undefined);
  }, [synchronizationOptions]);

  const init = async () => {
    const categories = (await BApi.category
      .getAllCategories({ additionalItems: CategoryAdditionalItem.EnhancerOptions })).data ?? [];
    const categoryMediaLibraryMap = _
      .groupBy((await BApi.mediaLibrary.getAllMediaLibraries()).data ?? [], x => x.categoryId);

    const enhancerName = enhancerIds.reduce<Record<number, string>>((s, t) => {
      s[t.value] = t.label;
      return s;
    }, {});
    const simpleCategories = categories.map(c => {
      const eIds = c.enhancerOptions?.filter(x => x.active).map(x => x.enhancerId);
      const nc: Category = {
        id: c.id,
        name: c.name,
        order: c.order,
        enhancers: eIds?.map(e => ({ id: e, name: enhancerName[e]! })),
        mediaLibraries: categoryMediaLibraryMap[c.id],
      };
      return nc;
    });

    setCategories(simpleCategories);
  };

  useEffect(() => {
    init();
  }, []);

  const saveOptions = async (options: typeof synchronizationOptions) => {
    setOptions(options);
    await BApi.options.patchResourceOptions({
      synchronizationOptions: options,
    });
  };

  return (
    <div className={'flex flex-col gap-2'}>
      <Alert
        color={'default'}
        title={(
          <div className={'flex items-center gap-2'}>
            <div>
              {t('You can customize media library synchronization behavior here. If you leave the option blank, we will attempt to apply the parent selection according to priority.')}
            </div>
            <Button
              size={'sm'}
              variant={'light'}
              radius={'sm'}
              color={'primary'}
              onPress={() => {
                const data: { label: string; description: string }[] = [
                  {
                    label: 'Priority',
                    description: 'Media library > Category > Global',
                  },
                  {
                    label: 'Resources with unknown path',
                    description: 'The resource path becomes unknown, usually because the file has been moved or the file name or parent folder name has been modified.',
                  },
                  {
                    label: 'Resources with unknown media library',
                    description: 'The media library becoming "unknown" is usually due to the deletion of the media library or category, but its internal resources are still retained.',
                  },
                  {
                    label: 'ReApply in enhancer',
                    description: 'The enhancer will reapply the data it has already acquired to the resources based on the latest configuration. This step is relatively quick since it does not attempt to retrieve new data.',
                  },
                  {
                    label: 'ReEnhance in enhancer',
                    description: 'It will delete all acquired enhanced data and applied enhanced data. Then, the enhancer will automatically attempt to reacquire and apply the data.',
                  },
                ];

                createPortal(Modal, {
                  defaultVisible: true,
                  title: t('Configuration item instructions'),
                  size: 'xl',
                  footer: {
                    actions: ['cancel'],
                    cancelProps: {
                      text: t('Close'),
                    },
                  },
                  children: (
                    <div className={'grid gap-x-4 gap-y-2'} style={{ gridTemplateColumns: 'auto 1fr' }}>
                      {data.map(d => {
                        return (
                          <>
                            <div className={'font-bold text-right'}>{t(d.label)}</div>
                            <div className={'opacity-80'}>{t(d.description)}</div>
                          </>
                        );
                      })}
                    </div>
                  ),
                });
              }}
            >
              {t('View configuration item instructions')}
            </Button>
          </div>
        )}
      />
      <GlobalOptions onChange={saveOptions} options={options} />
      {categories.map(c => {
        return (
          <CategoryOptions
            category={c}
            onChange={o => saveOptions({
              ...options,
              categoryOptionsMap: {
                ...options?.categoryOptionsMap,
                [c.id]: o,
              },
            })}
            options={options?.categoryOptionsMap?.[c.id]}
          />
        );
      })}
    </div>
  );
};
