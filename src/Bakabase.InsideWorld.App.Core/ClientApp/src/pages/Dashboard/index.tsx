import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { Icon, Loading } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { Chart, LineAdvance } from 'bizcharts';
import BApi from '@/sdk/BApi';
import type {
  BakabaseInsideWorldModelsModelsDtosDashboardStatistics,
  BakabaseInsideWorldModelsModelsDtosDashboardStatisticsTextAndCount,
} from '@/sdk/Api';
import { downloadTaskStatuses, ResourceProperty, ThirdPartyId } from '@/sdk/constants';

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
      console.log(chartData, trendingContentDomRef.current.clientHeight);
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
              {data.categoryResourceCounts && data.categoryResourceCounts.length > 0 && data.categoryResourceCounts.map((c, i) => {
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
              }) || (
                t('No content')
              )}
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
          <div className="block" style={{ flex: 1.5 }}>
            <div className={'title'}>{t('Resource tags')}</div>
            <div className={'content'}>
              {data.tagResourceCounts && data.tagResourceCounts.length > 0 && data.tagResourceCounts.map((t, i) => {
                return (
                  <div className="t-t-c" key={i}>
                    <div className="left">
                      <div className="label" title={t.label!}>
                        {t.label}
                      </div>
                      <div className="text" title={t.name!}>
                        {t.name}
                      </div>
                    </div>
                    <div className="right">
                      <div className="count">
                        {t.count}
                      </div>
                    </div>
                  </div>
                );
              }) || (
                t('No content')
              )}
            </div>
          </div>
          <div className="block" style={{ flex: 2.5 }}>
            <div className={'title'}>{t('Resource properties')}</div>
            <div className={'content'}>
              {data.propertyResourceCounts && data.propertyResourceCounts.length > 0 && data.propertyResourceCounts.map((c, i) => {
                const property = c.property as number as ResourceProperty;
                let propertyLabel = t(ResourceProperty[property]);
                if (property == ResourceProperty.CustomProperty) {
                  propertyLabel += `:${c.propertyKey}`;
                }
                return (
                  <div className="t-t-c" key={i}>
                    <div className="left">
                      <div
                        className="label"
                        title={propertyLabel!}
                      >{propertyLabel}</div>
                      <div
                        className="text"
                        title={c.value!}
                      >{c.value}</div>
                    </div>
                    <div className="right">
                      <div className="count">{c.count}</div>
                    </div>
                  </div>
                );
              }) || (
                t('No content')
              )}
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
                          <div className="t-t-c" title={c.name!}>
                            <div className="left">
                              <div className="text">
                                {t(c.name!)}
                              </div>
                            </div>
                            <div className="right">
                              <div className="count">{c.count}</div>
                            </div>
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
