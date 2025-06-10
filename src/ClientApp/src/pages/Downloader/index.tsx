import React, { useCallback, useEffect, useRef, useState } from 'react';

import './index.scss';
import { Balloon, Dialog } from '@alifd/next';
import moment from 'moment';
import { Axis, Chart, Interval, Legend, Tooltip } from 'bizcharts';
import { ControlledMenu, MenuItem, useMenuState } from '@szhsin/react-menu';
import { useUpdate, useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import {
  AiOutlineDelete,
  AiOutlineEdit,
  AiOutlineEllipsis,
  AiOutlineExport,
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
  AiOutlinePlusCircle,
  AiOutlineRedo,
  AiOutlineSearch,
  AiOutlineSetting,
  AiOutlineStop,
  AiOutlineWarning,
} from 'react-icons/ai';
import TaskDetailModal from './components/TaskDetailModal';
import type { ChipProps, CircularProgressProps } from '@/components/bakaui';
import {
  Button,
  Checkbox,
  Chip,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Input,
  Listbox,
  ListboxItem,
  Modal,
  Progress,
} from '@/components/bakaui';
import CustomIcon from '@/components/CustomIcon';
import '@szhsin/react-menu/dist/index.css';
import '@szhsin/react-menu/dist/transitions/slide.css';

import TaskCreation from '@/pages/Downloader/components/TaskDetail';
import { GetAllThirdPartyRequestStatistics } from '@/sdk/apis';
import {
  bilibiliDownloadTaskTypes,
  DownloadTaskAction,
  DownloadTaskActionOnConflict,
  DownloadTaskDtoStatus,
  downloadTaskDtoStatuses,
  ExHentaiDownloadTaskType,
  exHentaiDownloadTaskTypes,
  pixivDownloadTaskTypes,
  ResponseCode,
  ThirdPartyId,
  thirdPartyIds,
  ThirdPartyRequestResultType,
} from '@/sdk/constants';
import Configurations from '@/pages/Downloader/components/Configurations';
import BApi from '@/sdk/BApi';
import { buildLogger, useTraceUpdate } from '@/components/utils';
import SimpleLabel from '@/components/SimpleLabel';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import ThirdPartyIcon from '@/components/ThirdPartyIcon';
import type { DownloadTask } from '@/core/models/DownloadTask';

const testTasks: DownloadTask[] = [
  {
    key: '123121232312321321',
    thirdPartyId: ThirdPartyId.Bilibili,
    name: 'eeeeeeee',
    progress: 80,
    status: DownloadTaskDtoStatus.Downloading,
  },
  {
    key: 'cxzkocnmaqwkodn wkjodas1',
    name: 'pppppppppppp',
    progress: 30,
    status: DownloadTaskDtoStatus.Failed,
    message: 'dawsdasda',
  },
];

const DownloadTaskDtoStatusIceLabelStatusMap: Record<DownloadTaskDtoStatus, ChipProps['color']> = {
  [DownloadTaskDtoStatus.Idle]: 'default',
  [DownloadTaskDtoStatus.InQueue]: 'default',
  [DownloadTaskDtoStatus.Downloading]: 'primary',
  [DownloadTaskDtoStatus.Failed]: 'danger',
  [DownloadTaskDtoStatus.Complete]: 'success',
  [DownloadTaskDtoStatus.Starting]: 'warning',
  [DownloadTaskDtoStatus.Stopping]: 'warning',
  [DownloadTaskDtoStatus.Disabled]: 'default',
};

const DownloadTaskDtoStatusProgressBarColorMap: Record<DownloadTaskDtoStatus, CircularProgressProps['color']> = {
  [DownloadTaskDtoStatus.Idle]: 'default',
  [DownloadTaskDtoStatus.InQueue]: 'default',
  [DownloadTaskDtoStatus.Downloading]: 'primary',
  [DownloadTaskDtoStatus.Failed]: 'danger',
  [DownloadTaskDtoStatus.Complete]: 'success',
  [DownloadTaskDtoStatus.Starting]: 'warning',
  [DownloadTaskDtoStatus.Stopping]: 'warning',
  [DownloadTaskDtoStatus.Disabled]: 'default',
};

const RequestResultTypeIntervalColorMap = {
  [ThirdPartyRequestResultType.Succeed]: '#46bc15',
  [ThirdPartyRequestResultType.Failed]: '#ff3000',
  [ThirdPartyRequestResultType.Banned]: '#993300',
  [ThirdPartyRequestResultType.Canceled]: '#666',
  [ThirdPartyRequestResultType.TimedOut]: '#ff9300',
};

enum SelectionMode {
  Default,
  Ctrl,
  Shift,
}

type SearchForm = {
  statuses?: DownloadTaskDtoStatus[];
  keyword?: string;
  thirdPartyIds?: ThirdPartyId[];
};

const log = buildLogger('DownloadPage');

export default () => {
  const { t } = useTranslation();
  const [taskId, setTaskId] = useState<number | undefined>(undefined);
  const forceUpdate = useUpdate();
  const [form, setForm] = useState<SearchForm>({});
  const [configurationsVisible, setConfigurationsVisible] = useState(false);

  const gettingRequestStatistics = useRef(false);
  const [requestStatistics, setRequestStatistics] = useState([]);
  const requestStatisticsRef = useRef(requestStatistics);
  const [requestStatisticsChartVisible, setRequestStatisticsChartVisible] = useState(false);

  // const tasks = store.useModelState('downloadTasks');
  const tasks = testTasks;

  const [selectedTaskIds, setSelectedTaskIds] = useState<number[]>([]);
  const selectedTaskIdsRef = useRef(selectedTaskIds);
  const selectionModeRef = useRef(SelectionMode.Default);

  const tasksRef = useRef(tasks);

  const [menuProps, toggleMenu] = useMenuState();
  const { createPortal } = useBakabaseContext();

  useTraceUpdate({
    taskId,
    form,
    configurationsVisible,
    requestStatistics,
    requestStatisticsChartVisible,
    tasks,
    selectedTaskIds,
    menuProps,
  }, 'DownloaderPage');

  log('Rendering');

  const startTasksManually = async (ids: number[], actionOnConflict = DownloadTaskActionOnConflict.NotSet) => {
    const rsp = await BApi.downloadTask.startDownloadTasks({
      ids,
      actionOnConflict,
    }, {
      // @ts-ignore
      ignoreError: rsp => rsp.code == ResponseCode.Conflict,
    });
    if (rsp.code == ResponseCode.Conflict) {
      Dialog.show({
        title: t('Found some conflicted tasks'),
        content: rsp.message,
        v2: true,
        width: 'auto',
        closeMode: ['mask', 'esc', 'close'],
        okProps: {
          children: t('Download selected tasks firstly'),
        },
        cancelProps: {
          children: t('Add selected tasks to the queue'),
        },
        onOk: async () => {
          return await BApi.downloadTask.startDownloadTasks({
            ids,
            actionOnConflict: DownloadTaskActionOnConflict.StopOthers,
          });
        },
        onCancel: async () => await BApi.downloadTask.startDownloadTasks({
          ids,
          actionOnConflict: DownloadTaskActionOnConflict.Ignore,
        }),
      });
    }
  };

  useUpdateEffect(() => {
    requestStatisticsRef.current = requestStatistics;
  }, [requestStatistics]);

  useUpdateEffect(() => {
    selectedTaskIdsRef.current = selectedTaskIds;
  }, [selectedTaskIds]);
  const contextMenuAnchorPointRef = useRef({
    x: 0,
    y: 0,
  });

  const renderContextMenu = useCallback(() => {
    if (selectedTaskIdsRef.current.length == 0) {
      return;
    }

    const moreThanOne = selectedTaskIdsRef.current.length > 1;

    return (
      <ControlledMenu
        {...menuProps}
        anchorPoint={contextMenuAnchorPointRef.current}
        className={'downloader-page-context-menu'}
        onClose={() => {
          toggleMenu(false);
        }}
      >
        <MenuItem onClick={() => {
          startTasksManually(selectedTaskIdsRef.current);
        }}
        >
          <div>
            <CustomIcon
              type={'play-circle'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Start')}
          </div>
        </MenuItem>
        <MenuItem onClick={() => BApi.downloadTask.stopDownloadTasks(selectedTaskIdsRef.current)}>
          <div>
            <CustomIcon
              type={'timeout'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Stop')}
          </div>
        </MenuItem>
        <MenuItem onClick={() => {
          createPortal(Modal, {
            defaultVisible: true,
            title: t('Deleting {{count}} download tasks', { count: selectedTaskIdsRef.current.length }),
            onOk: async () => {
              await BApi.downloadTask.deleteDownloadTasks({ ids: selectedTaskIdsRef.current });
            },
          });
        }}
        >
          <div className={'danger'}>
            <CustomIcon
              type={'delete'}
            />
            {moreThanOne && (
              <>
                {t('Bulk')}
                &nbsp;
              </>
            )}
            {t('Delete')}
          </div>
        </MenuItem>
      </ControlledMenu>
    );
  }, [menuProps]);

  useEffect(() => {
    tasksRef.current = tasks;
  }, [tasks]);

  useEffect(() => {
    const getRequestStatisticsInterval = setInterval(() => {
      if (!gettingRequestStatistics.current) {
        gettingRequestStatistics.current = true;
        GetAllThirdPartyRequestStatistics()
          .invoke((a) => {
            if (JSON.stringify(a.data) != JSON.stringify(requestStatisticsRef.current)) {
              setRequestStatistics(a.data);
            }
          })
          .finally(() => {
            gettingRequestStatistics.current = false;
          });
      }
    }, 1000);

    const onMouseDown = (e) => {
      // console.log(e.target, clearTaskSelectionTargetsRef.current);
    };

    const onKeydown = (e: KeyboardEvent) => {
      // if (equalsOrIsChildOf(e.target as HTMLElement, tasksDomRef.current)) {
      switch (e.key) {
        case 'Control':
          selectionModeRef.current = SelectionMode.Ctrl;
          break;
        case 'Shift':
          selectionModeRef.current = SelectionMode.Shift;
          break;
        case 'Escape':
          setSelectedTaskIds([]);
          break;
        case 'a':
          if (e.ctrlKey) {
            e.stopPropagation();
            e.preventDefault();
            setSelectedTaskIds(tasksRef.current?.map((t) => t.id) || []);
          }
          break;
      }
      // }
    };

    const onKeyUp = (e) => {
      switch (e.key) {
        case 'Control':
          selectionModeRef.current = SelectionMode.Default;
          break;
        case 'Shift':
          selectionModeRef.current = SelectionMode.Default;
          break;
      }
    };

    window.addEventListener('keydown', onKeydown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('mousedown', onMouseDown);

    return () => {
      window.removeEventListener('keydown', onKeydown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('mousedown', onMouseDown);
      clearInterval(getRequestStatisticsInterval);
    };
  }, []);

  console.log(selectedTaskIdsRef.current, SelectionMode[selectionModeRef.current]);

  const onTaskClick = taskId => {
    console.log(SelectionMode[selectionModeRef.current]);
    switch (selectionModeRef.current) {
      case SelectionMode.Default:
        if (selectedTaskIdsRef.current.includes(taskId) && selectedTaskIdsRef.current.length == 1) {
          setSelectedTaskIds([]);
        } else {
          setSelectedTaskIds([taskId]);
        }
        break;
      case SelectionMode.Ctrl:
        if (selectedTaskIdsRef.current.includes(taskId)) {
          setSelectedTaskIds(selectedTaskIdsRef.current.filter((id) => id != taskId));
        } else {
          setSelectedTaskIds([...selectedTaskIdsRef.current, taskId]);
        }
        break;
      case SelectionMode.Shift:
        if (selectedTaskIdsRef.current.length == 0) {
          setSelectedTaskIds([taskId]);
        } else {
          const lastSelectedTaskId = selectedTaskIdsRef.current[selectedTaskIds.length - 1];
          const lastSelectedTaskIndex = tasks.findIndex((t) => t.id == lastSelectedTaskId);
          const currentTaskIndex = tasks.findIndex((t) => t.id == taskId);
          const start = Math.min(lastSelectedTaskIndex, currentTaskIndex);
          const end = Math.max(lastSelectedTaskIndex, currentTaskIndex);
          setSelectedTaskIds(tasks.slice(start, end + 1)
            .map((t) => t.id));
        }
        break;
    }
  };

  const renderTaskName = (task) => {
    let types;
    let taskNameShouldBeTranslated = false;
    switch (task.thirdPartyId) {
      case ThirdPartyId.ExHentai:
        types = exHentaiDownloadTaskTypes;
        if (task.type == ExHentaiDownloadTaskType.Watched) {
          taskNameShouldBeTranslated = true;
        }
        break;
      case ThirdPartyId.Pixiv:
        types = pixivDownloadTaskTypes;
        break;
      case ThirdPartyId.Bilibili:
        types = bilibiliDownloadTaskTypes;
        break;
    }

    const type = types?.find((t) => t.value == task.type).label || 'Unknown';

    const displayTaskName = (taskNameShouldBeTranslated ? t(task.name) : task.name) ?? task.key;

    if (displayTaskName) {
      return `${t(type)}: ${displayTaskName ?? task.key}`;
    } else {
      return t(type);
    }
  };

  const taskFilters: ((task: any) => boolean)[] = [];
  if (form.thirdPartyIds && form.thirdPartyIds.length > 0) {
    taskFilters.push(t => form.thirdPartyIds!.includes(t.thirdPartyId));
  }
  if (form.statuses && form.statuses.length > 0) {
    taskFilters.push(t => form.statuses!.includes(t.status));
  }

  if (form.keyword != undefined && form.keyword.length > 0) {
    const lowerCaseKeyword = form.keyword.toLowerCase();
    taskFilters.push(t => t.name?.toLowerCase().includes(lowerCaseKeyword) || t.key.toLowerCase().includes(lowerCaseKeyword));
  }

  const renderRequestStatisticsChart = () => {
    if (requestStatisticsChartVisible) {
      const thirdPartyRequestCounts = (requestStatistics || []).reduce<any[]>((s, t) => {
        Object.keys(t.counts || {})
          .forEach((r) => {
            s.push({
              id: t.id.toString(),
              name: ThirdPartyId[t.id],
              result: ThirdPartyRequestResultType[r],
              count: t.counts[r],
            });
          });
        return s;
      }, []);

      // console.log(dv.rows, data, thirdPartyRequestCounts);

      return (
        <Dialog
          visible
          onClose={() => setRequestStatisticsChartVisible(false)}
          onCancel={() => setRequestStatisticsChartVisible(false)}
          onOk={() => setRequestStatisticsChartVisible(false)}
          closeable
          footerActions={['ok']}
          title={t('Requests overview')}
          style={{ minWidth: 800 }}
        >
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
                (result) => RequestResultTypeIntervalColorMap[ThirdPartyRequestResultType[result]],
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
            <Tooltip
              shared
              title={'name'}
            />
            <Legend name={'result'} />
          </Chart>
        </Dialog>
      );
    } else {

    }
  };

  console.log(form);

  const filteredTasks = tasks.filter(x => taskFilters.every(f => f(x)));

  return (
    <div>
      {renderRequestStatisticsChart()}
      {renderContextMenu()}
      {configurationsVisible && (
        <Configurations
          onSaved={() => {
            setConfigurationsVisible(false);
          }}
          onClose={() => {
            setConfigurationsVisible(false);
          }}
        />
      )}
      <div className="grid gap-4 items-center" style={{ gridTemplateColumns: 'auto 1fr' }}>
        <div>{t('Source')}</div>
        <div className="flex items-center gap-4">
          {thirdPartyIds.map((s) => {
            const count = tasks.filter((t) => t.thirdPartyId == s.value).length;
            return (
              <Checkbox
                // disabled={count == 0}
                key={s.value}
                onValueChange={(checked) => {
                  let thirdPartyIds = form.thirdPartyIds || [];
                  if (checked) {
                    if (thirdPartyIds.every((a) => a != s.value)) {
                      thirdPartyIds.push(s.value);
                    }
                  } else {
                    thirdPartyIds = thirdPartyIds.filter((a) => a != s.value);
                  }
                  setForm({
                    ...form,
                    thirdPartyIds,
                  });
                }}
                size={'sm'}
                isSelected={form.thirdPartyIds?.some((a) => a == s.value)}
              >
                <div className={'flex items-center gap-1'}>
                  <ThirdPartyIcon thirdPartyId={s.value} />
                  <span>{s.label}{count > 0 && <span className={'count'}>({count})</span>}</span>
                </div>
              </Checkbox>
            );
          })}
        </div>
        <div>{t('Status')}</div>
        <div className="flex items-center gap-4">
          {downloadTaskDtoStatuses.map(((s) => {
    const count = tasks.filter((t) => t.status == s.value).length;
    const color = DownloadTaskDtoStatusIceLabelStatusMap[s.value];
    return (
      <Checkbox
        color={color}
        // disabled={count == 0}
        key={s.value}
        onValueChange={(checked) => {
          let statuses = form.statuses || [];
          if (checked) {
            if (statuses.every((a) => a != s.value)) {
              statuses.push(s.value);
            }
          } else {
            statuses = statuses.filter((a) => a != s.value);
          }
          setForm({
            ...form,
            statuses,
          });
        }}
        size={'sm'}
        isSelected={form.statuses?.some((a) => a == s.value)}
      >
        <Chip
          // size={'sm'}
          color={color}
          variant={'light'}
          classNames={{
            base: 'pl-0',
            content: 'pl-0',
          }}
        >
          {t(s.label)}{count > 0 && <span className={'count'}>({count})</span>}
        </Chip>
      </Checkbox>
    );
  }))}
        </div>
        <div>{t('Keyword')}</div>
        <div>
          <Input
            className={'w-[320px]'}
            fullWidth={false}
            startContent={<AiOutlineSearch className={'text-medium'} />}
            size={'sm'}
            onValueChange={keyword => setForm({
      ...form,
      keyword,
    })}
          />
        </div>
      </div>
      <div className="flex items-center justify-between gap-2 mt-2">
        <div className="flex items-center gap-1">
          <Button
            color={'primary'}
            size={'small'}
            onPress={() => {
              createPortal(TaskDetailModal, {

              });
      }}
          >
            <>
              <AiOutlinePlusCircle className={'text-medium'} />
              {t('Create task')}
            </>
          </Button>
          <Button
            color={'success'}
            size={'small'}
            variant={'flat'}
            onPress={() => {
        startTasksManually(undefined, DownloadTaskActionOnConflict.Ignore);
      }}
          >
            <AiOutlinePlayCircle className={'text-medium'} />
            {t('Start all')}
          </Button>
          <Button
            color={'warning'}
            size={'small'}
            variant={'flat'}
            onPress={() => {
        BApi.downloadTask.stopDownloadTasks([]);
      }}
          >
            <AiOutlineStop className={'text-medium'} />
            {t('Stop all')}
          </Button>

          {tasks?.length > 0 && (
          <div
            className="request-overview"
            onClick={() => {
          setRequestStatisticsChartVisible(true);
        }}
          >
            <div className={'title'}>{t('Requests overview')}</div>
            {requestStatistics?.map((rs) => {
          let successCount = 0;
          let failureCount = 0;
          Object.keys(rs.counts || {})
            .forEach((r) => {
              switch (parseInt(r)) {
                case ThirdPartyRequestResultType.Succeed:
                  successCount += rs.counts[r];
                  break;
                default:
                  failureCount += rs.counts[r];
                  break;
              }
            });
          return (
            <div className={'third-party'}>
              <SimpleLabel status={'info'}>
                {ThirdPartyId[rs.id]}
              </SimpleLabel>
              <div className={'statistics'}>
                <Balloon.Tooltip
                  trigger={(
                    <span className={'success-count'}>{successCount}</span>
                  )}
                  align={'t'}
                >
                  {t('Success')}
                </Balloon.Tooltip>
                /
                <Balloon.Tooltip
                  trigger={(
                    <span className={'failure-count'}>{failureCount}</span>
                  )}
                  align={'t'}
                >
                  {t('failure')}
                </Balloon.Tooltip>
              </div>
            </div>
          );
        })}
          </div>
    )}
        </div>
        <div className="flex items-center gap-1">
          <Button
            size={'sm'}
            onPress={() => {
        BApi.downloadTask.exportAllDownloadTasks();
      }}
            variant={'flat'}
          >
            <AiOutlineExport className={'text-medium'} />
            {t('Export all tasks')}
          </Button>
          <Button
            color={'secondary'}
            size={'small'}
            variant={'flat'}
            onPress={() => {
        setConfigurationsVisible(true);
      }}
          >
            <AiOutlineSetting className={'text-medium'} />
            {t('Configurations')}
          </Button>
        </div>
      </div>
      <Listbox
        variant={'flat'}
        isVirtualized
  // className="max-w-xs"
        label={'Select from 1000 items'}
        virtualization={{
    maxListboxHeight: 400,
    itemHeight: 75,
  }}
        selectionMode={'multiple'}
      >
        {filteredTasks.map((task, index) => {
    const hasErrorMessage = task.status == DownloadTaskDtoStatus.Failed && task.message;
    const selected = selectedTaskIds.indexOf(task.id) > -1;
    return (
      <ListboxItem key={index}>
        <div
          key={task.id}
          onContextMenu={e => {
            console.log(`Opening context menu from ${task.id}:${task.name}`);
            e.preventDefault();
            if (!selectedTaskIdsRef.current.includes(task.id)) {
              setSelectedTaskIds([task.id]);
            }
            contextMenuAnchorPointRef.current = {
              x: e.clientX,
              y: e.clientY,
            };
            toggleMenu(true);
            forceUpdate();
          }}
          // className={`${selected ? 'selected' : ''}`}
          className={'flex flex-col gap-1'}
          // style={style}
          onClick={() => onTaskClick(task.id)}
        >
          <div className={'flex items-center justify-between'}>
            <div className={'flex flex-col gap-1'}>
              <div className={'flex items-center gap-1'}>
                <ThirdPartyIcon thirdPartyId={task.thirdPartyId} />
                <span className={'text-lg'}>{task.name ?? task.key}</span>
              </div>
              <div className={'flex items-center gap-1'}>
                <span className={'opacity-60'}>{task.name && task.key}</span>
                <Chip
                  size={'sm'}
                  color={DownloadTaskDtoStatusIceLabelStatusMap[task.status]}
                  variant={'light'}
                >
                  {t(DownloadTaskDtoStatus[task.status])}
                  {task.current}
                </Chip>
                {task.status == DownloadTaskDtoStatus.Failed && (
                  <Button
                    size={'sm'}
                    color={'danger'}
                    variant={'light'}
                    onPress={() => {
                      if (hasErrorMessage) {
                        createPortal(Modal, {
                          defaultVisible: true,
                          size: 'xl',
                          title: t('Error'),
                          children: (
                            <pre>{task.message}</pre>
                          ),
                        });
                      }
                    }}
                    isIconOnly
                  >
                    <AiOutlineWarning className={'text-medium'} />
                    {task.failureTimes}
                  </Button>
                )}
                {task.nextStartDt && (
                  <Chip
                    size={'sm'}
                    color={'default'}
                  >
                    {t('Next start time')}:
                    {moment(task.nextStartDt)
                      .format('YYYY-MM-DD HH:mm:ss')}
                  </Chip>
                )}
              </div>
            </div>
            <div className={'flex items-center'}>
              {task.availableActions?.map((a, i) => {
                const action = parseInt(a, 10);
                switch (action) {
                  case DownloadTaskAction.StartManually:
                  case DownloadTaskAction.Restart:
                    return (
                      <Button
                        variant={'light'}
                        size={'sm'}
                        isIconOnly
                        onPress={() => {
                          startTasksManually([task.id]);
                        }}
                      >
                        {a == DownloadTaskAction.Restart ? (
                          <AiOutlineRedo className={'text-lg'} />
                        ) : (
                          <AiOutlinePlayCircle className={'text-lg'} />
                        )}
                      </Button>
                    );
                  case DownloadTaskAction.Disable:
                    return (
                      <Button
                        variant={'light'}
                        size={'sm'}
                        isIconOnly
                        onPress={() => {
                          BApi.downloadTask.stopDownloadTasks([task.id]);
                        }}
                      >
                        <AiOutlineStop className={'text-lg'} />
                      </Button>
                    );
                }
                return;
              })}
              <Button
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  createPortal(TaskDetailModal, {
                    id: task.id,
                  });
                }}
              >
                <AiOutlineEdit className={'text-lg'} />
              </Button>
              <Button
                variant={'light'}
                size={'sm'}
                isIconOnly
                onPress={() => {
                  BApi.tool.openFileOrDirectory({ path: task.downloadPath });
                }}
              >
                <AiOutlineFolderOpen className={'text-lg'} />
              </Button>
              <Dropdown>
                <DropdownTrigger>
                  <Button
                    variant={'light'}
                    size={'sm'}
                    isIconOnly
                  >
                    <AiOutlineEllipsis className={'text-lg'} />
                  </Button>
                </DropdownTrigger>
                <DropdownMenu onAction={key => {
                  switch (key as string) {
                    case 'delete':
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t('Are you sure to delete it?'),
                        onOk: () => BApi.downloadTask.deleteDownloadTasks({ ids: [task.id] }),
                      });
                      break;
                  }
                }}
                >
                  <DropdownItem
                    key="delete"
                    startContent={<AiOutlineDelete className={'text-lg'} />}
                    color={'danger'}
                  >
                    {t('Delete')}
                  </DropdownItem>
                </DropdownMenu>
              </Dropdown>
            </div>
          </div>
          <div className="progress">
            <Progress
              // value={task.progress}
              color={DownloadTaskDtoStatusProgressBarColorMap[task.status]}
              size={'sm'}
              // textRender={() => `${task.progress?.toFixed(2)}%`}
              // progressive={t.status != DownloadTaskStatus.Failed}
            />
          </div>
          {/* <CircularProgress */}
          {/*   value={task.progress} */}
          {/*   color={DownloadTaskDtoStatusProgressBarColorMap[task.status]} */}
          {/*   size={'sm'} */}
          {/* /> */}
        </div>
      </ListboxItem>
    );
  })}
      </Listbox>
      {/* {tasks?.length > 0 ? ( */
}
      {/*   <div */
}
      {/*     className={'tasks'} */
}
      {/*   > */
}
      {/*     <AutoSizer> */
}
      {/*       {({ */
}
      {/*         width, */
}
      {/*         height, */
}
      {/*       }) => ( */
}
      {/*         <List */
}
      {/*         // onScroll={onChildScroll} */
}
      {/*         // isScrolling={isScrolling} */
}
      {/*         // scrollTop={scrollTop} */
}
      {/*           overscanRowCount={2} */
}
      {/*         // scrollToIndex={scrollToIndex} */
}
      {/*           width={width} */
}
      {/*           height={height} */
}
      {/*         // autoHeight */
}
      {/*           rowCount={filteredTasks.length} */
}
      {/*           rowHeight={75} */
}
      {/*           rowRenderer={({ */
}
      {/*                         index, */
}
      {/*                         style, */
}
      {/*                         isVisible, */
}
      {/*                         isScrolling, */
}
      {/*                       }) => { */
}
      {/*           const task = filteredTasks[index]; */
}
      {/*           const hasErrorMessage = task.status == DownloadTaskDtoStatus.Failed && task.message; */
}
      {/*           const selected = selectedTaskIds.indexOf(task.id) > -1; */
}
      {/*           return ( */
}
      {/*             <div */
}
      {/*               key={task.id} */
}
      {/*               onContextMenu={e => { */
}
      {/*                 console.log(`Opening context menu from ${task.id}:${task.name}`); */
}
      {/*                 e.preventDefault(); */
}
      {/*                 if (!selectedTaskIdsRef.current.includes(task.id)) { */
}
      {/*                   setSelectedTaskIds([task.id]); */
}
      {/*                 } */
}
      {/*                 contextMenuAnchorPointRef.current = { */
}
      {/*                   x: e.clientX, */
}
      {/*                   y: e.clientY, */
}
      {/*                 }; */
}
      {/*                 toggleMenu(true); */
}
      {/*                 forceUpdate(); */
}
      {/*               }} */
}
      {/*               className={`download-item ${selected ? 'selected' : ''}`} */
}
      {/*               style={style} */
}
      {/*               onClick={() => onTaskClick(task.id)} */
}
      {/*             > */
}
      {/*               <div className="icon"> */
}
      {/*                 <img className={'max-w-[32px] max-h-[32px]'} src={NameIcon[task.thirdPartyId]} /> */
}
      {/*               </div> */
}
      {/*               <div className="content"> */
}
      {/*                 <div className="name"> */
}
      {/*                   <Balloon.Tooltip */
}
      {/*                     trigger={( */
}
      {/*                       <span onClick={() => { */
}
      {/*                         setTaskId(task.id); */
}
      {/*                       }} */
}
      {/*                       > */
}
      {/*                         {renderTaskName(task)} */
}
      {/*                       </span> */
}
      {/*                     )} */
}
      {/*                     triggerType={'hover'} */
}
      {/*                     align={'t'} */
}
      {/*                   > */
}
      {/*                     {task.key} */
}
      {/*                   </Balloon.Tooltip> */
}
      {/*                 </div> */
}
      {/*                 <div className="info"> */
}
      {/*                   <div className="left"> */
}
      {/*                     <SimpleLabel */
}
      {/*                       status={DownloadTaskDtoStatusIceLabelStatusMap[task.status]} */
}
      {/*                       className={hasErrorMessage ? 'has-error-message' : ''} */
}
      {/*                     > */
}
      {/*                       <span */
}
      {/*                         onClick={() => { */
}
      {/*                             if (hasErrorMessage) { */
}
      {/*                               Dialog.error({ */
}
      {/*                                 v2: true, */
}
      {/*                                 width: 1000, */
}
      {/*                                 title: t('Error'), */
}
      {/*                                 content: ( */
}
      {/*                                   <pre className={'select-text'}>{task.message}</pre> */
}
      {/*                                 ), */
}
      {/*                               }); */
}
      {/*                             } */
}
      {/*                           }} */
}
      {/*                       > */
}
      {/*                         {t(DownloadTaskDtoStatus[task.status])} */
}
      {/*                       </span> */
}
      {/*                     </SimpleLabel> */
}
      {/*                     {(task.status == DownloadTaskDtoStatus.Downloading || task.status == DownloadTaskDtoStatus.Starting || task.status == DownloadTaskDtoStatus.Stopping) && ( */
}
      {/*                       <Icon type={'loading'} size={'small'} /> */
}
      {/*                     )} */
}
      {/*                     <span>{task.current}</span> */
}
      {/*                   </div> */
}
      {/*                   <div className="right"> */
}
      {/*                     {task.failureTimes > 0 && ( */
}
      {/*                       <SimpleLabel */
}
      {/*                         status={'danger'} */
}
      {/*                         className={'failure-times'} */
}
      {/*                       > */
}
      {/*                         {t('Failure times')}: */
}
      {/*                         <span>{task.failureTimes}</span> */
}
      {/*                       </SimpleLabel> */
}
      {/*                     )} */
}
      {/*                     {task.nextStartDt && ( */
}
      {/*                       <SimpleLabel */
}
      {/*                         status={'info'} */
}
      {/*                         className={'next-start-dt'} */
}
      {/*                       > */
}
      {/*                         {t('Next start time')}: */
}
      {/*                         <span> */
}
      {/*                           {moment(task.nextStartDt) */
}
      {/*                               .format('YYYY-MM-DD HH:mm:ss')} */
}
      {/*                         </span> */
}
      {/*                       </SimpleLabel> */
}
      {/*                     )} */
}
      {/*                   </div> */
}
      {/*                 </div> */
}
      {/*                 <div className="progress"> */
}
      {/*                   <Progress */
}
      {/*                     // state={t.status == DownloadTaskStatus.Failed ? 'error' : 'normal'} */
}
      {/*                     className={'bar'} */
}
      {/*                     percent={task.progress} */
}
      {/*                     color={DownloadTaskDtoStatusProgressBarColorMap[task.status]} */
}
      {/*                     size={'small'} */
}
      {/*                     textRender={() => `${task.progress.toFixed(2)}%`} */
}
      {/*                     // progressive={t.status != DownloadTaskStatus.Failed} */
}
      {/*                   /> */
}
      {/*                 </div> */
}
      {/*               </div> */
}
      {/*               <div className="opt"> */
}
      {/*                 {task.availableActions?.map((a, i) => { */
}
      {/*                   const action = parseInt(a); */
}
      {/*                   switch (action) { */
}
      {/*                     case DownloadTaskAction.StartManually: */
}
      {/*                     case DownloadTaskAction.Restart: */
}
      {/*                       return ( */
}
      {/*                         <CustomIcon */
}
      {/*                           key={i} */
}
      {/*                           type={a == DownloadTaskAction.Restart ? 'redo' : 'play_fill'} */
}
      {/*                           title={t('Start now')} */
}
      {/*                           onClick={() => { */
}
      {/*                             startTasksManually([task.id]); */
}
      {/*                           }} */
}
      {/*                         /> */
}
      {/*                       ); */
}
      {/*                     case DownloadTaskAction.Disable: */
}
      {/*                       return ( */
}
      {/*                         <CustomIcon */
}
      {/*                           key={i} */
}
      {/*                           type={'stop'} */
}
      {/*                           title={t('Disable')} */
}
      {/*                           onClick={() => { */
}
      {/*                             BApi.downloadTask.stopDownloadTasks([task.id]); */
}
      {/*                           }} */
}
      {/*                         /> */
}
      {/*                       ); */
}
      {/*                   } */
}
      {/*                   return; */
}
      {/*                 })} */
}
      {/*                 <CustomIcon */
}
      {/*                   type={'folder-open'} */
}
      {/*                   title={t('Open folder')} */
}
      {/*                   onClick={() => { */
}
      {/*                     OpenFileOrDirectory({ */
}
      {/*                       path: task.downloadPath, */
}
      {/*                     }) */
}
      {/*                       .invoke(); */
}
      {/*                   }} */
}
      {/*                 /> */
}
      {/*                 <Dropdown */
}
      {/*                   className={'task-operations-dropdown'} */
}
      {/*                   trigger={ */
}
      {/*                     <CustomIcon */
}
      {/*                       type={'ellipsis'} */
}
      {/*                     /> */
}
      {/*                   } */
}
      {/*                   triggerType={['click']} */
}
      {/*                 > */
}
      {/*                   <Menu> */
}
      {/*                     /!* <Menu.Item title={t(t.status == DownloadTaskStatus.Paused ? 'Click to enable' : 'Click to disable')}> *!/ */
}
      {/*                     /!*   <div className={t.status == DownloadTaskStatus.Paused ? 'disabled' : 'enabled'}> *!/ */
}
      {/*                     /!*     <CustomIcon *!/ */
}
      {/*                     /!*       type={t.status == DownloadTaskStatus.Paused ? 'close-circle' : 'check-circle'} *!/ */
}
      {/*                     /!*       onClick={() => { *!/ */
}

      {/*                     /!*       }} *!/ */
}
      {/*                     /!*     /> *!/ */
}
      {/*                     /!*     {t(t.status == DownloadTaskStatus.Paused ? 'Disabled' : 'Enabled')} *!/ */
}
      {/*                     /!*   </div> *!/ */
}
      {/*                     /!* </Menu.Item> *!/ */
}
      {/*                     <Menu.Item> */
}
      {/*                       <div */
}
      {/*                         className={'remove'} */
}
      {/*                         onClick={() => { */
}
      {/*                           Dialog.confirm({ */
}
      {/*                             title: t('Are you sure to delete it?'), */
}
      {/*                             onOk: () => BApi.downloadTask.deleteDownloadTasks({ ids: [task.id] }), */
}
      {/*                           }); */
}
      {/*                         }} */
}
      {/*                       > */
}
      {/*                         <CustomIcon type={'delete'} /> */
}
      {/*                         {t('Remove')} */
}
      {/*                       </div> */
}
      {/*                     </Menu.Item> */
}
      {/*                   </Menu> */
}
      {/*                 </Dropdown> */
}
      {/*               </div> */
}
      {/*             </div> */
}
      {/*           ); */
}
      {/*         }} */
}
      {/*         /> */
}
      {/*     )} */
}
      {/*     </AutoSizer> */
}
      {/*     /!* )} *!/ */
}
      {/*     /!* </WindowScroller> *!/ */
}
      {/*   </div> */
}

      {/* ) : ( */
}
      {/*   <div className={'no-task-yet'}> */
}
      {/*     <Button */
}
      {/*       color={'primary'} */
}
      {/*       size={'large'} */
}
      {/*       onPress={() => { */
}
      {/*         setTaskId(0); */
}
      {/*       }} */
}
      {/*     > */
}
      {/*       {t('Create download task')} */
}
      {/*     </Button> */
}
      {/*   </div> */
}
      {/* )} */
}
    </div>
);
};

