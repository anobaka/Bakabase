import { useTranslation } from 'react-i18next';
import React, { useEffect, useRef, useState } from 'react';
import { Axis, Chart, Interval, Legend, Tooltip as ChartsTooltip } from 'bizcharts';
import { AiOutlineBarChart } from 'react-icons/ai';
import { ThirdPartyId, ThirdPartyRequestResultType } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Chip, Modal, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { components } from '@/sdk/BApi2';
import ThirdPartyIcon from '@/components/ThirdPartyIcon';


const RequestResultTypeIntervalColorMap = {
  [ThirdPartyRequestResultType.Succeed]: '#46bc15',
  [ThirdPartyRequestResultType.Failed]: '#ff3000',
  [ThirdPartyRequestResultType.Banned]: '#993300',
  [ThirdPartyRequestResultType.Canceled]: '#666',
  [ThirdPartyRequestResultType.TimedOut]: '#ff9300',
};

type RequestStatistics = components['schemas']['Bakabase.InsideWorld.Models.Models.Aos.ThirdPartyRequestStatistics'];

export default () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [requestStatistics, setRequestStatistics] = useState<RequestStatistics[]>([]);
  const gettingRequestStatistics = useRef(false);
  const requestStatisticsRef = useRef(requestStatistics);

  useEffect(() => {
    const getRequestStatisticsInterval = setInterval(() => {
      if (!gettingRequestStatistics.current) {
        gettingRequestStatistics.current = true;
        BApi.thirdParty.getAllThirdPartyRequestStatistics().then(a => {
            if (JSON.stringify(a.data) != JSON.stringify(requestStatisticsRef.current)) {
              setRequestStatistics(a.data ?? []);
            }
          })
          .finally(() => {
            gettingRequestStatistics.current = false;
          });
      }
    }, 1000);

    return () => {
      clearInterval(getRequestStatisticsInterval);
    };
  }, []);

  return (
    <div className="flex items-center gap-1" >
      <Button
        size={'sm'}
        variant={'light'}
        onPress={() => {
        const thirdPartyRequestCounts = (requestStatistics || []).reduce<any[]>((s, t) => {
          Object.keys(t.counts || {})
            .forEach((r) => {
              s.push({
                id: t.id.toString(),
                name: ThirdPartyId[t.id],
                result: ThirdPartyRequestResultType[r],
                count: t.counts?.[r],
              });
            });
          return s;
        }, []);

        createPortal(
          Modal, {
            size: 'xl',
            defaultVisible: true,
            children: (
              <Chart
                height={300}
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
                    (result) => RequestResultTypeIntervalColorMap[ThirdPartyRequestResultType[result]!],
                  ]}
                  position={'id*count'}
                />
                <Axis
                  name={'id'}
                  label={{
                    formatter: (id, item, index) => {
                      return ThirdPartyId[id];
                    },
                  }}
                />
                <ChartsTooltip
                  shared
                  title={'name'}
                />
                <Legend name={'result'} />
              </Chart>
            ),
            footer: {
              actions: ['ok'],
            },
            title: t('Requests overview'),
          },
        );
      }}
      >
        <div className="flex items-center gap-1">
          <AiOutlineBarChart className={'text-base'} />
          {t('Requests overview')}
          {requestStatistics?.map((rs) => {
            let successCount = 0;
            let failureCount = 0;
            Object.keys(rs.counts || {})
              .forEach((r) => {
                const rt = parseInt(r, 10) as ThirdPartyRequestResultType;
                switch (rt) {
                  case ThirdPartyRequestResultType.Succeed:
                    successCount += rs.counts![r]!;
                    break;
                  default:
                    failureCount += rs.counts![r]!;
                    break;
                }
              });
            return (
              <div className="flex items-center">
                <ThirdPartyIcon
                  thirdPartyId={rs.id}
                  size={'sm'}
                />
                <Tooltip
                  content={t('Success')}
                >
                  <Chip
                    size={'sm'}
                    variant={'light'}
                    color={'success'}
                    className={'p-0'}
                  >
                    {successCount}
                  </Chip>
                </Tooltip>
                /
                <Tooltip
                  content={t('Failure')}
                >
                  <Chip
                    size={'sm'}
                    variant={'light'}
                    color={'danger'}
                    className={'p-0'}
                  >
                    {failureCount}
                  </Chip>
                  {}
                </Tooltip>
              </div>
            );
          })}
        </div>
      </Button>
    </div>
  );
};
