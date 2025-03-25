import React, { useEffect, useState } from 'react';
// import './index.scss';
import { useTranslation } from 'react-i18next';
import { QuestionCircleOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import moment from 'moment';
import toast from 'react-hot-toast';
import {
  Button,
  DateInput,
  Progress,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  TimeInput,
  Tooltip,
} from '@/components/bakaui';
import { BTaskStatus } from '@/sdk/constants';
import store from '@/store';
import BApi from '@/sdk/BApi';
import type { BTask } from '@/core/models/BTask';

export default () => {
  const { t } = useTranslation();
  const taskOptions = store.useModelState('taskOptions');
  const bTasks = store.useModelState('bTasks');

  const [editingOptions, setEditingOptions] = useState<Record<string, { interval?: string; enableAfter?: string }>>({});

  const columns = [
    {
      key: 'name',
      label: 'Name',
    },
    {
      key: 'status',
      label: 'Status',
    },
    {
      key: 'progress',
      label: 'Progress',
    },
    {
      key: 'startedAt',
      label: 'Started At',
    },
    {
      key: 'interval',
      label: 'Interval',
    },
    {
      key: 'enableAfter',
      label: 'Enable after',
    },
  ];

  useEffect(() => {
  }, []);

  const patchOptions = async () => {
    await BApi.options.patchTaskOptions({
      tasks: (taskOptions.tasks || []).map(t => {
        return {
          ...t,
          interval: editingOptions[t.id]?.interval ?? t.interval,
          enableAfter: editingOptions[t.id]?.enableAfter ?? t.enableAfter,
        };
      }),
    });
    toast.success(t('Saved'));
    setEditingOptions({});
  };

  const renderInterval = (task: BTask) => {
    if (!task.isPersistent) {
      return t('Not supported');
    }

    const editingInterval = editingOptions[task.id]?.interval;
    if (editingInterval) {
      return (
        <TimeInput
          onBlur={() => {
            patchOptions();
          }}
          size={'sm'}
          granularity={'second'}
          value={dayjs.duration(moment.duration(editingInterval).asMilliseconds())}
          onChange={v => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                interval: v.format('HH:mm:ss'),
              },
            });
          }}
        />
      );
    } else {
      return (
        <Button
          variant={'light'}
          // color={'primary'}
          size={'sm'}
          onPress={() => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                interval: '00:05:00',
              },
            });
          }}
        >
          {task.interval ? dayjs.duration(moment.duration(task.interval).asMilliseconds()).format('HH:mm:ss') : t('Not set')}
        </Button>
      );
    }
  };

  const renderEnableAfter = (task: BTask) => {
    if (!task.isPersistent) {
      return t('Not supported');
    }

    const editingEnableAfter = editingOptions[task.id]?.enableAfter;
    if (editingEnableAfter) {
      return (
        <DateInput
          onBlur={() => {
            patchOptions();
          }}
          size={'sm'}
          granularity={'second'}
          value={dayjs(editingEnableAfter)}
          onChange={v => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                enableAfter: v?.format('YYYY-MM-dd HH:mm:ss'),
              },
            });
          }}
        />
      );
    } else {
      return (
        <Button
          variant={'light'}
          // color={'primary'}
          size={'sm'}
          onPress={() => {
            setEditingOptions({
              ...editingOptions,
              [task.id]: {
                enableAfter: dayjs().add(1, 'h').format('YYYY-MM-dd HH:mm:ss'),
              },
            });
          }}
        >
          {task.enableAfter ? dayjs(task.enableAfter).format('YYYY-MM-dd HH:mm:ss') : t('Not set')}
        </Button>
      );
    }
  };

  return (
    <Table aria-label="Example table with dynamic content" removeWrapper isStriped>
      <TableHeader columns={columns}>
        {(column) => <TableColumn key={column.key}>{column.label}</TableColumn>}
      </TableHeader>
      <TableBody>
        {bTasks.map(task => {
          return (
            <TableRow key={task.id}>
              <TableCell>
                <div className={'flex items-center gap-1'}>
                  {task.name}
                  {task.description && (
                    <Tooltip content={task.description} color={'secondary'}>
                      <QuestionCircleOutlined />
                    </Tooltip>
                  )}
                </div>
              </TableCell>
              <TableCell>
                {t(BTaskStatus[task.status])}
                {task.error && (
                  <Tooltip content={task.error} color={'danger'}>
                    <QuestionCircleOutlined />
                  </Tooltip>
                )}
                {task.reasonForUnableToStart && (
                  <Tooltip content={task.reasonForUnableToStart} color={'warning'}>
                    <QuestionCircleOutlined />
                  </Tooltip>
                )}
              </TableCell>
              <TableCell>
                <div className={'relative'}>
                  <Progress
                    color="primary"
                    size="sm"
                    value={task.percentage}
                  />
                  <div
                    className={'absolute top-0 left-0 flex items-center justify-center w-full h-full'}
                  >{task.percentage}%
                  </div>
                </div>
              </TableCell>
              <TableCell>
                {task.startedAt && dayjs(task.startedAt).format('YYYY-MM-DD HH:mm:ss')}
              </TableCell>
              <TableCell>
                {renderInterval(task)}
              </TableCell>
              <TableCell>
                {renderEnableAfter(task)}
              </TableCell>
            </TableRow>
          );
        })}
        {/* {(task) => ( */}
        {/*    */}
        {/* )} */}
      </TableBody>
    </Table>
  );

  // const renderOperations = (id, i, r) => {
  //   const elements = [];
  //   if (r.status == BackgroundTaskStatus.Running) {
  //     elements.push(<Button
  //       text
  //       type="primary"
  //       onClick={() => {
  //         StopBackgroundTask({
  //           id,
  //         }).invoke((a) => {
  //         });
  //       }}
  //     >
  //       Stop
  //     </Button>);
  //   }
  //   const opts = elements.reduce((s, t, i) => {
  //     if (i > 0) {
  //       s.push(<Divider direction="ver" />);
  //     }
  //     s.push(t);
  //     return s;
  //   }, []);
  //   return (
  //     <Box
  //       direction="row"
  //     >
  //       {opts}
  //     </Box>
  //   );
  // };

  // console.log(tasks);

  // const activeTasks = tasks.filter((t) => t.status != BackgroundTaskStatus.Running).length;
  //
  // return (
  //   <div className="background-task-page">
  //     <div className={'opt'}>
  //       <Button
  //         disabled={activeTasks.length == 0}
  //         type="normal"
  //         size={'small'}
  //         onClick={() => ClearInactiveBackgroundTasks().invoke()}
  //       >{i18n.t('Clear inactive tasks')}
  //       </Button>
  //     </div>
  //     <Table
  //       className={'tasks'}
  //       dataSource={tasks}
  //       hasBorder={false}
  //       useVirtual
  //       size={'small'}
  //       maxBodyHeight={750}
  //     >
  //       <Table.Column dataIndex={'name'} title={'Name'} width="20%" />
  //       <Table.Column dataIndex={'startDt'} title={'Start Time'} cell={(c) => moment(c).format('HH:mm:ss')} width="10%" />
  //       <Table.Column
  //         dataIndex={'startDt'}
  //         title={'Duration'}
  //         cell={(c) => {
  //           const start = moment(c);
  //           const end = moment();
  //           const diff = end.diff(start);
  //           return moment.utc(diff).format('H[h]m[m]');
  //         }}
  //         width="10%"
  //       />
  //       <Table.Column
  //         dataIndex={'status'}
  //         title={'Status'}
  //         cell={(c, i, r) => {
  //           switch (c) {
  //             case BackgroundTaskStatus.Failed:
  //             case BackgroundTaskStatus.Complete:
  //               return statusIcons[c];
  //             case BackgroundTaskStatus.Running:
  //               // return <Progress shape={'circle'} size="small" percent={r.percentage} />;
  //               return r.percentage > 0 ? <>{r.percentage}%&nbsp;{statusIcons[c]}</> : statusIcons[c];
  //           }
  //         }}
  //         width="8%"
  //       />
  //       <Table.Column dataIndex={'message'} title={'Message'} width="42%" cell={(c) => <pre>{c}</pre>} />
  //       <Table.Column
  //         width="10%"
  //         dataIndex={'id'}
  //         title={'Opt'}
  //         cell={renderOperations}
  //       />
  //     </Table>
  //   </div>
  // );
};
