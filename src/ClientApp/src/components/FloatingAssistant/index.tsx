import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { history } from 'ice';

import { useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import {
  CaretRightOutlined,
  CheckCircleOutlined,
  ClearOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  CloseOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  PauseCircleOutlined,
  PauseOutlined,
  PushpinOutlined,
  QuestionCircleOutlined,
  SettingOutlined,
  StopOutlined,
} from '@ant-design/icons';
import moment from 'moment';
import dayjs from 'dayjs';
import store from '@/store';
import { BTaskStatus } from '@/sdk/constants';
import {
  Badge,
  Button,
  Chip,
  Divider,
  Modal,
  Popover,
  Progress,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { buildLogger } from '@/components/utils';
import type { BTask } from '@/core/models/BTask';

const testTasks: BTask[] = [
  {
    id: 'Enhancement1',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 1,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
  {
    id: 'Enhancement2',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 2,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
  {
    id: 'Enhancement3',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 3,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
  {
    id: 'Enhancement4',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 4,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
  {
    id: 'Enhancement5',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 5,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
  {
    id: 'Enhancement6',
    name: 'BTask_Name_Enhancement',
    description: 'BTask_Description_Enhancement',
    percentage: 0,
    process: 'process1',
    interval: '00:02:00',
    enableAfter: '2025-03-26T11:13:24',
    status: 6,
    error: 'error1',
    messageOnInterruption: 'BTask_MessageOnInterruption_Enhancement',
    conflictWithTaskKeys: [
      'Enhancement',
    ],
    estimateRemainingTime: '01:02:03',
    startedAt: '2025-03-26T15:19:27.697964+08:00',
    reasonForUnableToStart: 'conflict with aaa',
    isPersistent: true,
    nextTimeStartAt: '2025-03-26T15:22:18.4050482+08:00',
  },
];

const AssistantStatus = {
  Idle: 0,
  Working: 1,
  AllDone: 2,
  Failed: 3,
};

enum TaskAction {
  Start = 1,
  Pause = 2,
  Resume = 3,
  Stop = 4,
  Clean = 5,
  Config = 6,
}

const ActionsFilter: Record<TaskAction, (task: BTask) => boolean> = {
  [TaskAction.Start]: task => task.isPersistent && (task.status == BTaskStatus.Cancelled || task.status == BTaskStatus.Error || task.status == BTaskStatus.Completed),
  [TaskAction.Pause]: task => task.status == BTaskStatus.Running,
  [TaskAction.Resume]: task => task.status == BTaskStatus.Paused,
  [TaskAction.Stop]: task => task.status == BTaskStatus.Running,
  [TaskAction.Clean]: task => !task.isPersistent && (task.status == BTaskStatus.Completed || task.status == BTaskStatus.Error || task.status == BTaskStatus.Cancelled),
  [TaskAction.Config]: task => task.isPersistent,
};

const log = buildLogger('FloatingAssistant');

export default () => {
  const [allDoneCircleDrawn, setAllDoneCircleDrawn] = useState('');
  const [status, setStatus] = useState(AssistantStatus.Working);
  const statusRef = useRef(status);
  const [cleaningTaskId, setCleaningTaskId] = useState<string | undefined>();
  const [tasksVisible, setTasksVisible] = useState(false);
  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();

  const portalRef = useRef<HTMLDivElement | null>(null);

  const bTasks = store.useModelState('bTasks');

  const columns = useRef<{ key: string; label: string }[]>([
    {
      key: 'name',
      label: 'Task list',
    },
    {
      key: 'status',
      label: 'Status',
    },
    {
      key: 'process',
      label: 'Process',
    },
    {
      key: 'progress',
      label: 'Progress',
    },
    {
      key: 'startedAt',
      label: 'Started at',
    },
    {
      key: 'elapsed',
      label: 'Elapsed',
    },
    {
      key: 'estimateRemainingTime',
      label: 'Estimate remaining time',
    },
    {
      key: 'nextTimeStartAt',
      label: 'Next time start at',
    },
    {
      key: 'operations',
      label: 'Operations',
    },
  ].map(x => ({
    ...x,
    label: t(x.label),
  })));

  // const bTasks = testTasks;

  useUpdateEffect(() => {
    statusRef.current = status;
  }, [status]);

  useEffect(() => {
    log('Initializing...');

    const queryTask = setInterval(() => {
      const tempTasks = store.getState().bTasks;
      let newStatus = AssistantStatus.AllDone;
      if (tempTasks.length > 0) {
        const ongoingTasks = tempTasks.filter((a) => a.status == BTaskStatus.Running);
        if (ongoingTasks.length > 0) {
          newStatus = AssistantStatus.Working;
        } else {
          const failedTasks = tempTasks.filter((a) => a.status == BTaskStatus.Error);
          if (failedTasks.length > 0) {
            newStatus = AssistantStatus.Failed;
          } else {
            const doneTasks = tempTasks.filter((a) => a.status == BTaskStatus.Completed);
            if (doneTasks.length > 0) {
              newStatus = AssistantStatus.AllDone;
            }
          }
        }
      }

      if (newStatus != statusRef.current) {
        setStatus(newStatus);
        if (newStatus == AssistantStatus.AllDone) {
          setTimeout(() => {
            setAllDoneCircleDrawn('drawn');
          }, 300);
        }
      }
    }, 100);

    return () => {
      log('Destroying...');
      clearInterval(queryTask);
    };
  }, []);

  const renderTaskStatus = (task: BTask) => {
    switch (task.status) {
      case BTaskStatus.NotStarted:
        return (
          <Chip
            variant={'light'}
            color={'warning'}
            size={'sm'}
          >
            <ClockCircleOutlined className={'text-base'} />
          </Chip>
        );
      case BTaskStatus.Cancelled:
        return (
          <Chip
            variant={'light'}
            color={'warning'}
            size={'sm'}
          >
            <CloseOutlined className={'text-base'} />
          </Chip>
        );
      case BTaskStatus.Paused:
        return (
          <Chip
            variant={'light'}
            color={'warning'}
            size={'sm'}
          >
            <PauseCircleOutlined className={'text-base'} />
          </Chip>
        );
      case BTaskStatus.Error:
        return (
          <Button
            size={'sm'}
            color={'danger'}
            variant={'light'}
            isIconOnly
            onPress={() => {
              if (task.error) {
                createPortal(Modal, {
                  defaultVisible: true,
                  size: 'xl',
                  classNames: {
                    wrapper: 'floating-assistant-modal',
                  },
                  title: t('Error'),
                  children: (
                    <pre>
                      {task.error}
                    </pre>
                  ),
                  footer: {
                    actions: ['cancel'],
                  },
                });
              }
            }}
          >
            <ExclamationCircleOutlined className={'text-base'} />
          </Button>
        );
      case BTaskStatus.Running:
        return (
          <Chip
            size={'sm'}
            variant={'light'}
          >
            <LoadingOutlined className={'text-base'} />
          </Chip>
        );
      case BTaskStatus.Completed:
        return (
          <Chip
            variant={'light'}
            color={'success'}
            size={'sm'}
          >
            <CheckCircleOutlined className={'text-base'} />
          </Chip>
        );
    }
  };

  const renderTaskOpts = (task: BTask) => {
    const opts: any[] = [];
    Object.keys(ActionsFilter).forEach((key) => {
      const filter = ActionsFilter[key];
      if (filter(task)) {
        const action: TaskAction = parseInt(key, 10) as TaskAction;
        switch (action) {
          case TaskAction.Start:
            opts.push(
              <Button
                color={'secondary'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  BApi.backgroundTask.startBackgroundTask(task.id);
                }}
              >
                <CaretRightOutlined className={'text-base'} />
              </Button>,
            );
            break;
          case TaskAction.Pause:
            opts.push(
              <Button
                color={'warning'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  BApi.backgroundTask.pauseBackgroundTask(task.id);
                }}
              >
                <PauseOutlined className={'text-base'} />
              </Button>,
            );
            break;
          case TaskAction.Resume:
            opts.push(
              <Button
                color={'secondary'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  BApi.backgroundTask.resumeBackgroundTask(task.id);
                }}
              >
                <CaretRightOutlined className={'text-base'} />
              </Button>,
            );
            break;
          case TaskAction.Stop:
            opts.push(
              <Button
                color={'danger'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t('Stopping task: {{taskName}}', { taskName: task.name }),
                    children: task.messageOnInterruption ?? t('Are you sure to stop this task?'),
                    onOk: async () => await BApi.backgroundTask.stopBackgroundTask(task.id),
                    classNames: {
                      wrapper: 'floating-assistant-modal',
                    },
                  });
                }}
              >
                <StopOutlined className={'text-base'} />
              </Button>,
            );
            break;
          case TaskAction.Clean:
            opts.push(
              <Button
                // color={'success'}
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  setCleaningTaskId(task.id);
                }}
              >
                <ClearOutlined className={'text-base'} />
              </Button>,
            );
            break;
          case TaskAction.Config:
            opts.push(
              <Button
                size={'sm'}
                isIconOnly
                variant={'light'}
                onPress={() => {
                  createPortal(
                    Modal, {
                      defaultVisible: true,
                      title: t('About to leave current page'),
                      children: t('Sure?'),
                      onOk: async () => {
                        history!.push('/backgroundtask');
                      },
                      classNames: {
                        wrapper: 'floating-assistant-modal',
                      },
                    },
                  );
                }}
              >
                <SettingOutlined className={'text-base'} />
              </Button>,
            );
            break;
        }
      }
    });
    return opts;
  };

  const renderTasks = () => {
    if (bTasks?.length > 0) {
      return (
        <Table aria-label="Example table with dynamic content" removeWrapper isStriped isCompact>
          <TableHeader columns={columns.current}>
            {(column) => <TableColumn key={column.key}>{column.label}</TableColumn>}
          </TableHeader>
          <TableBody>
            {bTasks.map(task => {
              return (
                <TableRow
                  key={task.id}
                  className={`transition-opacity ${task.id == cleaningTaskId ? 'opacity-0' : ''}`}
                  onTransitionEnd={(evt) => {
                    if (evt.propertyName == 'opacity' && task.id == cleaningTaskId) {
                      BApi.backgroundTask.cleanBackgroundTask(task.id);
                    }
                  }}
                >
                  <TableCell>
                    <div className={'flex items-center gap-1'}>
                      {task.isPersistent ? (
                        <Badge
                          isOneChar
                          variant={'faded'}
                          color="default"
                          size={'sm'}
                          content={<PushpinOutlined className={'text-xs opacity-60'} />}
                          placement="top-left"
                        >
                          &nbsp;{task.name}
                        </Badge>
                      ) : task.name}
                      {task.description && (
                        <Tooltip
                          color={'secondary'}
                          content={task.description}
                        >
                          <QuestionCircleOutlined className={'text-base opacity-60'} />
                        </Tooltip>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    {renderTaskStatus(task)}
                  </TableCell>
                  <TableCell>
                    {task.process && (
                      <Chip
                        variant={'light'}
                        color={'success'}
                      >
                        {task.process}
                      </Chip>
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
                    {task.startedAt && (
                      dayjs(task.startedAt).format('HH:mm:ss')
                    )}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      {task.elapsed && (
                        dayjs.duration(moment.duration(task.elapsed).asMilliseconds()).format('HH:mm:ss')
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    {task.estimateRemainingTime && (
                      dayjs.duration(moment.duration(task.estimateRemainingTime).asMilliseconds()).format('HH:mm:ss')
                    )}
                  </TableCell>
                  <TableCell>
                    {task.nextTimeStartAt && (
                      dayjs(task.nextTimeStartAt).format('HH:mm:ss')
                    )}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1">
                      {renderTaskOpts(task)}
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      );
    }
    return (
      <div className="h-[80px] flex items-center justify-center">
        {t('No background task')}
      </div>
    );
  };

  const clearableTasks = bTasks.filter((t) => !t.isPersistent && (t.status == BTaskStatus.Completed || t.status == BTaskStatus.Error || t.status == BTaskStatus.Cancelled));

  // log(tasks);

  return (
    <>
      <Popover
        onOpenChange={visible => {
          setTasksVisible(visible);
        }}
        trigger={(
          <div
            className={`portal ${Object.keys(AssistantStatus)[status]} floating-assistant ${tasksVisible ? '' : 'hide'}`}
            ref={portalRef}
          >
            {/* Working */}
            <div className="loader">
              <span />
            </div>
            {/* AllDone */}
            <div className="tick">
              <svg
                version="1.1"
                x="0px"
                y="0px"
                viewBox="0 0 37 37"
                enableBackground={'new 0 0 37 37'}
                // style="enable-background:new 0 0 37 37;"
                className={allDoneCircleDrawn}
              >
                <path
                  className="circ path"
                  fill={'none'}
                  stroke={'#08c29e'}
                  strokeWidth={3}
                  strokeLinejoin={'round'}
                  strokeMiterlimit={10}
                  d="M30.5,6.5L30.5,6.5c6.6,6.6,6.6,17.4,0,24l0,0c-6.6,6.6-17.4,6.6-24,0l0,0c-6.6-6.6-6.6-17.4,0-24l0,0C13.1-0.2,23.9-0.2,30.5,6.5z"
                />
                <polyline
                  className="tick path"
                  fill={'none'}
                  stroke={'#08c29e'}
                  strokeWidth={3}
                  strokeLinejoin={'round'}
                  strokeMiterlimit={10}
                  points="11.6,20 15.9,24.2 26.4,13.8"
                />
              </svg>
            </div>
            {/* Failed */}
            <div className={'failed flex items-center justify-center w-[48px] h-[48px] '}>
              <CloseCircleOutlined className={'text-5xl'} />
            </div>
          </div>
        )}
      >
        <div className={'flex flex-col gap-2 p-2 min-w-[300px]'}>
          {/* <div className={'font-bold'}>{t('Task list')}</div> */}
          {/* <Divider orientation={'horizontal'} /> */}
          <div className="flex flex-col gap-1 max-h-[600px] mt-2 overflow-auto">
            {renderTasks()}
          </div>
          <Divider orientation={'horizontal'} />
          <div className="flex items-center gap-2">
            {clearableTasks.length > 0 && (
              <Button
                size={'sm'}
                variant={'ghost'}
                onPress={() => BApi.backgroundTask.cleanInactiveBackgroundTasks()}
              >
                <ClearOutlined className={'text-base'} />
                {t('Clear inactive tasks')}
              </Button>
            )}
          </div>
        </div>
      </Popover>
    </>
  );
};
