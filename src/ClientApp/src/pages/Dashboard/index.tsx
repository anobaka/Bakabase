import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { Icon, Loading } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { Axis, Chart, Coordinate, Interval, Legend, LineAdvance, Tooltip } from 'bizcharts';
import BApi from '@/sdk/BApi';
import type {
  BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
  BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount,
} from '@/sdk/Api';
import { downloadTaskStatuses, PropertyPool, ThirdPartyId } from '@/sdk/constants';
import { Chip } from '@/components/bakaui';

const textColor = getComputedStyle(document.body).getPropertyValue('--bakaui-color');

export default () => {
  const { t } = useTranslation();
  const [data, setData] = useState<BakabaseInsideWorldModelsModelsDtosDashboardStatistics>({});
  const initializedRef = useRef(false);

  useEffect(() => {
    BApi.dashboard.getStatistics().then(res => {
      initializedRef.current = true;
      setData(res.data || {});
    });
  }, []);

  const renderPeriodResourceAddition = (period: string, counts: BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount[]) => {
    return (
      <>
        <div className={'title'}>{t(period)}</div>
        <div className={'content'}>
          {(counts && counts.length > 0) ? counts.map((c, i) => {
            return (
              <div className="t-t-c" title={c.name!} key={i}>
                <div className="left">
                  <div className="text">
                    {c.name}
                  </div>
                </div>
                <div className="right">
                  <div className="count">
                    {c.count}
                  </div>
                </div>
              </div>
            );
          }) : (
            t('No content')
          )}
        </div>
      </>
    );
  };

  const trendingContentDomRef = useRef<HTMLDivElement>(null);

  const renderTrending = () => {
    if (data && trendingContentDomRef.current && data.resourceTrending) {
      const chartData = data.resourceTrending?.map(r => ({
        week: r.offset == 0 ? t('This week') : r.offset == -1 ? t('Last week') : `${t('{{count}} weeks ago', { count: -(r.offset!) })}`,
        count: r.count,
      }));
      // console.log(chartData, trendingContentDomRef.current.clientHeight);
      return (
        <Chart
          padding={[10, 20, 50, 40]}
          autoFit
          height={trendingContentDomRef.current.clientHeight}
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
  return (
    <div className={'dashboard-page'}>
      <Loading visible={!initializedRef.current}>
        <section style={{ maxHeight: '40%' }}>
          <div className="block" style={{ flex: 1 }}>
            <div className={'title'}>{t('Overview')}</div>
            <div className={'content'}>
              {(data.categoryResourceCounts && data.categoryResourceCounts.length > 0) ? (
                <Chart
                  height={240}
                  data={data.categoryResourceCounts}
                  // scale={{
                  //   count: {
                  //     formatter: (val) => {
                  //       val = `${val * 100}%`;
                  //       return val;
                  //     },
                  //   },
                  // }}
                  interactions={['element-active']}
                  autoFit
                >
                  <Coordinate type="theta" radius={0.75} />
                  <Tooltip showTitle={false} />
                  <Axis visible={false} />
                  <Legend visible={false} />
                  <Interval
                    position="count"
                    adjust="stack"
                    color="name"
                    style={{
                      lineWidth: 1,
                      stroke: '#fff',
                    }}
                    label={[
                      'name',
                      (item) => {
                        return {
                          offset: 20,
                          content: (data) => {
                            return `${data.name}[${data.count}]`;
                          },
                          style: {
                            // fill: colors[item],
                            fill: textColor,
                          },
                        };
                      },
                    ]}
                  />
                </Chart>
              ) : (
                t('No content')
              )}


              {/* {data.categoryResourceCounts && data.categoryResourceCounts.length > 0 && data.categoryResourceCounts.map((c, i) => { */}
              {/*   return ( */}
              {/*     <div className="t-t-c" title={c.name!} key={i}> */}
              {/*       <div className="left"> */}
              {/*         <div className="text"> */}
              {/*           {c.name} */}
              {/*         </div> */}
              {/*       </div> */}
              {/*       <div className="right"> */}
              {/*         <div className="count"> */}
              {/*           {c.count} */}
              {/*         </div> */}
              {/*       </div> */}
              {/*     </div> */}
              {/*   ); */}
              {/* }) || ( */}
              {/*   t('No content') */}
              {/* )} */}
            </div>
          </div>
          <div className="block" style={{ flex: 1 }}>
            {renderPeriodResourceAddition('Added today', data.todayAddedCategoryResourceCounts || [])}
            {renderPeriodResourceAddition('Added this week', data.thisWeekAddedCategoryResourceCounts || [])}
            {renderPeriodResourceAddition('Added this month', data.thisMonthAddedCategoryResourceCounts || [])}
          </div>
          <div className="block trending" style={{ flex: 1 }}>
            <div className="title">{t('Trending')}</div>
            <div className="content" ref={trendingContentDomRef}>
              {renderTrending()}
            </div>
          </div>
        </section>
        <section style={{ maxHeight: '40%' }}>
          <div className="block" style={{ flex: 2.5 }}>
            <div className={'title flex items-center gap-2'}>
              {t('Property value coverage')}
              <div className={'text-sm opacity-60'}>
                {t('The more property value coverage, the more data is filled')}
              </div>
            </div>
            <div className={'flex items-start gap-8'}>
              <div className={'w-[200px]'}>
                <div className={'text-lg'}>
                  {t('Overall')}
                </div>
                <div className={'text-3xl'}>
                  {(data.totalFilledPropertyValueCount / data.totalExpectedPropertyValueCount * 100).toFixed(2)}%
                </div>
                <div className={'opacity-60 text-xs'}>
                  {data.totalFilledPropertyValueCount} / {data.totalExpectedPropertyValueCount}
                </div>
              </div>
              <div>
                <div className={'text-lg'}>
                  {t('Details')}
                </div>
                <div className={'flex flex-wrap gap-2'}>
                  {data.propertyValueCoverages?.map(x => {
                    return (
                      <div>
                        <div className={'flex items-center gap-1'}>
                          <Chip
                            variant={'flat'}
                            size={'sm'}
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
          </div>
        </section>
        <section>
          <div className="block" style={{ flex: 1.5 }}>
            <div className={'title'}>{t('Downloader')}</div>
            <div className="content">
              {(data.downloaderDataCounts && data.downloaderDataCounts.length > 0) ? (
                <>
                  <div className={'downloader-item'}>
                    <div>{t('Third party')}</div>
                    {downloadTaskStatuses.map((s, i) => {
                      return (
                        <div key={i}>
                          {t(s.label)}
                        </div>
                      );
                    })}
                  </div>
                  {data.downloaderDataCounts?.map(c => {
                    return (
                      <div className={'downloader-item'}>
                        <div>{t(ThirdPartyId[c.id as number])}</div>
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
                t('No content')
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
                              {t(c.name)}
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
                        {t('File mover')}
                      </div>
                    </div>
                    <div className="right">
                      <div className="count">
                        {data.fileMover?.sourceCount ?? 0}
                        <Icon type="arrow-double-right" size={'xs'} />
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
      </Loading>
    </div>
  );
};
