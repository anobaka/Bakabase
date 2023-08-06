import React, { useEffect, useState } from 'react';
import './index.scss';
import { Box, Button, Divider, Icon, Table } from '@alifd/next';
import { BackgroundTaskStatus } from '@/sdk/constants';
import moment from 'moment';
import { ClearInactiveBackgroundTasks, StopBackgroundTask } from '@/sdk/apis';
import store from '@/store';
import i18n from 'i18next';

const testData = [{
  name: 'Sync Subscription1',
  startDt: '2021-12-01 12:12:12',
  message: 'czxcjnskcjsdnfsdjifnsdfjisdfnosewkjnsedfijkosdnfjidcnsdjcisdnsdjivnweoknfmjiodsnvjisdvsd',
  status: BackgroundTaskStatus.Running,
  percentage: 85,
},
{
  name: 'Sync Subscription2',
  startDt: '2021-12-01 12:12:13',
  message: 'czxcjnskcjsdnfsdjifnsdfjisdfnosewkjnsedfijkosdnfjidcnsdjcisdnsdjivnweoknfmjiodsnvjisdvsd',
  status: BackgroundTaskStatus.Complete,
},
{
  name: 'Sync Subscription3',
  startDt: '2021-12-01 12:12:14',
  message: 'czxcjnskcjsdnfsdjifnsdfjisdfnosewkjnsedfijkosdnfjidcnsdjcisdnsdjivnweoknfmjiodsnvjisdvsd',
  status: BackgroundTaskStatus.Failed,
}];

const statusIcons = {
  [BackgroundTaskStatus.Running]: <Icon type="loading" />,
  [BackgroundTaskStatus.Complete]: <Icon type="success" style={{ color: 'green' }} />,
  [BackgroundTaskStatus.Failed]: <Icon type="error" style={{ color: 'red' }} />,
};

export default () => {
  const storeTasks = store.useModelState('backgroundTasks');
  const [tasks, setTasks] = useState([]);

  useEffect(() => {
    setTasks(JSON.parse(JSON.stringify(storeTasks)));
  }, [storeTasks]);

  useEffect(() => {
  }, []);

  const renderOperations = (id, i, r) => {
    const elements = [];
    if (r.status == BackgroundTaskStatus.Running) {
      elements.push(<Button
        text
        type="primary"
        onClick={() => {
          StopBackgroundTask({
            id,
          }).invoke((a) => {
          });
        }}
      >
        Stop
      </Button>);
    }
    const opts = elements.reduce((s, t, i) => {
      if (i > 0) {
        s.push(<Divider direction="ver" />);
      }
      s.push(t);
      return s;
    }, []);
    return (
      <Box
        direction="row"
      >
        {opts}
      </Box>
    );
  };

  // console.log(tasks);

  const activeTasks = tasks.filter((t) => t.status != BackgroundTaskStatus.Running).length;

  return (
    <div className="background-task-page">
      <div className={'opt'}>
        <Button
          disabled={activeTasks.length == 0}
          type="normal"
          size={'small'}
          onClick={() => ClearInactiveBackgroundTasks().invoke()}
        >{i18n.t('Clear inactive tasks')}
        </Button>
      </div>
      <Table
        className={'tasks'}
        dataSource={tasks}
        hasBorder={false}
        useVirtual
        size={'small'}
        maxBodyHeight={750}
      >
        <Table.Column dataIndex={'name'} title={'Name'} width="20%" />
        <Table.Column dataIndex={'startDt'} title={'Start Time'} cell={(c) => moment(c).format('HH:mm:ss')} width="10%" />
        <Table.Column
          dataIndex={'startDt'}
          title={'Duration'}
          cell={(c) => {
            const start = moment(c);
            const end = moment();
            const diff = end.diff(start);
            return moment.utc(diff).format('H[h]m[m]');
          }}
          width="10%"
        />
        <Table.Column
          dataIndex={'status'}
          title={'Status'}
          cell={(c, i, r) => {
            switch (c) {
              case BackgroundTaskStatus.Failed:
              case BackgroundTaskStatus.Complete:
                return statusIcons[c];
              case BackgroundTaskStatus.Running:
                // return <Progress shape={'circle'} size="small" percent={r.percentage} />;
                return r.percentage > 0 ? <>{r.percentage}%&nbsp;{statusIcons[c]}</> : statusIcons[c];
            }
          }}
          width="8%"
        />
        <Table.Column dataIndex={'message'} title={'Message'} width="42%" cell={(c) => <pre>{c}</pre>} />
        <Table.Column
          width="10%"
          dataIndex={'id'}
          title={'Opt'}
          cell={renderOperations}
        />
      </Table>
    </div>
  );
};
