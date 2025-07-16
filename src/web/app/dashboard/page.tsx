'use client';

import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { useTranslation } from 'react-i18next';
import { Chart, LineAdvance } from 'bizcharts';
import { useRouter } from 'next/navigation';
import { AiOutlineArrowRight, AiOutlinePlusCircle } from 'react-icons/ai';
import BApi from '@/sdk/BApi';
import type {
  BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
} from '@/sdk/Api';
import { downloadTaskStatuses, PropertyPool, ThirdPartyId } from '@/sdk/constants';
import { Button, Chip, Spinner } from '@/components/bakaui';

const textColor = getComputedStyle(document.body).getPropertyValue('--bakaui-color');

export default () => {
  const { t } = useTranslation();
  const [data, setData] = useState<BakabaseInsideWorldModelsModelsDtosDashboardStatistics>({});
  const [property, setProperty] = useState<any>();
  const initializedRef = useRef(false);

  useEffect(() => {
    BApi.dashboard.getStatistics().then(res => {
      initializedRef.current = true;
      setData(res.data || {});
    });

    BApi.dashboard.getPropertyStatistics().then(r => {
      setProperty(r.data);
    });
  }, []);

  const renderTrending = () => {
    if (data && data.resourceTrending) {
      const chartData = data.resourceTrending?.map(r => ({
        week: r.offset == 0 ? t<string>('This week') : r.offset == -1 ? t<string>('Last week') : `${t<string>('{{count}} weeks ago', { count: -(r.offset!) })}`,
        count: r.count,
      }));
      return (
        <Chart
          // padding={[10, 20, 50, 40]}
          autoFit
          data={chartData}
        >
          <LineAdvance
            shape="smooth"
            point
            area
            position="week*count"
          />

        </Chart>
      );
    }
    return;
  };

  const renderResourceCounts = () => {
    const list = data.categoryMediaLibraryCounts ?? [];
    const total = list.reduce((s, t) => s + t.mediaLibraryCounts.reduce((s1, t1) => s1 + t1.count, 0), 0);
    const categoryCounts: any[] = list.map(l => {
      return (
        <>
          <div className={'flex flex-col'}>
            <div className={'text-xl opacity-80'}>
              {l.categoryName}
            </div>
            <div className={'flex flex-wrap gap-1'}>
              {l.mediaLibraryCounts.map(c => {
                return (
                  <div>
                    <div className={'opacity-60 text-sm'}>{c.name}</div>
                    <div className={''}>{c.count}</div>
                  </div>
                );
              })}
            </div>
          </div>
        </>
      );
    });

    return (
      <div className={'flex gap-2 max-h-full'}>
        <div className={'w-[160px]'}>
          <div className={'text-lg'}>
            {t<string>('Resource count')}
          </div>
          <div className={'text-3xl'}>
            {total}
          </div>
        </div>
        {categoryCounts.length > 0 ? (
          <div className={'flex flex-wrap gap-1 overflow-auto gap-x-4'}>
            {categoryCounts}
          </div>
        ) : (
          <Button
            variant={'flat'}
            color={'primary'}
            onPress={() => {
              useRouter().push('/category');
            }}
          >
            <AiOutlinePlusCircle className={'text-medium'} />
            {t<string>('Add your resources')}
          </Button>
        )}
      </div>
    );
  };

  return (
    <div className={'dashboard-page'}>
      {initializedRef.current ? (<>
        <section className={'h-1/3 max-h-1/3'}>
          <div className="block w-2/3">
            <div className={'title'}>{t<string>('Overview')}</div>
            <div className={'content min-h-0'}>
              {renderResourceCounts()}
            </div>
          </div>
          <div className="block trending" style={{ flex: 1 }}>
            <div className="title">{t<string>('Trending')}</div>
            <div className="content">
              {renderTrending()}
            </div>
          </div>
        </section>
        <section style={{ maxHeight: '40%' }}>
          <div className="block" style={{ flex: 2.5 }}>
            <div className={'title flex items-center gap-2'}>
              {t<string>('Property value coverage')}
              <div className={'text-sm opacity-60'}>
                {t<string>('As more data is filled in, property value coverage increases')}
              </div>
            </div>
            {property ? (
              <div className={'flex items-start gap-8 min-h-0'}>
                <div className={'w-[200px]'}>
                  <div className={'text-lg'}>
                    {t<string>('Overall')}
                  </div>
                  <div className={'text-3xl'}>
                    {property.totalExpectedPropertyValueCount > 0
                      ? (property.totalFilledPropertyValueCount / property.totalExpectedPropertyValueCount * 100)
                        .toFixed(2) : 0}%
                  </div>
                  <div className={'opacity-60 text-xs'}>
                    {property.totalExpectedPropertyValueCount > 0
                      ? property.totalFilledPropertyValueCount / property.totalExpectedPropertyValueCount : 0}
                  </div>
                </div>
                <div className={'flex flex-col min-h-0 max-h-full'}>
                  <div className={'text-lg'}>
                    {t<string>('Details')}
                  </div>
                  <div className={'flex flex-wrap gap-2 min-h-0 overflow-auto'}>
                    {property.propertyValueCoverages?.map(x => {
                      return (
                        <div>
                          <div className={'flex items-center gap-1'}>
                            <Chip
                              variant={'flat'}
                              size={'sm'}
                              radius={'sm'}
                              color={x.pool == PropertyPool.Reserved ? 'secondary' : 'success'}
                            >
                              {x.name}
                            </Chip>
                            <div>
                              {(x.filledCount / x.expectedCount * 100).toFixed(2)}%
                            </div>
                          </div>
                          <div className={'opacity-60 text-xs text-center'}>
                            {x.filledCount} / {x.expectedCount}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              </div>
            ) : (
              <div className={'flex justify-center py-4'}>
                <Spinner />
              </div>
            )}
          </div>
        </section>
        <section>
          <div className="block" style={{ flex: 1.5 }}>
            <div className={'title'}>{t<string>('Downloader')}</div>
            <div className="content">
              {(data.downloaderDataCounts && data.downloaderDataCounts.length > 0) ? (
                <>
                  <div className={'downloader-item'}>
                    <div>{t<string>('Third party')}</div>
                    {downloadTaskStatuses.map((s, i) => {
                      return (
                        <div key={i}>
                          {t<string>(s.label)}
                        </div>
                      );
                    })}
                  </div>
                  {data.downloaderDataCounts?.map(c => {
                    return (
                      <div className={'downloader-item'}>
                        <div>{t<string>(ThirdPartyId[c.id]!)}</div>
                        {downloadTaskStatuses.map((s, i) => {
                          return (
                            <div key={i}>
                              {c.statusAndCounts?.[s.value] ?? 0}
                            </div>
                          );
                        })}
                      </div>
                    );
                  })}
                </>
              ) : (
                t<string>('No content')
              )}
            </div>
          </div>
          <div className="blocks">
            {data.otherCounts?.map((list, r) => {
              return (
                <section key={r}>
                  {list.map((c, j) => {
                    return (
                      <div className="block" key={j}>
                        <div className="content">
                          <div className="flex items-center gap-1" key={j}>
                            <Chip
                              size={'sm'}
                              radius={'sm'}
                            >
                              {t<string>(c.name)}
                            </Chip>
                            {c.count}
                          </div>
                        </div>
                      </div>
                    );
                  })}

                </section>
              );
            })}
            <section>
              <div className="block">
                <div className="content">
                  <div className="t-t-c file-mover">
                    <div className="left">
                      <div className="text">
                        {t<string>('File mover')}
                      </div>
                    </div>
                    <div className="right">
                      <div className="count">
                        {data.fileMover?.sourceCount ?? 0}
                        <AiOutlineArrowRight />
                        {data.fileMover?.targetCount ?? 0}
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </section>
          </div>
          <div className="block hidden" style={{ flex: 1.5 }} />
        </section>
      </>) : (
        <div className={'w-full h-full flex items-center justify-center'}>
          <Spinner size={'lg'} />
        </div>
      )}
    </div>
  );
};
