import React, { useEffect, useState } from 'react';
import { Axis, Chart, Coordinate, DonutChart, Interaction, Interval, Legend, Tooltip, useTheme } from 'bizcharts';
import { GetStatistics, GetUIOptions } from '@/sdk/apis';
import './index.scss';
import i18n from 'i18next';
import { DownloadTaskDtoStatus, StartupPage, ThirdPartyRequestResultType, UiTheme } from '@/sdk/constants';
import { history } from 'ice';
import store from '@/store';

export default () => {
  const [statistics, setStatistics] = useState({});

  const [clientAppState, clientAppDispatchers] = store.useModel('clientApp');

  const [theme, setTheme] = useTheme(window?.uiTheme == UiTheme.Dark ? 'dark' : 'light');

  const loadStatistics = () => {
    GetStatistics()
      .invoke((a) => {
        setStatistics(a.data || {});
      });
  };

  useEffect(() => {
    if (!clientAppState.firstPageViewed) {
      clientAppDispatchers.setState({
        firstPageViewed: true,
      });
      GetUIOptions().invoke((a) => {
        console.log(a.data);

        switch (a.data?.startupPage) {
          case StartupPage.Resource:
            history.push('/resource');
            break;
          default:
            loadStatistics();
            break;
        }
      });
    } else {
      loadStatistics();
    }
  }, []);

  const resourcePieDataList = [
    {
      title: 'Added today',
      className: 'today',
      data: statistics.resourceAddedCountsToday || [],
    },
    {
      title: 'Added this week',
      className: 'this-week',
      data: statistics.resourceAddedCountsThisWeek || [],
    },
    {
      title: 'Total',
      className: 'total',
      data: statistics.totalResourceCounts || [],
    },
  ];

  const tagData = (statistics.top10TagCounts || []).reverse();
  const downloadTaskData = statistics.downloadTaskStatusCounts || [];
  const thirdPartyRequestCounts = (statistics.thirdPartyRequestCounts || []).reduce((s, t) => {
    Object.keys(t.counts || {}).forEach((r) => {
      s.push({
        id: t.id.id,
        name: t.id.name,
        result: ThirdPartyRequestResultType[r],
        count: t.counts[r],
      });
    });
    return s;
  }, []);

  console.log(thirdPartyRequestCounts);

  return (
    <div className={'dashboard-page'}>
      <div className="resource block">
        <div className="title">
          {i18n.t('Resource overview')}
        </div>
        <div className="counts">
          {resourcePieDataList.map((pd) => {
            const sum = pd.data?.reduce((s, t) => {
              return s + t.count;
            }, 0) ?? 0;
            return (
              <div className={pd.className}>
                <div className="title">
                  {i18n.t(pd.title)}
                </div>
                {sum == 0 ? (
                  <div className={'nothing'}>{i18n.t('No data')}</div>
                ) : (
                  <div className="pie">
                    <DonutChart
                      theme={theme}
                      data={pd.data || []}
                      label={{
                        formatter: (d) => {
                          return `${d.name}(${d.count})`;
                        },
                      }}
                      autoFit
                      height={250}
                      radius={0.8}
                      padding="auto"
                      angleField="count"
                      colorField="name"
                      // pieStyle={{
                      //   stroke: 'white',
                      //   lineWidth: 5,
                      // }}
                      legend={false}
                    />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
      <div className="line2">
        <div className="tags block">
          <div className="title">{i18n.t('Tag')}</div>
          {tagData.length == 0 ? (
            <div className={'nothing'}>
              {i18n.t('No data')}
            </div>
          ) : (
            <div className={'chart'}>
              <Chart
                theme={theme}
                height={300}
                data={tagData}
                autoFit
                scale={{
                  tickInterval: 1,
                }}
              >
                <Coordinate transpose />
                <Interval
                  position="name*count"
                  label={[
                    'count',
                    (val) => ({
                      position: 'middle',
                      offsetX: -15,
                      style: {
                        fill: '#fff',
                      },
                    }),
                  ]}
                />
              </Chart>
            </div>
          )}
        </div>
        <div className="download-tasks block">
          <div className="title">{i18n.t('Download task')}</div>
          {downloadTaskData.length == 0 ? (
            <div className={'nothing'}>
              {i18n.t('No data')}
            </div>
          ) : (
            <div className={'chart'}>
              <Chart
                height={300}
                data={downloadTaskData}
                autoFit
                theme={theme}
              >
                <Coordinate type="theta" radius={0.75} />
                <Tooltip showTitle={false} />
                <Axis visible={false} />
                <Interval
                  position="count"
                  adjust="stack"
                  color={
                    ['name', (name) => {
                      switch (name) {
                        case DownloadTaskDtoStatus[DownloadTaskDtoStatus.Failed]:
                          return '#dd5546';
                      }
                      return undefined;
                    }]
                  }
                  style={{
                    lineWidth: 1,
                    stroke: '#fff',
                  }}
                  label={['count', {
                    // label 太长自动截断
                    layout: { type: 'limit-in-plot', cfg: { action: 'ellipsis' } },
                    content: (data) => {
                      return `${data.name}: ${data.count}`;
                    },
                  }]}
                />
                <Interaction type="element-single-selected" />
              </Chart>
            </div>
          )}
        </div>
      </div>
      <div className="line3">
        <div className="third-party-request-counts block">
          <div className="title">{i18n.t('Third party requests')}</div>
          {thirdPartyRequestCounts.length == 0 ? (
            <div className={'nothing'}>
              {i18n.t('No data')}
            </div>
          ) : (
            <div className={'chart'}>
              <Chart
                theme={theme}
                height={300}
                padding="auto"
                data={thirdPartyRequestCounts}
                autoFit
              >
                <Interval
                  adjust={[
                    {
                      type: 'stack',
                    },
                  ]}
                  color={[
                    'result',
                    (result) => {
                      switch (result) {
                        case ThirdPartyRequestResultType[ThirdPartyRequestResultType.Failed]:
                          return '#dd5546';
                      }
                    },
                  ]}
                  position={'id*count'}
                />
                <Axis
                  name={'id'}
                  label={{
                    formatter: (id, item, index) => {
                      return thirdPartyRequestCounts.find((a) => a.id == id)?.name;
                    } }}
                />
                <Tooltip
                  shared
                  title={'name'}
                />
                <Legend name={'result'} />
              </Chart>
            </div>
          )}
        </div>
        <div />
      </div>
    </div>
  );
};
