import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { DeleteOutlined } from '@ant-design/icons';
import BApi from '@/sdk/BApi';
import { Button, Card, CardBody, CardFooter, CardHeader, Chip, Divider } from '@/components/bakaui';
import { ResourceCacheType } from '@/sdk/constants';

type CategoryCacheOverview = {
  categoryId: number;
  categoryName: string;
  resourceCacheCountMap: Record<number, number>;
  resourceCount: number;
};

type CacheOverview = {
  categoryCaches: CategoryCacheOverview[];
};


export default () => {
  const { t } = useTranslation();
  const [cacheOverview, setCacheOverview] = useState<CacheOverview>();

  const reload = async () => {
    const r = await BApi.cache.getCacheOverview();
    setCacheOverview(r.data);
  };

  useEffect(() => {
    reload();
  }, []);

  return (
    <div>
      <div className={'text-xl font-bold mb-2'}>{t('Category caches')}</div>
      <div>
        {(cacheOverview?.categoryCaches && cacheOverview.categoryCaches.length > 0) ? (
          <div className={'grid grid-cols-4 gap-2'}>
            {cacheOverview.categoryCaches.map((item, idx) => {
              return (
                <Card>
                  <CardHeader>
                    <div>
                      <div className="text-md">{item.categoryName}</div>
                      <div className="text-small text-default-500">{t('Resource count')}: {item.resourceCount}</div>
                    </div>
                  </CardHeader>
                  <Divider />
                  <CardBody>
                    {Object.keys(item.resourceCacheCountMap).map((key, idx) => {
                      return (
                        <div key={idx} className={'flex items-center gap-2'}>
                          <Chip
                            size={'sm'}
                            radius={'sm'}
                          >
                            {t('Cached')} {t(ResourceCacheType[key])}
                          </Chip>
                          {item.resourceCacheCountMap[key]}
                          <Button
                            size={'sm'}
                            isIconOnly
                            color={'danger'}
                            variant={'light'}
                            onClick={() => {
                              BApi.cache.deleteResourceCacheByCategoryIdAndCacheType(item.categoryId, parseInt(key, 10) as ResourceCacheType).then(r => {
                                if (!r.code) {
                                  reload();
                                }
                              });
                            }}
                          >
                            <DeleteOutlined className={'text-base'} />
                          </Button>
                        </div>
                      );
                    })}
                  </CardBody>
                  {/* <Divider /> */}
                  {/* <CardFooter> */}
                  {/*   <Link isExternal showAnchorIcon href="https://github.com/nextui-org/nextui"> */}
                  {/*     Visit source code on GitHub. */}
                  {/*   </Link> */}
                  {/* </CardFooter> */}
                </Card>
              );
            })}
          </div>
        ) : t('No cache for now')}
      </div>
    </div>
  );
};
